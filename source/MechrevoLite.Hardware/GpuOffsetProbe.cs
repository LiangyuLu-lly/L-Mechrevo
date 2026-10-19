// ⭐⭐ 2026-10-19（机主：GCU 最多 -900 显存？NV 范围到底多少）：
//   **纯只读**探针 —— 打印 NVAPI P0 范围/当前值、厂商字段范围、当前值、以及我们 UI 采用的范围。
//   ⛔ 不调用任何 Set、⛔ 不改配置、⛔ 不写硬件。
using System;
using System.IO;
using System.Text;
using MechrevoLite.Gpu;

namespace MechrevoLite.Hardware;

internal static class GpuOffsetProbe
{
	internal static string Run(string? outputPath = null)
	{
		string path = string.IsNullOrWhiteSpace(outputPath)
			? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
				"lm-gpu-offset-probe-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".txt")
			: outputPath;
		try
		{
			File.WriteAllText(path, "lm-gpu-offset-probe 开始 " + DateTime.Now.ToString("HH:mm:ss") + "\r\n", Encoding.UTF8);
		}
		catch (Exception ex)
		{
			return "ERR 报告写不了: " + ex.Message;
		}
		void L(string s)
		{
			try { File.AppendAllText(path, s + "\r\n", Encoding.UTF8); } catch { }
			try { Logger.WriteLine("[gpu-offset-probe] " + s); } catch { }
		}
		MechrevoHw? hw = null;
		try
		{
			L("管理员: " + (MechrevoLite.Helpers.ProcessHelper.IsUserAdministrator() ? "是" : "否"));
			// NVAPI 只读范围/当前值
			string nvapiLine;
			int nvApiCore = 0;
			int nvApiMem = 0;
			bool nvapiRangeOk = NvapiPStates.TryGetP0OffsetRange(out int nvCoreMin, out int nvCoreMax, out int nvMemMin, out int nvMemMax);
			bool nvapiCurrentOk = NvapiPStates.TryGetP0Offsets(out nvApiCore, out nvApiMem);
			if (nvapiRangeOk)
			{
				nvapiLine = "NVAPI P0 范围：core=" + nvCoreMin + ".." + nvCoreMax + " · mem=" + nvMemMin + ".." + nvMemMax + " MHz（来源=NVAPI）";
			}
			else
			{
				nvapiLine = "NVAPI P0 范围：不可用：" + NvapiPStates.LastError;
			}
			L(nvapiLine);
			// 厂商字段：需要只读连接 GCU 状态
			try
			{
				hw = new MechrevoHw();
				string connectNote = hw.ConnectAsync().GetAwaiter().GetResult() ? "已连接（只读）" : "未连接";
				L("GCU 连接: " + connectNote);
			}
			catch (Exception ex)
			{
				L("GCU 连接异常（已吞掉）: " + ex.Message);
			}
			bool nvidia = hw != null && hw.DgpuGeneration != MechrevoLite.Gpu.DgpuGenerationKind.NoDgpu;
			if (hw == null || !nvidia)
			{
				L("非 NVIDIA 机型（或未识别到独显）⇒ 拒绝：本探针只对 NVIDIA 机型有意义");
				return path;
			}
			L("厂商字段范围：core=" + hw.GpuCoreOffsetMinimum + ".." + hw.GpuCoreOffsetMaximum
				+ " · mem=" + hw.GpuMemoryOffsetMinimum + ".." + hw.GpuMemoryOffsetMaximum);
			L("当前值：NVAPI core=" + (nvapiCurrentOk ? nvApiCore.ToString() : "读不到")
				+ " mem=" + (nvapiCurrentOk ? nvApiMem.ToString() : "读不到")
				+ " · 厂商 core=" + hw.GpuCoreClockOffset + " mem=" + hw.GpuMemClockOffset);
			// 与 UI 同一份范围实现（CustomModeForm.GpuOffsetUiRange）
			(int uiCoreMin, int uiCoreMax, int uiMemMin, int uiMemMax) = MechrevoLite.CustomModeForm.GpuOffsetUiRange(hw);
			L("我们 UI 采用：slider core=-500..500 mem=-2000..2000 · numeric core=" + uiCoreMin + ".." + uiCoreMax
				+ " mem=" + uiMemMin + ".." + uiMemMax
				+ "（= max(驱动下限,-900) .. min(驱动上限,+2000)）");
			L("只读完成；本探针没有调用任何 Set / 没有改配置。");
			return path;
		}
		catch (Exception ex)
		{
			L("探针异常（已吞掉）: " + ex);
			return path;
		}
		finally
		{
			try { hw?.Dispose(); } catch { }
		}
	}
}
