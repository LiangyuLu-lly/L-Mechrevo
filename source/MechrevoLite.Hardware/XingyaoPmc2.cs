using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;

namespace MechrevoLite.Hardware;

/// <summary>
/// 星耀 14（P916F-STX，ITE IT5571 EC）的 **PMC2 电池限充只读探针**。
///
/// <para><b>依据</b>（参考存档：<c>docs/refs/Mechrevo-Xingyao-14-20261022</c>）：
/// PMC2 数据口 <c>0x68</c>、命令/状态口 <c>0x6C</c>；状态位 bit0=OBF（有回包）、bit1=IBF（主机需等待）。
/// 只读命令：<c>F1 12</c> 读状态、<c>F1 13</c> 读 T1、<c>F1 14</c> 读 T2。
/// 写入命令 <c>F2/F3</c> 与使能 <c>F1 11</c> 属于**写操作**，本类一律不执行。</para>
///
/// <para><b>驱动</b>：I/O 端口访问复用本机已安装的 <c>inpoutx64</c>（与本机无界风扇曲线同一套本地驱动；
/// LM 只加载、不分发）。未安装时只报告不可用。</para>
///
/// <para><b>语义</b>：T1=下限（低于 T1 充电）、T2=上限（高于 T2 主动放电）；仅用于诊断/状态展示，
/// ⛔ 本类不写任何 EC 阈值。</para>
/// </summary>
internal static class XingyaoPmc2
{
	private const short PortData = 0x68;

	private const short PortCommandStatus = 0x6C;

	private const byte CmdReadState = 0x12;

	private const byte CmdReadT1 = 0x13;

	private const byte CmdReadT2 = 0x14;

	private const byte CommandFamily = 0xF1;

	private const int TimeoutMs = 250;

	private static readonly object Gate = new object();

	private static readonly string[] DllSearchDirs =
	{
		AppContext.BaseDirectory,
		@"E:\JG_BAT",
		@"D:\JG_BAT",
		@"C:\JG_BAT",
		Path.Combine(AppContext.BaseDirectory, "JG_BAT"),
		Environment.GetFolderPath(Environment.SpecialFolder.System)
	};

	private static bool _initialized;

	private static bool _available;

	private static string _initDetail = "(未初始化)";

	private static IntPtr _dll;

	[UnmanagedFunctionPointer(CallingConvention.Winapi)]
	private delegate int Inp32Delegate(short port);

	[UnmanagedFunctionPointer(CallingConvention.Winapi)]
	private delegate void Out32Delegate(short port, int value);

	[UnmanagedFunctionPointer(CallingConvention.Winapi)]
	private delegate int IsInpOutDriverOpenDelegate();

	private static Inp32Delegate _inp32;

	private static Out32Delegate _out32;

	/// <summary>是否疑似星耀 14（P916F）平台：GCU 载荷 WJXY / 机型含星耀 / 板号含 P916F。</summary>
	internal static bool IsXingyaoPlatform()
	{
		try
		{
			if (string.Equals(GcuIdentity.Variant, "WJXY", StringComparison.OrdinalIgnoreCase))
			{
				return true;
			}
		}
		catch
		{
		}
		try
		{
			BoardWhitelist.BoardProductEvidence evidence = BoardWhitelist.GetBoardProductEvidence();
			string text = ((evidence.RegSystemProduct ?? string.Empty) + " " + (evidence.WmiModel ?? string.Empty) + " " + (evidence.Product ?? string.Empty) + " " + (evidence.RegBaseBoard ?? string.Empty) + " " + (evidence.WmiBaseBoard ?? string.Empty)).Trim();
			if (text.IndexOf("星耀", StringComparison.OrdinalIgnoreCase) >= 0
				|| text.IndexOf("XINGYAO", StringComparison.OrdinalIgnoreCase) >= 0
				|| text.IndexOf("P916F", StringComparison.OrdinalIgnoreCase) >= 0)
			{
				return true;
			}
		}
		catch
		{
		}
		return false;
	}

	/// <summary>诊断包用：只读输出平台识别、驱动可用性、PMC2 限充状态（enabled/T1/T2）与参考语义。</summary>
	internal static string Describe()
	{
		StringBuilder sb = new StringBuilder();
		sb.AppendLine("[星耀 P916F PMC2 电池限充（只读）]");
		try
		{
			sb.AppendLine("平台识别：" + (IsXingyaoPlatform() ? "命中（GCU 载荷 WJXY / 机型含星耀 / 板号含 P916F）" : "未命中（本机不是星耀 P916F，跳过）"));
			if (!IsXingyaoPlatform())
			{
				return sb.ToString();
			}
			sb.AppendLine("通道计划：① PawnIO ACPI-EC（0x62/0x66 + 0x92 双字节地址）读 XRAM 0x0D01/0x0D13/0x0D14（无需 inpoutx64）；② 兜底 inpoutx64 PMC2 命令 F1 12/13/14");
			// 2026-10-22（机主：没有 inpoutx64 就读不到？）：先走已内嵌的 PawnIO 通道读 XRAM —— 限充阈值就在 EC XRAM 0x0D13/0x0D14。
			bool xramOk = TryReadXramValues(out byte xramEnabled, out byte xramT1, out byte xramT2, out string xramDetail);
			if (xramOk)
			{
				sb.AppendLine($"PawnIO ACPI-EC 读数：enabled={xramEnabled} · T1={xramT1}% · T2={xramT2}%");
				sb.AppendLine("参考语义：SOC<T1 充电 · T1<=SOC<=T2 保持(Not charging) · SOC>T2 主动放电（AC 在线）");
				sb.AppendLine("边界：本探针只读取 XRAM；写 T1/T2/使能尚未实现（PMC2 命令通道未接管，需另做）。");
				sb.AppendLine();
				return sb.ToString();
			}
			sb.AppendLine("PawnIO ACPI-EC 读取失败/不可用：" + xramDetail);
			if (!EnsureInitialized(out string detail))
			{
				sb.AppendLine("驱动：" + detail);
				sb.AppendLine("结论：无法读取（未写任何端口以外的数据；写入阈值仍需驱动 + 实机验证）");
				return sb.ToString();
			}
			sb.AppendLine("驱动：" + detail);
			bool stateOk = TryReadByte(CmdReadState, out byte state, out string stateDetail);
			bool t1Ok = TryReadByte(CmdReadT1, out byte t1, out string t1Detail);
			bool t2Ok = TryReadByte(CmdReadT2, out byte t2, out string t2Detail);
			if (stateOk && t1Ok && t2Ok)
			{
				sb.AppendLine($"PMC2 读数：enabled={state} · T1={t1}% · T2={t2}%");
				sb.AppendLine("参考语义：SOC<T1 充电 · T1<=SOC<=T2 保持(Not charging) · SOC>T2 主动放电（AC 在线）");
			}
			else
			{
				sb.AppendLine("PMC2 读取失败：" + stateDetail + " / " + t1Detail + " / " + t2Detail);
				sb.AppendLine("结论：通道或驱动不可用（未写阈值；不据此判定硬件不支持）");
			}
			sb.AppendLine("边界：本探针只读；写 T1/T2/使能需另行走 owner 批准与实机验证流程，⛔ 不在此处下发。");
		}
		catch (Exception ex)
		{
			sb.AppendLine("PMC2 只读探针异常（已忽略）：" + ex.GetType().Name + " " + ex.Message);
		}
		sb.AppendLine();
		return sb.ToString();
	}

	/// <summary>只读：经 PawnIO ACPI-EC（0x62/0x66 + 命令 0x92 双字节地址）读星耀限充状态/阈值。</summary>
	private static bool TryReadXramValues(out byte enabled, out byte t1, out byte t2, out string detail)
	{
		enabled = 0;
		t1 = 0;
		t2 = 0;
		try
		{
			if (!EcPortIo.TryReadByte(0x0D01, out int state) || state < 0)
			{
				detail = "读 XRAM[0x0D01] 失败（PawnIO/EC 通道不可用）";
				return false;
			}
			enabled = (byte)((state >> 4) & 1);
			if (!EcPortIo.TryReadByte(0x0D13, out int rawT1) || rawT1 < 0 || rawT1 > 100)
			{
				detail = "读 XRAM[0x0D13] 失败或越界（raw=" + rawT1 + "）";
				return false;
			}
			if (!EcPortIo.TryReadByte(0x0D14, out int rawT2) || rawT2 < 0 || rawT2 > 100)
			{
				detail = "读 XRAM[0x0D14] 失败或越界（raw=" + rawT2 + "）";
				return false;
			}
			t1 = (byte)rawT1;
			t2 = (byte)rawT2;
			detail = "ok";
			return true;
		}
		catch (Exception ex)
		{
			detail = ex.GetType().Name + " " + ex.Message;
			return false;
		}
	}

	private static bool EnsureInitialized(out string detail)
	{
		lock (Gate)
		{
			if (_initialized)
			{
				detail = _initDetail;
				return _available;
			}
			_initialized = true;
			try
			{
				if (!TryLoadDll(out _dll, out string loadDetail))
				{
					_initDetail = "inpoutx64.dll 未找到/未安装：" + loadDetail;
					detail = _initDetail;
					return false;
				}
				if (!NativeLibrary.TryGetExport(_dll, "Inp32", out IntPtr inp)
					|| !NativeLibrary.TryGetExport(_dll, "Out32", out IntPtr outp))
				{
					_initDetail = "inpoutx64.dll 缺少 Inp32/Out32 导出";
					detail = _initDetail;
					return false;
				}
				_inp32 = Marshal.GetDelegateForFunctionPointer<Inp32Delegate>(inp);
				_out32 = Marshal.GetDelegateForFunctionPointer<Out32Delegate>(outp);
				if (NativeLibrary.TryGetExport(_dll, "IsInpOutDriverOpen", out IntPtr openPtr))
				{
					IsInpOutDriverOpenDelegate open = Marshal.GetDelegateForFunctionPointer<IsInpOutDriverOpenDelegate>(openPtr);
					if (open() == 0)
					{
						_initDetail = "inpoutx64 驱动未安装/未加载（IsInpOutDriverOpen=0）";
						detail = _initDetail;
						return false;
					}
				}
				_available = true;
				_initDetail = "inpoutx64 可用（本机已安装驱动；LM 只加载、不分发）";
				detail = _initDetail;
				return true;
			}
			catch (Exception ex)
			{
				_initDetail = "初始化异常：" + ex.GetType().Name + " " + ex.Message;
				detail = _initDetail;
				return false;
			}
		}
	}

	private static bool TryLoadDll(out IntPtr handle, out string detail)
	{
		handle = IntPtr.Zero;
		foreach (string name in new[] { "inpoutx64.dll", "inpoutx64" })
		{
			if (NativeLibrary.TryLoad(name, out handle))
			{
				detail = name;
				return true;
			}
		}
		foreach (string dir in DllSearchDirs)
		{
			try
			{
				string path = Path.Combine(dir, "inpoutx64.dll");
				if (File.Exists(path) && NativeLibrary.TryLoad(path, out handle))
				{
					detail = path;
					return true;
				}
			}
			catch
			{
			}
		}
		detail = "已搜索应用目录/JG_BAT/System32 均未找到";
		return false;
	}

	private static bool TryReadByte(byte subCommand, out byte value, out string detail)
	{
		value = 0;
		try
		{
			if (!WaitIbfClear(out detail))
			{
				return false;
			}
			_out32(PortCommandStatus, CommandFamily);
			if (!WaitIbfClear(out detail))
			{
				return false;
			}
			_out32(PortData, subCommand);
			if (!WaitObfSet(out detail))
			{
				return false;
			}
			value = (byte)(_inp32(PortData) & 0xFF);
			detail = "ok";
			return true;
		}
		catch (Exception ex)
		{
			detail = ex.GetType().Name + " " + ex.Message;
			return false;
		}
	}

	private static bool WaitIbfClear(out string detail)
	{
		Stopwatch stopwatch = Stopwatch.StartNew();
		while (stopwatch.ElapsedMilliseconds < TimeoutMs)
		{
			if ((_inp32(PortCommandStatus) & 0x02) == 0)
			{
				detail = "ok";
				return true;
			}
			Thread.Sleep(1);
		}
		detail = "等待 IBF 清空超时";
		return false;
	}

	private static bool WaitObfSet(out string detail)
	{
		Stopwatch stopwatch = Stopwatch.StartNew();
		while (stopwatch.ElapsedMilliseconds < TimeoutMs)
		{
			if ((_inp32(PortCommandStatus) & 0x01) != 0)
			{
				detail = "ok";
				return true;
			}
			Thread.Sleep(1);
		}
		detail = "等待 OBF 置位超时";
		return false;
	}
}
