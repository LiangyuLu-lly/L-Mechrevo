// **只读探测命令** --wujie-smu-probe（0 写入）。
using System;
using System.IO;
using System.Text;
using MechrevoLite.Helpers;
using MechrevoLite.Mode;
using PawnIO;

namespace MechrevoLite.Hardware;

internal static class WujieSmuProbe
{
	internal static string Run()
	{
		string path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
			"lm-wujie-smu-probe-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".txt");
		try
		{
			File.WriteAllText(path, "lm-wujie-smu-probe 开始 " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + "\r\n", Encoding.UTF8);
		}
		catch (Exception ex)
		{
			return "ERR 桌面文件写不了: " + ex.Message;
		}
		void L(string s)
		{
			try { File.AppendAllText(path, s + "\r\n", Encoding.UTF8); } catch { }
			try { Logger.WriteLine("[wujie-smu-probe] " + s); } catch { }
		}
		try
		{
			L("用途: 只读确认「无界系列 + AMD APU」是否可开放 SMU 自定义（PL/温度墙/曲线，与宝龙达 30 系同源）。");
			L("只读声明: 本探针不发任何 SMU 写命令（不调用任何 Set*），不写 EC/注册表/配置。一个字节的设置都不写。");
			L("管理员: " + (ProcessHelper.IsUserAdministrator() ? "是" : "否（PawnIO 通常只允许管理员打开 ⇒ 预计读到 AccessDenied）"));

			// ── ① 只读判据（与 UI/日志**同一口径**：WujieSmuGate.Evaluate）──
			WujieSmuVerdict verdict = WujieSmuGate.Evaluate();
			MechrevoDeviceCapabilities identity = MechrevoDeviceCapabilities.Current;
			L("机型名(SystemProductName)=" + Show(verdict.Model) + " · 系列=" + Show(identity.SystemFamily) + " · SKU=" + Show(identity.SystemSku) + " · 板号=" + Show(verdict.Board)
				+ " · CPU=" + Show(verdict.CpuName) + " · CpuVendor=" + Show(verdict.CpuVendorRaw)
				+ " · APU判据=" + (verdict.ApuOk ? "是（AMD 且无 NVIDIA 独显）" : "否")
				+ "（isAmd=" + verdict.IsAmd + " · hasNvidia=" + verdict.HasNvidia + "）");
			L("无界机型命中: 命中字段=" + (string.IsNullOrEmpty(verdict.HitField) ? "无（四个字段均不含 WUJIE/无界）" : verdict.HitField)
				+ " · 值=" + Show(verdict.HitValue));

			// ── ② SMU 代号/家族/版本（只读；与 SmuAvailability 同一单例）──
			RyzenSmuService smu = null;
			try
			{
				smu = ModeControl.SharedSmu;
			}
			catch (Exception ex)
			{
				L("SMU 单例读取异常: " + ex.GetType().Name + " " + ex.Message);
			}
			if (smu == null)
			{
				L("CpuCodeName=读不到 · Family=读不到 · SMU版本=读不到（SmuAvailability: " + verdict.SmuReason + "）");
			}
			else
			{
				L("CpuCodeName=" + smu.CpuCodeName + " · Family=" + smu.Family
					+ " · SMU版本=0x" + smu.SmuVersion.ToString("X", System.Globalization.CultureInfo.InvariantCulture));
				L("可写命令支持情况: LegacyFamilyCommandsApproved=" + RyzenSmuService.LegacyFamilyCommandsApproved(smu.CpuCodeName)
					+ "（true=时间窗/温度墙等写命令族已核准；false ⇒ 主动不发命令号）");

				// ── ③ 当前功耗读数（只读；PM 表 read 路径）──
				try
				{
					SmuPowerSnapshot snapshot = smu.GetPowerSnapshot();
					if (snapshot == null)
					{
						L("当前 PL1/PL2/STAPM 读数: 读不到（PM 表 resolve/transfer/read 失败或表首字段为 0）");
					}
					else
					{
						L($"当前 PL1(SPL/stapm)={snapshot.Stapm:F1}W · PL2(fPPT/fast)={snapshot.Fast:F1}W"
							+ $" · slow(sPPT)={snapshot.Slow:F1}W · tctl={snapshot.TctlTemp:F0}℃"
							+ $" · PM表版本=0x{snapshot.TableVersion:X6}");
						L("当前时间窗（只读）: STAPM时间窗=" + ShowSeconds(snapshot.StapmTimeSeconds)
							+ " · slow时间窗=" + ShowSeconds(snapshot.SlowTimeSeconds));
					}
				}
				catch (Exception ex)
				{
					L("当前 PL1/PL2/STAPM 读数异常: " + ex.GetType().Name + " " + ex.Message);
				}
				try
				{
					PowerLimits limits = smu.GetPowerLimits();
					L(limits == null
						? "既有 GetPowerLimits(): 读不到（与上面同源，供对照）"
						: $"既有 GetPowerLimits(): SPL={limits.Stapm:F1}W · sPPT={limits.Slow:F1}W · fPPT={limits.Fast:F1}W · tctl={limits.TctlTemp:F0}℃");
				}
				catch (Exception ex)
				{
					L("既有 GetPowerLimits() 异常: " + ex.GetType().Name + " " + ex.Message);
				}
			}

			// ── ④ 结论（逐条列哪一条不满足）──
			L("⇒ 判据结果：可开放 SMU 自定义=" + (verdict.Allowed ? "True" : "False")
				+ "（model含无界=" + verdict.IsWujie + " · amd=" + verdict.IsAmd + " · apu=" + verdict.ApuOk
				+ " · smuReady=" + verdict.SmuReady + " · 命中字段=" + verdict.HitField + "）");
			if (!verdict.Allowed)
			{
				L("   不满足项: " + verdict.UnmetReasons);
			}
			L("结论: 只读完成；本报告**没有任何写入动作**。");
			return path;
		}
		catch (Exception ex)
		{
			L("探针异常: " + ex.GetType().Name + " " + ex.Message);
			L(ex.StackTrace ?? "");
			return path;
		}
	}

	private static string Show(string text)
	{
		return string.IsNullOrWhiteSpace(text) ? "(空/读不到)" : text;
	}

	private static string ShowSeconds(int? seconds)
	{
		return seconds.HasValue ? (seconds.Value + "s") : "读不到";
	}
}
