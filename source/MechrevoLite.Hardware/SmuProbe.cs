// （30 系 · SMU **只读**探针）
using System;
using System.IO;
using System.Reflection;
using System.Text;
using MechrevoLite.Helpers;
using PawnIO;

namespace MechrevoLite.Hardware;

internal static class SmuProbe
{
	internal static string Run()
	{
		string path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
			"lm-smu-probe-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".txt");
		try
		{
			File.WriteAllText(path, "lm-smu-probe 开始 " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + "\r\n", Encoding.UTF8);
		}
		catch (Exception ex)
		{
			return "ERR 桌面文件写不了: " + ex.Message;
		}
		void L(string s)
		{
			try { File.AppendAllText(path, s + "\r\n", Encoding.UTF8); } catch { }
			try { Logger.WriteLine("[smu-probe] " + s); } catch { }
		}
		try
		{
			L("用途: 只读确认 30 系（蛟龙16 / MRID6 / 6800H）的 SMU 通路能否读 PL / 温度墙 / 时间窗。");
			L("只读声明: 本探针不发任何 SMU 写命令（不调用任何 Set*），只读代号/版本/PM 表。一个字节的设置都不写。");
			bool admin = ProcessHelper.IsUserAdministrator();
			L("管理员: " + (admin ? "是" : "否（PawnIO 通常只允许管理员打开 ⇒ 预计读到 AccessDenied）"));

			// ① PawnIO 驱动状态（用 ConnectResult 区分"没装"与"装了指纹不对/没权限"）
			PawnIOWrapper.ConnectResult connectResult;
			using (PawnIOWrapper probeIo = new PawnIOWrapper())
			{
				connectResult = probeIo.Connect();
			}
			L("PawnIO: IsPawnInstalled=" + (connectResult != PawnIOWrapper.ConnectResult.NotInstalled)
				+ " · Connect=" + connectResult + " (" + DescribeConnect(connectResult) + ")");
			if (connectResult != PawnIOWrapper.ConnectResult.OK)
			{
				L("结论: ⛔ 读不到 SMU —— 原因 = " + DescribeConnect(connectResult));
				L("      需要管理员权限（不要用本探针自动提权；请人工以管理员身份运行）：L-Mechrevo.exe --smu-probe");
				L("      （模块/驱动状态：PawnIO=" + connectResult + "；本报告没有任何写入动作。）");
				return path;
			}

			// ② SMU 服务初始化（载入内嵌 RyzenSMU.bin；全部是读）
			using RyzenSmuService smu = new RyzenSmuService();
			bool initialized;
			try
			{
				initialized = smu.Initialize(Assembly.GetExecutingAssembly());
			}
			catch (Exception ex)
			{
				L("SMU 初始化异常: " + ex.GetType().Name + " " + ex.Message);
				initialized = false;
			}
			if (!initialized)
			{
				L("SMU: 初始化失败（PawnIO 已连上，但 RyzenSMU 模块载入/代号/SMU 版本任一步没过）");
				L("结论: ⛔ 读不到 SMU —— 无法判断 PL/温度墙/时间窗；本报告没有任何写入动作。");
				return path;
			}
			L("SMU: 初始化成功");
			L("       代号=" + smu.CpuCodeName + " · 家族=" + smu.Family + " · SMU 版本=0x" + smu.SmuVersion.ToString("X"));
			L("       族驱动命令白名单（LegacyFamilyCommandsApproved）= " + RyzenSmuService.LegacyFamilyCommandsApproved(smu.CpuCodeName));
			L("       说明: 白名单=false 的代号 ⇒ 时间窗/温度墙会返回 NotSupported（主动不发，不猜命令号）。");
			if (smu.CpuCodeName != CpuCodeName.Undefined)
			{
				L("       30 系（Rembrandt/6800H）应落在家族 Mobile；本机若为 HawkPoint/Phoenix 家族，命令组相同，可作为写通路的参照机。");
			}

			// ③ 读 PM 表：当前 limits + tctl + 两个时间窗
			SmuPowerSnapshot snapshot = null;
			try
			{
				snapshot = smu.GetPowerSnapshot();
			}
			catch (Exception ex)
			{
				L("PM 表读取异常: " + ex.GetType().Name + " " + ex.Message);
			}
			if (snapshot == null)
			{
				L("当前 limits: 读不到（PM 表 resolve/transfer/read 失败，或表首字段为 0 = 还没刷出来）");
			}
			else
			{
				L($"PM 表版本: 0x{snapshot.TableVersion:X6}");
				L($"当前 limits（只读）: SPL(stapm)={snapshot.Stapm:F1}W · sPPT(slow/PL1 口径)={snapshot.Slow:F1}W · "
					+ $"fPPT(fast/PL2 口径)={snapshot.Fast:F1}W · tctl={snapshot.TctlTemp:F0}℃");
				L("当前时间窗（只读）: STAPM 时间窗=" + ShowSeconds(snapshot.StapmTimeSeconds)
					+ " · slow(平均功耗) 时间窗=" + ShowSeconds(snapshot.SlowTimeSeconds));
				L("⚠️ 时间窗读不到 ≠ 机器没有该设置：可能是该 PM 表版本没有这两个字段（未核实的表版本我们不猜偏移）。");
			}

			// ④ **再读一次**（仍然只读）：判断两个"时间窗"字段是不是**稳定**的当前值。
			//   ⚠️ 真机实测教训（本机 Phoenix / PM 表 0x4C0009）：0x918/0x91C 两个字段
			//      连续两次读数会在 25~67 之间来回跳（疑似动态量，或该表版本的偏移不符）⇒
			//      **不能**把它当"用户可设置的当前值"显示/验证。这里如实标注"不稳定"。
			//   ⚠️ 只读：两次都是 PM 表读路径，未写任何东西。
			SmuPowerSnapshot second = null;
			try
			{
				second = smu.GetPowerSnapshot();
			}
			catch (Exception ex2)
			{
				L("二次采样异常: " + ex2.GetType().Name + " " + ex2.Message);
			}
			if (snapshot != null && second != null)
			{
				bool stapmStable = snapshot.StapmTimeSeconds.HasValue && second.StapmTimeSeconds.HasValue
					&& Math.Abs(snapshot.StapmTimeSeconds.Value - second.StapmTimeSeconds.Value) <= 1;
				bool slowStable = snapshot.SlowTimeSeconds.HasValue && second.SlowTimeSeconds.HasValue
					&& Math.Abs(snapshot.SlowTimeSeconds.Value - second.SlowTimeSeconds.Value) <= 1;
				L($"时间窗稳定性（连续两次采样）: STAPM={ShowSeconds(snapshot.StapmTimeSeconds)} → {ShowSeconds(second.StapmTimeSeconds)} ⇒ 稳定={stapmStable} · "
					+ $"slow={ShowSeconds(snapshot.SlowTimeSeconds)} → {ShowSeconds(second.SlowTimeSeconds)} ⇒ 稳定={slowStable}");
				if (!stapmStable)
				{
					L("⚠️ STAPM 时间窗**读数不稳定** ⇒ 本机 UI **不会**建「功耗时间窗（STAPM，秒）」那一行"
						+ "（遵守\"读不到/不可信就不显示\"），⛔ 也不会拿它做写入回读确认；"
						+ "30 系真机（PM 表 0x40000x）的该字段仍待真机确认。");
				}
			}

			// ⑤ 与既有 GetPowerLimits 口径对照（ModeControl.ReadRyzenLimits 用的就是它）
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

			// ⑤ 命令号语义（只写事实，不实测）
			L("时间窗/温度墙命令号（参考 RyzenAdj LGPL / ZenMaster 两个独立实现的语义，本仓库重写为 C# 表）：");
			L("  slow_time = MP1 0x17 · stapm_time = MP1 0x18 · tctl_temp = MP1 0x19（Renoir/APU 组：含 Rembrandt 6800H、HawkPoint）");
			L("  Raven/Picasso/Dali = 0x1D/0x1E/0x1F · DragonRange/FireRange = 0x60/0x4E/0x3F");
			L("  ⚠️ 没有 fast_time 这条命令（fast limit = PL2 是瞬时限值，没有时间参数）。");
			L("  ⚠️ 本探针**只读** ⇒ 以上命令号**没有实测写入**；写入结果需在 30 系真机上由自定义模式「应用」实测（写入后会回读确认）。");

			L("结论: SMU 可读=" + (snapshot != null) + " · 管理员=" + (admin ? "是" : "否")
				+ " · 家族=" + smu.Family + " · 代号=" + smu.CpuCodeName);
			return path;
		}
		catch (Exception ex)
		{
			L("探针异常: " + ex.GetType().Name + " " + ex.Message);
			L(ex.StackTrace ?? "");
			return path;
		}
	}

	private static string DescribeConnect(PawnIOWrapper.ConnectResult result)
	{
		return result switch
		{
			PawnIOWrapper.ConnectResult.OK => "已连上 PawnIO 设备",
			PawnIOWrapper.ConnectResult.NotInstalled => "PawnIO 驱动未安装（设备不存在）",
			PawnIOWrapper.ConnectResult.AccessDenied => "拒绝访问（需要管理员权限）",
			PawnIOWrapper.ConnectResult.OtherError => "其它错误（设备/驱动异常）",
			_ => result.ToString()
		};
	}

	private static string ShowSeconds(int? seconds)
	{
		return seconds.HasValue ? (seconds.Value + "s") : "读不到";
	}
}
