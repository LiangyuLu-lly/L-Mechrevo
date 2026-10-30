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

	// ── ACPI 标准 EC 命令（RD_EC = 0x80 · 单字节地址 · 访问 0x00~0xFF 标准空间）──
	//	⭐ 2026-10-16 新增：EC 的地址空间【不止一套】，此前只实现了厂商 XRAM mailbox ⚠️
	//	   · 0x92/0x93 → XRAM mailbox，用【双字节】地址（0x8150 限充 / 0x8410 EC版本 / 0x847C SOC）
	//	   · 0x80/0x81 → ACPI 标准空间，用【单字节】地址（0x9A 键盘背光 / 0x20 Fn锁 / 0xE4 散热模式）
	//	⭐ 依据（斗战者，同为宝龙达代工）：
	//	   DriverBridge.ReadEc: 0x66←0x80, 0x62←(byte)reg, 读 0x62    ← 单字节
	//	   其 EC 物理窗口 EC_BASE=0xFE800400 / EC_SIZE=0xFF(256) ⇒ 映射的正是这 256 字节标准空间 ✅
	//	⚠️ 这解释了为何此前用 0x92 读键盘背光怎么都不对 —— 地址空间不同 ✅
	//	⚠️ 2026-10-18：机主批准加 **0x81（WR_EC）写实验**（ACPI 标准空间 · 8 位地址）——
	//	   ⭐ 只允许 `reg=0x60`（0x1060 的低字节）做**一次**实验；⛔ 不写风扇寄存器；
	//	   ⭐ 每步等 IBF 清并记录 0x66 状态位（IBF/OBF）✅
	private const byte EcCmdAcpiRead = 0x80;

	/// <summary>⭐ ACPI 标准空间写（WR_EC = 0x81 · 8 位地址）—— 仅供 `--ec81` 一次性实验 ✅</summary>
	private const byte EcCmdAcpiWrite = 0x81;

	// ── 端口 0x66 的状态位 ──
	private const byte StatusObf = 1;  // Output Buffer Full：有数据可读

	private const byte StatusIbf = 2;  // Input Buffer Full：EC 尚未取走我们写入的字节

	internal const int TimeoutMs = 250; // 照开源实现（250ms 超时、不做 flush/retry）

	private const string ModuleResourceName = "LMechrevo.LpcACPIEC.bin";

	private const string MutexName = "Global\\Access_EC";

	private static readonly object Gate = new object();

	private static PawnIOWrapper _pawn;

	private static bool _loadAttempted;

	private static bool _inpoutFallback;

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
	/// ⭐⭐ 2026-10-18（7.3 `Write_EC_Init` 对照结论 · **只读**）：⭐ 读 ACPI EC 状态口 `0x66`（EC_SC）✅。
	/// <para>⭐ 7.3 `RyzenAccess.ECStatus(EC_IBF)` 在每次 `Write_EC_EX/Read_EC_EX` **之前**轮询 `0x66` 的 IBF 位
	/// （bit1；写要等它清空）——这是我们 LPC 写路径缺的一步；本方法只读，⛔ 不写任何字节 ✅</para>
	/// </summary>
	internal static bool TryReadStatus(out int status)
	{
		status = -1;
		lock (Gate)
		{
			if (_poisoned)
			{
				_lastReadFailure = "通道已中毒：" + _poisonReason;
				return false;
			}
			if (!EnsureModule())
			{
				_lastReadFailure = "模块未装载：" + _loadFailure;
				return false;
			}
			if (!TryEnterEcMutex(out Mutex mutex, out bool held))
			{
				_lastReadFailure = "无法获取 " + MutexName + " 互斥体";
				return false;
			}
			try
			{
				if (!PortIn(PortStatus, out byte value, IoKind.Read))
				{
					return false;
				}
				_lastStatusSeen = value;
				status = value;
				return true;
			}
			catch (Exception ex)
			{
				_lastReadFailure = "读 0x66 异常：" + ex.GetType().Name;
				return false;
			}
			finally
			{
				ReleaseEcMutex(mutex, held);
			}
		}
	}

	/// <summary>
	/// ⭐⭐ 2026-10-18（7.3 `ECStatus(EC_IBF)` 等价 · **只读**）：⭐ 轮询 `0x66` 直到 IBF（bit1）清空 ✅。
	/// <para>⭐ 给 LPC 写事务做前置条件（7.3 在每次 `Write_EC_EX` 前都做）；超时/读不到 ⇒ false，调用方 best-effort 继续 ✅</para>
	/// </summary>
	internal static bool TryWaitAcpiIbfClear(int timeoutMs, out string detail)
	{
		detail = "";
		int tries = 0;
		long deadline = Environment.TickCount64 + Math.Max(1, timeoutMs);
		while (true)
		{
			if (!TryReadStatus(out int status))
			{
				detail = "读 0x66 失败：" + LastReadFailure;
				return false;
			}
			if ((status & StatusIbf) == 0)
			{
				detail = "0x66=0x" + status.ToString("X2") + " · IBF=0（轮询 " + tries + " 次）";
				return true;
			}
			if (Environment.TickCount64 >= deadline)
			{
				detail = "0x66=0x" + status.ToString("X2") + " · IBF=1 超时 " + timeoutMs + "ms（" + tries + " 次）";
				return false;
			}
			tries++;
			Thread.Sleep(1);
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
	// 读失败就返回 false（ 不重试、 不污染正常读写路径的失败计数/中毒判定）。</para>
	internal static bool TryReadByteFast(int address, out int value, int timeoutMs = 4)
	{
		value = -1;
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
				return false;
			}
			try
			{
				return ReadByteCoreFast(address, out value, timeoutMs);
			}
			catch
			{
				// ⭐ 探针专用：异常也不中毒（只读、继续扫下一个地址）✅
				return false;
			}
			finally
			{
				ReleaseEcMutex(mutex, held);
			}
		}
	}

	/// <summary>
	/// ⭐ 快速连续读（逐字节 <see cref="TryReadByteFast" />；只用于只读探针）✅。
	/// <para>⚠️ 每个字节都算一次独立事务；失败记为 -1，继续下一个 ✅。</para>
	/// </summary>
	internal static bool TryReadBytesFast(int address, int count, out int[] values, int timeoutMs = 4)
	{
		values = new int[count];
		bool allOk = true;
		for (int i = 0; i < count; i++)
		{
			if (!TryReadByteFast(address + i, out int v, timeoutMs))
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

	/// <summary>⭐ 快速读的单次事务核心（不重试、不记失败计数）✅。</summary>
	private static bool ReadByteCoreFast(int address, out int value, int timeoutMs)
	{
		value = -1;
		if (!WriteCommand(EcCmdRead, IoKind.Read, timeoutMs))
		{
			return false;
		}
		if (!WriteData((byte)((address >> 8) & 0xFF), IoKind.Read, timeoutMs))
		{
			return false;
		}
		if (!WriteData((byte)(address & 0xFF), IoKind.Read, timeoutMs))
		{
			return false;
		}
		if (!ReadData(out byte data, timeoutMs))
		{
			return false;
		}
		value = data;
		return true;
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
	//  ⭐⭐ 2026-10-18（机主批准 · **ACPI EC 写**）：
	//    ⭐ 背景：LPC/SuperIO 通道读成功、写 ioctl 全 OK，但 EC **不认**（回读不变）
	//      ⇒ 写改走 ACPI EC 的 XRAM mailbox（命令 `0x93` = 写 · 与读 `0x92` 同一套地址空间）✅
	//    ⭐ 白名单（**3+1**）：0xC83C（Fan1 目标）· 0xC83D（Fan2 目标）· 0xB20（接管掩码，仅 bit1/bit3）
	//      · 0x1060（**仅** `当前值 | 0x80` 解锁）✅
	//    ⭐ 每次写后**立刻用 ACPI EC 回读比对**（⚠️ 读数用**稳定读**：最多 3 次取多数，
	//      因为机主发现 LPC 侧 0xC411 两次读到 0x23/0x42 不一致 ⚠️）✅
	//    ⭐ 其它地址一律拒绝（⛔ 不给误写 EC 留口子）✅
	// ══════════════════════════════════════════════════════════════════

	/// <summary>⭐ ACPI EC 写白名单：Fan1 目标（0xC83C）✅</summary>
	internal const int AcpiFan1TargetAddress = 0xC83C;

	/// <summary>⭐ ACPI EC 写白名单：Fan2 目标（0xC83D）✅</summary>
	internal const int AcpiFan2TargetAddress = 0xC83D;

	/// <summary>⭐ ACPI EC 写白名单：接管掩码（0xB20）✅</summary>
	internal const int AcpiFanManualMaskAddress = 0xB20;

	/// <summary>⭐ ACPI EC 写白名单：解锁寄存器（0x1060，**仅** `|0x80`）✅</summary>
	internal const int AcpiEcUnlockAddress = 0x1060;

	/// <summary>⭐ 目标档上限（x100RPM）= 68 → 与 LPC 通道一致 ✅</summary>
	internal const int AcpiFanTargetMaxUnits100Rpm = 68;

	private const int AcpiManualMaskBits = 0x0A;

	private const int AcpiManualMaskBitFan1 = 0x02;

	private const int AcpiManualMaskBitFan2 = 0x08;

	private const int AcpiUnlockBit = 0x80;

	private static string _lastAcpiWriteTrace = "";

	/// <summary>⭐ 最近一次 ACPI EC 写的**命令/地址/数据/回读**（诊断用，报告直接打印）✅</summary>
	internal static string LastAcpiWriteTrace
	{
		get
		{
			lock (Gate)
			{
				return _lastAcpiWriteTrace;
			}
		}
	}

	/// <summary>2026-10-22（无界风扇曲线）：无界 EC 的主/副扇占空比目标（0..200 = 0..100%）。
	/// 与宝龙达一样走 ACPI EC 0x92/0x93 的 XRAM 双字节地址；无界转速 0xC834/0xC835 已用同一通道读过。</summary>
	internal const int WujieFan1DutyAddress = 0x1804;

	internal const int WujieFan2DutyAddress = 0x1809;

	internal const int WujieFanDutyMaxRaw = 200;

	/// <summary>⭐ ACPI EC 写白名单（3+1 个地址）✅</summary>
	internal static bool IsAcpiFanWriteAddress(int address)
	{
		return address == AcpiFan1TargetAddress
			|| address == AcpiFan2TargetAddress
			|| address == AcpiFanManualMaskAddress
			|| address == AcpiEcUnlockAddress
			|| address == WujieFan1DutyAddress
			|| address == WujieFan2DutyAddress;
	}

	/// <summary>
	/// ⭐⭐ 稳定读：最多读 <paramref name="attempts" /> 次，取**出现 ≥2 次**的值；不足 2 次成功/一致 ⇒ false ✅。
	/// <para>⚠️ 机主实测 LPC 侧 `0xC411` 两次读到 `0x23`/`0x42` ⇒ 单次读数不可信；ACPI 侧同样加稳定读 ✅</para>
	/// </summary>
	internal static bool TryReadByteStable(int address, out int value, out string note, int attempts = 3)
	{
		value = -1;
		int[] values = new int[attempts];
		int okCount = 0;
		string all = "";
		for (int i = 0; i < attempts; i++)
		{
			bool ok = TryReadByte(address, out values[i]);
			if (ok)
			{
				okCount++;
			}
			all += (i > 0 ? "/" : "") + (ok ? "0x" + values[i].ToString("X2") : "读不到");
		}
		if (okCount < 2)
		{
			note = "读 " + attempts + " 次仅成功 " + okCount + " 次（" + all + "）";
			return false;
		}
		int best = -1;
		int bestCount = 0;
		for (int i = 0; i < attempts; i++)
		{
			if (values[i] < 0)
			{
				continue;
			}
			int count = 0;
			for (int j = 0; j < attempts; j++)
			{
				if (values[j] == values[i])
				{
					count++;
				}
			}
			if (count > bestCount)
			{
				bestCount = count;
				best = values[i];
			}
		}
		if (bestCount < 2)
		{
			note = "读数不一致（" + all + "）⇒ 无法判定";
			return false;
		}
		value = best;
		note = all + " ⇒ 取 0x" + best.ToString("X2") + "（" + bestCount + "/" + okCount + "）";
		return true;
	}

	/// <summary>⭐ ACPI EC 身份（稳定读）：`0x2000 == 0x55` 且 `0xC411` 非 0/0xFF ✅</summary>
	internal static bool TryReadEcIdentityAcpi(out int chipId, out int version, out string note)
	{
		chipId = -1;
		version = -1;
		bool idOk = TryReadByteStable(0x2000, out chipId, out string idNote);
		bool verOk = TryReadByteStable(0xC411, out version, out string verNote);
		bool valid = idOk && verOk && chipId == 0x55 && version != 0x00 && version != 0xFF;
		note = "0x2000 " + idNote + " · 0xC411 " + verNote + " ⇒ " + (valid ? "身份通过 ✅" : "身份未通过 ⚠️");
		return valid;
	}

	/// <summary>⭐ 白名单写 + **立刻用 ACPI EC 稳定回读比对**（⭐ 记录命令/地址/数据/回读）✅</summary>
	private static bool TryWriteByteVerified(int address, int value, out int readBack)
	{
		readBack = -1;
		if (!IsAcpiFanWriteAddress(address))
		{
			return false;
		}
		// ⭐⭐ 2026-10-18（机主双报告 · 参考实现强锁场景实测）：⭐ ACPI 0x93 写路径原来**没有 IBF 等待**；
		//   ⭐ 这里与 LPC 同款：写前轮询 0x66 的 IBF（bit1）清空（最多 ~50ms），只读、失败不阻塞 ✅
		bool ibfClear = TryWaitAcpiIbfClear(50, out string ibfDetail);
		string ibfNote = "0x66 IBF 预检：" + (ibfClear ? "已清" : "⚠️ 未清/不可读（继续）") + " · " + ibfDetail;
		bool writeOk = TryWriteByte(address, value);
		string writeNote = writeOk ? "write=OK" : "write=FAIL(" + LastReadFailure + ")";
		bool readOk = TryReadByteStable(address, out readBack, out string readNote);
		string trace = "ACPI EC 写：cmd=0x93 · addr=0x" + address.ToString("X4")
			+ " · data=0x" + (value & 0xFF).ToString("X2") + " · " + ibfNote
			+ " · " + writeNote
			+ " · 回读 " + (readOk ? "0x" + readBack.ToString("X2") : "读不到")
			+ "（" + readNote + "）";
		lock (Gate)
		{
			_lastAcpiWriteTrace = trace;
		}
		return writeOk && readOk && readBack == (value & 0xFF);
	}

	/// <summary>⭐ ACPI EC 写风扇目标档（仅 0xC83C/0xC83D · 0..68）+ 回读比对 ✅</summary>
	internal static bool TryWriteAcpiFanTarget(int address, int units100Rpm, out int readBack)
	{
		readBack = -1;
		if (address != AcpiFan1TargetAddress && address != AcpiFan2TargetAddress)
		{
			return false;
		}
		if (units100Rpm < 0 || units100Rpm > AcpiFanTargetMaxUnits100Rpm)
		{
			return false;
		}
		return TryWriteByteVerified(address, units100Rpm, out readBack);
	}

	/// <summary>2026-10-22（无界风扇曲线）：写无界主/副扇占空比（0..200）+ 回读比对。
	/// ⚠️ 只有地址为 0x1804/0x1809 且值在 0..200 才放行；其余一律拒绝。</summary>
	internal static bool TryWriteWujieFanDuty(int address, int rawValue, out int readBack)
	{
		readBack = -1;
		if (address != WujieFan1DutyAddress && address != WujieFan2DutyAddress)
		{
			return false;
		}
		if (rawValue < 0 || rawValue > WujieFanDutyMaxRaw)
		{
			return false;
		}
		return TryWriteByteVerified(address, rawValue, out readBack);
	}

	/// <summary>2026-10-22（无界风扇曲线 · 25Hz 周期写）：只写不回读的白名单快路径。
	/// ⚠️ 只有 0x1804/0x1809、值 0..200 才放行；调用方负责周期末/停止前做一次回读校验。</summary>
	internal static bool TryWriteWujieFanDutyFast(int address, int rawValue)
	{
		if (address != WujieFan1DutyAddress && address != WujieFan2DutyAddress)
		{
			return false;
		}
		if (rawValue < 0 || rawValue > WujieFanDutyMaxRaw)
		{
			return false;
		}
		return TryWriteByte(address, rawValue);
	}

	/// <summary>⭐ ACPI EC 读-改-写 0xB20（只改 bit1/bit3，保留其它位）+ 回读 ✅</summary>
	internal static bool TryWriteAcpiFanManualMask(bool fan1Manual, bool fan2Manual, out int readBack, out string note)
	{
		readBack = -1;
		note = "";
		if (!TryReadByteStable(AcpiFanManualMaskAddress, out int current, out string curNote))
		{
			note = "读当前 0xB20 失败：" + curNote;
			return false;
		}
		int wanted = (current & ~AcpiManualMaskBits)
			| (fan1Manual ? AcpiManualMaskBitFan1 : 0)
			| (fan2Manual ? AcpiManualMaskBitFan2 : 0);
		if (current == wanted)
		{
			readBack = current;
			note = "0xB20 已是目标值 0x" + current.ToString("X2") + "，无需写 ✅";
			return true;
		}
		bool ok = TryWriteByteVerified(AcpiFanManualMaskAddress, wanted, out readBack);
		note = "0xB20 0x" + current.ToString("X2") + " → 回读 0x"
			+ (readBack < 0 ? "读不到" : readBack.ToString("X2")) + " ⇒ ok=" + ok;
		return ok;
	}

	/// <summary>⭐ ACPI EC 解锁：**仅** `0x1060 ← 当前值 | 0x80`；身份通过才执行；回读确认 bit7 ✅</summary>
	internal static bool TryUnlockEcRamAcpi(out int before, out int after, out string note)
	{
		before = -1;
		after = -1;
		if (!TryReadEcIdentityAcpi(out _, out _, out string idNote))
		{
			note = "身份未通过 ⇒ ⛔ 不解锁（" + idNote + "）";
			return false;
		}
		if (!TryReadByteStable(AcpiEcUnlockAddress, out before, out string readNote))
		{
			note = "读 0x1060 失败：" + readNote;
			return false;
		}
		int wanted = before | AcpiUnlockBit;
		if (before == wanted)
		{
			after = before;
			note = "0x1060=0x" + before.ToString("X2") + " ⇒ 已解锁（bit7=1），无需写 ✅";
			return true;
		}
		bool ok = TryWriteByteVerified(AcpiEcUnlockAddress, wanted, out after);
		bool bit7 = (after & AcpiUnlockBit) != 0;
		note = "解锁 0x1060 0x" + before.ToString("X2") + " → 回读 0x"
			+ (after < 0 ? "读不到" : after.ToString("X2")) + "（bit7=" + (bit7 ? "1" : "0") + "）⇒ ok=" + (ok && bit7);
		return ok && bit7;
	}

	/// <summary>⭐ ACPI EC：把 0x1060 写回测试前原值（finally 恢复用）✅</summary>
	internal static bool TryRestoreEcUnlockRegisterAcpi(int originalValue, out int readBack)
	{
		readBack = -1;
		if (originalValue < 0 || originalValue > 0xFF)
		{
			return false;
		}
		return TryWriteByteVerified(AcpiEcUnlockAddress, originalValue, out readBack);
	}

	/// <summary>
	/// ⭐⭐ ACPI EC：交回固件 —— ⭐ **先清 `0xB20` 的 bit1/bit3（交出控制权）→ 回读确认 → 再清目标** ✅。
	/// <para>⚠️⚠️ 2026-10-18（机主安全提问）：⛔ 绝不能在掩码仍置位时写 0 目标（EC 可能把风扇拉到 0 转）✅</para>
	/// </summary>
	internal static bool TryRestoreFanFirmwareAcpi(out string detail)
	{
		// ① 先交出控制权（清掩码）
		bool mask = TryWriteAcpiFanManualMask(fan1Manual: false, fan2Manual: false, out int rbMask, out string maskNote);
		string tMask = LastAcpiWriteTrace;
		bool maskClear = rbMask >= 0 && (rbMask & 0x0A) == 0;
		// ② 再清残值
		bool fan1 = TryWriteAcpiFanTarget(AcpiFan1TargetAddress, 0, out int rb1);
		string t1 = LastAcpiWriteTrace;
		bool fan2 = TryWriteAcpiFanTarget(AcpiFan2TargetAddress, 0, out int rb2);
		string t2 = LastAcpiWriteTrace;
		bool targetsZero = rb1 == 0 && rb2 == 0;
		detail = "先清掩码 0xB20→0x" + (rbMask < 0 ? "读不到" : rbMask.ToString("X2"))
			+ "（bit1/bit3=" + (maskClear ? "已清 ✅" : "⚠️ 未清") + " · " + maskNote + "）"
			+ " · 再清目标 0xC83C→" + (rb1 < 0 ? "读不到" : rb1.ToString())
			+ " · 0xC83D→" + (rb2 < 0 ? "读不到" : rb2.ToString())
			+ "（" + (targetsZero ? "已归零 ✅" : "⚠️ 未归零") + "）";
		lock (Gate)
		{
			_lastAcpiWriteTrace = tMask + "\n" + t1 + "\n" + t2;
		}
		return mask && maskClear && fan1 && fan2 && targetsZero;
	}

	// ══════════════════════════════════════════════════════════════════
	//  ⭐⭐ 2026-10-18（机主批准 · **充电阈值白名单写**）：
	//    参考实现：JiaoLong16Pro-Battery-threshold（BM5235 / 2023 款实测；只取协议事实，⛔ 未复制代码）✅
	//    白名单**仅 4 个地址**：
	//      · 0x8153 / 0x8154 = 阈值高/低（**仅 1..100**）
	//      · 0x8151 = 命令字（**仅 0x0D 启用 / 0x0E 准备 / 0x0F 关闭**）
	//      · 0x8150 = 触发（**仅 0x10**）
	//    ⚠️ 其它地址一律拒绝；每次写后立刻回读比对；⛔ 不重试、⛔ 不做额外清理写 ✅
	// ══════════════════════════════════════════════════════════════════
	internal const int BatteryHighParamRegister = 0x8153;

	internal const int BatteryLowParamRegister = 0x8154;

	internal const int BatteryCommandRegister = 0x8151;

	internal const int BatteryTriggerRegister = 0x8150;

	/// <summary>⭐ 充电阈值白名单写（4 地址 + 值域约束 + 回读比对）✅。</summary>
	internal static bool TryWriteBatteryByte(int address, byte value, out int readBack)
	{
		readBack = -1;
		if (address != BatteryHighParamRegister && address != BatteryLowParamRegister
			&& address != BatteryCommandRegister && address != BatteryTriggerRegister)
		{
			return false;
		}
		if ((address == BatteryHighParamRegister || address == BatteryLowParamRegister)
			&& (value < 1 || value > 100))
		{
			return false;
		}
		if (address == BatteryCommandRegister && value != 0x0D && value != 0x0E && value != 0x0F)
		{
			return false;
		}
		if (address == BatteryTriggerRegister && value != 0x10)
		{
			return false;
		}
		bool writeOk = TryWriteByte(address, value);
		bool readOk = TryReadByteStable(address, out readBack, out _);
		return writeOk && readOk && readBack == value;
	}

	/// <summary>
	/// ⭐⭐ 2026-10-18（机主实测 bug）：⭐ 触发寄存器 `0x8150` 是**自清零/脉冲型** ——
	/// 写入 `0x10` 后 EC 立即执行并清回 `0x00` ⇒ ⛔ **不能校验回读==0x10**（参考实现是 write → poll until 0）。
	/// <para>⭐ 本方法只要求**写入调用成功**；<paramref name="readBack" /> 仅作诊断回读（可能是 0）。</para>
	/// </summary>
	internal static bool TryWriteBatteryTriggerPulse(out int readBack)
	{
		readBack = -1;
		if (!TryWriteByte(BatteryTriggerRegister, 0x10))
		{
			return false;
		}
		// ⭐ 仅诊断回读：自清零寄存器，写完立刻读通常就是 0x00（⛔ 不据此判失败）✅
		TryReadByteStable(BatteryTriggerRegister, out readBack, out _);
		return true;
	}

	/// <summary>
	/// ⭐⭐ 2026-10-18（机主批准 · **0x81 写实验**）：⭐ ACPI 标准空间写（WR_EC=`0x81` · **8 位地址**）——
	/// 每步 `WaitIbfClear` + 记录 0x66 状态位（IBF/OBF）✅。
	/// <para>⛔ 严格限制：**只允许 `reg == 0x60`**（`0x1060` 的低字节）做**一次**实验；
	/// 其它寄存器一律拒绝 —— ⛔ 不再尝试更多写花色，避免写坏机器 ✅</para>
	/// <para>⭐ 依据（只取协议形态）：斗战者 `DriverBridge.ReadEc` ⇒ `0x66←0x81` → `0x62←reg` → `0x62←value`，每步等 IBF ✅</para>
	/// </summary>
	internal static bool TryWriteByteEc81Experiment(int reg, int value, out string trace)
	{
		trace = "";
		if (reg != 0x60)
		{
			trace = "拒绝：0x81 实验只允许 reg=0x60（0x1060 低字节）";
			return false;
		}
		if (value < 0 || value > 0xFF)
		{
			trace = "拒绝：value 超出 byte";
			return false;
		}
		lock (Gate)
		{
			if (_poisoned)
			{
				trace = "通道已中毒：" + _poisonReason;
				return false;
			}
			if (!EnsureModule())
			{
				trace = "模块未装载：" + _loadFailure;
				return false;
			}
			if (!TryEnterEcMutex(out Mutex mutex, out bool held))
			{
				Poison("无法获取 " + MutexName + " 互斥体");
				trace = "无法获取 " + MutexName + " 互斥体";
				return false;
			}
			try
			{
				_lastStatusSeen = -1;
				if (!WriteCommand(EcCmdAcpiWrite, IoKind.Write))
				{
					trace = "0x66←0x81 失败（等 IBF 超时）· " + DescribeLastStatus();
					return false;
				}
				trace += "0x66←0x81（写命令）· " + DescribeLastStatus() + "\n";
				if (!WriteData((byte)reg, IoKind.Write))
				{
					trace += "0x62←0x" + reg.ToString("X2") + "（地址）失败 · " + DescribeLastStatus();
					return false;
				}
				trace += "0x62←0x" + reg.ToString("X2") + "（8 位地址）· " + DescribeLastStatus() + "\n";
				if (!WriteData((byte)value, IoKind.Write))
				{
					trace += "0x62←0x" + value.ToString("X2") + "（数据）失败 · " + DescribeLastStatus();
					return false;
				}
				trace += "0x62←0x" + value.ToString("X2") + "（数据）· " + DescribeLastStatus();
				return true;
			}
			catch (Exception ex)
			{
				Poison("0x81 实验写抛异常：" + ex.GetType().Name);
				trace = "异常：" + ex.GetType().Name;
				return false;
			}
			finally
			{
				ReleaseEcMutex(mutex, held);
			}
		}
	}

	/// <summary>⭐ 0x81 实验回滚：把 reg=0x60 写回原值（仅同一条实验路径）✅</summary>
	internal static bool TryRestoreEc81Experiment(int originalValue, out string trace)
	{
		return TryWriteByteEc81Experiment(0x60, originalValue, out trace);
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

	/// <summary>
	/// ACPI 标准空间读（RD_EC = 0x80 · <b>单字节</b>地址 · 0x00~0xFF）。
	/// <para>与 <see cref="ReadByteCore"/> 的区别：那套是厂商 XRAM mailbox（0x92 ＋ 双字节地址），
	/// 够不到键盘背光 0x9A 这类落在标准空间的寄存器。</para>
	/// </summary>
	private static bool ReadByteAcpiCore(int reg, out int value)
	{
		value = -1;
		SettleBeforeTransaction();
		if (!WriteCommand(EcCmdAcpiRead, IoKind.Read))
		{
			return false;
		}
		if (!WriteData((byte)(reg & 0xFF), IoKind.Read))
		{
			return false;
		}
		// ⭐⭐ 2026-10-16 实测修正：ACPI 标准空间读**不等 OBF**，照斗战者的时序 ⚠️
		//   依据：斗战者 DriverBridge.ReadEc = `0x66←0x80` → `0x62←reg` → `Sleep(5)` → **直接读** `0x62`
		//   ⭐ 实测证据（本机诊断包 …-235908 的 ②e）：用「等 OBF 置位」读 0x80 时，
		//      `0x66` 状态**恒为 0x00**、OBF **从未置位** ⇒ 13 个地址全部超时并迅速中毒 ⚠️
		//   ⚠️ 这与 0x92(XRAM) 的行为**不同** —— 那条路的 OBF 会正常置位（②d 已实测读到字节）✅
		//   ⇒ 故此处与 <see cref="ReadByteCore"/> 分道：那条等 OBF，这条不等 ✅
		Thread.Sleep(5);
		// ⚠️⚠️ **可信度警告（2026-10-16 本机实测 · 诊断包 …-000002）**：
		//   本机走 0x80 时 OBF（bit0）**从未置位**，但"直接读"仍能拿到字节 ⚠️
		//   ⭐ 决定性证据：实测 `0x40` 读到 `0x07`，**恰等于同一进程里 H2RAM `0x7C3` 的对照读值**（②b）
		//   ⇒ ⚠️ **强烈怀疑读到的是上一次事务残留在数据寄存器里的字节**，⛔ **不能当成可靠读数** ⚠️
		//   ⇒ 其余地址全 0x00 亦同此理（无残留时读到空值）。
		//   ⭐ 本条与既有的「**IOCTL 返回成功 ≠ 地址可达**」是**同一类陷阱** ⇒ 故把状态位记下来，
		//      让诊断能区分「**读到了**」与「**读对了**」✅
		//   ⚠️ 判断依据：⭐ **OBF 置位**才是「EC 真的给出了数据」；未置位 ⇒ 本次读数**不可信**。
		//      本机（同方翼龙15Pro）OBF 恒 0 ⇒ 该机 EC **不支持 0x80**（机型差异，非代码缺陷）；
		//      ⭐ 蛟龙（宝龙达 IT5570）上应能观察到 OBF 置位 —— **必须以真机结果为准** ✅
		if (PortIn(PortStatus, out byte st, IoKind.Read))
		{
			_lastStatusSeen = st;
		}
		if (!PortIn(PortData, out byte data, IoKind.Read))
		{
			return false;
		}
		value = data;
		return true;
	}

	/// <summary>
	/// 只读：读 ACPI 标准空间一个字节（<paramref name="reg"/> 限 0x00~0xFF）。
	/// <para>任何失败都返回 false，并按需标记中毒。<b>绝不抛出</b>。</para>
	/// <para>⚠️ 与 <see cref="TryReadByte"/>（XRAM）是<b>两套地址空间</b>，别混用。</para>
	/// </summary>
	internal static bool TryReadByteAcpi(int reg, out int value)
	{
		value = -1;
		if (reg < 0 || reg > 0xFF)
		{
			_lastReadFailure = "标准空间地址越界：0x" + reg.ToString("X4") + "（只允许 0x00~0xFF）";
			return false;
		}
		lock (Gate)
		{
			if (_poisoned)
			{
				_lastReadFailure = "通道已中毒：" + _poisonReason;
				return false;
			}
			if (!EnsureModule())
			{
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
				bool ok = ReadByteAcpiCore(reg, out value);
				NoteReadOutcome(ok);
				return ok;
			}
			catch (Exception ex)
			{
				Poison("读标准空间 0x" + reg.ToString("X2") + " 抛异常：" + ex.GetType().Name);
				return false;
			}
			finally
			{
				ReleaseEcMutex(mutex, held);
			}
		}
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
	private static bool WriteCommand(byte command, IoKind kind, int timeoutMs = TimeoutMs)
	{
		if (command != EcCmdRead && command != EcCmdWrite && command != EcCmdAcpiRead && command != EcCmdAcpiWrite)
		{
			Poison("命令 0x" + command.ToString("X2") + " 不在白名单（只允许 0x92/0x93/0x80/0x81）");
			return false;
		}
		if (!WaitIbfClear(kind, timeoutMs))
		{
			return false;
		}
		return PortOut(PortStatus, command, kind);
	}

	/// <summary>向 0x62 写一个数据字节（先等 IBF 清空）。</summary>
	private static bool WriteData(byte data, IoKind kind, int timeoutMs = TimeoutMs)
	{
		if (!WaitIbfClear(kind, timeoutMs))
		{
			return false;
		}
		return PortOut(PortData, data, kind);
	}

	/// <summary>从 0x62 读一个数据字节（先等 OBF 置位）。</summary>
	private static bool ReadData(out byte data, int timeoutMs = TimeoutMs)
	{
		data = 0;
		if (!WaitObfSet(IoKind.Read, timeoutMs))
		{
			return false;
		}
		return PortIn(PortData, out data, IoKind.Read);
	}

	/// <summary>轮询 0x66 直到 IBF（bit1）为 0，即 EC 已取走我们上一次写入的字节。</summary>
	private static bool WaitIbfClear(IoKind kind, int timeoutMs = TimeoutMs)
	{
		long deadline = Environment.TickCount64 + timeoutMs;
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
				ReportSoftFailure(kind, "等 IBF 清空超时（" + timeoutMs + "ms · 最后状态 = 0x"
					+ status.ToString("X2") + " ⇒ EC 未取走我们写入的字节）");
				return false;
			}
			Thread.Sleep(1);
		}
	}

	/// <summary>轮询 0x66 直到 OBF（bit0）为 1，即 EC 已准备好一个可读字节。</summary>
	private static bool WaitObfSet(IoKind kind, int timeoutMs = TimeoutMs)
	{
		long deadline = Environment.TickCount64 + timeoutMs;
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
				ReportSoftFailure(kind, "等 OBF 置位超时（" + timeoutMs + "ms · 最后状态 = 0x"
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
			if (_pawn != null && _pawn.IsModuleLoaded)
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
			if (I2ecInpOut.TryReadPort((short)port, out value))
			{
				_ioCount++;
				return true;
			}
			ReportSoftFailure(kind, "端口读 0x" + port.ToString("X2") + " 失败（PawnIO/inpoutx64 都不可用）");
			return false;
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
			if (_pawn != null && _pawn.IsModuleLoaded)
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
			if (I2ecInpOut.TryWritePort((short)port, value))
			{
				_ioCount++;
				return true;
			}
			ReportSoftFailure(kind, "端口写 0x" + port.ToString("X2") + " 失败（PawnIO/inpoutx64 都不可用）");
			return false;
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

	private static bool TryUseInpoutFallback(string reason)
	{
		if (!I2ecInpOut.CanUsePortIo(out string detail))
		{
			return false;
		}
		_inpoutFallback = true;
		_loadFailure = "PawnIO 不可用（" + reason + "）⇒ 已回退 inpoutx64 端口 I/O";
		Logger.WriteError("[ec] " + _loadFailure + " · " + detail);
		return true;
	}

	private static bool EnsureModule()
	{
		if (_pawn != null && _pawn.IsModuleLoaded)
		{
			return true;
		}
		if (_loadAttempted)
		{
			return _inpoutFallback;
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
				if (TryUseInpoutFallback(_loadFailure))
				{
					return true;
				}
				return false;
			}
			byte[] moduleData = LoadModuleResource();
			if (moduleData == null || moduleData.Length == 0)
			{
				_loadFailure = "内嵌资源 " + ModuleResourceName + " 缺失或为空";
				pawn.Dispose();
				if (TryUseInpoutFallback(_loadFailure))
				{
					return true;
				}
				return false;
			}
			if (!pawn.LoadModule(moduleData))
			{
				_loadFailure = "模块装载被拒（LoadModule 返回 false）";
				pawn.Dispose();
				if (TryUseInpoutFallback(_loadFailure))
				{
					return true;
				}
				return false;
			}
			_pawn = pawn;
			return true;
		}
		catch (Exception ex)
		{
			_loadFailure = "装载抛异常：" + ex.GetType().Name + "：" + ex.Message;
			pawn?.Dispose();
			if (TryUseInpoutFallback(_loadFailure))
			{
				return true;
			}
			return false;
		}
	}

	private static byte[] LoadModuleResource()
	{
		using Stream? stream = PawnIOModuleLoader.Open(ModuleResourceName);
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
		// ⚠️ 2026-10-16：下面这段在 <see cref="InterTransactionDelayMs" /> 取当前值 0 时**编译期即不可达**
		//    ⇒ 编译器报 CS0162「检测到无法访问的代码」。但这是**有意保留**的机制，⛔ 不是死代码遗留：
		//    见该常量自己的说明 —— 延迟被 2026-10-16 的 A/B 实测否定后才关成 0，
		//    保留整段是为了将来在**真机证据**支持下"只改一个数字"即可重新启用
		//    （诊断段仍在读它的值，`DiagnosticBaolongdaInfo` 会如实打印"当前事务间隔 = 0ms"）。
		//    ⇒ 因此用 pragma **只静默该告警**：⛔ 不删代码、⛔ 不改逻辑、⛔ 不动任何安全护栏。
		//    ⭐ 将来把常量改成 > 0 时这段自然被启用，届时可一并移除本 pragma。
#pragma warning disable CS0162
		try
		{
			Thread.Sleep(InterTransactionDelayMs);
		}
		catch (Exception)
		{
		}
#pragma warning restore CS0162
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

	/// <summary>⭐ 诊断：把最近一次 0x66 状态位展开成 `status=0x..（IBF=.. · OBF=..）` ✅</summary>
	internal static string DescribeLastStatus()
	{
		int status = _lastStatusSeen;
		if (status < 0)
		{
			return "status=未读到";
		}
		return "status=0x" + status.ToString("X2")
			+ "（IBF=" + (((status & StatusIbf) != 0) ? "1" : "0")
			+ " · OBF=" + (((status & StatusObf) != 0) ? "1" : "0") + "）";
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
