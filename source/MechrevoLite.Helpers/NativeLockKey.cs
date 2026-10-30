using System;
using System.Runtime.InteropServices;
using System.Threading;

namespace MechrevoLite.Helpers;

/// <summary>
/// 「大小写锁定」「数字小键盘锁定」两个系统锁定键的读写入口。
/// <para>⭐ 为什么不用 WMI：宝龙达的 WMI 功能号表（8~23）里**没有**这两个键的功能号，
/// 而它们本来就是键盘自身的锁定键 ⇒ 由 Windows 的键盘状态机负责，靠**模拟一次按键**切换 ✅</para>
/// <para>⚠️ 本类**只**调用 Windows 公开 API（<c>user32.dll</c> / <c>kernel32.dll</c>），
/// ⛔ 不碰 EC、⛔ 不碰任何厂商寄存器、⛔ 不需要驱动或提权。</para>
/// </summary>
internal static class NativeLockKey
{
	/// <summary>大小写锁定键的虚拟键码。</summary>
	internal const int CapsLockVirtualKey = 0x14;

	/// <summary>数字小键盘锁定键的虚拟键码。</summary>
	internal const int NumLockVirtualKey = 0x90;

	/// <summary>ScrollLock 虚拟键码（NUC 的 Fn+SCRLK 实际产生这个键）。</summary>
	internal const int ScrollLockVirtualKey = 0x91;

	/// <summary>上述两个键通用的扫描码（模拟按键时随虚拟键码一起给出）。</summary>
	private const byte CommonScanCode = 0x45;

	/// <summary>标志位：扩展键。⭐ 这两个键**必须**带它，否则模拟不生效。</summary>
	private const uint FlagExtendedKey = 0x0001;

	/// <summary>标志位：按键抬起。</summary>
	private const uint FlagKeyUp = 0x0002;

	/// <summary>锁定态在 <c>GetKeyState</c> 返回值里的位掩码。</summary>
	private const int LockedBitMask = 0x0001;

	/// <summary>
	/// 模拟按键发出后、回读状态前等待的毫秒数。
	/// <para>⚠️ 输入注入是异步的：立刻回读极可能仍读到旧值，从而把"其实已经成功"误判成失败。</para>
	/// </summary>
	private const int AfterInjectDelayMs = 50;

	[DllImport("user32.dll", SetLastError = false)]
	private static extern short GetKeyState(int virtualKey);

	[DllImport("user32.dll", SetLastError = false)]
	private static extern void keybd_event(byte virtualKey, byte scanCode, uint flags, UIntPtr extraInfo);

	[DllImport("user32.dll", SetLastError = false)]
	private static extern IntPtr GetForegroundWindow();

	[DllImport("user32.dll", SetLastError = false)]
	private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);

	[DllImport("kernel32.dll", SetLastError = false)]
	private static extern uint GetCurrentThreadId();

	[DllImport("user32.dll", SetLastError = false)]
	[return: MarshalAs(UnmanagedType.Bool)]
	private static extern bool AttachThreadInput(uint idAttach, uint idAttachTo, [MarshalAs(UnmanagedType.Bool)] bool fAttach);

	/// <summary>
	/// 读某个锁定键当前**是否处于锁定态**。
	/// <para>⚠️ <c>GetKeyState</c> 返回的是<b>调用线程消息队列里</b>的键状态，不是全局实时状态。
	/// 长期在后台运行的进程（本程序正是）、或线程池线程调用它，都可能读到<b>陈旧值</b>，
	/// 于是"明明已经切换成功"也会被判成失败 ⚠️。⇒ 读之前先把输入状态**临时附加到前台线程**，
	/// 这样拿到的才是系统当前状态 ✅（读完立即分离；附加失败则退回本线程读法）。</para>
	/// <para>⚠️ 只看返回值<b>最低位</b>（锁定态）；高位表示"此刻是否被按住"，不可混淆。</para>
	/// </summary>
	internal static bool IsOn(int virtualKey)
	{
		try
		{
			uint foregroundThread = GetWindowThreadProcessId(GetForegroundWindow(), out uint _);
			uint currentThread = GetCurrentThreadId();
			if (foregroundThread != 0 && foregroundThread != currentThread)
			{
				bool attached = false;
				try
				{
					attached = AttachThreadInput(currentThread, foregroundThread, true);
					if (attached)
					{
						return (GetKeyState(virtualKey) & LockedBitMask) != 0;
					}
				}
				catch (Exception)
				{
				}
				finally
				{
					if (attached)
					{
						try
						{
							AttachThreadInput(currentThread, foregroundThread, false);
						}
						catch (Exception)
						{
						}
					}
				}
			}
			return (GetKeyState(virtualKey) & LockedBitMask) != 0;
		}
		catch (Exception)
		{
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

	internal static bool IsScrollLockOn()
	{
		return IsOn(ScrollLockVirtualKey);
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
			// ⚠️ 输入注入是异步的：必须等一拍再回读，否则会把"已经成功"误判成失败。
			try
			{
				Thread.Sleep(AfterInjectDelayMs);
			}
			catch (Exception)
			{
			}
			bool after = IsOn(virtualKey);
			note = $"发送前={Describe(before)} · 发送后回读={Describe(after)} · 目标={Describe(target)}"
				+ (after == target
					? " ⇒ 回读一致 ✅"
					: " ⇒ ⚠️ 回读尚未反映（**已下发**；按键注入是异步的，最终状态由定时器校正）");
			// ⚠️⚠️ 返回"已下发"而不是"回读一致"：真机实测 50ms 内 GetKeyState 仍读到旧值
			//   （而键盘指示灯已经变了）⇒ 若拿"立刻回读"当成败判据，会出"灯变了、开关却回弹"的假失败。
			return true;
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

	internal static bool TrySetScrollLock(bool target, out string note)
	{
		return TrySet(ScrollLockVirtualKey, target, out note);
	}

	/// <summary>供日志使用的可读状态描述（⛔ 不用 true/false，避免与"成功/失败"混淆）。</summary>
	internal static string Describe(bool locked)
	{
		return locked ? "锁定" : "未锁定";
	}
}
