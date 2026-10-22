using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace MechrevoLite.Hardware;

/// <summary>
/// 无界（8000 系）电池电压修复的<b>内置</b>实现：直接调用本机已安装的 inpoutx64 驱动
/// （P/Invoke <c>Inp32/Out32</c>）访问 IT5571 的 I2EC 接口（0x681 地址高 / 0x682 地址低 / 0x683 数据）。
/// <para>不依赖外部脚本；驱动与 DLL 由用户机器提供（LM 只加载、使用安装包自带文件）。</para>
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

	// 2026-10-22（fanctl README 的坑：两个 EC 句柄并发会把地址窗口改掉 ⇒ 静默读错寄存器）：
	// I2EC 是三步端口操作（地址高→地址低→数据），必须**跨进程**串行化。
	// UI 风扇曲线控制器 与 SYSTEM 电池修复常驻任务各一个进程，进程内 lock 挡不住对方，故加全局互斥体。
	// 2026-10-22：跨进程互斥改用**文件锁**（FileShare.None）—— 不需要 ACL 配置，
	// SYSTEM 的电池保持任务与管理员 UI 进程都能访问 %ProgramData%\L-Mechrevo\i2ec.lock。
	private static FileStream _ecLockStream;

	private static string EcLockPath()
	{
		string dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "L-Mechrevo");
		Directory.CreateDirectory(dir);
		return Path.Combine(dir, "i2ec.lock");
	}

	private static bool TryEnterEcMutex(out bool held)
	{
		held = false;
		try
		{
			string path = EcLockPath();
			long deadline = Environment.TickCount64 + 500;
			while (true)
			{
				try
				{
					_ecLockStream = new FileStream(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
					held = true;
					return true;
				}
				catch (IOException)
				{
					// 另一个进程正持有 ⇒ 轮询等待
				}
				catch (UnauthorizedAccessException)
				{
					return false;
				}
				if (Environment.TickCount64 >= deadline)
				{
					return false;
				}
				Thread.Sleep(5);
			}
		}
		catch
		{
			return false;
		}
	}

	private static void ExitEcMutex(bool held)
	{
		if (!held)
		{
			return;
		}
		try
		{
			_ecLockStream?.Dispose();
		}
		catch
		{
		}
		_ecLockStream = null;
	}

	// 2026-10-22（fanctl README 风险提示）：进程被强杀时内存里的“原值”会丢，
	// 下次启动若把已覆盖值当原值，关闭功能就无法恢复。首次写 EC 前把原值落盘，
	// 正常停止/关闭功能时恢复并删除；崩溃/断电后下次启动优先用落盘原值。
	private static string OriginalsPath()
	{
		string dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "L-Mechrevo");
		Directory.CreateDirectory(dir);
		return Path.Combine(dir, "i2ec-originals.txt");
	}

	private static void SaveOriginals()
	{
		try
		{
			string text = "version=3\ndesiredV=" + _origDesiredV + "\nqueuedV=" + _origQueuedV
				+ "\ndesiredI=" + _origDesiredI + "\nqueuedI=" + _origQueuedI
				+ "\nqueuedInputI=" + _origQueuedInputI + "\ngate=" + _origGate + "\n";
			File.WriteAllText(OriginalsPath(), text);
		}
		catch (Exception ex)
		{
			Logger.WriteLine("[battery-fix] 保存 EC 原值失败（已吞掉）：" + ex.Message);
		}
	}

	private static bool TryLoadOriginals()
	{
		try
		{
			string path = OriginalsPath();
			if (!File.Exists(path))
			{
				return false;
			}
			Dictionary<string, int> values = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
			foreach (string line in File.ReadAllLines(path))
			{
				int index = line.IndexOf('=');
				if (index > 0 && int.TryParse(line.Substring(index + 1).Trim(), out int value))
				{
					values[line.Substring(0, index).Trim()] = value;
				}
			}
			if (!(values.ContainsKey("version") && values["version"] >= 3
				&& values.ContainsKey("desiredV") && values.ContainsKey("queuedV") && values.ContainsKey("desiredI")
				&& values.ContainsKey("queuedI") && values.ContainsKey("queuedInputI") && values.ContainsKey("gate")))
			{
				// 旧版（小端）写下的原值不可信 ⇒ 删掉，按当前 EC 重新取。
				DeleteOriginals();
				return false;
			}
			if (values["desiredV"] < MinimumPlausibleVoltageMv || values["desiredV"] > MaximumPlausibleVoltageMv
				|| values["queuedV"] < MinimumPlausibleVoltageMv || values["queuedV"] > MaximumPlausibleVoltageMv)
			{
				DeleteOriginals();
				return false;
			}
			_origDesiredV = values["desiredV"];
			_origQueuedV = values["queuedV"];
			_origDesiredI = values["desiredI"];
			_origQueuedI = values["queuedI"];
			_origQueuedInputI = values["queuedInputI"];
			_origGate = values["gate"];
			_originalsCaptured = true;
			return true;
		}
		catch
		{
			return false;
		}
	}

	private static void DeleteOriginals()
	{
		try
		{
			string path = OriginalsPath();
			if (File.Exists(path))
			{
				File.Delete(path);
			}
		}
		catch
		{
		}
	}

	private static readonly string[] DllSearchDirs =
	{
		AppContext.BaseDirectory,
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

	// 2026-10-22（机主：电池电压修复影响快充）：不压低原厂快充电流，只把偏低的值补到 2816；
	// 输入限流按参考脚本 60s 复查，EC 改成其它 PD 值时退出覆盖，避免和充电协商打架。
	private static int _targetDesiredI;

	private static int _targetQueuedI;

	private static bool _inputOverrideActive;

	private static long _nextInputCheckTick;

	private static volatile bool _running;

	internal static bool IsRunning => _running;

	internal static string InitDetail => _initDetail;

	/// <summary>2026-10-22：驱动刚安装/服务刚启动后允许重新初始化（不清已加载的 DLL）。</summary>
	internal static void ResetInitialization()
	{
		lock (Gate)
		{
			_initialized = false;
			_available = false;
			_initDetail = "(重新初始化)";
		}
	}

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
		detail = "尝试系统目录/程序目录均失败（Windows 不自带该驱动，需要安装 inpoutx64）";
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

	/// <summary>2026-10-22（机主：改电压上限黑屏重启）：只有基础目标 0x030E 是**大端**；desired/queued/current/pack 是**小端**（与电池脚本一致）。
	/// 实测 0x030E 基础目标=0x44C0=17600mV；按小端会读成 0xC044=49220mV（校验误判）。</summary>
	private static int ReadBe16(int address) => (ReadByte(address) << 8) | ReadByte(address + 1);

	private static void WriteBe16(int address, int value)
	{
		WriteByte(address, (value >> 8) & 0xFF);
		WriteByte(address + 1, value & 0xFF);
	}

	private static int ReadLe16(int address) => ReadByte(address) | (ReadByte(address + 1) << 8);

	private static void WriteLe16(int address, int value)
	{
		WriteByte(address, value & 0xFF);
		WriteByte(address + 1, (value >> 8) & 0xFF);
	}

	private const int MinimumPlausibleVoltageMv = 12000;

	private const int MaximumPlausibleVoltageMv = 18000;

	private static int PlausibleVoltage(int value, int fallback) => (value >= MinimumPlausibleVoltageMv && value <= MaximumPlausibleVoltageMv) ? value : fallback;

	private static int PlausibleCurrent(int value, int fallback) => (value >= 0 && value <= 6000) ? value : fallback;

	private static int ReadCells() => ((ReadByte(0x0491) & 0xC0) == 0xC0) ? 4 : (((ReadByte(0x0491) & 0xC0) == 0x80) ? 3 : 2);

	/// <summary>2026-10-22（无界风扇曲线）：I2EC 通道是否可用（inpoutx64 已装且导出齐全）。</summary>
	internal static bool IsAvailable
	{
		get
		{
			return EnsureInitialized(out _);
		}
	}

	/// <summary>2026-10-22（无界风扇曲线）：通用 I2EC 单字节读（走 0x681/0x682/0x683 窗口）。</summary>
	internal static bool TryReadEcByte(int address, out int value, out string detail)
	{
		value = -1;
		if (!EnsureInitialized(out detail))
		{
			return false;
		}
		lock (Gate)
		{
			if (!TryEnterEcMutex(out bool held))
			{
				detail = "I2EC 忙（等待全局互斥体超时）";
				return false;
			}
			try
			{
				value = ReadByte(address);
				detail = "ok";
				return true;
			}
			catch (Exception ex)
			{
				detail = ex.GetType().Name + " " + ex.Message;
				return false;
			}
			finally
			{
				ExitEcMutex(held);
			}
		}
	}

	/// <summary>2026-10-22（无界风扇曲线）：通用 I2EC 单字节写 + **回读确认**（写入后立即读回）。</summary>
	internal static bool TryWriteEcByte(int address, int value, out string detail)
	{
		detail = "";
		if (!EnsureInitialized(out detail))
		{
			return false;
		}
		lock (Gate)
		{
			if (!TryEnterEcMutex(out bool held))
			{
				detail = "I2EC 忙（等待全局互斥体超时）";
				return false;
			}
			try
			{
				WriteByte(address, value & 0xFF);
				int readBack = ReadByte(address);
				if (readBack != (value & 0xFF))
				{
					detail = "回读不一致：写 0x" + (value & 0xFF).ToString("X2") + " / 读 0x" + readBack.ToString("X2");
					return false;
				}
				detail = "ok";
				return true;
			}
			catch (Exception ex)
			{
				detail = ex.GetType().Name + " " + ex.Message;
				return false;
			}
			finally
			{
				ExitEcMutex(held);
			}
		}
	}

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
			int baseV = ReadBe16(0x030E);
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

	/// <summary>启动内置覆盖：周期保持 17600mV/2816mA + 解停充门；停止时恢复原值。</summary>
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
				Logger.WriteLine("[battery-fix] 字节序=base:BE / desired/current:LE（与参考脚本一致）");
				int baseV = ReadBe16(0x030E);
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
				// 2026-10-22（机主：改电压黑屏重启）：不同批次 EC 的 desired/queued 字节序可能不同。
				// 启动前先确认当前 desired/queued 是合理电压；读出来不像电压（如 49.2V）⇒ 拒绝写入，避免过压。
				int currentDesiredCheck = ReadLe16(AddrDesiredVoltage);
				int currentQueuedCheck = ReadLe16(AddrQueuedVoltage);
				if (currentDesiredCheck < MinimumPlausibleVoltageMv || currentDesiredCheck > MaximumPlausibleVoltageMv
					|| currentQueuedCheck < MinimumPlausibleVoltageMv || currentQueuedCheck > MaximumPlausibleVoltageMv)
				{
					// 旧版“全部按大端”写 17600 会在 LE 寄存器里留下 [44,C0]，
					// 按 LE 读成 0xC044=49220；交换回来若落在合理范围，说明是已知旧版错位值，
					// 允许启动并用正确的 LE 17600 覆盖；其它未知值一律拒绝。
					int swappedDesired = ((currentDesiredCheck & 0xFF) << 8) | ((currentDesiredCheck >> 8) & 0xFF);
					int swappedQueued = ((currentQueuedCheck & 0xFF) << 8) | ((currentQueuedCheck >> 8) & 0xFF);
					bool knownCorruption = swappedDesired >= MinimumPlausibleVoltageMv && swappedDesired <= MaximumPlausibleVoltageMv
						&& swappedQueued >= MinimumPlausibleVoltageMv && swappedQueued <= MaximumPlausibleVoltageMv;
					if (!knownCorruption)
					{
						detail = "EC 当前 desired/queued=" + currentDesiredCheck + "/" + currentQueuedCheck
							+ "mV 不在合理范围且不是已知旧版错位 ⇒ 拒绝写入（未改任何寄存器）";
						return false;
					}
					Logger.WriteError("[battery-fix] 检测到旧版字节序遗留值 desiredV=" + currentDesiredCheck + "mV（交换后=" + swappedDesired
						+ "mV）⇒ 允许启动并用正确的 17600mV 覆盖");
				}
					if (!TryLoadOriginals())
					{
						int currentDesiredV = ReadLe16(AddrDesiredVoltage);
						int currentQueuedV = ReadLe16(AddrQueuedVoltage);
						if (currentDesiredV > MaximumPlausibleVoltageMv || currentQueuedV > MaximumPlausibleVoltageMv)
						{
							// 旧版本按小端写 17600 会变成 49220mV（过压），这里明确记录并按 17600 修正。
							Logger.WriteError("[battery-fix] EC 当前目标电压异常（desiredV=" + currentDesiredV + "mV / queuedV=" + currentQueuedV
								+ "mV，疑似旧版字节序错误）⇒ 按 17600mV 修正");
						}
						_origDesiredV = PlausibleVoltage(currentDesiredV, ExpectedBaseVoltageMv);
						_origQueuedV = PlausibleVoltage(currentQueuedV, ExpectedBaseVoltageMv);
						_origDesiredI = PlausibleCurrent(ReadLe16(AddrDesiredCurrent), ChargeCurrentMa);
						_origQueuedI = PlausibleCurrent(ReadLe16(AddrQueuedCurrent), ChargeCurrentMa);
						_origQueuedInputI = PlausibleCurrent(ReadLe16(AddrQueuedInputCurrent), InputCurrentSourceMa);
						_origGate = ReadByte(AddrStopGate);
						_originalsCaptured = true;
						SaveOriginals();
					}
				_targetDesiredI = Math.Clamp(Math.Max(ChargeCurrentMa, _origDesiredI), ChargeCurrentMa, 6000);
				_targetQueuedI = Math.Clamp(Math.Max(ChargeCurrentMa, _origQueuedI), ChargeCurrentMa, 6000);
				_inputOverrideActive = _origQueuedInputI == InputCurrentSourceMa;
				_nextInputCheckTick = Environment.TickCount64 + 60000L;
				Logger.WriteLine("[battery-fix] 充电电流：原厂 desired/queued=" + _origDesiredI + "/" + _origQueuedI
					+ "mA ⇒ 保持不低于 " + ChargeCurrentMa + "mA（实际写 " + _targetDesiredI + "/" + _targetQueuedI + "mA）；输入限流覆盖=" + _inputOverrideActive);
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
					lock (Gate)
					{
						if (!TryEnterEcMutex(out bool held))
						{
							continue;
						}
						try
						{
							WriteLe16(AddrDesiredVoltage, ExpectedBaseVoltageMv);
							WriteLe16(AddrQueuedVoltage, ExpectedBaseVoltageMv);
							WriteLe16(AddrDesiredCurrent, _targetDesiredI);
							WriteLe16(AddrQueuedCurrent, _targetQueuedI);
							int queuedInput = ReadLe16(AddrQueuedInputCurrent);
							long nowTick = Environment.TickCount64;
							if (_inputOverrideActive)
							{
								if (queuedInput == InputCurrentSourceMa || queuedInput == ChargeCurrentMa)
								{
									WriteLe16(AddrQueuedInputCurrent, ChargeCurrentMa);
								}
								else
								{
									// EC/PD 已把输入限流改成别的值 ⇒ 停止覆盖，避免把快充协商顶回去。
									_inputOverrideActive = false;
									_nextInputCheckTick = nowTick + 60000L;
									Logger.WriteLine("[battery-fix] 输入限流=" + queuedInput + "mA（非 2304/2816）⇒ 停止覆盖，60s 后复查");
								}
							}
							else if (nowTick >= _nextInputCheckTick)
							{
								_nextInputCheckTick = nowTick + 60000L;
								if (queuedInput == InputCurrentSourceMa)
								{
									_inputOverrideActive = true;
									WriteLe16(AddrQueuedInputCurrent, ChargeCurrentMa);
								}
							}
							WriteByte(AddrStopGate, 0);
						}
						finally
						{
							ExitEcMutex(held);
						}
					}
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
			if (_cts == null && !_originalsCaptured && !TryLoadOriginals())
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
			if (!_originalsCaptured && !TryLoadOriginals())
			{
				return;
			}
			TryEnterEcMutex(out bool held);
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
				DeleteOriginals();
			}
			catch (Exception ex)
			{
				Logger.WriteError("[battery-fix] 恢复原值失败（已吞掉）：" + ex.GetType().Name + " " + ex.Message);
			}
			finally
			{
				ExitEcMutex(held);
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
