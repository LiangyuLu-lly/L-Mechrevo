using System;
using System.Runtime.InteropServices;

namespace MechrevoLite.Helpers;

/// <summary>
/// 「大小写锁定」「数字小键盘锁定」两个系统锁定键的读写入口。
/// <para>⭐ 为什么不用 WMI：宝龙达的 WMI 功能号表（8~23）里**没有**这两个键的功能号，
/// 而它们本来是键盘自身的锁定键 ⇒ 由 Windows 的键盘状态机负责，靠**模拟一次按键**即可切换 ✅</para>
/// <para>⚠️ 本类**只**调用 Windows 公开 API（<c>user32.dll</c> 的 <c>GetKeyState</c> / <c>keybd_event</c>），
/// ⛔ 不碰 EC、⛔ 不碰任何厂商寄存器、⛔ 不需要驱动或提权。</para>
/// </summary>
internal static class NativeLockKey
{
	/// <summary>大小写锁定键的虚拟键码。</summary>
	internal const int CapsLockVirtualKey = 0x14;

	/// <summary>数字小键盘锁定键的虚拟键码。</summary>
	internal const int NumLockVirtualKey = 0x90;

	/// <summary>上述两个键通用的扫描码（模拟按键时随虚拟键码一起给出）。</summary>
	private const byte CommonScanCode = 0x45;

	/// <summary>标志位：扩展键。⭐ 这两个键**必须**带它，否则模拟不生效。</summary>
	private const uint FlagExtendedKey = 0x0001;

	/// <summary>标志位：按键抬起。</summary>
	private const uint FlagKeyUp = 0x0002;

	/// <summary>锁定态在 <c>GetKeyState</c> 返回值里的位掩码。</summary>
	private const int LockedBitMask = 0x0001;

	[DllImport("user32.dll", SetLastError = false)]
	private static extern short GetKeyState(int virtualKey);

	[DllImport("user32.dll", SetLastError = false)]
	private static extern void keybd_event(byte virtualKey, byte scanCode, uint flags, UIntPtr extraInfo);

	/// <summary>
	/// 读某个锁定键当前**是否处于锁定态**。
	/// <para>⚠️ <c>GetKeyState</c> 的返回值里：<b>最低位</b> = 锁定态，<b>高位</b> = 此刻是否被按住。
	/// 只看最低位 —— 否则"键正被按住的那一瞬间"会被误判成已锁定 ✅</para>
	/// </summary>
	internal static bool IsOn(int virtualKey)
	{
		try
		{
			return (GetKeyState(virtualKey) & LockedBitMask) != 0;
		}
		catch (Exception)
		{
			// 进程不是交互式会话、或 user32 调用异常时：如实返回"读不到"（false），不抛给调用方。
			return false;
		}
	}

	internal static bool IsCapsLockOn()
	{
		return IsOn(CapsLockVirtualKey);
	}

	internal static bool IsNumLockOn()
	{
		return IsOn(NumLockVirtualKey);
	}

	/// <summary>
	/// 把某个锁定键设成目标态；**已经到位就不发**（幂等）。
	/// <para>⭐ 为什么要幂等：锁定键是"按一次翻转一次"的语义，
	/// 若无条件按一次，界面与真实状态已经一致时反而会把它翻到反面 ❌</para>
	/// </summary>
	/// <param name="virtualKey">虚拟键码。</param>
	/// <param name="target">目标态（true = 锁定）。</param>
	/// <param name="note">结果说明（写日志用）。</param>
	/// <returns>调用后**回读**到的状态是否等于目标态。</returns>
	internal static bool TrySet(int virtualKey, bool target, out string note)
	{
		note = "";
		try
		{
			bool before = IsOn(virtualKey);
			if (before == target)
			{
				note = "当前已是目标态 ⇒ 未发送模拟按键";
				return true;
			}
			// 一次切换 = 按下 + 抬起（带扩展键标志）。
			keybd_event((byte)virtualKey, CommonScanCode, FlagExtendedKey, UIntPtr.Zero);
			keybd_event((byte)virtualKey, CommonScanCode, FlagExtendedKey | FlagKeyUp, UIntPtr.Zero);
			bool after = IsOn(virtualKey);
			note = $"发送前={Describe(before)} · 发送后回读={Describe(after)} · 目标={Describe(target)}";
			return after == target;
		}
		catch (Exception ex)
		{
			note = "模拟按键异常：" + ex.Message;
			return false;
		}
	}

	internal static bool TrySetCapsLock(bool target, out string note)
	{
		return TrySet(CapsLockVirtualKey, target, out note);
	}

	internal static bool TrySetNumLock(bool target, out string note)
	{
		return TrySet(NumLockVirtualKey, target, out note);
	}

	/// <summary>供日志使用的可读状态描述（⛔ 不用 true/false，避免与"成功/失败"混淆）。</summary>
	internal static string Describe(bool locked)
	{
		return locked ? "锁定" : "未锁定";
	}
}
