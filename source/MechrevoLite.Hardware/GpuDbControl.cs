using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;

namespace MechrevoLite.Hardware;

/// <summary>
/// 40 系显卡 + 50 系 GCU 的 Dynamic Boost 联动：
/// 狂暴档打开 DB（基础 TGP + DB 额度），其它档关闭 DB。
/// <para>只写 DB 相关 EC 地址；写前记录原值，失败按原值回滚；不改其它寄存器。</para>
/// <para>DB 写序列按机型自适应（不同固件的副本/字段不同）：</para>
/// <list type="bullet">
/// <item>0x743：DB 控制位（bit0=DB 功能使能、bit1=DB 使能、bit2=ConfigurableTGP 功能使能）</item>
/// <item>0x746：DB 最大 TGP（编码 0x00/0x05/0x0F/0x19 对应 0/5/15/25W）</item>
/// <item>0x7D5：第二份 DB 最大 TGP（仅当原值是合法编码时才同步写；0xFF 视为未使用）</item>
/// <item>0x744 / 0x745：ConfigurableTGP 值 / DB 总目标（50 系 GCU 语义，两字段暂不写）</item>
/// </list>
/// </summary>
internal static class GpuDbControl
{
	private const int AddrCtrl = 0x743;

	private const int AddrConfigTgp = 0x744;

	private const int AddrDbTarget = 0x745;

	private const int AddrDbMax = 0x746;

	private const int AddrDbTarget2 = 0x7D4;

	private const int AddrDbMax2 = 0x7D5;

	private const uint ReadIoctl = 2621482120u;

	private const uint WriteIoctl = 2621482124u;

	private static readonly int[] DbCodes = { 0x00, 0x05, 0x0F, 0x19 };

	private static readonly int[] DbWatts = { 0, 5, 15, 25 };

	[DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
	private static extern nint CreateFile(string name, uint access, uint share, nint security, uint disposition, uint flags, nint template);

	[DllImport("kernel32.dll", SetLastError = true)]
	private static extern bool CloseHandle(nint handle);

	[DllImport("kernel32.dll", SetLastError = true)]
	private static extern bool DeviceIoControl(nint handle, uint code, nint inBuffer, int inSize, nint outBuffer, int outSize, out int returned, nint overlapped);

	internal static bool Supported()
	{
		MechrevoHw hw = Program.hw;
		if (hw == null || !hw.IsGcuConnected || hw.DgpuGeneration != Gpu.DgpuGenerationKind.Gen40)
		{
			return false;
		}
		return string.Equals(GcuIdentity.Variant, "50", StringComparison.OrdinalIgnoreCase);
	}

	/// <summary>按视觉档位联动：1=狂暴 ⇒ 开 DB；其它 ⇒ 关 DB。</summary>
	internal static void SyncForVisualMode(int visualMode, string reason)
	{
		if (!Supported())
		{
			return;
		}
		bool enable = visualMode == 1;
		if (TryApply(enable, out string detail))
		{
			Logger.WriteError("[db] " + reason + "：视觉档=" + visualMode + " ⇒ DB " + (enable ? "开" : "关") + " · " + detail);
		}
		else
		{
			Logger.WriteError("[db] " + reason + "：视觉档=" + visualMode + " ⇒ DB " + (enable ? "开" : "关") + " 未生效 · " + detail);
		}
	}

	private static bool TryApply(bool enable, out string detail)
	{
		detail = string.Empty;
		nint handle = CreateFile("\\\\.\\ACPIDriver", 0xC0000000u, 3u, 0, 3u, 0u, 0);
		if (handle == new nint(-1))
		{
			detail = "ACPIDriver 打不开";
			return false;
		}
		try
		{
			Dictionary<int, int> original = new Dictionary<int, int>();
			foreach (int addr in new[] { AddrCtrl, AddrDbMax, AddrDbMax2 })
			{
				int value = Read(handle, addr);
				if (value < 0)
				{
					detail = "0x" + addr.ToString("X3") + " 读不到，拒绝写入";
					return false;
				}
				original[addr] = value;
			}
			int ctrl = original[AddrCtrl];
			if (enable)
			{
				int code = ResolveDbMaxCode(original[AddrDbMax]);
				if (!WriteVerified(handle, AddrDbMax, code))
				{
					Restore(handle, original);
					detail = "0x746 写入失败";
					return false;
				}
				if (IsValidDbCode(original[AddrDbMax2]) && original[AddrDbMax2] != code)
				{
					WriteVerified(handle, AddrDbMax2, code);
				}
				// 读-改-写：置 bit2 / bit0 / bit1（保留其它位），等价官方狂暴档的 0x07
				int wanted = (ctrl | 0x07) & 0xFF;
				if (!WriteVerified(handle, AddrCtrl, wanted))
				{
					Restore(handle, original);
					detail = "0x743 写入失败";
					return false;
				}
				int ctrlAfter = Read(handle, AddrCtrl);
				int maxAfter = Read(handle, AddrDbMax);
				bool ok = (ctrlAfter & 0x07) == 0x07;
				detail = "0x743=0x" + ctrlAfter.ToString("X2") + " · 0x746=0x" + maxAfter.ToString("X2")
					+ " · 0x7D5=" + (IsValidDbCode(original[AddrDbMax2]) ? "已同步" : "跳过(0xFF)");
				if (!ok)
				{
					Restore(handle, original);
					detail += " ⇒ 回读不符，已回滚";
				}
				return ok;
			}
			// 关 DB：清 bit1（DB 使能）；保留功能使能位
			int wantedOff = ctrl & 0xFD;
			if (!WriteVerified(handle, AddrCtrl, wantedOff))
			{
				detail = "0x743 写入失败";
				return false;
			}
			int after = Read(handle, AddrCtrl);
			bool okOff = (after & 0x02) == 0;
			detail = "0x743=0x" + after.ToString("X2");
			if (!okOff)
			{
				Restore(handle, original);
				detail += " ⇒ 回读不符，已回滚";
			}
			return okOff;
		}
		finally
		{
			CloseHandle(handle);
		}
	}

	private static void Restore(nint handle, Dictionary<int, int> original)
	{
		foreach (KeyValuePair<int, int> item in original)
		{
			if (item.Value >= 0)
			{
				WriteVerified(handle, item.Key, item.Value);
			}
		}
	}

	private static bool IsValidDbCode(int value)
	{
		foreach (int code in DbCodes)
		{
			if (value == code)
			{
				return true;
			}
		}
		return false;
	}

	private static int ResolveDbMaxCode(int current)
	{
		// 已知机型（GM5HG0A 4060 / 旷世 4090）驱动 Default→Max 差都是 25W ⇒ 用 0x19；
		// 若某机型固件把额度写在别处，需要再补 profile。
		return 0x19;
	}

	private static int Read(nint handle, int address)
	{
		nint inBuf = Marshal.AllocHGlobal(4);
		nint outBuf = Marshal.AllocHGlobal(16);
		try
		{
			Marshal.WriteInt32(inBuf, 0, address);
			if (!DeviceIoControl(handle, ReadIoctl, inBuf, 4, outBuf, 16, out int returned, 0) || returned <= 0)
			{
				return -1;
			}
			return Marshal.ReadByte(outBuf);
		}
		finally
		{
			Marshal.FreeHGlobal(inBuf);
			Marshal.FreeHGlobal(outBuf);
		}
	}

	private static bool WriteVerified(nint handle, int address, int value)
	{
		nint inBuf = Marshal.AllocHGlobal(8);
		nint outBuf = Marshal.AllocHGlobal(16);
		try
		{
			Marshal.WriteInt32(inBuf, 0, address);
			Marshal.WriteByte(inBuf, 4, (byte)value);
			return DeviceIoControl(handle, WriteIoctl, inBuf, 5, outBuf, 16, out int _, 0);
		}
		finally
		{
			Marshal.FreeHGlobal(inBuf);
			Marshal.FreeHGlobal(outBuf);
		}
	}
}
