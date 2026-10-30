using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;

namespace MechrevoLite.Hardware;

/// <summary>
/// 星耀14 （P916F-STX，ITE IT5571 EC） 的 PMC2 充电控制，使用 inpoutx64 访问数据端口 0x68 和命令端口 0x6C。
/// 协议依据：
/// https://github.com/YMGPwcca/Mechrevo-Xingyao-14/blob/main/docs/battery-charge-limit.md
/// 电量低于 T1 时充电，处于 T1～T2 时保持，高于 T2 时主动放电。
/// </summary>
internal static class XingyaoPmc2
{
	/// <summary>PMC2 数据端口：发送子命令或阈值，并读取返回数据。</summary>
	private const short PortData = 0x68;

	/// <summary>PMC2 命令与状态端口：写入命令；读取状态，bit0=OBF、bit1=IBF。</summary>
	private const short PortCommandStatus = 0x6C;

	/// <summary>启用及查询命令组，通过 <see cref="PortCommandStatus"/> 发送。</summary>
	/// <remarks>命令示例均使用十六进制。例如 F1 12：向 0x6C 发送 0xF1，再向 0x68 发送查询启用状态的子命令 0x12。</remarks>
	private const byte CommandFamily = 0xF1;

	/// <summary>启用子命令：F1 11 设置 XRAM[0x0D01].bit4。</summary>
	private const byte CmdEnable = 0x11;

	/// <summary>查询启用状态的子命令：F1 12 返回 0 或 1。</summary>
	private const byte CmdReadState = 0x12;

	/// <summary>查询 T1 的子命令：F1 13 读取 XRAM[0x0D13]，表示充电与保持的边界。</summary>
	private const byte CmdReadT1 = 0x13;

	/// <summary>查询 T2 的子命令：F1 14 读取 XRAM[0x0D14]，电量高于此阈值时接通电源仍主动放电。</summary>
	private const byte CmdReadT2 = 0x14;

	/// <summary>独立设置 T1 的命令：向 0x6C 写入 0xF2，再向 0x68 写入阈值，无需发送 F1。</summary>
	/// <remarks>例如 T1=80% 使用 F2 50，两个字节均为十六进制，0x50=80。</remarks>
	private const byte CmdSetT1 = 0xF2;

	/// <summary>独立设置 T2 的命令：向 0x6C 写入 0xF3，再向 0x68 写入阈值。</summary>
	/// <remarks>例如 T2=100% 使用 F3 64，两个字节均为十六进制，0x64=100。</remarks>
	private const byte CmdSetT2 = 0xF3;

	/// <summary>PMC2 状态等待及残留应答清理的超时时限，单位为毫秒。</summary>
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

	private static IntPtr _dll;

	[UnmanagedFunctionPointer(CallingConvention.Winapi)]
	private delegate short Inp32Delegate(short port);

	[UnmanagedFunctionPointer(CallingConvention.Winapi)]
	private delegate void Out32Delegate(short port, short value);

	[UnmanagedFunctionPointer(CallingConvention.Winapi)]
	private delegate int IsInpOutDriverOpenDelegate();

	private static Inp32Delegate _inp32;
	private static Out32Delegate _out32;

	/// <summary>通过注册表及 WMI 提供的主板型号识别 P916F-STX 平台。</summary>
	/// <returns>任一来源的主板型号匹配时返回 <see langword="true"/>。</returns>
	internal static bool IsXingyaoPlatform()
	{
		BoardWhitelist.BoardProductEvidence evidence = BoardWhitelist.GetBoardProductEvidence();
		return MatchesBoard(evidence.RegBaseBoard) || MatchesBoard(evidence.WmiBaseBoard);
	}

	/// <summary>检查主板型号是否为 P916F-STX 或 XINGYAO Series-P916F-STX。</summary>
	/// <param name="board">主板型号；比较时去除两端空白并忽略大小写。</param>
	/// <returns>型号匹配时返回 <see langword="true"/>；空值或其他型号返回 <see langword="false"/>。</returns>
	private static bool MatchesBoard(string board)
	{
		string value = (board ?? string.Empty).Trim();
		return string.Equals(value, "P916F-STX", StringComparison.OrdinalIgnoreCase)
			|| string.Equals(value, "XINGYAO Series-P916F-STX", StringComparison.OrdinalIgnoreCase);
	}

	/// <summary>读取启用状态及 T1、T2；整个读取过程独占本类的 PMC2 通道。</summary>
	/// <returns>EC 实际返回的状态：Enabled 为 0 或 1，T1、T2 为 0～100 的百分比。</returns>
	/// <remarks>调用时完成平台及驱动检查，初始化或通信失败时抛出异常。</remarks>
	internal static (byte Enabled, byte T1, byte T2) ReadState()
	{
		lock (Gate)
		{
			EnsureInitialized();
			return ReadStateCore();
		}
	}

	/// <summary>设置并启用阈值，读取验证；已经达到目标状态时保持现有参数。</summary>
	/// <param name="t1">充电与保持的边界，范围为 0～100，且必须小于或等于 <paramref name="t2"/>。</param>
	/// <param name="t2">保持与主动放电的边界，范围为 0～100。</param>
	/// <returns>经过读取验证的状态，Enabled 为 1，T1、T2 与目标参数一致。</returns>
	/// <exception cref="ArgumentOutOfRangeException">任一阈值超出 0～100。</exception>
	/// <exception cref="ArgumentException"><paramref name="t1"/> 大于 <paramref name="t2"/>。</exception>
	/// <exception cref="InvalidOperationException">驱动不可用、返回状态无效或读取验证失败。</exception>
	/// <exception cref="TimeoutException">PMC2 通信超时。</exception>
	internal static (byte Enabled, byte T1, byte T2) Restore(int t1, int t2)
	{
		// 固件接受 0～100（包含端点），通过数值字节传输，例如 80%=0x50、100%=0x64。
		if (t1 < 0 || t1 > 100)
		{
			throw new ArgumentOutOfRangeException(nameof(t1), t1, "T1 必须处于 0～100。");
		}
		if (t2 < 0 || t2 > 100)
		{
			throw new ArgumentOutOfRangeException(nameof(t2), t2, "T2 必须处于 0～100。");
		}
		// 文档中 T1>T2 的特殊分支仅经静态分析，本接口拒绝该组合。
		if (t1 > t2)
		{
			throw new ArgumentException("T1 必须小于或等于 T2。", nameof(t1));
		}

		lock (Gate)
		{
			EnsureInitialized();
			var state = ReadStateCore();
			// 文档 Persistence：耗尽电池后的状态曾清零；恢复时先比较，再设置、验证、启用、验证。
			if (state.Enabled == 1 && state.T1 == t1 && state.T2 == t2)
			{
				return state;
			}
			if (state.T1 != t1)
			{
				Command(CmdSetT1, (byte)t1, readResponse: true);
			}
			if (state.T2 != t2)
			{
				Command(CmdSetT2, (byte)t2, readResponse: true);
			}
			state = ReadStateCore();
			if (state.T1 != t1 || state.T2 != t2)
			{
				throw new InvalidOperationException($"PMC2 阈值验证失败：目标 {t1}/{t2}，实际 {state.T1}/{state.T2}。");
			}
			if (state.Enabled != 1)
			{
				Command(CommandFamily, CmdEnable, readResponse: false);
			}
			state = ReadStateCore();
			if (state.Enabled != 1 || state.T1 != t1 || state.T2 != t2)
			{
				throw new InvalidOperationException($"PMC2 启用验证失败：目标 1/{t1}/{t2}，实际 {state.Enabled}/{state.T1}/{state.T2}。");
			}
			return state;
		}
	}

	/// <summary>供诊断包读取板型、驱动和充电参数。</summary>
	/// <returns>包含 PMC2 通道和实际充电参数的诊断文本；板型未匹配时返回跳过读取的说明。</returns>
	internal static string Describe()
	{
		StringBuilder sb = new StringBuilder();
		sb.AppendLine("[星耀 P916F-STX PMC2 电池限充]");
		if (!IsXingyaoPlatform())
		{
			sb.AppendLine("板型未匹配 P916F-STX，跳过读取。");
			return sb.ToString();
		}
		var state = ReadState();
		sb.AppendLine("通道：inpoutx64 PMC2，数据端口 0x68，命令/状态端口 0x6C。");
		sb.AppendLine($"PMC2 读数：enabled={state.Enabled} · T1={state.T1}% · T2={state.T2}%");
		sb.AppendLine("电量低于 T1 时充电，处于 T1～T2 时保持，高于 T2 时主动放电。");
		return sb.ToString();
	}

	/// <summary>检查目标平台，加载 inpoutx64 的端口访问函数，并确认驱动已打开。</summary>
	/// <remarks>调用方必须持有 <see cref="Gate"/>；成功初始化后复用已加载的函数。</remarks>
	/// <exception cref="PlatformNotSupportedException">主板型号未匹配 P916F-STX。</exception>
	/// <exception cref="DllNotFoundException">无法找到或加载 inpoutx64.dll。</exception>
	/// <exception cref="EntryPointNotFoundException">DLL 缺少所需导出函数。</exception>
	/// <exception cref="InvalidOperationException">inpoutx64 驱动未打开。</exception>
	private static void EnsureInitialized()
	{
		if (_initialized)
		{
			return;
		}
		if (!IsXingyaoPlatform())
		{
			throw new PlatformNotSupportedException("PMC2 充电控制仅支持星耀14 P916F-STX。");
		}
		if (_dll == IntPtr.Zero)
		{
			_dll = LoadDll();
		}
		_inp32 = Marshal.GetDelegateForFunctionPointer<Inp32Delegate>(NativeLibrary.GetExport(_dll, "Inp32"));
		_out32 = Marshal.GetDelegateForFunctionPointer<Out32Delegate>(NativeLibrary.GetExport(_dll, "Out32"));
		var isOpen = Marshal.GetDelegateForFunctionPointer<IsInpOutDriverOpenDelegate>(NativeLibrary.GetExport(_dll, "IsInpOutDriverOpen"));
		if (isOpen() == 0)
		{
			throw new InvalidOperationException("inpoutx64 驱动未安装或未加载。");
		}
		_initialized = true;
	}

	/// <summary>按配置的目录顺序查找并加载 inpoutx64.dll；目录中未找到时按库名加载。</summary>
	/// <returns>供端口访问函数使用的本机库句柄。</returns>
	/// <exception cref="DllNotFoundException">无法找到或加载 DLL。</exception>
	/// <exception cref="BadImageFormatException">DLL 格式或体系结构不兼容。</exception>
	private static IntPtr LoadDll()
	{
		foreach (string dir in DllSearchDirs)
		{
			string path = Path.Combine(dir, "inpoutx64.dll");
			if (File.Exists(path))
			{
				return NativeLibrary.Load(path);
			}
		}
		return NativeLibrary.Load("inpoutx64.dll");
	}

	/// <summary>依次发送 F1 12、F1 13、F1 14，读取并校验启用状态与两个阈值。</summary>
	/// <returns>Enabled 为 0 或 1，T1、T2 为 0～100 的百分比。</returns>
	/// <remarks>调用方必须完成初始化并持有 <see cref="Gate"/>。</remarks>
	/// <exception cref="InvalidOperationException">EC 返回的启用状态或阈值超出有效范围。</exception>
	/// <exception cref="TimeoutException">PMC2 通信超时。</exception>
	private static (byte Enabled, byte T1, byte T2) ReadStateCore()
	{
		byte enabled = Command(CommandFamily, CmdReadState, readResponse: true).Value;
		if (enabled > 1)
		{
			throw new InvalidOperationException($"PMC2 启用状态无效：{enabled}。");
		}
		byte t1 = Command(CommandFamily, CmdReadT1, readResponse: true).Value;
		if (t1 > 100)
		{
			throw new InvalidOperationException($"PMC2 T1 超出 0～100：{t1}。");
		}
		byte t2 = Command(CommandFamily, CmdReadT2, readResponse: true).Value;
		if (t2 > 100)
		{
			throw new InvalidOperationException($"PMC2 T2 超出 0～100：{t2}。");
		}
		return (enabled, t1, t2);
	}

	/// <summary>执行一次 PMC2 命令通信，清除残留应答后发送命令及参数，并按需读取返回数据。</summary>
	/// <param name="command">发送到命令端口 0x6C 的命令字节，例如 0xF1、0xF2 或 0xF3。</param>
	/// <param name="data">发送到数据端口 0x68 的子命令或阈值字节。</param>
	/// <param name="readResponse">是否等待 OBF 并读取一个应答字节；为 false 时等待 EC 接收参数。</param>
	/// <returns>需要应答时返回读取的字节，否则返回 <see langword="null"/>。</returns>
	/// <remarks>调用方必须完成初始化并持有 <see cref="Gate"/>。</remarks>
	/// <exception cref="TimeoutException">清除残留应答或等待 IBF、OBF 状态超时。</exception>
	private static byte? Command(byte command, byte data, bool readResponse)
	{
		// 设置阈值也会产生应答，每条命令之前清除残留数据。
		Stopwatch drainTimer = Stopwatch.StartNew();
		while ((_inp32(PortCommandStatus) & 0x01) != 0)
		{
			if (drainTimer.ElapsedMilliseconds >= TimeoutMs)
			{
				throw new TimeoutException("PMC2 清除残留应答超时。");
			}
			_inp32(PortData);
		}
		// 文档实测时序：等待 IBF=0，发送命令；再次等待 IBF=0，发送参数。
		WaitStatus(0x02, 0, "发送命令前等待 IBF 清空");
		_out32(PortCommandStatus, command);
		WaitStatus(0x02, 0, "发送参数前等待 IBF 清空");
		_out32(PortData, data);
		if (!readResponse)
		{
			WaitStatus(0x02, 0, "等待参数被 EC 接收");
			return null;
		}
		// OBF=1 表示 EC 已提供返回数据，此时从数据端口读取一个字节。
		WaitStatus(0x01, 0x01, "等待 OBF 应答");
		return (byte)(_inp32(PortData) & 0xFF);
	}

	/// <summary>轮询 PMC2 状态端口，直到指定状态位符合预期或超过等待时限。</summary>
	/// <param name="mask">状态位掩码：0x02 检查 IBF，0x01 检查 OBF。</param>
	/// <param name="expected">应用掩码后的预期值，例如 0 表示对应状态位已清空。</param>
	/// <param name="operation">当前等待操作的说明，用于超时异常消息。</param>
	/// <exception cref="TimeoutException">在 <see cref="TimeoutMs"/> 毫秒内未达到预期状态。</exception>
	private static void WaitStatus(int mask, int expected, string operation)
	{
		Stopwatch stopwatch = Stopwatch.StartNew();
		while (stopwatch.ElapsedMilliseconds < TimeoutMs)
		{
			if ((_inp32(PortCommandStatus) & mask) == expected)
			{
				return;
			}
			Thread.Sleep(1);
		}
		throw new TimeoutException($"PMC2 {operation}超时（{TimeoutMs} ms）。");
	}
}
