// 2026-10-18（宝龙达 · 风扇接管）：⭐⭐ **SMU-EC 通道封装**（7.3 `RyzenAccess` 等价物）⭐⭐
//
// ⭐⭐ 命令号来源（必须如实记录）：**没有 SMU 命令号**。
//   （实现细节略）
//     · `EC.RW.ECRW_Tool.Fan_Speed`（约 931-947 行）调用
//       `SMUCommands.RyzenAccess.Write_EC_EX(51260/51261, speed)` · `Read_EC_EX(2848)` · `Write_EC_Init()`；
//     · 但 `SMUCommands.RyzenAccess.Write_EC_EX / Read_EC_EX`（约 1567 / 1588 行）的实现是
//       `RyzenNbAccesss.WriteIoPortByte(EC_ADDR_PORT = 0x4E, EC_DATA_PORT = 0x4F)`（`0x2E/0x2F` 命令 +
//       `0x11/0x10/0x12` 索引），即 **LPC 端口 I/O**，⚠️ **不是 SMU 邮箱命令**；
//     · 同文件的 SMU 邮箱命令表（`SMUCommands.commands`，约 999 行起）只有 CPU 侧命令
//       （Curve/Clock/Stapm/Fast/Slow/Thm/CoGfx…），**没有 EC 读/写命令**；
//     · `Write_EC_Init`（约 1555 行）= 读 `0x2000/0x2001`，若 `0x2000 == 0x55` 则读 `0x1060` 并写 `|0x80`。
//    ⇒ ⭐ 结论：7.3 的 "EC_EX" 通道本质就是 LPC 端口 I/O；我们**不发送任何 SMU 命令号**（⛔ 不猜、⛔ 不向 SMU 乱发）。
//      本类按 7.3 的**方法语义**封装 `TryReadEc / TryWriteEc / TryEcInit`，底层复用 `EcLpcIo`
//      （与 7.3 相同的 ITE EC-RAM 端口时序）。若将来找到真正的 SMU-EC 命令号，只需替换本类实现。
//
// ⛔ 安全：只读不写；写入只允许风扇白名单（0xC83C/0xC83D/0xB20/0x1060 解锁位），每次写后回读；
//    其它地址拒绝；⛔ 不抛出异常。

using System;

namespace MechrevoLite.Hardware;

/// <summary>⭐⭐ 7.3 `RyzenAccess` 的 EC 读写封装（实为 LPC 端口 I/O；无 SMU 命令号）✅。</summary>
internal static class SmuEcAccess
{
	/// <summary>⭐ 只读 EC 一个字节（LPC 通道，稳定读 3 次取多数）✅</summary>
	internal static bool TryReadEc(ushort address, out byte value, out string note)
	{
		value = 0;
		note = "";
		if (!EcLpcIo.TryReadByteStable(address, out int read, out note))
		{
			return false;
		}
		value = (byte)(read & 0xFF);
		return true;
	}

	/// <summary>
	/// ⭐ 写 EC 一个字节（严格白名单）：仅 `0xC83C/0xC83D`（0..68）与 `0x1060`（仅 `当前值|0x80`）✅。
	/// <para>⚠️ 掩码 `0xB20` 的位级写请用 <see cref="TryWriteManualMask" />（保留其它位）✅</para>
	/// </summary>
	internal static bool TryWriteEc(ushort address, byte value, out string note)
	{
		note = "";
		if (address == EcLpcIo.Fan1TargetAddress || address == EcLpcIo.Fan2TargetAddress)
		{
			if (value > EcLpcIo.FanTargetMaxUnits100Rpm)
			{
				note = "拒绝：目标档 " + value + " 超出 0..68";
				return false;
			}
			bool ok = EcLpcIo.TryWriteFanTarget(address, value, out int readBack);
			note = "LPC 写 0x" + address.ToString("X4") + "=" + value + " ⇒ 回读=" + (readBack < 0 ? "读不到" : readBack.ToString()) + " · ok=" + ok;
			return ok;
		}
		if (address == EcLpcIo.EcUnlockAddress)
		{
			if (!EcLpcIo.TryReadByteStable(address, out int current, out string readNote))
			{
				note = "拒绝：读 0x1060 失败（" + readNote + "）";
				return false;
			}
			if (value != (current | 0x80))
			{
				note = "拒绝：0x1060 只允许 当前值|0x80（当前 0x" + current.ToString("X2") + "，请求 0x" + value.ToString("X2") + "）";
				return false;
			}
			bool ok = EcLpcIo.TryUnlockEcRam(out _, out _, out string unlockNote);
			note = "LPC 解锁：0x1060 ⇒ " + unlockNote;
			return ok;
		}
		note = "拒绝：地址 0x" + address.ToString("X4") + " 不在 SMU-EC 白名单";
		return false;
	}

	/// <summary>
	/// ⭐⭐ 7.3 `Write_EC_Init` 等价：⭐ 先读 `0x2000`/`0x2001`（芯片 ID1/ID2），
	/// `0x2000 == 0x55` 才读 `0x1060` 并写 `|0x80`（回读确认）✅。
	/// <para>⭐ 7.3 逐步：`EC_CHIP_ID1 = Read_EC_EX(8192)` → `EC_CHIP_ID2 = Read_EC_EX(8193)` →
	/// `if (85 == EC_CHIP_ID1) { EC_CHIP_Ver = Read_EC_EX(4192); EC_CHIP_Ver |= 128; Write_EC_EX(4192, EC_CHIP_Ver); }` ✅</para>
	/// </summary>
	internal static bool TryEcInit(out string note)
	{
		// ⭐ 7.3 先读两个芯片 ID；⭐ 我们身份判据还看 0xC411（EC 版本），多读无害 ✅
		bool id2Ok = EcLpcIo.TryReadByteStable(0x2001, out int id2, out string id2Note);
		string id2Text = "0x2001=" + (id2Ok ? "0x" + id2.ToString("X2") : "读不到") + "（" + id2Note + "）";
		if (!EcLpcIo.TryReadEcIdentity(out _, out _, out string idNote))
		{
			note = "身份未通过 ⇒ ⛔ 不解锁（" + idNote + "）· " + id2Text;
			return false;
		}
		bool ok = EcLpcIo.TryUnlockEcRam(out int before, out int after, out string unlockNote);
		note = "Init：0x1060 " + (before < 0 ? "读不到" : "0x" + before.ToString("X2"))
			+ " → " + (after < 0 ? "读不到" : "0x" + after.ToString("X2")) + " · " + unlockNote + " · " + id2Text;
		return ok;
	}

	/// <summary>⭐ 写 0xB20 接管掩码（只改 bit1/bit3，保留其它位；回读确认）✅</summary>
	internal static bool TryWriteManualMask(bool bit1, bool bit3, out string note)
	{
		bool ok = EcLpcIo.TryWriteFanManualMask(bit1, bit3, out int readBack);
		note = "LPC 写 0xB20（bit1=" + (bit1 ? "1" : "0") + " bit3=" + (bit3 ? "1" : "0")
			+ "）⇒ 回读=0x" + (readBack < 0 ? "读不到" : readBack.ToString("X2")) + " · ok=" + ok;
		return ok;
	}
}
