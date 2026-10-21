using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace MechrevoLite.Hardware;

/// <summary>
/// 无界（8000 系）电池电压修复的<b>内置</b>实现：直接调用本机已安装的 inpoutx64 驱动
/// （P/Invoke <c>Inp32/Out32</c>）访问 IT5571 的 I2EC 接口（0x681 地址高 / 0x682 地址低 / 0x683 数据）。
/// <para>不依赖 Python / JG_BAT 脚本；驱动与 DLL 由用户机器提供（LM 只加载、不分发）。</para>
/// <para>只用于覆盖 EC 辅助老化降额：desired/queued = 17600mV、电流 2816mA、解停充门；停止时恢复原值。</para>
/// </summary>
internal static class I2ecInpOut
{
	private const short PortAddrHigh = 0x681;

	private const short PortAddrLow = 0x682;

	private const short PortData = 0x683;

	private const int ExpectedBaseVoltageMv = 17600;

	private const int ChargeCurrentMa = 2816;

	private const int InputCurrentSourceMa = 2304;

	private const int AddrDesiredVoltage = 0x0522;

	private const int AddrQueuedVoltage = 0x0836;

	private const int AddrDesiredCurrent = 0x0520;

	private const int AddrQueuedCurrent = 0x0834;

	private const int AddrQueuedInputCurrent = 0x083A;

	private const int AddrStopGate = 0x0574;

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

	private static IsInpOutDriverOpenDelegate _isOpen;

	private static CancellationTokenSource _cts;

	private static Task _loop;

	private static int _origDesiredV;

	private static int _origQueuedV;

	private static int _origDesiredI;

	private static int _origQueuedI;

	private static int _origQueuedInputI;

	private static int _origGate;

	private static bool _originalsCaptured;

	private static volatile bool _running;

	internal static bool IsRunning => _running;

	internal static string InitDetail => _initDetail;

	/// <summary>加载 inpoutx64.dll 并确认驱动可用（只读，不写端口）。</summary>
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
				if (!TryGetExport<Inp32Delegate>(_dll, "Inp32", out _inp32)
					|| !TryGetExport<Out32Delegate>(_dll, "Out32", out _out32))
				{
					_initDetail = "inpoutx64.dll 缺少 Inp32/Out32 导出";
					detail = _initDetail;
					return false;
				}
				TryGetExport<IsInpOutDriverOpenDelegate>(_dll, "IsInpOutDriverOpen", out _isOpen);
				if (_isOpen != null)
				{
					int open = 0;
					try
					{
						open = _isOpen();
					}
					catch
					{
						open = 0;
					}
					if (open == 0)
					{
						_initDetail = "inpoutx64 驱动未安装/未加载（IsInpOutDriverOpen=0；需先安装 inpoutx64 签名驱动）";
						detail = _initDetail;
						return false;
					}
				}
				_available = true;
				_initDetail = "inpoutx64 可用（" + _dllPath + "）";
				detail = _initDetail;
				Logger.WriteLine("[battery-fix] " + _initDetail);
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

	private static string _dllPath = string.Empty;

	private static bool TryLoadDll(out IntPtr handle, out string detail)
	{
		string[] names =
		{
			"inpoutx64.dll",
			"inpoutx64"
		};
		foreach (string name in names)
		{
			if (NativeLibrary.TryLoad(name, out handle))
			{
				detail = name;
				_dllPath = name;
				return true;
			}
		}
		foreach (string dir in DllSearchDirs)
		{
			try
			{
				if (string.IsNullOrWhiteSpace(dir))
				{
					continue;
				}
				string full = Path.Combine(dir, "inpoutx64.dll");
				if (!File.Exists(full))
				{
					continue;
				}
				if (NativeLibrary.TryLoad(full, out handle))
				{
					detail = full;
					_dllPath = full;
					return true;
				}
			}
			catch
			{
			}
		}
		handle = IntPtr.Zero;
		detail = "尝试系统目录/程序目录/E:/D:/C:\\JG_BAT 均失败（Windows 不自带该驱动，需要安装 inpoutx64）";
		return false;
	}

	private static bool TryGetExport<T>(IntPtr dll, string name, out T del) where T : Delegate
	{
		del = null;
		try
		{
			if (!NativeLibrary.TryGetExport(dll, name, out IntPtr fn) || fn == IntPtr.Zero)
			{
				return false;
			}
			del = Marshal.GetDelegateForFunctionPointer<T>(fn);
			return true;
		}
		catch
		{
			return false;
		}
	}

	private static bool SelectAddress(int address)
	{
		_out32(PortAddrHigh, (address >> 8) & 0xFF);
		_out32(PortAddrLow, address & 0xFF);
		return true;
	}

	private static int ReadByte(int address)
	{
		SelectAddress(address);
		return _inp32(PortData) & 0xFF;
	}

	private static void WriteByte(int address, int value)
	{
		SelectAddress(address);
		_out32(PortData, value & 0xFF);
	}

	private static int ReadLe16(int address) => ReadByte(address) | (ReadByte(address + 1) << 8);

	private static void WriteLe16(int address, int value)
	{
		WriteByte(address, value & 0xFF);
		WriteByte(address + 1, (value >> 8) & 0xFF);
	}

	private static int ReadCells() => ((ReadByte(0x0491) & 0xC0) == 0xC0) ? 4 : (((ReadByte(0x0491) & 0xC0) == 0x80) ? 3 : 2);

	/// <summary>只读状态文本（内置）。</summary>
	internal static bool TryReadStatus(out string detail)
	{
		detail = string.Empty;
		if (!EnsureInitialized(out string init))
		{
			detail = init;
			return false;
		}
		try
		{
			int mode = ReadByte(0x200D);
			int id = ReadByte(0x0454);
			int baseV = ReadLe16(0x030E);
			int desiredV = ReadLe16(AddrDesiredVoltage);
			int queuedV = ReadLe16(AddrQueuedVoltage);
			int desiredI = ReadLe16(AddrDesiredCurrent);
			int queuedI = ReadLe16(AddrQueuedCurrent);
			int queuedInput = ReadLe16(AddrQueuedInputCurrent);
			int gate = ReadByte(AddrStopGate);
			int soc = ReadByte(0x04AB);
			int packV = ReadLe16(0x0438);
			int current = ReadLe16(0x050A);
			if ((current & 0x8000) != 0)
			{
				current -= 0x10000;
			}
			StringBuilder sb = new StringBuilder();
			sb.AppendLine("[内置] I2EC 可用（inpoutx64）");
			sb.AppendLine(string.Format("0x200D=0x{0:X2}（模式位={1}）· 0x0454=0x{2:X2}（身份=0x{3:X2}）", mode, (mode & 3) == 3, id, id & 0xFD));
			sb.AppendLine("基础电压=" + baseV + "mV · desired=" + desiredV + "mV · queued=" + queuedV + "mV · pack=" + packV + "mV");
			sb.AppendLine("电流：battery=" + current + "mA · desired=" + desiredI + "mA · queued=" + queuedI + "mA · input=" + queuedInput + "mA");
			sb.AppendLine("停充门 0x0574=0x" + gate.ToString("X2") + " · SOC=" + soc + "% · cells=" + ReadCells());
			detail = sb.ToString().TrimEnd();
			return true;
		}
		catch (Exception ex)
		{
			detail = "内置状态读取异常：" + ex.GetType().Name + " " + ex.Message;
			return false;
		}
	}

	/// <summary>启动内置覆盖（等价 batteryctl.py hold-full）：周期保持 17600mV/2816mA + 解停充门；停止时恢复原值。</summary>
	internal static bool TryStart(out string detail)
	{
		detail = string.Empty;
		if (!EnsureInitialized(out string init))
		{
			detail = init;
			return false;
		}
		lock (Gate)
		{
			if (_running)
			{
				detail = "内置覆盖已在运行";
				return true;
			}
			try
			{
				int mode = ReadByte(0x200D);
				if ((mode & 0x03) != 0x03)
				{
					detail = "I2EC 当前不是读写模式（0x200D=0x" + mode.ToString("X2") + "）⇒ 不启动";
					return false;
				}
				int baseV = ReadLe16(0x030E);
				int cells = ReadCells();
				if (cells != 4)
				{
					detail = "EC 报告 " + cells + "S ≠ 4S ⇒ 不启动";
					return false;
				}
				if (baseV != ExpectedBaseVoltageMv)
				{
					detail = "基础目标 " + baseV + "mV ≠ 已验证的 " + ExpectedBaseVoltageMv + "mV ⇒ 不启动";
					return false;
				}
				_origDesiredV = ReadLe16(AddrDesiredVoltage);
				_origQueuedV = ReadLe16(AddrQueuedVoltage);
				_origDesiredI = ReadLe16(AddrDesiredCurrent);
				_origQueuedI = ReadLe16(AddrQueuedCurrent);
				_origQueuedInputI = ReadLe16(AddrQueuedInputCurrent);
				_origGate = ReadByte(AddrStopGate);
				_originalsCaptured = true;
				_cts = new CancellationTokenSource();
				CancellationToken token = _cts.Token;
				_running = true;
				_loop = Task.Run(() => LoopAsync(token));
				AppDomain.CurrentDomain.ProcessExit += OnProcessExit;
				detail = "内置覆盖已启动（原值 desiredV=" + _origDesiredV + "/queuedV=" + _origQueuedV
					+ " gate=" + _origGate + "；停止时恢复）";
				Logger.WriteLine("[battery-fix] " + detail);
				return true;
			}
			catch (Exception ex)
			{
				_running = false;
				detail = "启动失败：" + ex.GetType().Name + " " + ex.Message;
				return false;
			}
		}
	}

	private static async Task LoopAsync(CancellationToken token)
	{
		try
		{
			while (!token.IsCancellationRequested)
			{
				try
				{
					WriteLe16(AddrDesiredVoltage, ExpectedBaseVoltageMv);
					WriteLe16(AddrQueuedVoltage, ExpectedBaseVoltageMv);
					WriteLe16(AddrDesiredCurrent, ChargeCurrentMa);
					WriteLe16(AddrQueuedCurrent, ChargeCurrentMa);
					int queuedInput = ReadLe16(AddrQueuedInputCurrent);
					if (queuedInput == InputCurrentSourceMa || queuedInput == ChargeCurrentMa)
					{
						WriteLe16(AddrQueuedInputCurrent, ChargeCurrentMa);
					}
					WriteByte(AddrStopGate, 0);
				}
				catch (Exception ex)
				{
					Logger.WriteError("[battery-fix] 内置覆盖写入异常（继续重试）：" + ex.GetType().Name + " " + ex.Message);
				}
				try
				{
					await Task.Delay(TimeSpan.FromSeconds(2.0), token).ConfigureAwait(continueOnCapturedContext: false);
				}
				catch (OperationCanceledException)
				{
					break;
				}
			}
		}
		finally
		{
			_running = false;
		}
	}

	/// <summary>停止内置覆盖并恢复原值（幂等）。</summary>
	internal static void Stop()
	{
		lock (Gate)
		{
			if (_cts == null && !_originalsCaptured)
			{
				return;
			}
			try
			{
				_cts?.Cancel();
			}
			catch
			{
			}
			try
			{
				_loop?.Wait(TimeSpan.FromSeconds(3.0));
			}
			catch
			{
			}
			_loop = null;
			_cts = null;
			_running = false;
			if (!_originalsCaptured)
			{
				return;
			}
			try
			{
				WriteLe16(AddrDesiredVoltage, _origDesiredV);
				WriteLe16(AddrQueuedVoltage, _origQueuedV);
				WriteLe16(AddrDesiredCurrent, _origDesiredI);
				WriteLe16(AddrQueuedCurrent, _origQueuedI);
				WriteLe16(AddrQueuedInputCurrent, _origQueuedInputI);
				WriteByte(AddrStopGate, _origGate);
				Logger.WriteLine("[battery-fix] 内置覆盖已停止并恢复原值（desiredV=" + _origDesiredV
					+ " / queuedV=" + _origQueuedV + " / gate=" + _origGate + "）");
			}
			catch (Exception ex)
			{
				Logger.WriteError("[battery-fix] 恢复原值失败（已吞掉）：" + ex.GetType().Name + " " + ex.Message);
			}
			_originalsCaptured = false;
		}
	}

	private static void OnProcessExit(object sender, EventArgs e)
	{
		try
		{
			Stop();
		}
		catch
		{
		}
	}
}
