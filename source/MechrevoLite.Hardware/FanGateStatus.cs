// 2026-10-18（宝龙达 · 风扇闸门）：⭐⭐ **只读**闸门状态查询 `--fan-gate-status` ⭐⭐
//
// ⭐ 用途（机主排查 UI 为什么不显示「启用软件闭环 / 双风扇联动」）：
//    打印 `FanCurveVerified`（= 读 `fan_ec_verified.cfg` 的结果）、`PreferredChannel`、
//    配置文件路径/是否存在/内容、平台与代际判据。
//
// ⛔ 本命令**纯只读**：不探测 EC、不写任何字节、不改 cfg、不发 WMI；
//    ⚠️ 它是**独立进程**：`FanCurveVerified` 是「此刻读 cfg 的结果」；正在运行的 LM 进程
//       在启动时把闸门缓存到内存 ⇒ 改 cfg 后需重启 LM 才生效（本命令会打印这条提示）。
//
// ⭐ 用法：`L-Mechrevo.exe --fan-gate-status`（普通权限即可；不需要管理员）
//
// 许可/署名：与本树同一实现，见 THIRD-PARTY-NOTICES.md 与 LICENSES\。

using System;
using System.IO;
using Microsoft.Win32;
using MechrevoLite.Hardware;

namespace MechrevoLite.Hardware;

/// <summary>⭐⭐ 风扇闸门**只读**状态查询（<c>--fan-gate-status</c>）✅。</summary>
internal static class FanGateStatus
{
	internal static int Run()
	{
		try
		{
			string board = ReadBaseBoardProduct();
			bool is30 = string.Equals(board, "MRID6", StringComparison.OrdinalIgnoreCase);
			string cfgPath = FanEcVerifyStore.GetConfigPath();
			bool exists = FanEcVerifyStore.MarkerExists();
			bool verified = FanEcVerifyStore.Load(is30);
			Console.WriteLine("[fan-gate-status] 平台/闸门（⭐ 只读）");
			Console.WriteLine("  板号 = " + (board.Length == 0 ? "（读不到）" : board) + " ⇒ 代际 = " + (is30 ? "30 系" : "40 系（或非宝龙达）"));
			Console.WriteLine("  FanCurveVerified（读 cfg 结果）= " + verified);
			Console.WriteLine("  PreferredChannel（本进程）= " + FanWriter.ChannelName(FanWriter.PreferredChannel) + "（⚠️ 独立进程新建 ⇒ 通常 None；见运行中 LM 日志）");
			Console.WriteLine("  cfg 路径 = " + cfgPath);
			Console.WriteLine("  cfg 存在 = " + exists);
			if (exists)
			{
				try
				{
					string[] lines = File.ReadAllLines(cfgPath);
					Console.WriteLine("  cfg 内容：");
					foreach (string line in lines)
					{
						Console.WriteLine("    " + line);
					}
				}
				catch (Exception ex)
				{
					Console.WriteLine("  cfg 读失败：" + ex.GetType().Name + " " + ex.Message);
				}
			}
			Console.WriteLine("  ⭐ 放行规则：上述 cfg 含 fan_ec_verified_30=1 / fan_ec_verified_40=1 / fan_ec_verified=1 ⇒ 重启 LM 后 UI 放开");
			Console.WriteLine("  ⛔ 本命令未写任何文件、未读 EC、未发 WMI");
			try
			{
				Logger.WriteLine("[fan-gate-status] board=" + board + " is30=" + is30 + " verified=" + verified
					+ " cfg=" + cfgPath + " exists=" + exists);
			}
			catch
			{
			}
			return 0;
		}
		catch (Exception ex)
		{
			Console.WriteLine("[fan-gate-status] 异常：" + ex.GetType().Name + " " + ex.Message);
			return 1;
		}
	}

	private static string ReadBaseBoardProduct()
	{
		try
		{
			using RegistryKey key = Registry.LocalMachine.OpenSubKey("HARDWARE\\DESCRIPTION\\System\\BIOS", writable: false);
			return (key?.GetValue("BaseBoardProduct") as string ?? string.Empty).Trim();
		}
		catch
		{
			return string.Empty;
		}
	}
}
