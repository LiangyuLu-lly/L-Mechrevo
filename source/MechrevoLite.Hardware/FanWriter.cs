// 2026-10-18（宝龙达 · 风扇接管）：⭐⭐ **风扇写入统一门面** `FanWriter` ⭐⭐
//
// ⭐⭐ 背景：WMI 写（20=1）会卡死；LPC 端口 I/O 读成功、写被 EC 拒绝；ACPI EC 读成功、写待验证。
//    机主批准：一次性把写入链路做完，但**默认不开放**（UI 隐藏，验证通过后才放开）。
//
// ⭐ 通道优先级（本类内部）：
//    ① `AcpiEc`   —— `EcPortIo`（0x62/0x66 · XRAM mailbox 0x93 写）✅ 新候选，实测未定
//    ② `SmuEcLpc` —— `SmuEcAccess`（= 7.3 `RyzenAccess` 的真实实现：LPC 0x4E/0x4F 端口 I/O；
//                   ⚠️ 不存在 SMU 邮箱命令，见 `SmuEcAccess.cs` 顶部来源说明）
//    ③ 若首选通道失败 ⇒ 自动尝试次选；成功过的通道记入 <see cref="PreferredChannel" />，下轮优先用。
//
// ⭐ 对外 API（只有这三个写入口，白名单/回读/恢复都封在内部）：
//    · <see cref="TrySetFanTargets" />（两路目标档 0..68）
//    · <see cref="TrySetManualMask" />（0xB20 的 bit1/bit3，读-改-写）
//    · <see cref="TryReleaseToFirmware" />（目标 0 + 清位，交回固件）
//    ⛔ 每次写后立刻回读比对；失败返回 false 并写日志；⛔ 不抛出异常。
//
// ⭐ 闸门：`FanEcVerifyStore` 保存“已验证”标记（%APPDATA%\MechrevoLite\fan_ec_verified.cfg）；
//    默认无标记 ⇒ `FanCurveVerified=false` ⇒ UI 隐藏曲线/强冷；只有 `--fan-smu-ec-test` 验证通过才写入。

using System;
using System.IO;

namespace MechrevoLite.Hardware;

/// <summary>⭐ 写通道（⛔ 不代表 SMU 邮箱命令；`SmuEcLpc` 实为 LPC 端口 I/O）✅</summary>
internal enum FanWriteChannel
{
	None,
	AcpiEc,
	SmuEcLpc
}

/// <summary>⭐⭐ 风扇写入统一门面（ACPI EC → LPC；回读校验/恢复都在内部）✅。</summary>
internal static class FanWriter
{
	private static FanWriteChannel _preferred = FanWriteChannel.None;

	/// <summary>⭐ 上次成功的通道（None = 还没成功过）✅</summary>
	internal static FanWriteChannel PreferredChannel => _preferred;

	/// <summary>⭐ 两路目标档（0..68）；按通道优先级尝试，成功即记住通道 ✅。</summary>
	internal static bool TrySetFanTargets(int fan1Units, int fan2Units, out string detail)
	{
		detail = "";
		int f1 = Math.Clamp(fan1Units, 0, 68);
		int f2 = Math.Clamp(fan2Units, 0, 68);
		foreach (FanWriteChannel channel in Candidates())
		{
			if (TryWriteTargetsOn(channel, f1, f2, out string channelDetail))
			{
				_preferred = channel;
				detail = "通道=" + ChannelName(channel) + " · " + channelDetail;
				return true;
			}
			detail += (detail.Length == 0 ? "" : " | ") + ChannelName(channel) + "失败：" + channelDetail;
			// ⭐ 该通道失败：尝试把自己写过的部分恢复（best-effort，不抛）
			try
			{
				if (channel == FanWriteChannel.AcpiEc)
				{
					EcPortIo.TryRestoreFanFirmwareAcpi(out _);
				}
				else
				{
					EcLpcIo.TryRestoreFanFirmware(out _);
				}
			}
			catch
			{
			}
		}
		return false;
	}

	/// <summary>⭐ 0xB20 接管掩码（只改 bit1/bit3，保留其它位）；回读确认 ✅。</summary>
	internal static bool TrySetManualMask(bool bit1, bool bit3, out string detail)
	{
		detail = "";
		foreach (FanWriteChannel channel in Candidates())
		{
			bool ok;
			string channelDetail;
			if (channel == FanWriteChannel.AcpiEc)
			{
				ok = EcPortIo.TryWriteAcpiFanManualMask(bit1, bit3, out int readBack, out channelDetail);
				channelDetail = "ACPI 0xB20 回读=0x" + (readBack < 0 ? "读不到" : readBack.ToString("X2")) + " · " + channelDetail;
			}
			else
			{
				ok = SmuEcAccess.TryWriteManualMask(bit1, bit3, out channelDetail);
			}
			if (ok)
			{
				_preferred = channel;
				detail = "通道=" + ChannelName(channel) + " · " + channelDetail;
				return true;
			}
			detail += (detail.Length == 0 ? "" : " | ") + ChannelName(channel) + "失败：" + channelDetail;
		}
		return false;
	}

	/// <summary>⭐ 交回固件：两路目标 0 + 清 bit1/bit3（按成功过的通道，失败再换通道）✅。</summary>
	internal static bool TryReleaseToFirmware(out string detail)
	{
		detail = "";
		foreach (FanWriteChannel channel in Candidates())
		{
			bool ok;
			string channelDetail;
			if (channel == FanWriteChannel.AcpiEc)
			{
				ok = EcPortIo.TryRestoreFanFirmwareAcpi(out channelDetail);
			}
			else
			{
				ok = EcLpcIo.TryRestoreFanFirmware(out channelDetail);
			}
			if (ok)
			{
				_preferred = channel;
				detail = "通道=" + ChannelName(channel) + " · " + channelDetail;
				return true;
			}
			detail += (detail.Length == 0 ? "" : " | ") + ChannelName(channel) + "失败：" + channelDetail;
		}
		return false;
	}

	/// <summary>⭐ 只读：两路转速字节 0xC834/0xC835（≈RPM/100，稳定读；ACPI 优先）✅。</summary>
	internal static bool TryReadRpmUnits(out int fan1Units, out int fan2Units, out string detail)
	{
		fan1Units = -1;
		fan2Units = -1;
		bool acpi1 = EcPortIo.TryReadByteStable(0xC834, out int a1, out string an1);
		bool acpi2 = EcPortIo.TryReadByteStable(0xC835, out int a2, out string an2);
		if (acpi1 && acpi2)
		{
			fan1Units = a1;
			fan2Units = a2;
			detail = "ACPI 0xC834=0x" + a1.ToString("X2") + "（" + an1 + "）· 0xC835=0x" + a2.ToString("X2") + "（" + an2 + "）";
			return true;
		}
		bool lpc1 = EcLpcIo.TryReadByteStable(0xC834, out int l1, out string ln1);
		bool lpc2 = EcLpcIo.TryReadByteStable(0xC835, out int l2, out string ln2);
		if (lpc1 && lpc2)
		{
			fan1Units = l1;
			fan2Units = l2;
			detail = "LPC 0xC834=0x" + l1.ToString("X2") + "（" + ln1 + "）· 0xC835=0x" + l2.ToString("X2") + "（" + ln2 + "）";
			return true;
		}
		detail = "ACPI 读失败（" + (acpi1 ? "0xC834 ok" : "0xC834 " + an1) + "）· LPC 读失败（" + (lpc1 ? "ok" : ln1) + "）";
		return false;
	}

	private static bool TryWriteTargetsOn(FanWriteChannel channel, int fan1Units, int fan2Units, out string detail)
	{
		detail = "";
		if (channel == FanWriteChannel.AcpiEc)
		{
			bool w1 = EcPortIo.TryWriteAcpiFanTarget(EcPortIo.AcpiFan1TargetAddress, fan1Units, out int rb1);
			string t1 = EcPortIo.LastAcpiWriteTrace;
			bool w2 = EcPortIo.TryWriteAcpiFanTarget(EcPortIo.AcpiFan2TargetAddress, fan2Units, out int rb2);
			string t2 = EcPortIo.LastAcpiWriteTrace;
			detail = "ACPI 0xC83C=" + fan1Units + "（回读 " + (rb1 < 0 ? "读不到" : rb1.ToString()) + " · " + t1 + "）"
				+ " · 0xC83D=" + fan2Units + "（回读 " + (rb2 < 0 ? "读不到" : rb2.ToString()) + " · " + t2 + "）";
			return w1 && w2;
		}
		bool l1 = SmuEcAccess.TryWriteEc(0xC83C, (byte)fan1Units, out string d1);
		bool l2 = SmuEcAccess.TryWriteEc(0xC83D, (byte)fan2Units, out string d2);
		detail = "LPC 0xC83C=" + fan1Units + "（" + d1 + "）· 0xC83D=" + fan2Units + "（" + d2 + "）";
		return l1 && l2;
	}

	private static System.Collections.Generic.IEnumerable<FanWriteChannel> Candidates()
	{
		if (_preferred == FanWriteChannel.AcpiEc)
		{
			yield return FanWriteChannel.AcpiEc;
			yield return FanWriteChannel.SmuEcLpc;
		}
		else if (_preferred == FanWriteChannel.SmuEcLpc)
		{
			yield return FanWriteChannel.SmuEcLpc;
			yield return FanWriteChannel.AcpiEc;
		}
		else
		{
			yield return FanWriteChannel.AcpiEc;
			yield return FanWriteChannel.SmuEcLpc;
		}
	}

	internal static string ChannelName(FanWriteChannel channel)
	{
		return channel switch
		{
			FanWriteChannel.AcpiEc => "ACPI-EC",
			FanWriteChannel.SmuEcLpc => "SMU-EC(=LPC)",
			_ => "None"
		};
	}
}

/// <summary>
/// ⭐⭐ 闸门配置：`%APPDATA%\MechrevoLite\fan_ec_verified.cfg` ✅。
/// <para>⭐ 默认无文件 ⇒ 未验证 ⇒ UI 隐藏；只有 `--fan-smu-ec-test` 验证通过才写 `fan_ec_verified_30=1`/`_40=1`（或通用 `fan_ec_verified=1`）✅</para>
/// </summary>
internal static class FanEcVerifyStore
{
	// ⭐ 进程内缓存（⭐ 属性/闭环每 tick 都会查闸门 ⇒ ⛔ 不能每次读文件）✅
	private static bool? _cached30;

	private static bool? _cached40;

	private static string ConfigPath()
	{
		return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
			"MechrevoLite", "fan_ec_verified.cfg");
	}

	/// <summary>⭐ 读“已验证”标记（默认 false；文件不存在/读失败都按 false；进程内缓存）✅</summary>
	internal static bool Load(bool is30Series)
	{
		bool? cached = is30Series ? _cached30 : _cached40;
		if (cached.HasValue)
		{
			return cached.Value;
		}
		bool result = false;
		try
		{
			string path = ConfigPath();
			if (!File.Exists(path))
			{
				result = false;
			}
			else
			{
				foreach (string raw in File.ReadAllLines(path))
				{
					string line = raw.Trim();
					if (line.Length == 0 || line.StartsWith('#'))
					{
						continue;
					}
					int eq = line.IndexOf('=');
					if (eq <= 0)
					{
						continue;
					}
					string key = line.Substring(0, eq).Trim();
					string value = line.Substring(eq + 1).Trim();
					if (value != "1")
					{
						continue;
					}
					if (key == "fan_ec_verified")
					{
						result = true;
						break;
					}
					if (is30Series && key == "fan_ec_verified_30")
					{
						result = true;
						break;
					}
					if (!is30Series && key == "fan_ec_verified_40")
					{
						result = true;
						break;
					}
				}
			}
		}
		catch
		{
			result = false;
		}
		if (is30Series)
		{
			_cached30 = result;
		}
		else
		{
			_cached40 = result;
		}
		return result;
	}

	/// <summary>⭐ 写“已验证”标记（仅验证命令调用；⛔ 产品运行期不写）✅</summary>
	internal static bool Save(bool is30Series, bool verified, out string detail)
	{
		detail = "";
		try
		{
			string path = ConfigPath();
			string dir = Path.GetDirectoryName(path) ?? "";
			if (dir.Length > 0)
			{
				Directory.CreateDirectory(dir);
			}
			string key = is30Series ? "fan_ec_verified_30" : "fan_ec_verified_40";
			File.WriteAllText(path,
				"# L-Mechrevo fan EC verification (written by --fan-smu-ec-test only)\n"
				+ key + "=" + (verified ? "1" : "0") + "\n");
			// ⭐ 更新进程内缓存（保存后当前进程立即可见；UI 仍需重启才重新读闸门）✅
			if (is30Series)
			{
				_cached30 = verified;
			}
			else
			{
				_cached40 = verified;
			}
			detail = path + " ⇒ " + key + "=" + (verified ? "1" : "0");
			return true;
		}
		catch (Exception ex)
		{
			detail = "写配置失败：" + ex.GetType().Name + " " + ex.Message;
			return false;
		}
	}
}
