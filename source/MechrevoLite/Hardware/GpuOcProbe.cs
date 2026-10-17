using System;
using System.IO;
using System.Text;
using System.Threading;
using MechrevoLite.Gpu;
using MechrevoLite.Helpers;

namespace MechrevoLite.Hardware;

/// <summary>
/// 2026-10-19（宝龙达 · GPU 偏移真机探针）：只读基线 → 核心 +50 → 回读 → 核心 +100 → 回读 →
/// 显存 +50/+100 → 回读 → **归零并回读确认**。
/// <para>⚠️ 机主指定的测试强度：偏移绝对不超过 ±100 MHz；+50 回读不变就说明本机 vBIOS 锁了该偏移，
/// 如实记录、⛔ 不再硬写 +100。</para>
/// <para>结果写桌面 <c>lm-gpu-oc-probe-*.txt</c>，同时逐行写 <c>%APPDATA%\MechrevoLite\log.txt</c>。</para>
/// </summary>
internal static class GpuOcProbe
{
	internal static string Run()
	{
		string path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
			"lm-gpu-oc-probe-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".txt");
		try
		{
			File.WriteAllText(path, "lm-gpu-oc-probe 开始 " + DateTime.Now.ToString("HH:mm:ss") + "\r\n", Encoding.UTF8);
		}
		catch (Exception ex)
		{
			return "ERR 桌面文件写不了: " + ex.Message;
		}
		void L(string s)
		{
			try { File.AppendAllText(path, s + "\r\n", Encoding.UTF8); } catch { }
			try { Logger.WriteLine("[gpu-oc-probe] " + s); } catch { }
		}
		try
		{
			L("管理员: " + (ProcessHelper.IsUserAdministrator() ? "是" : "否（NVAPI 写偏移通常会被拒绝）"));
			L("接口: nvapi64.dll / NvAPI_GPU_GetPstates20 / NvAPI_GPU_SetPstates20（P-States 20 · P0 · 单位 MHz）");
			if (!NvapiPStates.TryGetP0Offsets(out int baseCore, out int baseMem))
			{
				L("读基线失败: " + NvapiPStates.LastError);
				return path;
			}
			L($"基线: core={baseCore} MHz mem={baseMem} MHz");
			if (NvapiPStates.TryGetP0OffsetRange(out int coreMin, out int coreMax, out int memMin, out int memMax))
			{
				L($"驱动 P0 范围: core={coreMin}..{coreMax} MHz mem={memMin}..{memMax} MHz");
			}
			else
			{
				L("驱动 P0 范围读取失败: " + NvapiPStates.LastError);
			}
			bool coreWritable = false;
			bool memWritable = false;
			try
			{
				coreWritable = Test("核心", baseCore + 50, baseMem, 50);
				if (coreWritable)
				{
					coreWritable = Test("核心", baseCore + 100, baseMem, 100);
				}
				else
				{
					L("⚠️ 核心 +50 回读没有变化 ⇒ 本机 vBIOS 锁核心偏移（如实记录，不写 +100、不硬写）");
				}
				memWritable = Test("显存", baseCore, baseMem + 50, 50);
				if (memWritable)
				{
					memWritable = Test("显存", baseCore, baseMem + 100, 100);
				}
				else
				{
					L("⚠️ 显存 +50 回读没有变化 ⇒ 本机显存偏移不可写（如实记录，不写 +100）");
				}
			}
			finally
			{
				bool resetOk = NvapiPStates.TrySetP0Offsets(0, 0, out int resetCore, out int resetMem);
				L($"归零: 写 core=0 mem=0 ⇒ ok={resetOk} · 回读 core={resetCore} mem={resetMem}"
					+ (string.IsNullOrEmpty(NvapiPStates.LastError) ? "" : " · " + NvapiPStates.LastError));
				bool zeroConfirmed = false;
				for (int i = 0; i < 10; i++)
				{
					Thread.Sleep((i == 0) ? 150 : 300);
					if (NvapiPStates.TryGetP0Offsets(out int nowCore, out int nowMem))
					{
						L($"归零回读[{i}]: core={nowCore} MHz mem={nowMem} MHz");
						if (nowCore == 0 && nowMem == 0)
						{
							zeroConfirmed = true;
							break;
						}
					}
					else
					{
						L($"归零回读[{i}]失败: {NvapiPStates.LastError}");
					}
				}
				L(zeroConfirmed ? "归零回读确认: core=0 mem=0 ✅" : "⚠️ 归零回读未确认（见上方数值）");
			}
			L($"结论: 核心偏移可写={coreWritable} · 显存偏移可写={memWritable}");
			L("说明: 本探针只验证 NVAPI P-States 20 的 P0 通路；宝龙达机型没有 GCU，走同一套 NVAPI P0。");
		}
		catch (Exception ex)
		{
			L("探针异常: " + ex.GetType().Name + " " + ex.Message);
			L(ex.StackTrace ?? "");
		}
		return path;

		bool Test(string label, int targetCoreMhz, int targetMemMhz, int deltaMhz)
		{
			bool ok = NvapiPStates.TrySetP0Offsets(targetCoreMhz, targetMemMhz, out int appliedCore, out int appliedMem);
			bool match = ok && appliedCore == targetCoreMhz && appliedMem == targetMemMhz;
			L($"{label} +{deltaMhz} MHz: 写 core={targetCoreMhz} mem={targetMemMhz} MHz ⇒ ok={ok} · 回读 core={appliedCore} mem={appliedMem} MHz · 一致={match}"
				+ (string.IsNullOrEmpty(NvapiPStates.LastError) ? "" : " · " + NvapiPStates.LastError));
			return match;
		}
	}
}
