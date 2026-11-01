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
/// 无界（8000 系）风扇/EC 的内置 I2EC 读写通道：直接调用本机已安装的 inpoutx64 驱动
/// （P/Invoke <c>Inp32/Out32</c>）访问 IT5571 的 I2EC 接口（0x681 地址高 / 0x682 地址低 / 0x683 数据）。
/// </summary>
internal static class I2ecInpOut
{
	/// <summary>2026-10-27（table 后端）：I2EC 序列中的一步写；<paramref name="DelayAfterMs" /> 用于锁存脉冲的 50ms 间隔。</summary>
	internal readonly record struct I2ecWriteStep(int Address, int Value, int DelayAfterMs = 0);

	/// <summary>2026-10-27（table 后端）：序列写完后的回读校验项（地址 + 期望字节）。</summary>
	internal readonly record struct I2ecVerifyStep(int Address, int Expected);

	private const short PortAddrHigh = 0x681;

	private const short PortAddrLow = 0x682;

	private const short PortData = 0x683;

	private static readonly object Gate = new object();

	// 2026-10-22（fanctl README 的坑：两个 EC 句柄并发会把地址窗口改掉 ⇒ 静默读错寄存器）：
	// I2EC 是三步端口操作（地址高→地址低→数据），必须**跨进程**串行化。
	// UI 风扇曲线控制器与其它进程共享 I2EC 时，进程内 lock 挡不住对方，故加全局互斥体。
	// 2026-10-22：跨进程互斥改用**文件锁**（FileShare.None）—— 不需要 ACL 配置，
	// SYSTEM 的电池保持任务与管理员 UI 进程都能访问 %ProgramData%\L-Mechrevo\i2ec.lock。
	private static FileStream _ecLockStream;

	private static string EcLockPath()
	{
		string dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "L-Mechrevo");
		Directory.CreateDirectory(dir);
		return Path.Combine(dir, "i2ec.lock");
	}

	/// <summary>2026-10-23（机主：电池 2ms 循环下风扇曲线掉写）：风扇控制器运行期间写一个跨进程标记，
	/// 电池保持循环看到标记就主动多让路，保证 25Hz 风扇写能拿到 I2EC。</summary>
	private static string FanPriorityPath()
	{
		string dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "L-Mechrevo");
		return Path.Combine(dir, "i2ec.fan");
	}

	internal static void SetFanWritePriority(bool on)
	{
		try
		{
			string path = FanPriorityPath();
			if (on)
			{
				Directory.CreateDirectory(Path.GetDirectoryName(path));
				File.WriteAllText(path, Environment.ProcessId.ToString());
			}
			else
			{
				File.Delete(path);
			}
		}
		catch
		{
		}
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
				Thread.Sleep(1);
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

	internal static string InitDetail => _initDetail;

	/// <summary>给 ACPI EC 通道（0x62/0x66）做后备：只检查 inpoutx64 是否可用。</summary>
	internal static bool CanUsePortIo(out string detail)
	{
		return EnsureInitialized(out detail);
	}

	/// <summary>通用端口读（仅限 ACPI EC 白名单 0x62/0x66 的调用方使用）。</summary>
	internal static bool TryReadPort(short port, out byte value)
	{
		value = 0;
		try
		{
			if (!EnsureInitialized(out _))
			{
				return false;
			}
			value = (byte)(_inp32(port) & 0xFF);
			return true;
		}
		catch
		{
			return false;
		}
	}

	/// <summary>通用端口写（仅限 ACPI EC 白名单 0x62/0x66 的调用方使用）。</summary>
	internal static bool TryWritePort(short port, byte value)
	{
		try
		{
			if (!EnsureInitialized(out _))
			{
				return false;
			}
			_out32(port, value);
			return true;
		}
		catch
		{
			return false;
		}
	}

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
				Logger.WriteLine("[i2ec] " + _initDetail);
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

	/// <summary>2026-10-22（无界风扇曲线）：通用 I2EC 单字节读（走 0x681/0x682/0x683 窗口）。</summary>
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

	/// <summary>
	/// 2026-10-27（task-43 · table 后端）：**整段 I2EC 写序列**（锁一次端口锁 → 连续写 → 可选回读校验）。
	/// <para>⭐ 为什么必须新增：曲线表 16 字节 + 隔离位 + 锁存脉冲（含 50ms 间隔）是**一组不可分割**的操作；
	/// 若逐字节调用 <see cref="TryWriteEcByte" />，每次都会释放跨进程文件锁，别的进程（ACPI 电池驱动/另一实例）
	/// 可能插进“地址高→地址低→数据”三步之间 ⇒ 撕裂写（历史事故：0x0409 被写成 0xA0）。
	/// 本方法在整个序列期间独占进程内 <c>Gate</c> 与跨进程 <c>i2ec.lock</c> ✅。</para>
	/// <para>⚠️ 序列总时长受 <see cref="I2ecWriteStep.DelayAfterMs" /> 控制（锁存脉冲需要 50ms），
	/// 调用方应保持序列短小（本项目只写表前 8 字节）。</para>
	/// </summary>
	internal static bool TryWriteEcSequence(IReadOnlyList<I2ecWriteStep> steps, IReadOnlyList<I2ecVerifyStep> verifySteps, out string detail)
	{
		detail = "";
		if (steps == null || steps.Count == 0)
		{
			detail = "空写序列";
			return false;
		}
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
				foreach (I2ecWriteStep step in steps)
				{
					WriteByte(step.Address, step.Value & 0xFF);
					if (step.DelayAfterMs > 0)
					{
						Thread.Sleep(Math.Min(step.DelayAfterMs, 200));
					}
				}
				if (verifySteps != null)
				{
					foreach (I2ecVerifyStep verify in verifySteps)
					{
						int actual = ReadByte(verify.Address);
						if (actual != (verify.Expected & 0xFF))
						{
							detail = "回读不一致 0x" + verify.Address.ToString("X4") + "：写 0x"
								+ (verify.Expected & 0xFF).ToString("X2") + " / 读 0x" + actual.ToString("X2");
							return false;
						}
					}
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

	/// <summary>启动内置覆盖：周期保持 17600mV/2816mA + 解停充门；停止时恢复原值。</summary>


	/// <summary>停止内置覆盖并恢复原值（幂等）。</summary>

}
