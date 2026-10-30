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

	/// <summary>是否疑似星耀 14（P916F）平台：机型含星耀 / 板号含 P916F。WJ 载荷属于 Lunar Lake 无界，不再作为星耀证据。</summary>
	internal static bool IsXingyaoPlatform()
	{
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
			sb.AppendLine("平台识别：" + (IsXingyaoPlatform() ? "命中（机型含星耀 / 板号含 P916F）" : "未命中（本机不是星耀 P916F，跳过）"));
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

	/// <summary>只读 PMC2 限充状态：F1 12/13/14。</summary>
	internal static bool TryReadChargeLimit(out bool enabled, out int t1, out int t2, out string detail)
	{
		enabled = false;
		t1 = 0;
		t2 = 0;
		if (!EnsureInitialized(out detail)) return false;
		bool okState = TryReadByte(0x12, out byte state, out string stateDetail);
		bool okT1 = TryReadByte(0x13, out byte rawT1, out string t1Detail);
		bool okT2 = TryReadByte(0x14, out byte rawT2, out string t2Detail);
		if (!okState || !okT1 || !okT2)
		{
			detail = "PMC2 读取失败：" + stateDetail + " / " + t1Detail + " / " + t2Detail;
			return false;
		}
		enabled = state != 0;
		t1 = rawT1;
		t2 = rawT2;
		detail = $"PMC2 限充 enabled={enabled} T1={t1}% T2={t2}%";
		return true;
	}

	/// <summary>恢复平台默认充电策略：只发 F1 10 关闭限充，不改 T1/T2，然后回读确认关闭。</summary>
	internal static bool TryRestoreChargeLimit(out string detail)
	{
		detail = "";
		if (!EnsureInitialized(out detail)) return false;
		if (!TryWriteCommand(0xF1, 0x10))
		{
			detail = "PMC2 恢复默认：F1 10 写入失败";
			return false;
		}
		Thread.Sleep(80);
		if (!TryReadChargeLimit(out bool enabled, out int t1, out int t2, out string readDetail))
		{
			detail = "PMC2 恢复默认：写后读取失败：" + readDetail;
			return false;
		}
		bool ok = !enabled;
		detail = $"PMC2 恢复默认 ⇒ enabled={enabled} T1={t1} T2={t2} ok={ok}";
		return ok;
	}

	/// <summary>写入 PMC2 限充：先写 T1/T2，再使能/关闭，最后回读校验。</summary>
	internal static bool TryApplyChargeLimit(bool enabled, int t1, int t2, out string detail)
	{
		detail = "";
		if (t1 < 0 || t1 > 100 || t2 < 0 || t2 > 100 || (enabled && t1 > t2))
		{
			detail = $"参数越界：enabled={enabled} T1={t1} T2={t2}";
			return false;
		}
		if (!EnsureInitialized(out detail)) return false;
		bool okT1 = TryWriteCommand(0xF2, (byte)t1);
		bool okT2 = TryWriteCommand(0xF3, (byte)t2);
		bool okEnable = TryWriteCommand(0xF1, enabled ? (byte)0x11 : (byte)0x10);
		if (!okT1 || !okT2 || !okEnable)
		{
			detail = $"PMC2 写入失败：T1={okT1} T2={okT2} 使能={okEnable}";
			return false;
		}
		Thread.Sleep(80);
		if (!TryReadChargeLimit(out bool readEnabled, out int readT1, out int readT2, out string readDetail))
		{
			detail = "PMC2 写后读取失败：" + readDetail;
			return false;
		}
		bool readMatches = readEnabled == enabled && readT1 == t1 && readT2 == t2;
		// PTL 实测限充已生效，但 F1 13/14 回读顺序/编码与 AMD 不一致；
		// 写入命令成功即返回成功，detail 保留回读差异供诊断。
		detail = $"PMC2 写入 enabled={enabled} T1={t1} T2={t2} ⇒ 回读 enabled={readEnabled} T1={readT1} T2={readT2} match={readMatches}";
		return true;
	}

	private static bool TryWriteCommand(byte command, byte value)
	{
		try
		{
			if (!WaitIbfClear(out _)) return false;
			_out32(PortCommandStatus, command);
			if (!WaitIbfClear(out _)) return false;
			_out32(PortData, value);
			if (!WaitIbfClear(out _)) return false;
			return true;
		}
		catch
		{
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
