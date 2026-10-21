using System;
using System.IO;
using System.Text;
using System.Threading;
using PawnIO;

namespace MechrevoLite.Hardware;

/// <summary>
/// LPC / SuperIO EC 寄存器窗口通道（经 PawnIO 的 <c>LpcIO</c> 模块，slot 1 = <c>0x4E</c>/<c>0x4F</c>）。
///
/// <para><b>为什么再开一条通道</b>：EC 的访问方式<b>不止一套</b>，实测与上游共确认四条：
/// ① ACPI EC 标准端口 <c>0x62</c>/<c>0x66</c>（命令 <c>0x92</c> 读 XRAM / <c>0x80</c> 读标准空间）
/// —— 由 <see cref="EcPortIo"/> 走 <c>LpcACPIEC</c> 模块；
/// ② <b>LPC EC-RAM 命令协议（本类）</b> —— 端口对 <c>0x2E</c>/<c>0x2F</c>（slot 0）
/// 或 <c>0x4E</c>/<c>0x4F</c>（slot 1）：<c>+0</c> 是**命令口**（<c>0x2E</c>=设置索引寄存器 ·
/// <c>0x2F</c>=访问数据）、<c>+1</c> 是**数据口**；索引寄存器 <c>0x11</c> 地址高 / <c>0x10</c> 地址低 / <c>0x12</c> 数据；
/// ⚠️ 2026-10-18 机主实测全 0 后已修正：**不是**标准 SuperIO index/data 两连写（详见
/// <c>docs\notes\风扇EC时序对照-20261018.md</c>）；
/// ③ 厂商控制台 7.3 的 <c>0x2E</c>/<c>0x2F</c> ＋ <c>Write_EC_Init</c> 解锁前置；
/// ④ 斗战者的物理内存窗口 <c>0xFE800400</c>（长 <c>0xFF</c>，只覆盖标准空间）。</para>
///
/// <para>⚠️ <b>四套的地址空间不同，绝不能跨套搬地址。</b> 例：两个上游都自称在读「EC 版本」，
/// 但一个说 <c>0xC411</c>、一个说 <c>0x8410~0x8413</c> —— 这不可能是笔误，只能是两套空间。
/// 本类存在的意义就是把第 ② 套打通，从而能读/写 <c>0xC4xx</c>/<c>0xC8xx</c> 段
/// （蛟龙控制台口径：EC 版本 <c>0xC411</c>、风扇目标 <c>0xC83C</c>/<c>0xC83D</c>、接管掩码 <c>0xB20</c>）。</para>
///
/// <para><b>槽位选择</b>：上游 <c>LpcIO.p</c> 注释写明
/// <i>"Slot 0 is at 0x2e/0x2f, slot 1 at 0x4e/0x4f"</i>。
/// <b>两个槽位都支持</b>：默认（<see cref="TryReadByte"/>）走 <b>slot 1</b>，
/// 因为 <c>JiaolongControl</c> 的 <c>ECMemoryTable</c> 用的就是
/// <c>EC_ADDR_PORT = 0x4E</c> / <c>EC_DATA_PORT = 0x4F</c>（厂商控制台走的是 slot 0）；
/// <see cref="TryReadByteOnSlot"/> 可显式指定槽位，供诊断做「**同地址两槽位并列对照**」——
/// ⭐ 那是把「读回全 <c>0xFF</c> 是**槽位不对**」与「**本机读不到**」分开的唯一手段。
/// <c>ioctl_select_slot</c> 会 reset 模块内部状态，故<b>每次事务前都重新选择一次</b>。</para>
///
/// <para><b>⚠️ 诚实边界：读也包含对端口/索引寄存器的写。</b>
/// 按 ITE EC-RAM 命令协议，读一个 EC 字节必须先写命令 <c>0x2E</c>/索引号、再写命令 <c>0x2F</c> 访问数据
/// （这些是**寻址/命令**，不是改 EC 数据）。⭐ 2026-10-18 起本类**额外**提供一条
/// **严格白名单的 EC 写路径**（仅 <c>0xC83C</c>/<c>0xC83D</c>/<c>0xB20</c>，见 <see cref="TryWriteByte"/>）——
/// ⛔ 其它地址一律拒绝并中毒；⛔ 不发 <c>0x93</c>/<c>0x81</c>。</para>
///
/// <para><b>安全约束</b>（与 <see cref="EcPortIo"/> <b>同构</b>，逐条对齐 —— 两套护栏松紧必须一致）：
/// ① 槽位固定 slot 1；模块本身只允许<b>所选槽位</b>的 <c>+0</c>/<c>+1</c> 两个端口（外加 ASUS 的
/// <c>0x25C</c>/<c>0x25D</c> 与枚举出的 BAR），故到不了无关端口；
/// ② <b>子寄存器白名单</b>（只放 <c>0x10</c>/<c>0x11</c>/<c>0x12</c>），C# 侧再挡一次；
/// ③ <b>Poisoned 机制</b>：读<b>软失败</b>（Execute 返回 false）只累计「连续读失败」，
/// 达 <see cref="MaxConsecutiveReadFailures"/> 次才升级为全局中毒；
/// <b>异常 / 白名单违规立即中毒</b>；失败<b>不自动恢复</b>（保留现场，杜绝盲目重试）；
/// ④ 单实例互斥（<c>Access_ISABUS.HTP.Method</c>，best-effort：拿不到也继续，但记录）；
/// ⑤ <b>惰性装载</b>（启动时不装载，只在真要读 LPC EC 时才装载）；
/// ⑥ 全程 try/catch，<b>绝不把异常抛给调用方</b>。</para>
///
/// <para><b>为什么不并入 <see cref="EcPortIo"/></b>：<see cref="PawnIOWrapper"/> 的模块装载是
/// <b>一次性</b>的（<c>_loaded</c> 置位后不再接受第二个模块），而两条通道用的模块不同
/// （<c>LpcIO</c> vs <c>LpcACPIEC</c>）、端口白名单不同、互斥体也不同
/// （<c>Access_ISABUS.HTP.Method</c> vs <c>Access_EC</c>）⇒ 只能是两个 wrapper 实例。
/// 拆成独立类是为了让<b>安全策略保持同构</b>，避免出现「两套松紧不一」的护栏。</para>
///
/// <para>⚠️ 本类原为**只读诊断**；2026-10-18 机主批准后新增**风扇白名单写**
/// （<see cref="TryWriteByte"/> / <see cref="TryWriteFanTarget"/> / <see cref="TryWriteFanManualMask"/> /
/// <see cref="TryRestoreFanFirmware"/>）—— ⛔ 仅 3 个地址、写后回读、失败即 false；
/// 限充等其它写入仍按既有决定**暂缓**。</para>
/// </summary>
internal static class EcLpcIo
{
	// ── LPC/SuperIO 的两个槽位（上游 LpcIO.p：slot 0 = 0x2E/0x2F、slot 1 = 0x4E/0x4F）──
	//
	//  ⭐ 协议事实：两个槽位是**同一套 SuperIO 协议**的两个端口基址（不是两套协议），
	//    用 `ioctl_select_slot(0|1)` 切换；子寄存器语义（0x11 地址高 · 0x10 地址低 · 0x12 数据）**完全相同**。
	//    · slot 0（0x2E/0x2F）—— 厂商控制台 7.3 走这条（含 `Write_EC_Init` 解锁前置）
	//    · slot 1（0x4E/0x4F）—— 蛟龙开源控制台的 ECMemoryTable.EC_ADDR_PORT/EC_DATA_PORT 走这条
	//  ⚠️ ⇒ 二者指向**同一片 XRAM**，只是入口不同 —— 这正是 ②f 做「同地址两槽位并列对照」的原因：
	//     ⭐ 若 slot 0 能读到 0x55（芯片 ID）而 slot 1 全 0xFF ⇒ 说明**通道与协议都对、只是槽位因模具而异**；
	//     ⭐ 若两个槽位都全 0xFF ⇒ 只能说**本机读不到**（⚠️ 可能是没有该 SuperIO 设备、也可能未进入配置态
	//        或地址本就不存在 —— 三者本探针**分不开**，故不得据此下"不存在"的结论）。
	internal const ulong SlotZero = 0;

	internal const ulong SlotOne = 1;

	/// <summary>默认槽位 = slot 1（蛟龙控制台口径，上游 ECMemoryTable 用的就是它）。</summary>
	internal const ulong DefaultSlot = SlotOne;

	/// <summary>槽位 → 索引端口（上游 LpcIO.p：slot 0 = 0x2E、slot 1 = 0x4E）。</summary>
	internal static ushort IndexPortFor(ulong slot)
	{
		return (slot == SlotZero) ? ((ushort)0x2E) : ((ushort)0x4E);
	}

	/// <summary>槽位 → 数据端口（= 索引端口 + 1）。</summary>
	internal static ushort DataPortFor(ulong slot)
	{
		return (ushort)(IndexPortFor(slot) + 1);
	}

	private static ulong _lastSlotUsed = DefaultSlot;

	// ── SuperIO 子寄存器（协议事实：只取端口对/寄存器号/访问顺序，未抄任何代码）──
	private const byte SubRegAddressHigh = 0x11;

	private const byte SubRegAddressLow = 0x10;

	private const byte SubRegData = 0x12;

	private const string ModuleResourceName = "LMechrevo.LpcIO.bin";

	// ⚠️ 与 EcPortIo 的 "Global\Access_EC" 不同：上游 LpcIO.p 的注释点名了这个互斥体
	private const string MutexName = "Global\\Access_ISABUS.HTP.Method";

	internal const int TimeoutMs = 250;

	private static readonly object Gate = new object();

	private static PawnIOWrapper _pawn;

	private static bool _loadAttempted;

	private static string _loadFailure = "";

	private static bool _poisoned;

	private static string _poisonReason = "";

	private static int _ioCount;

	// ⭐⭐ 2026-10-18（EC 写排查 · 仪表化）：⭐ 把**最近一次写操作**的完整端口序列（地址+值+ioctl 结果/错误码）
	//   记下来，报告里可直接和开源蛟龙 `Blding64.EC_RAM_WRITE` 逐条并列 ✅
	//   ⚠️ 只记录、⛔ 不改变任何控制流；⭐ 只在 `TryWriteByteOperation` 期间开启（普通读不记，⛔ 不刷屏）✅
	private static readonly StringBuilder _ioTrace = new StringBuilder();

	private static bool _ioTraceEnabled;

	/// <summary>⭐ 最近一次写操作的**最后一条 ioctl** 是否成功（诊断用）✅</summary>
	internal static bool LastIoctlOk { get; private set; }

	/// <summary>⭐ 最近一次写操作的**最后一条 ioctl** 的 Win32 错误码（0=成功；诊断用）✅</summary>
	internal static int LastIoctlError { get; private set; }

	/// <summary>⭐ 最近一次写操作的完整端口序列（每行：端口/值/ioctl 结果；⭐ 诊断用）✅</summary>
	internal static string LastWriteTrace
	{
		get
		{
			lock (Gate)
			{
				return _ioTrace.ToString();
			}
		}
	}

	/// <summary>连续读失败达此次数才升级为全局中毒（软失败）。与 <see cref="EcPortIo"/> 同值。</summary>
	internal const int MaxConsecutiveReadFailures = 3;

	private static int _consecutiveReadFailures;

	private static string _lastReadFailure = "";

	private static string _pendingSoftFailure = "";

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

	/// <summary>供诊断：最后一次读失败的具体原因（成功时为空串）。</summary>
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
				return "· 尚未尝试（惰性装载：只在真要读 LPC EC 时才装载模块）";
			}
			if (_pawn == null || !_pawn.IsModuleLoaded)
			{
				return "⛔ 模块未装载（" + _loadFailure + "）";
			}
			string text = "✅ 模块已装载（LpcIO · 支持 slot 0 = 0x2E/0x2F 与 slot 1 = 0x4E/0x4F"
				+ " · 同一套子寄存器语义 · 最近一次事务用 slot " + _lastSlotUsed
				+ "（0x" + IndexPortFor(_lastSlotUsed).ToString("X2")
				+ "/0x" + DataPortFor(_lastSlotUsed).ToString("X2") + "）"
				+ " · 子寄存器白名单仅 0x10/0x11/0x12）· 已发 Execute " + _ioCount + " 次";
			if (_consecutiveReadFailures > 0)
			{
				text = text + " · ⚠️ 当前已**连续读失败** " + _consecutiveReadFailures + "/"
					+ MaxConsecutiveReadFailures + " 次（⭐ 达到 " + MaxConsecutiveReadFailures
					+ " 次才升级为全局中毒 ⇒ ⭐ 未达上限说明只是**个别地址**不响应 ✅）";
			}
			return text;
		}
	}

	/// <summary>供诊断：PawnIO 驱动本身是否已安装（不装载模块、不发 I/O，代价极低）。</summary>
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
	/// 只读：经 LPC/SuperIO 窗口读 EC 一个字节（**默认槽位 slot 1**，蛟龙控制台口径）。
	/// <para>任何失败都返回 false，并按需标记中毒（软失败计数 / 硬失败立即）。<b>绝不抛出</b>。</para>
	/// </summary>
	internal static bool TryReadByte(int address, out int value)
	{
		return TryReadByteOnSlot(DefaultSlot, address, out value);
	}

	/// <summary>
	/// 只读：**指定槽位**读 EC 一个字节。
	///
	/// <para>⭐ 为什么要能换槽位：slot 0（<c>0x2E</c>/<c>0x2F</c>）与 slot 1（<c>0x4E</c>/<c>0x4F</c>）
	/// 是**同一套 SuperIO 协议**的两个端口基址、指向**同一片 XRAM**（上游 <c>LpcIO.p</c>：
	/// <i>"Slot 0 is at 0x2e/0x2f, slot 1 at 0x4e/0x4f"</i>）。
	/// 同一地址在两个槽位各读一次，即可把「**读回全 <c>0xFF</c> 是槽位不对**」与
	/// 「**本机读不到**」分开 —— 只读一个槽位时这两种失败长得一样。</para>
	///
	/// <para>任何失败都返回 false，并按需标记中毒。<b>绝不抛出</b>。</para>
	/// </summary>
	internal static bool TryReadByteOnSlot(ulong slot, int address, out int value)
	{
		value = -1;
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
			if (!TryEnterMutex(out Mutex mutex, out bool held))
			{
				Poison("无法获取 " + MutexName + " 互斥体");
				return false;
			}
			try
			{
				bool ok = ReadByteCore(slot, address, out value);
				NoteReadOutcome(ok);
				return ok;
			}
			catch (Exception ex)
			{
				Poison("读 LPC EC slot " + slot + " 0x" + address.ToString("X4") + " 抛异常：" + ex.GetType().Name);
				return false;
			}
			finally
			{
				ReleaseMutex(mutex, held);
			}
		}
	}

	/// <summary>只读：连续读若干字节（逐字节走同一时序）。</summary>
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

	// ══════════════════════════════════════════════════════════════════
	//  ⭐⭐ 2026-10-18（机主批准 · **EC 白名单写**）：
	//    ⭐ 背景：WMI 功能号 20=1 在真机上会**卡死**（连 try/finally 都跑不完）
	//      ⇒ 风扇接管彻底改走 EC（与开源蛟龙 `Blding64` 同一套寄存器语义，⛔ 未复制代码）✅
	//    ⭐ 风扇白名单**3 个地址**：0xC83C（Fan1/CPU 目标）· 0xC83D（Fan2/GPU 目标）· 0xB20（接管掩码）
	//    ⭐⭐ 2026-10-18（机主批准 · 解锁）：⭐ 额外**仅一个**地址 `0x1060`，且**只允许 `当前值 | 0x80`**
	//       （只置 bit7、保留其它位；回读确认 bit7=1；⭐ 只在 `0x2000 == 0x55` 时执行）✅
	//    ⭐ 值域：目标 0..68（x100RPM）· 0xB20 只改 bit1/bit3（0x02/0x08，⭐ 读-改-写，保留其它位）✅
	//    ⭐ 每次写完**立刻回读同一地址并比对**；失败/超时沿用本类中毒策略 ✅
	//    ⚠️ 其它地址一律**拒绝并中毒**（⛔ 不给误写其它 EC 寄存器留口子）✅
	// ══════════════════════════════════════════════════════════════════

	/// <summary>⭐ Fan1（CPU）目标档地址（开源蛟龙 `Fan1_RPM_SET`）✅</summary>
	internal const int Fan1TargetAddress = 0xC83C;

	/// <summary>⭐ Fan2（GPU）目标档地址（开源蛟龙 `Fan2_RPM_SET`）✅</summary>
	internal const int Fan2TargetAddress = 0xC83D;

	/// <summary>⭐ 手动接管掩码地址（开源蛟龙 `0xB20`：bit1 = Fan1 手动 · bit3 = Fan2 手动）✅</summary>
	internal const int FanManualMaskAddress = 0xB20;

	/// <summary>⭐ 目标档上限（x100RPM）= 68（⭐ 与 <see cref="BaolongdaProfile.FanManualMaxUnits100Rpm" /> 同源）✅</summary>
	internal const int FanTargetMaxUnits100Rpm = BaolongdaProfile.FanManualMaxUnits100Rpm;

	private const int ManualMaskBits = 0x0A;

	private const int ManualMaskBitFan1 = 0x02;

	private const int ManualMaskBitFan2 = 0x08;

	/// <summary>⭐ EC 解锁寄存器地址（开源蛟龙 `EC_init()`：`0x1060 |= 0x80`）✅</summary>
	internal const int EcUnlockAddress = 0x1060;

	/// <summary>⭐ 解锁位（bit7）✅</summary>
	private const int EcUnlockBit = 0x80;

	/// <summary>⭐ EC 写白名单（⭐ 风扇 3 个地址 + 键盘背光亮度 0x1807 的 4 个值）✅</summary>
	internal static bool IsFanWriteAddress(int address)
	{
		return address == Fan1TargetAddress || address == Fan2TargetAddress || address == FanManualMaskAddress;
	}
	// <summary> （30 系键盘背光亮度）：EC 亮度寄存器 0x1807（值 = PWM：0x00/0x66/0xB3/0xFF）。</summary>
	internal const int KeyboardBrightnessAddress = 0x1807;

	/// <summary>⭐ 0x1807 **只允许的 4 个值**（档位 0/1/2/3 ⇒ 0x00/0x66/0xB3/0xFF）✅。</summary>
	private static readonly byte[] KeyboardBrightnessValues = new byte[4] { 0x00, 0x66, 0xB3, 0xFF };

	/// <summary>⭐ 地址是否键盘背光亮度 0x1807 ✅。</summary>
	internal static bool IsKeyboardBrightnessWriteAddress(int address)
	{
		return address == KeyboardBrightnessAddress;
	}

	/// <summary>⭐ 值是否 0x1807 允许的 4 个 PWM 值之一 ✅。</summary>
	internal static bool IsKeyboardBrightnessValue(byte value)
	{
		return Array.IndexOf(KeyboardBrightnessValues, value) >= 0;
	}

	/// <summary>
	/// ⭐⭐ 2026-10-18（机主发现读数偶发不一致后）：⭐ LPC 通道**稳定读** —— 最多 3 次取出现 ≥2 次的值 ✅。
	/// <para>⚠️ 机主实测 `0xC411` 两次读到 `0x23`/`0x42` ⇒ 单次读数不可信；⭐ 身份判据改走本方法 ✅</para>
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

	/// <summary>
	/// ⭐⭐ 2026-10-18（机主实测确认后的**最终判据**）：⭐ EC **可访问性**只认**身份** ——
	/// ⭐ `0x2000` 芯片 ID **必须 == `0x55`** 且 `0xC411` EC 版本**非 0x00/0xFF** ✅。
	/// <para>⚠️ 不再要求「风扇三地址非全 0」：⭐ 机主报告 `0xC83C/0xC83D` 在**固件自动控制时本来就是 0**，
	/// 而 `0xC834/0xC835=0x14`（2000 RPM）与 WMI 13 吻合 ⇒ ⭐ 全 0 那条判据是**假否决**，已删除 ✅</para>
	/// <para>⭐ 读数走**稳定读**（3 次取多数）—— 机主实测 `0xC411` 曾出现 `0x23`/`0x42` 不一致 ⚠️</para>
	/// <para>⛔ 本方法只读（只发读命令）；⛔ 不执行任何 EC 写（解锁见 <see cref="TryUnlockEcRam" />）✅</para>
	/// </summary>
	internal static bool TryReadEcIdentity(out int chipId, out int version, out string note)
	{
		chipId = -1;
		version = -1;
		bool idRead = TryReadByteStable(0x2000, out chipId, out string idNote);
		bool verRead = TryReadByteStable(0xC411, out version, out string verNote);
		// ⭐ 判据（机主 2026-10-18 报告确认）：0x2000 == 0x55 且 0xC411 非 0/0xFF
		bool idValid = idRead && chipId == 0x55;
		bool verValid = verRead && version != 0x00 && version != 0xFF;
		note = "0x2000 " + idNote + "（" + (idValid ? "0x55 ✅" : "⚠️ 判不可访问") + "）"
			+ " · 0xC411 " + verNote + "（" + (verValid ? "非 0 ✅" : "无效 ⚠️") + "）";
		return idValid && verValid;
	}
	// 以及 起键盘背光亮度 <c>0x1807</c> 的 4 个 PWM 值）+ **立刻回读比对** 。
	internal static bool TryWriteByte(int address, byte value, out int readBack)
	{
		readBack = -1;
		if (IsKeyboardBrightnessWriteAddress(address))
		{
			if (!IsKeyboardBrightnessValue(value))
			{
				_lastReadFailure = "0x1807 只允许 0x00/0x66/0xB3/0xFF（拒绝写 0x" + value.ToString("X2") + "）";
				return false;
			}
		}
		else if (!IsFanWriteAddress(address))
		{
			Poison("EC 写被拒：地址 0x" + address.ToString("X4") + " 不在白名单（只允许 0xC83C/0xC83D/0xB20，及 0x1807 的 4 个亮度值）");
			_lastReadFailure = "写地址不在白名单";
			return false;
		}
		return TryWriteByteOperation(address, value, out readBack);
	}

	/// <summary>⭐ 底层白名单写操作（**调用方必须自行校验地址/值域**）+ 立刻回读比对 ✅。</summary>
	private static bool TryWriteByteOperation(int address, byte value, out int readBack)
	{
		readBack = -1;
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
			if (!TryEnterMutex(out Mutex mutex, out bool held))
			{
				Poison("无法获取 " + MutexName + " 互斥体");
				return false;
			}
			try
			{
				// ⭐ 仪表化：只记录**这一次写操作**的完整端口序列（含 ioctl 结果/错误码）✅
				_ioTrace.Clear();
				_ioTraceEnabled = true;
				// ⭐⭐ 2026-10-18（7.3 `Write_EC_Init` 对照结论）：⭐ 7.3 在每次 `Write_EC_EX` 前都先
				//   `ECStatus(EC_IBF)` —— 轮询 ACPI EC 状态口 `0x66` 的 IBF（bit1）直到清空；我们之前**缺这一步**。
				//   ⭐ 这里只做**只读**预检（通过 ACPI 通道读 0x66；读不到/超时 ⇒ best-effort 继续，⛔ 不阻塞写入）✅
				bool ibfClear = EcPortIo.TryWaitAcpiIbfClear(50, out string ibfDetail);
				_ioTrace.Append("0x66 IBF 预检：").Append(ibfClear ? "已清" : "⚠️ 未清/不可读（继续）")
					.Append(" · ").Append(ibfDetail).Append('\n');
				bool wrote = WriteByteCore(DefaultSlot, address, value);
				int check = -1;
				bool readOk = wrote && ReadByteCore(DefaultSlot, address, out check);
				readBack = check;
				bool ok = wrote && readOk && check == value;
				if (!ok)
				{
					ReportSoftFailure("EC 写 0x" + address.ToString("X4") + "=" + value
						+ " 回读=" + (readOk ? check.ToString() : "读不到") + "（比对不一致）");
				}
				NoteReadOutcome(ok);
				return ok;
			}
			catch (Exception ex)
			{
				Poison("写 LPC EC 0x" + address.ToString("X4") + " 抛异常：" + ex.GetType().Name);
				return false;
			}
			finally
			{
				_ioTraceEnabled = false;
				ReleaseMutex(mutex, held);
			}
		}
	}

	/// <summary>
	/// ⭐⭐ 2026-10-18（机主批准 · 解锁）：⭐ **仅** `0x1060 ← 当前值 | 0x80`（只置 bit7，保留其它位）✅。
	/// <para>⚠️ 严格限制：① 只在 `0x2000 == 0x55`（身份通过）时执行；② 值必须是 `current|0x80`；
	/// ③ 写后回读确认 bit7=1 且值一致；④ 失败如实返回 false（调用方不得继续写风扇）✅</para>
	/// <para>⭐ 依据：开源蛟龙 `EC_init()` —— `id = EC_RAM_READ(0x2000); if (id == 0x55) v = EC_RAM_READ(0x1060); EC_RAM_WRITE(0x1060, v|0x80);`
	/// （⭐ 只取协议事实，⛔ 未复制代码）</para>
	/// </summary>
	internal static bool TryUnlockEcRam(out int before, out int after, out string note)
	{
		before = -1;
		after = -1;
		if (!TryReadEcIdentity(out int chipId, out int version, out string idNote))
		{
			note = "身份未通过 ⇒ ⛔ 不执行解锁（" + idNote + "）";
			return false;
		}
		if (!TryReadByte(EcUnlockAddress, out before))
		{
			note = "读 0x1060 失败 ⇒ ⛔ 不执行解锁";
			return false;
		}
		int wanted = before | EcUnlockBit;
		if (before == wanted)
		{
			after = before;
			note = "0x1060=0x" + before.ToString("X2") + " ⇒ ⭐ 已解锁（bit7=1），无需写 ✅";
			return true;
		}
		if (!TryWriteByteOperation(EcUnlockAddress, (byte)wanted, out after))
		{
			note = "写 0x1060 失败（0x" + before.ToString("X2") + " → 回读="
				+ (after < 0 ? "读不到" : "0x" + after.ToString("X2")) + "）⇒ ⛔ 不继续";
			return false;
		}
		bool ok = (after & EcUnlockBit) != 0 && after == wanted;
		note = "解锁写：0x1060 0x" + before.ToString("X2") + " → 0x" + after.ToString("X2")
			+ "（bit7=" + (((after & EcUnlockBit) != 0) ? "1" : "0") + " ⇒ " + (ok ? "确认 ✅" : "未确认 ⚠️") + "）"
			+ " · 身份 " + idNote;
		return ok;
	}

	/// <summary>
	/// ⭐⭐ 2026-10-18（机主批准 · finally 恢复）：⭐ 把 `0x1060` **写回测试前读到的原值**（仅此地址）✅。
	/// <para>⚠️ 只给 `--fan-ec-test` 的 finally 用；写完回读比对，失败返回 false（如实上报）✅</para>
	/// </summary>
	internal static bool TryRestoreEcUnlockRegister(int originalValue, out int readBack)
	{
		readBack = -1;
		if (originalValue < 0 || originalValue > 0xFF)
		{
			return false;
		}
		if (!TryWriteByteOperation(EcUnlockAddress, (byte)originalValue, out readBack))
		{
			return false;
		}
		return readBack == originalValue;
	}

	/// <summary>⭐ 写风扇目标档（**仅** 0xC83C/0xC83D · 值域 0..68）+ 回读比对 ✅。</summary>
	internal static bool TryWriteFanTarget(int address, int units100Rpm, out int readBack)
	{
		readBack = -1;
		if (address != Fan1TargetAddress && address != Fan2TargetAddress)
		{
			return false;
		}
		if (units100Rpm < 0 || units100Rpm > FanTargetMaxUnits100Rpm)
		{
			return false;
		}
		return TryWriteByte(address, (byte)units100Rpm, out readBack);
	}

	/// <summary>
	/// ⭐⭐ 2026-10-18：⭐ **读-改-写** 0xB20 —— 只改 bit1/bit3（0x02/0x08），⭐ 保留其它位 ✅。
	/// <para>⭐ 允许的掩码位组合：0x00 / 0x02 / 0x08 / 0x0A（⭐ 与机主批准的值域一致）✅</para>
	/// </summary>
	internal static bool TryWriteFanManualMask(bool fan1Manual, bool fan2Manual, out int readBack)
	{
		readBack = -1;
		if (!TryReadByte(FanManualMaskAddress, out int current))
		{
			return false;
		}
		int wanted = (current & ~ManualMaskBits)
			| (fan1Manual ? ManualMaskBitFan1 : 0)
			| (fan2Manual ? ManualMaskBitFan2 : 0);
		int bits = wanted & ManualMaskBits;
		if (bits != 0x00 && bits != ManualMaskBitFan1 && bits != ManualMaskBitFan2 && bits != ManualMaskBits)
		{
			return false; // ⭐ 理论不可达 —— ⭐ 仍显式挡住 ✅
		}
		if (current == wanted)
		{
			readBack = current;
			return true; // 已经是目标值 ⇒ ⛔ 不重复写 ✅
		}
		return TryWriteByte(FanManualMaskAddress, (byte)wanted, out readBack);
	}

	/// <summary>
	/// ⭐⭐⭐ **交回固件**（EC 版）：⭐ **先清 `0xB20` 的 bit1/bit3（交出接管）→ 回读确认 → 再清目标** ✅。
	/// <para>⚠️⚠️ 2026-10-18（机主安全提问）：⭐ 顺序**不能反** —— ⛔ 绝不能在掩码仍置位时写 0 目标，
	/// 否则 EC 可能把风扇拉到 0 转（CPU 负载下危险）✅</para>
	/// <para>⚠️ 调用方**必须**在 finally 里调用（⭐ 与开源蛟龙 `RemoveFanSpeed` 同语义）✅</para>
	/// </summary>
	internal static bool TryRestoreFanFirmware(out string detail)
	{
		// ① 先交出控制权（清掩码）
		bool mask = TryWriteFanManualMask(fan1Manual: false, fan2Manual: false, out int rbMask);
		bool maskClear = rbMask >= 0 && (rbMask & 0x0A) == 0;
		// ② 再清残值（此时目标已不被 EC 使用）
		bool fan1 = TryWriteFanTarget(Fan1TargetAddress, 0, out int rb1);
		bool fan2 = TryWriteFanTarget(Fan2TargetAddress, 0, out int rb2);
		bool targetsZero = rb1 == 0 && rb2 == 0;
		detail = "先清掩码 0xB20→0x" + (rbMask < 0 ? "读不到" : rbMask.ToString("X2"))
			+ "（bit1/bit3=" + (maskClear ? "已清 ✅" : "⚠️ 未清") + "）"
			+ " · 再清目标 0xC83C→" + (rb1 < 0 ? "读不到" : rb1.ToString())
			+ " · 0xC83D→" + (rb2 < 0 ? "读不到" : rb2.ToString())
			+ "（" + (targetsZero ? "已归零 ✅" : "⚠️ 未归零") + "）";
		return mask && maskClear && fan1 && fan2 && targetsZero;
	}

	// ══════════════════════════════════════════════════════════════════
	//  LPC EC-RAM 时序（调用方必须已持锁 + 已确保模块 + 已取互斥体）
	//
	//  ⚠️ 2026-10-18 修正：**不是**标准 SuperIO index/data，而是 ITE EC-RAM 命令协议 ——
	//     · +0（0x4E）= 命令口：0x2E = 设置索引寄存器；0x2F = 访问数据
	//     · +1（0x4F）= 数据口：命令 0x2E 后写索引号；命令 0x2F 后写数据 / 读回数据
	//  读一个 EC 字节：
	//    ① 选槽位（ioctl_select_slot <slot> ⇒ 端口基址 0x2E（slot 0）或 0x4E（slot 1））
	//    ② 命令 0x2E + 索引 0x11 + 命令 0x2F + 地址高字节
	//    ③ 命令 0x2E + 索引 0x10 + 命令 0x2F + 地址低字节
	//    ④ 命令 0x2E + 索引 0x12 + 命令 0x2F + 读数据口 ⇒ 数据字节
	//  （只取协议事实；未抄任何第三方代码 —— 逐行对照见 docs\notes\风扇EC时序对照-20261018.md）
	// ══════════════════════════════════════════════════════════════════

	// ⭐⭐ 2026-10-18（时序修正 · 依开源蛟龙 Blding64 的 EC_RAM_READ/WRITE 逐行对照）：
	//   ⚠️ 我们原来把 0x4E/0x4F 当成**标准 SuperIO index/data**（0x4E←子寄存器号、0x4F←值）❌
	//   ⭐ 蛟龙的真实时序是 **ITE EC-RAM 命令协议**：0x4E = 命令口 · 0x4F = 数据口
	//     · 命令 0x2E =「设置索引寄存器」⇒ 0x4E←0x2E, 0x4F←寄存器号
	//     · 命令 0x2F =「访问数据」    ⇒ 0x4E←0x2F, 0x4F←值（写）/ 读 0x4F（读）
	//   ⇒ 一次寄存器访问 = **4 个端口写**（命令+寄存器号 2 个，命令+数据 2 个），不是 2 个 ❌
	//   ⭐ 证据（Blding64.cs 187 行 · EC_RAM_READ）：
	//       WritePort(0x4E,0x2E); WritePort(0x4F,0x11);   // 索引 = 0x11（地址高）
	//       WritePort(0x4E,0x2F); WritePort(0x4F,high);   // 数据 = high
	//       WritePort(0x4E,0x2E); WritePort(0x4F,0x10); WritePort(0x4E,0x2F); WritePort(0x4F,low);
	//       WritePort(0x4E,0x2E); WritePort(0x4F,0x12); WritePort(0x4E,0x2F);
	//       ReadPort(0x4F);                               // 数据 = 该地址的值
	//   ⭐ 这与机主实测「全 0」完全吻合：旧时序把 0x11 当命令写到 0x4E，EC 不会吐出 XRAM 数据 ⚠️
	private const byte EcCmdSetIndex = 0x2E;

	private const byte EcCmdAccessData = 0x2F;

	private static bool ReadByteCore(ulong slot, int address, out int value)
	{
		value = -1;
		// ⭐ 每次事务前重选槽位：ioctl_select_slot 会 reset 模块内部状态，
		//    且其他调用方（含本类的另一槽位读）可能改过它 ⇒ 重设一次最稳（代价 1 次 Execute）
		if (!SelectSlot(slot))
		{
			return false;
		}
		if (!EcWriteIndex(SubRegAddressHigh, (byte)((address >> 8) & 0xFF)))
		{
			return false;
		}
		if (!EcWriteIndex(SubRegAddressLow, (byte)(address & 0xFF)))
		{
			return false;
		}
		if (!EcReadIndex(SubRegData, out byte data))
		{
			return false;
		}
		value = data;
		return true;
	}

	/// <summary>
	/// ⭐⭐ 2026-10-18（时序修正）：⭐ **EC 数据写**（调用方已持锁/模块/互斥体）——
	/// 按蛟龙 `EC_RAM_WRITE`：设置索引 0x11/0x10 后，用命令 0x2F 把值写入索引 0x12 ✅。
	/// <para>⚠️ 数据索引 0x12 是**唯一**写 EC 数据的入口（⭐ 白名单由 <see cref="TryWriteByte" /> 把守）✅</para>
	/// </summary>
	private static bool WriteByteCore(ulong slot, int address, byte value)
	{
		if (!SelectSlot(slot))
		{
			return false;
		}
		if (!EcWriteIndex(SubRegAddressHigh, (byte)((address >> 8) & 0xFF)))
		{
			return false;
		}
		if (!EcWriteIndex(SubRegAddressLow, (byte)(address & 0xFF)))
		{
			return false;
		}
		return EcWriteIndex(SubRegData, value);
	}

	/// <summary>⭐ 按蛟龙时序：命令 0x2E 设置索引寄存器 = <paramref name="indexRegister" />，再把 <paramref name="value" /> 写到它 ✅</summary>
	private static bool EcWriteIndex(byte indexRegister, byte value)
	{
		ushort indexPort = IndexPortFor(_lastSlotUsed);
		ushort dataPort = DataPortFor(_lastSlotUsed);
		return PioOutb(indexPort, EcCmdSetIndex, "命令 0x2E（设置索引）")
			&& PioOutb(dataPort, indexRegister, "索引寄存器 0x" + indexRegister.ToString("X2"))
			&& PioOutb(indexPort, EcCmdAccessData, "命令 0x2F（访问数据）")
			&& PioOutb(dataPort, value, "写入数据");
	}

	/// <summary>⭐ 按蛟龙时序：命令 0x2E 设置索引寄存器，再命令 0x2F 读回该索引的数据 ✅</summary>
	private static bool EcReadIndex(byte indexRegister, out byte value)
	{
		value = 0;
		ushort indexPort = IndexPortFor(_lastSlotUsed);
		ushort dataPort = DataPortFor(_lastSlotUsed);
		if (!PioOutb(indexPort, EcCmdSetIndex, "命令 0x2E（设置索引）")
			|| !PioOutb(dataPort, indexRegister, "索引寄存器 0x" + indexRegister.ToString("X2"))
			|| !PioOutb(indexPort, EcCmdAccessData, "命令 0x2F（访问数据）"))
		{
			return false;
		}
		return PioInb(dataPort, out value);
	}

	/// <summary>
	/// ⭐ 原始端口写（PawnIO LpcIO `ioctl_pio_outb`；模块只放行**所选槽位 +0/+1** 等端口）✅。
	/// <para>⚠️ 用于 ITE EC-RAM 命令协议（0x4E/0x4F）；⭐ 端口号由 <see cref="IndexPortFor" />/<see cref="DataPortFor" /> 决定 ✅</para>
	/// </summary>
	private static bool PioOutb(ushort port, byte value, string what)
	{
		try
		{
			bool ok = _pawn.Execute("ioctl_pio_outb", new ulong[2] { port, value }, null);
			LastIoctlOk = ok;
			LastIoctlError = ok ? 0 : _pawn.LastError;
			Trace("pio_outb", port, value, null, ok, LastIoctlError, what);
			if (!ok)
			{
				ReportSoftFailure("ioctl_pio_outb(0x" + port.ToString("X2") + ",0x" + value.ToString("X2") + " · " + what
					+ ") 失败（Win32=" + LastIoctlError + "）");
				return false;
			}
			_ioCount++;
			return true;
		}
		catch (Exception ex)
		{
			Trace("pio_outb", port, value, null, false, -1, "异常：" + ex.GetType().Name);
			Poison("PIO 写 0x" + port.ToString("X2") + " 抛异常：" + ex.GetType().Name);
			return false;
		}
	}

	/// <summary>实验：直接读任意端口（受 LpcIO 模块白名单限制；失败返回 false 并给原因）。</summary>
	internal static bool TryRawPortRead(ushort port, out byte value, out string note)
	{
		value = 0;
		note = "";
		lock (Gate)
		{
			if (_poisoned)
			{
				note = "通道已中毒：" + _poisonReason;
				return false;
			}
			if (!EnsureModule())
			{
				note = "模块未装载：" + _loadFailure;
				return false;
			}
			if (!TryEnterMutex(out Mutex mutex, out bool held))
			{
				note = "无法获取互斥体";
				return false;
			}
			try
			{
				bool ok = PioInb(port, out byte v);
				value = v;
				note = ok ? "成功" : ("被拒（Win32=" + LastIoctlError + "）");
				return ok;
			}
			catch (Exception ex)
			{
				note = "异常：" + ex.GetType().Name;
				return false;
			}
			finally
			{
				ReleaseMutex(mutex, held);
			}
		}
	}

	/// <summary>实验：直接写任意端口（受 LpcIO 模块白名单限制；失败返回 false 并给原因）。</summary>
	internal static bool TryRawPortWrite(ushort port, byte value, out string note)
	{
		note = "";
		lock (Gate)
		{
			if (_poisoned)
			{
				note = "通道已中毒：" + _poisonReason;
				return false;
			}
			if (!EnsureModule())
			{
				note = "模块未装载：" + _loadFailure;
				return false;
			}
			if (!TryEnterMutex(out Mutex mutex, out bool held))
			{
				note = "无法获取互斥体";
				return false;
			}
			try
			{
				bool ok = PioOutb(port, value, "原始端口写");
				note = ok ? "成功" : ("被拒（Win32=" + LastIoctlError + "）");
				return ok;
			}
			catch (Exception ex)
			{
				note = "异常：" + ex.GetType().Name;
				return false;
			}
			finally
			{
				ReleaseMutex(mutex, held);
			}
		}
	}

	/// <summary>⭐ 原始端口读（PawnIO LpcIO `ioctl_pio_inb`）✅</summary>
	private static bool PioInb(ushort port, out byte value)
	{
		value = 0;
		try
		{
			ulong[] output = new ulong[1];
			bool ok = _pawn.Execute("ioctl_pio_inb", new ulong[1] { port }, output);
			LastIoctlOk = ok;
			LastIoctlError = ok ? 0 : _pawn.LastError;
			byte read = ok ? (byte)(output[0] & 0xFF) : (byte)0;
			Trace("pio_inb", port, null, ok ? (int?)read : null, ok, LastIoctlError, "读回");
			if (!ok)
			{
				ReportSoftFailure("ioctl_pio_inb(0x" + port.ToString("X2") + ") 失败（Win32=" + LastIoctlError + "）");
				return false;
			}
			_ioCount++;
			value = read;
			return true;
		}
		catch (Exception ex)
		{
			Trace("pio_inb", port, null, null, false, -1, "异常：" + ex.GetType().Name);
			Poison("PIO 读 0x" + port.ToString("X2") + " 抛异常：" + ex.GetType().Name);
			return false;
		}
	}

	/// <summary>⭐ 仪表化：把一条端口 I/O 追加到 <see cref="LastWriteTrace" />（⭐ 仅在写操作期间记录）✅</summary>
	private static void Trace(string ioctl, ushort port, byte? writeValue, int? readValue, bool ok, int win32, string what)
	{
		if (!_ioTraceEnabled)
		{
			return;
		}
		_ioTrace.Append(ioctl).Append("(0x").Append(port.ToString("X2")).Append(", ");
		if (writeValue.HasValue)
		{
			_ioTrace.Append("0x").Append(writeValue.Value.ToString("X2"));
		}
		else
		{
			_ioTrace.Append('—');
		}
		_ioTrace.Append(")");
		if (readValue.HasValue)
		{
			_ioTrace.Append(" ⇒ 读回=0x").Append(readValue.Value.ToString("X2"));
		}
		_ioTrace.Append(" · ioctl_rc=").Append(ok ? "0(OK)" : "FAIL(Win32=" + win32 + ")");
		_ioTrace.Append(" · ").Append(what).Append('\n');
	}

	/// <summary>
	/// 选择 LPC/SuperIO 槽位（幂等；每次事务前调用）。
	/// ⚠️ 槽位是模块内部状态（<c>g_register_port</c>）⇒ 必须与随后的子寄存器读写**同一槽位**，
	/// 故每次事务前都重选一次，不依赖上一次的选择（本类现在会交替读两个槽位）。
	/// </summary>
	private static bool SelectSlot(ulong slot)
	{
		try
		{
			if (!_pawn.Execute("ioctl_select_slot", new ulong[1] { slot }, null))
			{
				ReportSoftFailure("选择 LPC 槽位 " + slot + " 失败（ioctl_select_slot）");
				return false;
			}
			_lastSlotUsed = slot;
			return true;
		}
		catch (Exception ex)
		{
			Poison("ioctl_select_slot 抛异常：" + ex.GetType().Name);
			return false;
		}
	}

	/// <summary>写 SuperIO 子寄存器（<b>仅用于寻址窗口</b>；受子寄存器白名单约束）。</summary>
	private static bool SuperIoOutb(byte reg, byte val, string what)
	{
		if (!IsSubRegAllowed(reg))
		{
			Poison("子寄存器 0x" + reg.ToString("X2") + " 不在白名单（只允许 0x10/0x11/0x12）");
			return false;
		}
		try
		{
			if (!_pawn.Execute("ioctl_superio_outb", new ulong[2] { reg, val }, null))
			{
				ReportSoftFailure("SuperIO 写子寄存器 0x" + reg.ToString("X2") + "（" + what + "）失败");
				return false;
			}
			_ioCount++;
			return true;
		}
		catch (Exception ex)
		{
			Poison("SuperIO 写 0x" + reg.ToString("X2") + " 抛异常：" + ex.GetType().Name);
			return false;
		}
	}

	/// <summary>读 SuperIO 子寄存器（受子寄存器白名单约束）。</summary>
	private static bool SuperIoInb(byte reg, out byte val)
	{
		val = 0;
		if (!IsSubRegAllowed(reg))
		{
			Poison("子寄存器 0x" + reg.ToString("X2") + " 不在白名单（只允许 0x10/0x11/0x12）");
			return false;
		}
		try
		{
			ulong[] output = new ulong[1];
			if (!_pawn.Execute("ioctl_superio_inb", new ulong[1] { reg }, output))
			{
				ReportSoftFailure("SuperIO 读子寄存器 0x" + reg.ToString("X2") + " 失败");
				return false;
			}
			_ioCount++;
			val = (byte)(output[0] & 0xFF);
			return true;
		}
		catch (Exception ex)
		{
			Poison("SuperIO 读 0x" + reg.ToString("X2") + " 抛异常：" + ex.GetType().Name);
			return false;
		}
	}

	/// <summary>子寄存器白名单（C# 侧的第二道校验；模块本身只按槽位放行端口）。</summary>
	private static bool IsSubRegAllowed(byte reg)
	{
		return reg == SubRegAddressHigh || reg == SubRegAddressLow || reg == SubRegData;
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
			byte[] blob = LoadModuleResource();
			if (blob == null || blob.Length == 0)
			{
				_loadFailure = "内嵌资源缺失（" + ModuleResourceName + "）";
				pawn.Dispose();
				return false;
			}
			if (!pawn.LoadModule(blob))
			{
				_loadFailure = "LpcIO 模块装载被拒（LoadModule 返回 false）";
				pawn.Dispose();
				return false;
			}
			_pawn = pawn;
			return true;
		}
		catch (Exception ex)
		{
			_loadFailure = "装载抛异常：" + ex.GetType().Name;
			try
			{
				pawn?.Dispose();
			}
			catch (Exception)
			{
			}
			return false;
		}
	}

	private static byte[] LoadModuleResource()
	{
		using Stream stream = typeof(EcLpcIo).Assembly.GetManifestResourceStream(ModuleResourceName);
		if (stream == null)
		{
			return null;
		}
		using MemoryStream memoryStream = new MemoryStream();
		stream.CopyTo(memoryStream);
		return memoryStream.ToArray();
	}

	// ── 互斥体（best-effort：拿不到也继续，但记录）──

	private static bool TryEnterMutex(out Mutex mutex, out bool held)
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

	private static void ReleaseMutex(Mutex mutex, bool held)
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

	// ── 失败分级 ──

	/// <summary>
	/// 收尾一次读事务：成功则清零；<b>软失败</b>只累计计数（达上限才升级为全局中毒）；
	/// <b>硬失败</b>（白名单违规 / 异常）已直接中毒 ⇒ 此处不再计数。与 <see cref="EcPortIo"/> 同构。
	/// </summary>
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

	private static void ReportSoftFailure(string what)
	{
		_pendingSoftFailure = what;
	}

	private static void Poison(string reason)
	{
		if (!_poisoned)
		{
			_poisoned = true;
			_poisonReason = reason;
		}
	}
}
