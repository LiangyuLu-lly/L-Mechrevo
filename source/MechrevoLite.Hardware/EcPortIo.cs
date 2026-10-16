using System;
using System.IO;
using System.Reflection;
using System.Threading;
using PawnIO;

namespace MechrevoLite.Hardware;

/// <summary>
/// ACPI EC 端口 I/O 通道（经 PawnIO 的 <c>LpcACPIEC</c> 模块）。
///
/// <para><b>为什么需要它</b>：既有的 <c>\\.\ACPIDriver</c>（<c>UWACPIDriver.sys</c>）通道按文档只覆盖
/// <b>H2RAM 0x700–0x7FF</b>，机制上到不了 <b>XRAM</b>（EC 版本 <c>0x8410~0x8413</c>、
/// 限充阈值 <c>0x8153/0x8154</c> 都在 XRAM）。实测也印证了这一点：该通道对窗口外的地址
/// <b>不报错、而是回 0xFF 填充值</b>（见诊断段 ②b 的实测记录）。</para>
///
/// <para><b>为什么是 PawnIO</b>：用户态无权执行端口 I/O 指令，必须由内核组件代劳。
/// <c>WinRing0</c>（签名 2007–2014 已过期 + 在微软易受攻击驱动列表里 + 要求用户关内存完整性）
/// 与 <c>inpoutx64</c>（许可只自由用于个人/非商业）都<b>不可随包分发</b>；
/// PawnIO 的 <c>LpcACPIEC</c> 模块是 LGPL-2.1-or-later，<b>可分发</b>，
/// 且该模块本身<b>只放行端口 0x62/0x66</b>（超出即 STATUS_ACCESS_DENIED）。</para>
///
/// <para><b>安全约束</b>（照开源实现的保守范式，逐条落实）：
/// ① 端口白名单（只 0x62/0x66，与模块一致，双重校验）
/// ② EC 命令白名单（只放已确认的 0x92/0x93）
/// ③ <b>Poisoned 机制</b>：任何一次超时或异常即标记中毒，后续<b>拒绝一切 I/O</b>，杜绝盲目重试
/// ④ 单实例互斥（<c>Global\Access_EC</c>，best-effort）
/// ⑤ <b>惰性装载</b>（启动时不装载，只在真的要读 XRAM 时才装载）
/// ⑥ 全程 try/catch，绝不把异常抛给调用方。</para>
///
/// <para>⚠️ 本类目前<b>只用于诊断只读</b>。<see cref="TryWriteByte"/> 虽已实现白名单与中毒保护，
/// 但<b>尚无任何调用方</b>，限充/风扇等写入路径未接入（限充按用户指示暂缓）。</para>
/// </summary>
internal static class EcPortIo
{
	// ── ACPI EC 端口角色（IT5570 / ACPI 定义；与开源实现及厂商控制台一致）──
	private const ushort PortData = 0x62;   // 数据

	private const ushort PortStatus = 0x66; // 状态/命令

	// ── EC 命令白名单（只放已确认的命令）──
	private const byte EcCmdRead = 0x92;

	private const byte EcCmdWrite = 0x93;

	// ── 端口 0x66 的状态位 ──
	private const byte StatusObf = 1;  // Output Buffer Full：有数据可读

	private const byte StatusIbf = 2;  // Input Buffer Full：EC 尚未取走我们写入的字节

	internal const int TimeoutMs = 250; // 照开源实现（250ms 超时、不做 flush/retry）

	private const string ModuleResourceName = "LMechrevo.LpcACPIEC.bin";

	private const string MutexName = "Global\\Access_EC";

	private static readonly object Gate = new object();

	private static PawnIOWrapper _pawn;

	private static bool _loadAttempted;

	private static string _loadFailure = "";

	private static bool _poisoned;

	private static string _poisonReason = "";

	private static int _ioCount;

	// ── 读失败的分级处理（2026-10-16 修正）──
	//  ⚠️ 原实现「**任何**超时/异常都立即全局中毒」经**真机实测暴露缺陷** ⚠️：
	//    本机 ②d 实测（诊断包 …-233138）：0x8410/0x8411/0x8412 **三字节成功**（0xAE/0x13/0x00，
	//    值各不相同 ⇒ ⭐ 端口通道机制**确实在工作** ✅），而第 4 字节 0x8413 等 OBF 超时
	//    ⇒ ⭐ 立刻全局中毒 ⇒ ⭐ 后面 SOC / 模式两条**最有价值的交叉校验全部丢失** ⚠️。
	//  ⇒ ⭐ 代价过大：**读是安全操作**（Poisoned 机制的初衷是防「盲目重试**写**」造成损害）✅
	//  ⇒ 改为**分级**：
	//    ① 读路径**软失败**（超时 / Execute 返回 false）⇒ ⭐ 只标记该地址失败、**继续读后面的** ✅，
	//       并累计「连续读失败」；达到 <see cref="MaxConsecutiveReadFailures"/> 才升级为全局中毒
	//       （⭐ 防止通道真坏时无休止地试 ✅）
	//    ② 写路径失败 / 白名单违规 / 异常 ⇒ ⭐ **仍立即中毒**（保守，护栏不动）⚠️
	internal const int MaxConsecutiveReadFailures = 3;

	// ── 事务间隔（⭐ 机制保留、**当前置 0 未启用** —— 见下方 A/B 实测结论）──
	//  ⚠️ 曾据 ACPI 规范（`ACPI_EC_DELAY`）猜测「背靠背连发导致命令被丢弃」，
	//    但 2026-10-16 **A/B 实测否定了这个猜测** ⚠️：
	//      · delay=5ms：4 字节全读到 ✅（1 次）
	//      · delay=0ms：4 字节全读到 ✅（2 次）、第 4 字节超时 ⛔（1 次）
	//    ⇒ ⭐ **加不加延迟都会间歇性超时** ⇒ ⭐ 延迟**不是**该超时的成因、也**不是**有效缓解 ⚠️
	//  ⭐ 更关键的旁证：超时那一刻 `0x66` 的状态字节 = `0x00`（OBF=0 且 IBF=0）
	//    ⇒ ⭐ **EC 已取走我们写入的字节（IBF 已清），只是没给出数据字节（OBF 从未置位）**
	//    ⇒ ⭐ 这是「**EC 未在 250ms 内应答**」的签名，⛔ 不是「命令被丢弃」的签名 ⚠️
	//  ⇒ 故**不采用**延迟缓解（⛔ 不引入无依据的等待）；
	//    ⭐ 真正的保障是**分级中毒**：即使超时，⭐ 交叉校验也已先拿到、且不会作废整节 ✅
	//  ⚠️ 若在蛟龙真机上仍复现，候选方向是**放长 <see cref="TimeoutMs"/>**（250→500ms），
	//    或该地址本就未实现 —— 两者都要**真机再加证据**，⛔ 不在这里猜 ⚠️
	internal const int InterTransactionDelayMs = 0;

	private static int _consecutiveReadFailures;

	private static string _lastReadFailure = "";

	private static string _pendingSoftFailure = "";

	private static int _lastStatusSeen = -1;

	/// <summary>是否已中毒。中毒后拒绝一切 I/O（<b>失败不自动恢复</b>，保留现场）。</summary>
	internal static bool IsPoisoned
	{
		get
		{
			lock (Gate)
			{
				return _poisoned;
			}
		}
	}

	/// <summary>供诊断：通道可用性的一句话描述（不含任何隐私）。</summary>
	internal static string DescribeChannel()
	{
		lock (Gate)
		{
			if (_poisoned)
			{
				return "⛔ 已中毒 ⇒ 后续拒绝一切 I/O（原因：" + _poisonReason + "）";
			}
			if (!_loadAttempted)
			{
				return "· 尚未尝试（惰性装载：只在真要读 XRAM 时才装载模块）";
			}
			if (_pawn == null || !_pawn.IsModuleLoaded)
			{
				return "⛔ 模块未装载（" + _loadFailure + "）";
			}
			string text = "✅ 模块已装载（LpcACPIEC · 端口白名单仅 0x62/0x66）· 已发端口 I/O " + _ioCount + " 次";
			if (_consecutiveReadFailures > 0)
			{
				text = text + " · ⚠️ 当前已**连续读失败** " + _consecutiveReadFailures + "/" + MaxConsecutiveReadFailures
					+ " 次（⭐ 达到 " + MaxConsecutiveReadFailures + " 次才升级为全局中毒 ⇒ ⭐ 读到 " + MaxConsecutiveReadFailures
					+ " 次仍失败说明通道确实坏了；未达上限说明只是**个别地址**不响应 ✅）";
			}
			return text;
		}
	}

	/// <summary>供诊断：PawnIO 驱动本身是否已安装（不装载模块、不发端口 I/O，代价极低）。</summary>
	internal static string DescribePawnInstalled()
	{
		try
		{
			using PawnIOWrapper probe = new PawnIOWrapper();
			return probe.Connect() switch
			{
				PawnIOWrapper.ConnectResult.OK => "✅ 已安装（设备可打开）",
				PawnIOWrapper.ConnectResult.NotInstalled => "⛔ 未安装（需用户**显式点击**安装，见 Install-PawnIO.ps1）",
				PawnIOWrapper.ConnectResult.AccessDenied => "⚠️ 已安装但拒绝访问（Win32 error 5 —— 需以管理员运行）",
				_ => "⚠️ 连接失败：其它错误",
			};
		}
		catch (Exception ex)
		{
			return "⚠️ 检测失败：" + ex.GetType().Name;
		}
	}

	/// <summary>
	/// 只读：读 XRAM 一个字节。
	/// <para>任何失败都返回 false，并按需标记中毒（超时/异常）。<b>绝不抛出</b>。</para>
	/// </summary>
	internal static bool TryReadByte(int address, out int value)
	{
		value = -1;
		lock (Gate)
		{
			if (_poisoned)
			{
				// ⭐ 让诊断能拿到「为什么读不到」——已中毒时后续每读都拒，如实落因 ✅
				_lastReadFailure = "通道已中毒：" + _poisonReason;
				return false;
			}
			if (!EnsureModule())
			{
				// ⭐ 同上：模块未装载时后续每读必失败，把装载失败原因落到 _lastReadFailure ✅
				_lastReadFailure = "模块未装载：" + _loadFailure;
				return false;
			}
			if (!TryEnterEcMutex(out Mutex mutex, out bool held))
			{
				Poison("无法获取 " + MutexName + " 互斥体");
				return false;
			}
			try
			{
				bool ok = ReadByteCore(address, out value);
				// ⭐ 分级收尾：软失败只累计「连续读失败」计数（原因已由等待/端口层记入 _pendingSoftFailure）；
				//    ⚠️ 硬失败（白名单违规 / 异常 / 写路径）已直接中毒，此处不会再计数 ✅
				NoteReadOutcome(ok);
				return ok;
			}
			catch (Exception ex)
			{
				// ⚠️ 异常仍**立即**中毒（保守，护栏不动）
				Poison("读 0x" + address.ToString("X4") + " 抛异常：" + ex.GetType().Name);
				return false;
			}
			finally
			{
				ReleaseEcMutex(mutex, held);
			}
		}
	}

	/// <summary>只读：连续读若干字节（逐字节调用 <see cref="TryReadByte"/> 的时序）。</summary>
	internal static bool TryReadBytes(int address, int count, out int[] values)
	{
		values = new int[count];
		bool allOk = true;
		for (int i = 0; i < count; i++)
		{
			if (!TryReadByte(address + i, out int v))
			{
				values[i] = -1;
				allOk = false;
			}
			else
			{
				values[i] = v;
			}
		}
		return allOk;
	}

	/// <summary>
	/// 写 XRAM 一个字节。
	/// <para>⚠️ <b>目前无调用方</b>：限充按用户指示暂缓，故此路径尚未接入任何功能。
	/// 保留它是为了协议完整性与将来的显式接入；它同样受端口白名单与中毒机制约束。</para>
	/// </summary>
	internal static bool TryWriteByte(int address, int value)
	{
		lock (Gate)
		{
			if (_poisoned)
			{
				return false;
			}
			if (!EnsureModule())
			{
				return false;
			}
			if (!TryEnterEcMutex(out Mutex mutex, out bool held))
			{
				Poison("无法获取 " + MutexName + " 互斥体");
				return false;
			}
			try
			{
				return WriteByteCore(address, value);
			}
			catch (Exception ex)
			{
				Poison("写 0x" + address.ToString("X4") + " 抛异常：" + ex.GetType().Name);
				return false;
			}
			finally
			{
				ReleaseEcMutex(mutex, held);
			}
		}
	}

	// ══════════════════════════════════════════════════════════════════
	//  EC mailbox 时序（调用方必须已持锁 + 已确保模块 + 已取互斥体）
	//
	//  读：向 0x66 发 0x92 → 经 0x62 依次传「地址高字节、地址低字节」→ 从 0x62 读数据
	//  写：向 0x66 发 0x93 → 经 0x62 传「地址高字节、地址低字节、数据」
	//  （协议事实取自开源实现的实测结论；只取时序，未抄任何代码）
	// ══════════════════════════════════════════════════════════════════

	private static bool ReadByteCore(int address, out int value)
	{
		value = -1;
		SettleBeforeTransaction();
		if (!WriteCommand(EcCmdRead, IoKind.Read))
		{
			return false;
		}
		if (!WriteData((byte)((address >> 8) & 0xFF), IoKind.Read))
		{
			return false;
		}
		if (!WriteData((byte)(address & 0xFF), IoKind.Read))
		{
			return false;
		}
		if (!ReadData(out byte data))
		{
			return false;
		}
		value = data;
		return true;
	}

	private static bool WriteByteCore(int address, int value)
	{
		SettleBeforeTransaction();
		if (!WriteCommand(EcCmdWrite, IoKind.Write))
		{
			return false;
		}
		if (!WriteData((byte)((address >> 8) & 0xFF), IoKind.Write))
		{
			return false;
		}
		if (!WriteData((byte)(address & 0xFF), IoKind.Write))
		{
			return false;
		}
		return WriteData((byte)(value & 0xFF), IoKind.Write);
	}

	/// <summary>向 0x66 发命令（先等 IBF 清空）。<paramref name="command"/> 受命令白名单约束。</summary>
	private static bool WriteCommand(byte command, IoKind kind)
	{
		if (command != EcCmdRead && command != EcCmdWrite)
		{
			Poison("命令 0x" + command.ToString("X2") + " 不在白名单（只允许 0x92/0x93）");
			return false;
		}
		if (!WaitIbfClear(kind))
		{
			return false;
		}
		return PortOut(PortStatus, command, kind);
	}

	/// <summary>向 0x62 写一个数据字节（先等 IBF 清空）。</summary>
	private static bool WriteData(byte data, IoKind kind)
	{
		if (!WaitIbfClear(kind))
		{
			return false;
		}
		return PortOut(PortData, data, kind);
	}

	/// <summary>从 0x62 读一个数据字节（先等 OBF 置位）。</summary>
	private static bool ReadData(out byte data)
	{
		data = 0;
		if (!WaitObfSet(IoKind.Read))
		{
			return false;
		}
		return PortIn(PortData, out data, IoKind.Read);
	}

	/// <summary>轮询 0x66 直到 IBF（bit1）为 0，即 EC 已取走我们上一次写入的字节。</summary>
	private static bool WaitIbfClear(IoKind kind)
	{
		long deadline = Environment.TickCount64 + TimeoutMs;
		while (true)
		{
			if (!PortIn(PortStatus, out byte status, kind))
			{
				return false;
			}
			_lastStatusSeen = status;
			if ((status & StatusIbf) == 0)
			{
				return true;
			}
			if (Environment.TickCount64 >= deadline)
			{
				ReportSoftFailure(kind, "等 IBF 清空超时（" + TimeoutMs + "ms · 最后状态 = 0x"
					+ status.ToString("X2") + " ⇒ EC 未取走我们写入的字节）");
				return false;
			}
			Thread.Sleep(1);
		}
	}

	/// <summary>轮询 0x66 直到 OBF（bit0）为 1，即 EC 已准备好一个可读字节。</summary>
	private static bool WaitObfSet(IoKind kind)
	{
		long deadline = Environment.TickCount64 + TimeoutMs;
		while (true)
		{
			if (!PortIn(PortStatus, out byte status, kind))
			{
				return false;
			}
			_lastStatusSeen = status;
			if ((status & StatusObf) != 0)
			{
				return true;
			}
			if (Environment.TickCount64 >= deadline)
			{
				ReportSoftFailure(kind, "等 OBF 置位超时（" + TimeoutMs + "ms · 最后状态 = 0x"
					+ status.ToString("X2") + " ⇒ EC 未给出可读字节）");
				return false;
			}
			Thread.Sleep(1);
		}
	}

	/// <summary>读一个端口字节（白名单双重校验：即便模块已限制，这里再挡一次）。</summary>
	private static bool PortIn(ushort port, out byte value, IoKind kind)
	{
		value = 0;
		if (!IsPortAllowed(port))
		{
			// ⚠️ 白名单违规是**硬失败**（代码缺陷/越界）⇒ 立即中毒，与读写路径无关
			Poison("端口 0x" + port.ToString("X2") + " 不在白名单（只允许 0x62/0x66）");
			return false;
		}
		try
		{
			ulong[] output = new ulong[1];
			if (!_pawn.Execute("ioctl_pio_read", new ulong[1] { port }, output))
			{
				ReportSoftFailure(kind, "端口读 0x" + port.ToString("X2") + " 失败（Execute 返回 false）");
				return false;
			}
			_ioCount++;
			value = (byte)(output[0] & 0xFF);
			return true;
		}
		catch (Exception ex)
		{
			// ⚠️ 异常是**硬失败** ⇒ 立即中毒
			Poison("端口读 0x" + port.ToString("X2") + " 抛异常：" + ex.GetType().Name);
			return false;
		}
	}

	/// <summary>写一个端口字节（白名单双重校验）。</summary>
	private static bool PortOut(ushort port, byte value, IoKind kind)
	{
		if (!IsPortAllowed(port))
		{
			Poison("端口 0x" + port.ToString("X2") + " 不在白名单（只允许 0x62/0x66）");
			return false;
		}
		try
		{
			if (!_pawn.Execute("ioctl_pio_write", new ulong[2]
			{
				port,
				value
			}, null))
			{
				ReportSoftFailure(kind, "端口写 0x" + port.ToString("X2") + " 失败（Execute 返回 false）");
				return false;
			}
			_ioCount++;
			return true;
		}
		catch (Exception ex)
		{
			Poison("端口写 0x" + port.ToString("X2") + " 抛异常：" + ex.GetType().Name);
			return false;
		}
	}

	private static bool IsPortAllowed(ushort port)
	{
		return port == PortData || port == PortStatus;
	}

	// ── 模块惰性装载 ──

	private static bool EnsureModule()
	{
		if (_pawn != null && _pawn.IsModuleLoaded)
		{
			return true;
		}
		if (_loadAttempted)
		{
			return false;
		}
		_loadAttempted = true;
		PawnIOWrapper pawn = null;
		try
		{
			pawn = new PawnIOWrapper();
			PawnIOWrapper.ConnectResult connect = pawn.Connect();
			if (connect != PawnIOWrapper.ConnectResult.OK)
			{
				_loadFailure = connect switch
				{
					PawnIOWrapper.ConnectResult.NotInstalled => "PawnIO 驱动未安装（Win32 error 2/3）",
					PawnIOWrapper.ConnectResult.AccessDenied => "PawnIO 设备拒绝访问（Win32 error 5，需管理员）",
					_ => "PawnIO 连接失败：" + connect,
				};
				pawn.Dispose();
				return false;
			}
			byte[] moduleData = LoadModuleResource();
			if (moduleData == null || moduleData.Length == 0)
			{
				_loadFailure = "内嵌资源 " + ModuleResourceName + " 缺失或为空";
				pawn.Dispose();
				return false;
			}
			if (!pawn.LoadModule(moduleData))
			{
				_loadFailure = "模块装载被拒（LoadModule 返回 false）";
				pawn.Dispose();
				return false;
			}
			_pawn = pawn;
			return true;
		}
		catch (Exception ex)
		{
			_loadFailure = "装载抛异常：" + ex.GetType().Name + "：" + ex.Message;
			pawn?.Dispose();
			return false;
		}
	}

	private static byte[] LoadModuleResource()
	{
		using Stream stream = typeof(EcPortIo).Assembly.GetManifestResourceStream(ModuleResourceName);
		if (stream == null)
		{
			return null;
		}
		using MemoryStream memoryStream = new MemoryStream();
		stream.CopyTo(memoryStream);
		return memoryStream.ToArray();
	}

	// ── 互斥体（best-effort：拿不到也继续，但记录）──

	private static bool TryEnterEcMutex(out Mutex mutex, out bool held)
	{
		mutex = null;
		held = false;
		try
		{
			mutex = new Mutex(initiallyOwned: false, MutexName);
			held = mutex.WaitOne(TimeoutMs);
			if (!held)
			{
				mutex.Dispose();
				mutex = null;
				return false;
			}
			return true;
		}
		catch (Exception)
		{
			// Global\ 命名空间可能因权限而无法创建 —— 不因此否决整条通道
			mutex?.Dispose();
			mutex = null;
			held = false;
			return true;
		}
	}

	private static void ReleaseEcMutex(Mutex mutex, bool held)
	{
		try
		{
			if (held)
			{
				mutex.ReleaseMutex();
			}
			mutex?.Dispose();
		}
		catch (Exception)
		{
		}
	}

	// ── 失败分级 / 供诊断的只读视图（2026-10-16）──

	/// <summary>端口 I/O 所属的**事务类型** —— 决定失败时是「只计数」还是「立即中毒」。</summary>
	private enum IoKind
	{
		/// <summary>读事务（⭐ 安全：软失败只累计计数，达到上限才升级为全局中毒）。</summary>
		Read,

		/// <summary>写事务（⚠️ 危险：任何软失败立即中毒）。</summary>
		Write
	}

	/// <summary>软失败上报：读 ⇒ 只记录原因（由连续失败计数决定是否升级）；写 ⇒ 立即中毒。</summary>
	private static void ReportSoftFailure(IoKind kind, string reason)
	{
		if (kind == IoKind.Write)
		{
			Poison("写路径 · " + reason);
		}
		else
		{
			_pendingSoftFailure = reason;
		}
	}

	/// <summary>读事务收尾：成功清零计数；软失败累计，达到上限才升级为全局中毒。</summary>
	private static void NoteReadOutcome(bool ok)
	{
		if (ok)
		{
			_consecutiveReadFailures = 0;
			_lastReadFailure = "";
			_pendingSoftFailure = "";
			return;
		}
		if (_poisoned)
		{
			// ⚠️ 硬失败（白名单违规 / 异常）已直接中毒 ⇒ 不必再计数
			return;
		}
		_consecutiveReadFailures++;
		_lastReadFailure = ((_pendingSoftFailure.Length > 0) ? _pendingSoftFailure : "读失败（未记录具体原因）");
		_pendingSoftFailure = "";
		if (_consecutiveReadFailures >= MaxConsecutiveReadFailures)
		{
			Poison("连续读失败 " + _consecutiveReadFailures + " 次（最后一次：" + _lastReadFailure + "）");
		}
	}

	/// <summary>
	/// 事务开始前的**可选**间隔（见 <see cref="InterTransactionDelayMs"/> 的说明）。
	/// ⭐ 当前该常量为 0 ⇒ **直接返回、不做任何等待**（2026-10-16 A/B 实测证明该延迟无益）✅
	/// 保留此机制是为了将来在**真机证据**支持下可以只改一个数字重新启用。绝不抛出。
	/// </summary>
	private static void SettleBeforeTransaction()
	{
		if (InterTransactionDelayMs <= 0)
		{
			return;
		}
		try
		{
			Thread.Sleep(InterTransactionDelayMs);
		}
		catch (Exception)
		{
		}
	}

	/// <summary>供诊断：最近一次读失败的原因（成功会清空）⭐ 用于如实标注「哪个地址为何失败」。</summary>
	internal static string LastReadFailure
	{
		get
		{
			lock (Gate)
			{
				return _lastReadFailure;
			}
		}
	}

	/// <summary>供诊断：最近一次在 0x66 上看到的状态字节（-1 = 尚未读过）。</summary>
	internal static int LastStatusSeen
	{
		get
		{
			lock (Gate)
			{
				return _lastStatusSeen;
			}
		}
	}

	// ── 中毒（失败不自动恢复，保留现场）──

	private static void Poison(string reason)
	{
		if (!_poisoned)
		{
			_poisoned = true;
			_poisonReason = reason;
		}
	}
}
