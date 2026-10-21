using System;
using System.Runtime.InteropServices;

namespace MechrevoLite.Hardware;

/// <summary>
/// 2026-10-22（无界白键盘 · EC 直控）：白色背光键盘在 EC 的 H2RAM 0x78C 上。
/// <para>实测（Fn 亮度键循环 0x01 → 0x21 → 0x41 → 0x01，视觉为“关/暗/亮”）：</para>
/// <list type="bullet">
/// <item>bit5~7 = 亮度档：<c>0=关、1=暗、2=亮</c>（只有两档亮度）；</item>
/// <item>bit4 = 写入时置 1 的“亮度更新”标记（写完由固件自清）；</item>
/// <item>低 5 位保留（bit0 常为 1）。</item>
/// </list>
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

	/// <summary>写亮度时固定置 1 的位（固件用它标记“亮度更新”，写完自清）。</summary>
	private const byte BrightnessWriteFlag = 0x10;

	/// <summary>亮度在 bit5~7：0=关、1=暗、2=亮。</summary>
	private const int BrightnessShift = 5;

	private const int MaxBrightnessLevel = 2;

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

	/// <summary>读当前状态（开/关 + 亮度档 0~2）。</summary>
	internal static bool TryGet(out bool on, out int brightnessLevel)
	{
		on = false;
		brightnessLevel = -1;
		int value = ReadRegister();
		if (value < 0)
		{
			return false;
		}
		brightnessLevel = (value >> BrightnessShift) & 0x07;
		on = brightnessLevel > 0;
		return true;
	}

	/// <summary>开/关灯：关=亮度码 0；开=恢复到上次亮度（原来是关则用最亮档）。</summary>
	internal static bool TrySetPower(bool on)
	{
		return TryApply(on, null);
	}

	/// <summary>设亮度档：0=关、1=暗、2=亮（实测只有两档亮度）。</summary>
	internal static bool TrySetBrightness(int level0to2)
	{
		if (level0to2 < 0 || level0to2 > MaxBrightnessLevel)
		{
			return false;
		}
		return TryApply(level0to2 > 0, level0to2);
	}

	/// <summary>同时设开/关与亮度（读-改-写）；只改 bit4~7。</summary>
	internal static bool TryApply(bool? on, int? brightnessLevel0to2)
	{
		if (brightnessLevel0to2.HasValue && (brightnessLevel0to2.Value < 0 || brightnessLevel0to2.Value > MaxBrightnessLevel))
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
				int currentLevel = (before >> BrightnessShift) & 0x07;
				int targetLevel = currentLevel;
				if (on.HasValue)
				{
					if (on.Value)
					{
						targetLevel = (currentLevel > 0) ? currentLevel : MaxBrightnessLevel;
					}
					else
					{
						targetLevel = 0;
					}
				}
				if (brightnessLevel0to2.HasValue)
				{
					targetLevel = brightnessLevel0to2.Value;
				}
				if (targetLevel > MaxBrightnessLevel)
				{
					targetLevel = MaxBrightnessLevel;
				}
				// 与固件写法一致：保留低 5 位、置 bit4、bit5~7 = 亮度档（0=关/1=暗/2=亮）
				int value = (before & 0x1F) | BrightnessWriteFlag | ((targetLevel & 0x07) << BrightnessShift);
				bool written = Write(handle, Register, (byte)value);
				int after = Read(handle, Register);
				int afterLevel = (after < 0) ? (-1) : ((after >> BrightnessShift) & 0x07);
				bool ok = written && afterLevel == targetLevel;
				Logger.WriteError("[kbd-ec] 键盘背光：0x78C 0x" + before.ToString("X2") + " -> 0x" + value.ToString("X2")
					+ "（" + DescribeLevel(targetLevel) + "）"
					+ " ⇒ " + (ok ? "回读一致" : ("未确认（写=" + (written ? "成功" : "失败") + " · 回读档=" + (afterLevel < 0 ? "读不到" : afterLevel.ToString()) + "）")));
				return ok;
			}
			finally
			{
				CloseHandle(handle);
			}
		}
	}

	private static string DescribeLevel(int level)
	{
		return level switch
		{
			0 => "关",
			1 => "暗",
			2 => "亮",
			_ => level.ToString()
		};
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
