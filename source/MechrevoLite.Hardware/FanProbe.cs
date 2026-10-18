// 2026-10-18（宝龙达 · 风扇接管接口）：⭐⭐ **只读探针** `--fan-probe` ⭐⭐
//
// ⭐⭐ 2026-10-18（机主 0.291.9.14 真机卡死后的**最高优先级**收紧）：
//    ⚠️ 写 WMI 功能号 20=1 会**卡死**（连 try/finally 都跑不完）⇒ ⛔ 本探针**不再写任何字节**！
//    ⭐ WMI 20/21 只保留**只读**对照；EC 写通路验证请用 `--fan-ec-test`（白名单写 + finally 恢复）。
//
// ⭐ 本探针只做：
//    0a. **EC 只读验证**：读 `0xC83C`（Fan1 目标）· `0xC83D`（Fan2 目标）· `0xB20`（接管掩码），
//        按"三地址都读得到 · 不是 0xFF 填充 · 目标档 0..0xC8"判可用/不可用 ✅
//    0.  **WMI 只读对照**：GET 13 / 20 / 21 的**原始 OutData hex** + 转速/开关/目标的只读解析 ✅
//    0b. **结论**：可用 / 不可用 / 接口不存在（⭐ 30/40 系分开；⭐ 只认 EC 实测）✅
//
// ⛔ 安全边界（硬性）：
//    · ⛔ 本探针**没有**任何 WMI Set / EC 数据写路径（EC 只写 SuperIO 窗口地址寄存器用于寻址）；
//    · ⛔ 不打包、不碰安装目录、不 kill LM；⭐ 结果写桌面 `lm-fan-probe-*.txt` + 日志 `[fan-probe]`。
//
// ⭐ 用法：`L-Mechrevo.exe --fan-probe`（⭐ WinExe 无控制台 ⇒ 看桌面文件 / log.txt；⭐ 需管理员跑 EC 段）。
//
// 许可/署名：与本树同一实现，见 THIRD-PARTY-NOTICES.md 与 LICENSES\。

using System;
using System.IO;
using System.Text;

namespace MechrevoLite.Hardware;

/// <summary>
/// ⭐⭐ 宝龙达风扇接管接口**只读**探针（<c>--fan-probe</c>）：EC 三地址 + WMI 13/20/21 只读对照 ✅。
/// </summary>
internal static class FanProbe
{
	internal static string Run()
	{
		string path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
			"lm-fan-probe-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".txt");
		try
		{
			File.WriteAllText(path, "lm-fan-probe 开始 " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + "\r\n", Encoding.UTF8);
		}
		catch (Exception ex)
		{
			return "ERR 桌面文件写不了: " + ex.Message;
		}
		void L(string s)
		{
			try { File.AppendAllText(path, s + "\r\n", Encoding.UTF8); } catch { }
			try { Logger.WriteLine("[fan-probe] " + s); } catch { }
		}

		try
		{
			L("=== 宝龙达风扇接管接口 · 只读探针（--fan-probe）===");
			L("⚠️ 2026-10-18：真机写 WMI 20=1 会卡死 ⇒ ⛔ 本探针**不写任何字节**（WMI 20/21 仅只读对照）");
			L("⭐ EC 写通路验证请用 `--fan-ec-test`（白名单写 + finally 恢复）");
			L("");

			string board = ReadBoardProduct();
			// ⭐ 满转按板号：裸 `MRID6` = 30 系 4900 RPM（机主新基准）；`MRID6-*`（含 MRID6-23）= 40 系 6000 RPM ✅
			int fullRpm = string.Equals(board, "MRID6", StringComparison.OrdinalIgnoreCase)
				? BaolongdaFanCurveSettings.FullRpm30Series
				: BaolongdaFanCurveSettings.FullRpm40Series;
			L("主板 = " + (board.Length == 0 ? "(空/读不到)" : board) + " ⇒ 按 " + fullRpm + " RPM 满转口径");
			L("");

			// ────────────────────────────────────────────────────────────────
			// 0a. EC 只读验证（⭐ 只认 EC 实测；30/40 系分开）
			// ────────────────────────────────────────────────────────────────
			bool ecVerified = ProbeEcFanRegistersReadOnly(L);
			L("");

			// ────────────────────────────────────────────────────────────────
			// 0. WMI 只读对照（⛔ 一个 Set 都不发）
			// ────────────────────────────────────────────────────────────────
			L("── 0. WMI 只读对照（⛔ 不写任何字节）──");
			BaolongdaWmi wmi = null;
			try
			{
				wmi = BaolongdaWmi.TryCreate();
			}
			catch (Exception ex)
			{
				L("WMI 探测异常：" + ex.GetType().Name + " " + ex.Message);
			}
			if (wmi == null)
			{
				L("WMI：⚠️ 没有 MICommonInterface 实例（`ACPI\\PNP0C14\\MIFS*` 探不到）");
			}
			else
			{
				L("实例 = " + wmi.InstancePath + " · 类GUID = " + (wmi.ClassGuid.Length == 0 ? "(无)" : wmi.ClassGuid));
				if (wmi.TryInvokeRawGet(BaolongdaWmi.Function.CpuGpuFanSpeed, null, out byte[] raw13))
				{
					L("GET 13 ⇒ " + Hex(raw13));
				}
				else
				{
					L("GET 13 ⇒ ⚠️ 读失败");
				}
				bool rpmOk = wmi.TryReadFanSpeeds(out BaolongdaWmi.FanSpeeds speeds);
				L("13 转速 = " + (rpmOk ? speeds.First + " / " + speeds.Second + " RPM" : "读不到"));
				if (wmi.TryInvokeRawGet(BaolongdaWmi.Function.MaxFanSpeedSwitch, new byte[1], out byte[] raw20))
				{
					L("GET 20（[4]=0）⇒ " + Hex(raw20));
				}
				else
				{
					L("GET 20（[4]=0）⇒ ⚠️ 读失败");
				}
				if (wmi.TryInvokeRawGet(BaolongdaWmi.Function.MaxFanSpeed, new byte[1], out byte[] raw21a))
				{
					L("GET 21（[4]=0）⇒ " + Hex(raw21a));
				}
				else
				{
					L("GET 21（[4]=0）⇒ ⚠️ 读失败");
				}
				if (wmi.TryInvokeRawGet(BaolongdaWmi.Function.MaxFanSpeed, new byte[1] { 1 }, out byte[] raw21b))
				{
					L("GET 21（[4]=1）⇒ " + Hex(raw21b));
				}
				else
				{
					L("GET 21（[4]=1）⇒ ⚠️ 读失败");
				}
				bool switchOk = wmi.TryReadFanManualSwitches(out bool ch0On, out bool ch1On);
				L("20 手动接管（只读解析）= " + (switchOk ? (ch0On ? "1" : "0") + "/" + (ch1On ? "1" : "0") : "读不到"));
				L("⚠️ 注意：WMI 20/21 的**写**通路已禁用（20=1 真机卡死，连 try/finally 都跑不完）");
				try
				{
					wmi.Dispose();
				}
				catch
				{
				}
			}
			L("");

			// ────────────────────────────────────────────────────────────────
			// 0b. 只读验证结论（⭐ 机主最高优先级 · 结论=不可用 ⇒ 绝不写；本探针本来也不写）
			// ────────────────────────────────────────────────────────────────
			L("── 0b. 只读验证结论 ──");
			string verdict;
			if (wmi == null)
			{
				verdict = "接口不存在（WMI MIFS 实例不存在" + (ecVerified ? "；EC 可读 ✅" : "；EC 未通过 ⚠️") + "）";
			}
			else if (ecVerified)
			{
				verdict = "可用（EC 0xC83C/0xC83D/0xB20 只读验证通过）";
			}
			else
			{
				verdict = "不可用（EC 三地址未全部读到合理值）";
			}
			L("结论：" + verdict);
			L("⭐ 下一步：在管理员下跑 `--fan-ec-test` 验证 EC **写**通路（本探针不写）；"
				+ "写通路通过前，产品 UI 不开放风扇曲线/强冷 ✅");
		}
		catch (Exception ex)
		{
			L("探针异常：" + ex.GetType().Name + " " + ex.Message);
			L(ex.StackTrace ?? "");
		}
		return path;
	}

	/// <summary>
	/// ⭐⭐ 2026-10-18：⭐ **EC 只读验证**（⭐ 只读：EC 数据一个字节都不写；只写 SuperIO 窗口地址寄存器寻址）。
	/// <para>⭐ 读 `0xC83C`（Fan1 目标）· `0xC83D`（Fan2 目标）· `0xB20`（手动接管掩码）✅</para>
	/// <para>⭐ 判据：三地址都读得到 · 不是 `0xFF` 填充 · 目标档在 `0..0xC8` ⇒ 可用 ✅</para>
	/// </summary>
	private static bool ProbeEcFanRegistersReadOnly(Action<string> log)
	{
		log("── 0a. EC 只读验证（⭐ 机主最高优先级 · 先验证再开放）──");
		bool ok = false;
		try
		{
			int fan1 = -1;
			int fan2 = -1;
			int mask = -1;
			bool r1 = EcLpcIo.TryReadByte(EcLpcIo.Fan1TargetAddress, out fan1);
			bool r2 = EcLpcIo.TryReadByte(EcLpcIo.Fan2TargetAddress, out fan2);
			bool r3 = EcLpcIo.TryReadByte(EcLpcIo.FanManualMaskAddress, out mask);
			log("EC 0xC83C（Fan1 目标）= " + (r1 ? "0x" + fan1.ToString("X2") : "读不到")
				+ " · 0xC83D（Fan2 目标）= " + (r2 ? "0x" + fan2.ToString("X2") : "读不到")
				+ " · 0xB20（接管掩码）= " + (r3 ? "0x" + mask.ToString("X2") : "读不到"));
			ok = r1 && r2 && r3
				&& mask != 0xFF
				&& fan1 >= 0 && fan1 <= 0xC8
				&& fan2 >= 0 && fan2 <= 0xC8;
			log("判据：三地址都读得到 · 不是 0xFF 填充 · 目标档 0..0xC8 ⇒ " + (ok ? "通过 ✅" : "未通过 ⚠️"));
			if (!ok)
			{
				log("EC 通道详情：" + EcLpcIo.DescribeChannel());
			}
			log("⚠️ 说明：本判据只认 EC 实测（⭐ 30/40 系分开）；WMI 13/20 仅对照，⛔ 不作为开放依据");
		}
		catch (Exception ex)
		{
			log("EC 读取异常：" + ex.GetType().Name + " " + ex.Message);
		}
		return ok;
	}

	private static string Hex(byte[] data)
	{
		if (data == null)
		{
			return "null";
		}
		if (data.Length == 0)
		{
			return "";
		}
		StringBuilder sb = new StringBuilder(data.Length * 3);
		for (int i = 0; i < data.Length; i++)
		{
			if (i > 0)
			{
				sb.Append(' ');
			}
			sb.Append(data[i].ToString("X2"));
		}
		return sb.ToString();
	}

	/// <summary>⭐ 只读主板型号（⭐ 只为了算"本机满转"：裸 `MRID6` = 30 系 4900 / `MRID6-*` = 40 系 6000）✅。</summary>
	private static string ReadBoardProduct()
	{
		try
		{
			using Microsoft.Win32.RegistryKey key = Microsoft.Win32.Registry.LocalMachine
				.OpenSubKey("HARDWARE\\DESCRIPTION\\System\\BIOS", writable: false);
			return (key?.GetValue("BaseBoardProduct") as string ?? string.Empty).Trim();
		}
		catch
		{
			return string.Empty;
		}
	}
}
