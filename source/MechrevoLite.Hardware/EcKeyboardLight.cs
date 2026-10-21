using System;
using System.Runtime.InteropServices;

namespace MechrevoLite.Hardware;

/// <summary>
/// 2026-10-22（无界白键盘 · EC 直控）：白色背光键盘在 EC 的 H2RAM 0x78C 上
/// —— bit1 = 电源开/关，bit4~7 = 亮度档（0~4）。
/// <para>⚠️ 只走 <c>\\.\ACPIDriver</c>（H2RAM 0x700~0x7FF），读-改-写保留其它位；不碰别的寄存器。</para>
/// <para>⚠️ 仅当机型是「无界 + GCU 不报键盘通道（solution=UNKNOWN）」时才由上层调用。</para>
/// </summary>
internal static class EcKeyboardLight
{
	/// <summary>键盘背光寄存器（H2RAM）。</summary>
	internal const int Register = 0x78C;

	private const uint IoctlEcRead = 2621482120u;

	private const uint IoctlEcWrite = 2621482124u;

	private const string DevicePath = "\\\\.\\ACPIDriver";

	private const byte PowerBit = 0x02;

	private const int BrightnessShift = 4;

	private static readonly object Gate = new object();

	[DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
	private static extern nint CreateFile(string name, uint access, uint share, nint security, uint disposition, uint flags, nint template);

	[DllImport("kernel32.dll", SetLastError = true)]
	private static extern bool CloseHandle(nint handle);

	[DllImport("kernel32.dll", SetLastError = true)]
	private static extern bool DeviceIoControl(nint handle, uint code, nint inBuffer, int inSize, nint outBuffer, int outSize, out int returned, nint overlapped);

	/// <summary>通道是否可用（能按读写方式打开 \\.\ACPIDriver）。只读探测，不写寄存器。</summary>
	internal static bool IsChannelAvailable()
	{
		try
		{
			nint handle = CreateFile(DevicePath, 3221225472u, 3u, IntPtr.Zero, 3u, 0u, IntPtr.Zero);
			if (handle == new IntPtr(-1))
			{
				return false;
			}
			CloseHandle(handle);
			return true;
		}
		catch
		{
			return false;
		}
	}

	/// <summary>只读当前寄存器值；读不到返回 -1。</summary>
	internal static int ReadRegister()
	{
		lock (Gate)
		{
			nint handle = CreateFile(DevicePath, 3221225472u, 3u, IntPtr.Zero, 3u, 0u, IntPtr.Zero);
			if (handle == new IntPtr(-1))
			{
				return -1;
			}
			try
			{
				return Read(handle, Register);
			}
			finally
			{
				CloseHandle(handle);
			}
		}
	}

	/// <summary>读当前状态（开/关 + 亮度档 0~4）。</summary>
	internal static bool TryGet(out bool on, out int brightnessLevel)
	{
		on = false;
		brightnessLevel = -1;
		int value = ReadRegister();
		if (value < 0)
		{
			return false;
		}
		on = (value & PowerBit) != 0;
		brightnessLevel = (value >> BrightnessShift) & 0x0F;
		return true;
	}

	/// <summary>开/关灯（读-改-写，保留亮度与其它位）。</summary>
	internal static bool TrySetPower(bool on)
	{
		return TryApply(on, null);
	}

	/// <summary>设亮度档 0~4（读-改-写，保留开关与其它位）。</summary>
	internal static bool TrySetBrightness(int level0to4)
	{
		if (level0to4 < 0 || level0to4 > 4)
		{
			return false;
		}
		return TryApply(null, level0to4);
	}

	/// <summary>同时设开/关与亮度（读-改-写）；只改 bit1 与 bit4~7。</summary>
	internal static bool TryApply(bool? on, int? brightnessLevel0to4)
	{
		if (brightnessLevel0to4.HasValue && (brightnessLevel0to4.Value < 0 || brightnessLevel0to4.Value > 4))
		{
			return false;
		}
		lock (Gate)
		{
			nint handle = CreateFile(DevicePath, 3221225472u, 3u, IntPtr.Zero, 3u, 0u, IntPtr.Zero);
			if (handle == new IntPtr(-1))
			{
				Logger.WriteError("[kbd-ec] 键盘背光：\\\\.\\ACPIDriver 打不开（UWACPIDriver 未装/未运行或权限不足）");
				return false;
			}
			try
			{
				int before = Read(handle, Register);
				if (before < 0)
				{
					Logger.WriteError("[kbd-ec] 键盘背光：读 0x78C 失败（EC 通道不通）");
					return false;
				}
				int value = before;
				if (on.HasValue)
				{
					if (on.Value)
					{
						value |= PowerBit;
					}
					else
					{
						value &= ~PowerBit;
					}
				}
				if (brightnessLevel0to4.HasValue)
				{
					value = (value & ~0xF0) | ((brightnessLevel0to4.Value & 0x0F) << BrightnessShift);
				}
				if (value == before)
				{
					return true;
				}
				bool written = Write(handle, Register, (byte)value);
				int after = Read(handle, Register);
				bool ok = written && after == value;
				Logger.WriteError("[kbd-ec] 键盘背光：0x78C 0x" + before.ToString("X2") + " -> 0x" + value.ToString("X2")
					+ "（开=" + (((value & PowerBit) != 0) ? "是" : "否") + " · 亮度=" + ((value >> BrightnessShift) & 0x0F) + "）"
					+ " ⇒ " + (ok ? "回读一致" : ("未确认（写=" + (written ? "成功" : "失败") + " · 回读=" + (after < 0 ? "读不到" : "0x" + after.ToString("X2")) + "）")));
				return ok;
			}
			finally
			{
				CloseHandle(handle);
			}
		}
	}

	private static int Read(nint handle, int address)
	{
		nint inBuf = Marshal.AllocHGlobal(4);
		nint outBuf = Marshal.AllocHGlobal(16);
		try
		{
			Marshal.WriteInt32(inBuf, 0, address);
			int returned;
			return (DeviceIoControl(handle, IoctlEcRead, inBuf, 4, outBuf, 16, out returned, IntPtr.Zero) && returned > 0) ? Marshal.ReadByte(outBuf) : (-1);
		}
		finally
		{
			Marshal.FreeHGlobal(inBuf);
			Marshal.FreeHGlobal(outBuf);
		}
	}

	private static bool Write(nint handle, int address, byte value)
	{
		nint inBuf = Marshal.AllocHGlobal(8);
		nint outBuf = Marshal.AllocHGlobal(16);
		try
		{
			Marshal.WriteInt32(inBuf, 0, address);
			Marshal.WriteByte(inBuf, 4, value);
			int returned;
			return DeviceIoControl(handle, IoctlEcWrite, inBuf, 5, outBuf, 16, out returned, IntPtr.Zero);
		}
		finally
		{
			Marshal.FreeHGlobal(inBuf);
			Marshal.FreeHGlobal(outBuf);
		}
	}
}
