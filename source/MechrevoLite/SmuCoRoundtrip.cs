using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using MechrevoLite.Helpers;
using PawnIO;

namespace MechrevoLite;

// ==============================================================================================
// 2026-10-05 coread: `--smu-co-roundtrip` —— **给"另一台支持降压的 AMD 台式机"用的独立验证工具**
//
// 用户原话（本轮需求）：
//   「我这个机器不支持降压写了也没有用，但是我有一个 AMD 的台式机是支持的
//     如果想要测试可以单独写一个脚本来验证」
//   「绝对不要在本机（这台 8745H 笔记本）执行任何写着测试」
//
// 它回答两个**只有真机才能回答**的问题（都是上一轮调查留下的未确定项）：
//   未确定 1：LM 走的是"**全核写**"（Phoenix -> MP1 0x4C / Raphael -> PSMU 0x07，一条命令），
//             而回读是"**每核读**"（Phoenix -> PSMU 0xE1 / Raphael -> PSMU 0xD5，每核一条）。
//             全核写的值能不能在每核读上看到？ -> 判定 CONSISTENT / NOT-CONSISTENT / UNKNOWN。
//   未确定 2：每核读的 arg0 到底是「裸核号」还是「core<<20」？ -> 判定 BARE / SHIFTED / UNDETERMINED。
//
// 严格约束（本轮硬约束）：
//   * **不新增任何写命令**：写入只用现有路径 RyzenSmuService.SetCoAll（= CurveCommand 那套），
//     恢复只用现有路径 SetCoAll(0) + DisableOc()。本文件不含任何新的 SMU 命令 ID。
//     它调用的 SetCoAll / SetCoPerCore / CurveReadCommand / DisableOc 是派生自 g-helper / ZenStates-Core
//     的表（见 RyzenSmuService.cs 头部与 ATTRIBUTION-CO-ADDENDUM.md）。
//   * **不带 --yes 绝不写入**：默认只读，打印"未写入"就结束。
//   * **finally 必须恢复**：无论成功失败/异常，只要真的写进去了，就先恢复再退出。
//   * 全程打印原始 hex（6 个 response word），并把同样内容落盘到桌面，方便用户回传。
//
// 退出码：0 = 跑完且结论为 CONSISTENT（或只读模式正常结束）；1 = 前置条件/异常；2 = NOT-CONSISTENT；
//         3 = UNKNOWN（读命令不可用/读失败/无法判定）。
// ==============================================================================================
internal static class SmuCoRoundtrip
{
	private const uint AttachParentProcess = 0xFFFFFFFFu;

	private const int DefaultMargin = -5;

	// CO 步数合法区间（与 RyzenSmuService.EncodeCurve 的 -40..0 对齐；上游 CO 允许 -50..50，
	// 但 LM 的写入路径只接受 -40..0，这里不能越过 LM 自己的约束）。
	private const int MinimumMargin = -40;
	private const int MaximumMargin = 0;

	private const int MaximumCoreCount = 64;

	// 每一步的兜底超时（SMU 单条命令内部已有 200ms 邮箱超时 + 1.5s 互斥超时，
	// 这里只是给"整段读/整段写"再压一道上限，超了就按失败走恢复流程）。
	private const int StepTimeoutMs = 30000;

	private static readonly StringBuilder Report = new StringBuilder();

	private static int _interruptRestoreArmed;

	private static RyzenSmuService? _service;

	[DllImport("kernel32.dll", SetLastError = true)]
	private static extern bool AttachConsole(uint dwProcessId);

	internal static int Run(string[] args)
	{
		bool yes = false;
		int margin = DefaultMargin;
		string? requestedOut = null;
		foreach (string arg in args)
		{
			string text = arg.Trim();
			if (text.Length == 0)
			{
				continue;
			}
			if (text == "--yes" || text == "-y")
			{
				yes = true;
				continue;
			}
			if (text == "--help" || text == "-h" || text == "/?")
			{
				Console.WriteLine("Usage: L-Mechrevo.exe --smu-co-roundtrip [margin] [--yes] [--out <path>]");
				Console.WriteLine("  margin   CO steps to write, -40..0 (default -5). Only written with --yes.");
				Console.WriteLine("  --yes    REALLY write the all-core CO margin (existing SetCoAll path).");
				Console.WriteLine("  --out    report file path (default: Desktop\\SMU-CO-roundtrip-<time>.txt)");
				return 0;
			}
			if (text.StartsWith("--out=", StringComparison.OrdinalIgnoreCase))
			{
				requestedOut = text.Substring(6).Trim('"');
				continue;
			}
			if (text == "--out")
			{
				// 形式二：--out <path>，由下面这个标记位在下一轮取走
				requestedOut = "\u0000";
				continue;
			}
			if (requestedOut == "\u0000")
			{
				requestedOut = text.Trim('"');
				continue;
			}
			if (int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsed))
			{
				margin = parsed;
				continue;
			}
			Console.WriteLine("[WARN] Unrecognized argument ignored: " + text);
		}
		if (requestedOut == "\u0000")
		{
			requestedOut = null;
		}

		TryAttachParentConsole();
		// Ctrl+C 兜底：写窗口期内按 Ctrl+C 不会被直接打死 —— 先恢复 CO 0 再退出。
		// （窗口被强制关闭/进程被 taskkill 时无法拦截，此时靠"CO 只对本次开机生效、重启即失效"兜底，
		//   这一句也会写进报告文件。）
		try
		{
			Console.CancelKeyPress += delegate(object? _, ConsoleCancelEventArgs e)
			{
				e.Cancel = true;
				if (Volatile.Read(ref _interruptRestoreArmed) == 1 && _service != null)
				{
					W("[INTERRUPT] Ctrl+C detected during the write window: restoring CO 0 before exit...");
					try
					{
						_service.SetCoAll(0);
					}
					catch (Exception)
					{
					}
					Volatile.Write(ref _interruptRestoreArmed, 0);
				}
				WriteReportFile(requestedOut);
				Environment.Exit(130);
			};
		}
		catch (Exception)
		{
		}

		string started = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);
		W("=== L-Mechrevo SMU Curve-Optimizer round-trip check ===");
		W("This tool is meant for an AMD desktop that really supports Curve Optimizer (undervolt).");
		W("started        : " + started);
		W("margin request : " + margin + " CO");
		W("write allowed  : " + (yes ? "YES (--yes)" : "NO (read-only run; nothing will be written)"));
		W("");

		int exitCode = 1;
		bool wrote = false;
		bool readAvailable = false;
		bool readOk = false;
		int coreCount = 0;
		List<int?> before = new List<int?>();
		List<int?> after = new List<int?>();
		List<int?> restored = new List<int?>();
		string verdict = "UNKNOWN";
		string verdictNote = "not evaluated";
		string encodingVerdict = "UNDETERMINED";
		string encodingNote = "not evaluated";

		try
		{
			// ----------------------------------------------------------------------------------
			// 前置条件（不满足就明确报错退出，绝不尝试任何 SMU 操作）
			// ----------------------------------------------------------------------------------
			if (!ProcessHelper.IsUserAdministrator())
			{
				W("[FATAL] Administrator rights are required (PawnIO + SMU mailbox).");
				F("【致命】需要管理员权限。请右键启动器/命令行 →「以管理员身份运行」后重试。");
				verdictNote = "not administrator";
				return 1;
			}
			if (!RyzenSmuService.IsPawnInstalled())
			{
				W("[FATAL] PawnIO is not installed (\\Device\\PawnIO not reachable).");
				W("        Install PawnIO first: https://pawnio.eu/  (L-Mechrevo can also install it from");
				W("        the CPU tuning window: 'Install missing driver'). Reboot if the installer asks.");
				F("【致命】没有找到 PawnIO 驱动。请先安装 PawnIO：https://pawnio.eu/");
				F("        （LM 的「CPU 调节」窗口里也有「安装缺失驱动」按钮；装完若提示重启就重启一次。）");
				verdictNote = "PawnIO not installed";
				return 1;
			}
			if (!CpuInfo.IsAMD)
			{
				W("[FATAL] This is not an AMD CPU: " + CpuInfo.Name);
				F("【致命】本机不是 AMD CPU：" + CpuInfo.Name);
				verdictNote = "not an AMD CPU";
				return 1;
			}
			_service = new RyzenSmuService();
			if (!_service.Initialize(Assembly.GetExecutingAssembly()))
			{
				W("[FATAL] PawnIO is reachable but RyzenSMU module initialization was rejected.");
				F("【致命】能打开 PawnIO，但 RyzenSMU 模块初始化被拒绝（驱动在、模块加载/识别失败）。");
				verdictNote = "RyzenSMU module init failed";
				return 1;
			}
			CpuCodeName codeName = _service.CpuCodeName;
			W("cpu            : " + CpuInfo.Name);
			W("codename       : " + codeName + " (family " + _service.Family + ")");
			W("smu version    : 0x" + _service.SmuVersion.ToString("X8", CultureInfo.InvariantCulture));
			W("physical cores : " + CpuInfo.PhysicalCoreCount);
			F("CPU            ：" + CpuInfo.Name);
			F("代号/SMU       ：" + codeName + " / 0x" + _service.SmuVersion.ToString("X", CultureInfo.InvariantCulture));

			uint? readCommand = RyzenSmuService.CurveReadCommand(codeName);
			(bool Psmu, uint Command)? writeCommand = RyzenSmuService.CurveCommand(codeName);
			(bool Psmu, uint Enable, uint Clock, uint Disable)? clockCommand = RyzenSmuService.ClockCommand(codeName);

			W("write path     : " + DescribeCurveCommand(writeCommand, margin));
			W("clock path     : " + (clockCommand.HasValue
				? ((clockCommand.Value.Psmu ? "PSMU 0x" : "MP1 0x") + clockCommand.Value.Disable.ToString("X2", CultureInfo.InvariantCulture) + " (existing DisableOc() is also called on restore)")
				: "none for this codename (restore only writes CO 0)"));
			W("read path      : " + ((readCommand.HasValue)
				? ("PSMU 0x" + readCommand.Value.ToString("X2", CultureInfo.InvariantCulture) + " per core (GetDldoPsmMargin)")
				: "none for this codename"));
			F("写入路径       ：" + DescribeCurveCommand(writeCommand, margin) + "（现有 SetCoAll，未新增命令）");
			F("回读路径       ：" + (readCommand.HasValue
				? ("每核一条 PSMU 0x" + readCommand.Value.ToString("X2", CultureInfo.InvariantCulture))
				: "该代号没有回读命令"));
			W("");

			if (margin < MinimumMargin || margin > MaximumMargin)
			{
				W("[FATAL] margin out of range: " + margin + " (allowed " + MinimumMargin + ".." + MaximumMargin + ")");
				verdictNote = "margin out of range";
				return 1;
			}
			if (!writeCommand.HasValue)
			{
				W("[FATAL] No CO write command for this codename: cannot run the write/read round trip.");
				F("【致命】该代号没有 CO 写命令，无法做往返验证。");
				verdictNote = "no CO write command";
				return 3;
			}
			readAvailable = readCommand.HasValue;
			if (!readAvailable)
			{
				W("[STOP] No per-core CO read command for this codename: nothing is written.");
				F("【停止】该代号没有 CO 回读命令 —— 写了也验证不了，所以**不写入**。");
				verdict = "UNKNOWN";
				verdictNote = "no read command for this codename";
				return 3;
			}

			coreCount = Math.Clamp(CpuInfo.PhysicalCoreCount, 1, MaximumCoreCount);

			// ----------------------------------------------------------------------------------
			// 2) 写前逐核读
			// ----------------------------------------------------------------------------------
			W("--- [1/5] pre-write per-core read (arg0 = bare core index) ---");
			F("");
			F("【1】写前逐核回读（arg0 = 裸核号）");
			before = ReadAllCores(coreCount, bare: true);
			readOk = AllReadable(before);
			if (!readOk)
			{
				W("[STOP] Per-core read failed; refusing to write anything.");
				F("【停止】逐核回读失败 —— 不写入任何东西。");
				verdict = "UNKNOWN";
				verdictNote = "per-core read failed before write";
				return 3;
			}

			// ----------------------------------------------------------------------------------
			// 7) 顺带判定 arg0 编码（写前基线）
			// ----------------------------------------------------------------------------------
			W("");
			W("--- [1b/5] arg0 encoding probe, pre-write (bare core vs core<<20) ---");
			ProbeEncoding(coreCount, margin, afterWrite: false, ref encodingVerdict, ref encodingNote);

			if (!yes)
			{
				W("");
				W("[READ-ONLY] --yes was not given: NOTHING WAS WRITTEN.");
				F("");
				F("【只读模式】没有给 --yes —— **未写入任何值**。");
				verdict = "UNKNOWN";
				verdictNote = "read-only run (no --yes); nothing was written";
				return 0;
			}

			// ----------------------------------------------------------------------------------
			// 3) 写入（唯一一次写入；只用现有 SetCoAll 路径）
			// ----------------------------------------------------------------------------------
			W("");
			W("--- [2/5] WRITE all-core CO " + margin + " via the existing SetCoAll path ---");
			F("");
			F("【2】写入：全核 CO " + margin + "（现有 SetCoAll 路径）");
			Volatile.Write(ref _interruptRestoreArmed, 1);
			wrote = true;
			Stopwatch stopwatch = Stopwatch.StartNew();
			SmuStatus writeStatus = _service.SetCoAll(margin);
			stopwatch.Stop();
			W("SetCoAll(" + margin + ") -> " + DescribeStatus(writeStatus) + "   (" + stopwatch.ElapsedMilliseconds + " ms)");
			F("SetCoAll(" + margin + ") -> " + DescribeStatus(writeStatus));
			if (writeStatus != SmuStatus.OK)
			{
				W("[WARN] the SMU did not accept the write. The round trip cannot be judged, but the restore");
				W("       step still runs below.");
				F("【注意】SMU 没有接受这次写入（成功仅表示接受；拒绝就是拒绝）。仍在下面走恢复。");
			}

			// ----------------------------------------------------------------------------------
			// 4) 写后等待 + 逐核读回
			// ----------------------------------------------------------------------------------
			W("");
			W("--- [3/5] post-write per-core read (waiting 750 ms first) ---");
			Thread.Sleep(750);
			F("");
			F("【3】写后逐核回读（先等 750 ms 让 SMU 生效）");
			after = ReadAllCores(coreCount, bare: true);
			bool afterOk = AllReadable(after);

			W("");
			W("--- [3b/5] arg0 encoding probe, post-write ---");
			ProbeEncoding(coreCount, margin, afterWrite: true, ref encodingVerdict, ref encodingNote);

			// ----------------------------------------------------------------------------------
			// 6) 判定
			// ----------------------------------------------------------------------------------
			if (!afterOk)
			{
				verdict = "UNKNOWN";
				verdictNote = "per-core read failed after write";
			}
			else if (writeStatus != SmuStatus.OK)
			{
				verdict = "UNKNOWN";
				verdictNote = "the SMU rejected the write (" + writeStatus + "); readback diff cannot be attributed to the write";
			}
			else
			{
				int matched = 0;
				int unchanged = 0;
				StringBuilder diff = new StringBuilder();
				for (int i = 0; i < coreCount; i++)
				{
					int? b = before[i];
					int? a = after[i];
					diff.Append(" core").Append(i).Append(':').Append(b?.ToString(CultureInfo.InvariantCulture) ?? "?")
						.Append("->").Append(a?.ToString(CultureInfo.InvariantCulture) ?? "?");
					if (b.HasValue && a.HasValue)
					{
						if (a.Value - b.Value == margin)
						{
							matched++;
						}
						else if (a.Value == b.Value)
						{
							unchanged++;
						}
					}
				}
				W("per-core diff (before -> after):" + diff);
				F("逐核差（写前 -> 写后）：" + diff);
				if (matched == coreCount)
				{
					verdict = "CONSISTENT";
					verdictNote = "all " + coreCount + " cores moved by exactly " + margin + " CO";
				}
				else if (unchanged == coreCount)
				{
					verdict = "NOT-CONSISTENT";
					verdictNote = "all " + coreCount + " cores are unchanged: the all-core write is NOT visible to the per-core read";
				}
				else
				{
					verdict = "NOT-CONSISTENT";
					verdictNote = "partial: " + matched + "/" + coreCount + " cores match, " + unchanged + " unchanged";
				}
			}
			exitCode = verdict switch
			{
				"CONSISTENT" => 0,
				"NOT-CONSISTENT" => 2,
				_ => 3,
			};
		}
		catch (Exception ex)
		{
			W("[EXCEPTION] " + ex.GetType().Name + ": " + ex.Message);
			F("【异常】" + ex);
			verdict = "UNKNOWN";
			verdictNote = "exception: " + ex.GetType().Name + ": " + ex.Message;
			exitCode = 1;
		}
		finally
		{
			// ----------------------------------------------------------------------------------
			// 5) 恢复（无论成功失败异常都要走到这里）+ 恢复后再读一次
			// ----------------------------------------------------------------------------------
			if (wrote && _service != null)
			{
				W("");
				W("--- [4/5] RESTORE (CO back to 0 / exit fixed-clock mode) ---");
				F("");
				F("【4】恢复（CO 归 0 / 退出固定频率模式）");
				try
				{
					SmuStatus restoreStatus = _service.SetCoAll(0);
					W("SetCoAll(0) -> " + DescribeStatus(restoreStatus));
					F("SetCoAll(0) -> " + DescribeStatus(restoreStatus));
				}
				catch (Exception ex)
				{
					W("SetCoAll(0) EXCEPTION: " + ex.Message);
					F("SetCoAll(0) 异常：" + ex.Message);
				}
				try
				{
					if (RyzenSmuService.SupportsClock(_service.CpuCodeName))
					{
						SmuStatus ocStatus = _service.DisableOc();
						W("DisableOc() (existing path) -> " + DescribeStatus(ocStatus));
						F("DisableOc()（现有路径）-> " + DescribeStatus(ocStatus));
					}
				}
				catch (Exception ex)
				{
					W("DisableOc() EXCEPTION: " + ex.Message);
					F("DisableOc() 异常：" + ex.Message);
				}
				Volatile.Write(ref _interruptRestoreArmed, 0);

				W("");
				W("--- [5/5] post-restore per-core read ---");
				F("");
				F("【5】恢复后逐核回读");
				try
				{
					restored = ReadAllCores(coreCount, bare: true);
					if (AllReadable(restored) && restored.TrueForAll((int? v) => v == 0))
					{
						W("restore verified: all " + coreCount + " cores read back as 0 CO");
						F("恢复已核实：全部 " + coreCount + " 核读回 0 CO");
					}
					else
					{
						W("[WARN] post-restore readback is not all zeros; please re-run or reboot before using the machine.");
						F("【注意】恢复后的回读不是全 0，请重跑一次或重启后再使用。");
					}
				}
				catch (Exception ex)
				{
					W("post-restore read EXCEPTION: " + ex.Message);
					F("恢复后回读异常：" + ex.Message);
				}
			}
			else if (!wrote)
			{
				W("");
				W("--- [4/5] restore skipped: nothing was written ---");
			}

			W("");
			W("=== VERDICT ===");
			W("write performed        : " + (wrote ? "YES (all-core SetCoAll " + margin + " attempted)" : "NO"));
			W("round trip             : " + verdict);
			W("  note                 : " + verdictNote);
			W("arg0 encoding          : " + encodingVerdict);
			W("  note                 : " + encodingNote);
			F("");
			F("=== 结论 ===");
			F("是否写入               ：" + (wrote ? "是（已尝试全核 SetCoAll " + margin + "）" : "否"));
			F("全核写 -> 每核读       ：" + verdict);
			F("  说明                 ：" + verdictNote);
			F("每核读 arg0 编码       ：" + encodingVerdict);
			F("  说明                 ：" + encodingNote);
			F("");
			F("CONSISTENT = 全核写能在每核读上看到（差值正好等于写入值）");
			F("NOT-CONSISTENT = 写后每核读没有变化（全核写与每核读不是同一张表/同一条通路的体现）");
			F("UNKNOWN = 读命令不可用、读失败、或只读模式未写入");
			F("BARE = arg0 是裸核号；SHIFTED = arg0 是 core<<20；UNDETERMINED = 两种编码结果相同/都不成立");
			F("");
			F("⚠️ 本工具只发一条全核 CO 写命令（默认 -5，属轻微值），并在退出前恢复为 0。");
			F("   CO 只对本次开机生效：万一工具被强制终止没来得及恢复，**重启电脑即回到默认**。");

			string reportPath = WriteReportFile(requestedOut);
			W("");
			W("report file            : " + (reportPath.Length > 0 ? "saved (see below)" : "WRITE FAILED"));
			if (reportPath.Length > 0)
			{
				try
				{
					Console.WriteLine("report path            : " + reportPath);
				}
				catch (Exception)
				{
					// 控制台编码问题不影响落盘
				}
			}
		}
		return exitCode;
	}

	// ------------------------------------------------------------------------------------------
	// 逐核读（bare=true -> arg0 = 裸核号；false -> arg0 = core<<20）
	// ------------------------------------------------------------------------------------------
	private static List<int?> ReadAllCores(int coreCount, bool bare)
	{
		List<int?> list = new List<int?>(coreCount);
		Stopwatch stopwatch = Stopwatch.StartNew();
		for (int core = 0; core < coreCount; core++)
		{
			if (stopwatch.ElapsedMilliseconds > StepTimeoutMs)
			{
				W("  core" + core + ": TIMEOUT: per-core read exceeded " + StepTimeoutMs + " ms; treating the rest as failed");
				list.Add(null);
				continue;
			}
			uint arg0 = bare ? (uint)core : ((uint)core << 20);
			uint[] response;
			SmuStatus status = _service!.ReadCurveMarginRaw(arg0, out response);
			int? margin = ((status == SmuStatus.OK) ? RyzenSmuService.DecodeCurveMargin(response[0]) : ((int?)null));
			list.Add(margin);
			string line = "  core" + core + ": arg0=0x" + arg0.ToString("X8", CultureInfo.InvariantCulture)
				+ " status=" + DescribeStatus(status)
				+ " resp=" + HexWords(response)
				+ " -> CO " + (margin.HasValue ? margin.Value.ToString(CultureInfo.InvariantCulture) : "?");
			W(line);
			F(line);
		}
		return list;
	}

	// ------------------------------------------------------------------------------------------
	// arg0 编码判定：同一核分别用「裸核号」与「core<<20」读，比较 status 与返回值。
	//   * 一个 OK 一个不 OK -> 直接判定
	//   * 两个都 OK 且都等于写入值 -> 无法区分（UNDETERMINED）
	//   * 两个都 OK 但只有一个等于写入值 -> 用值判定
	// ------------------------------------------------------------------------------------------
	private static void ProbeEncoding(int coreCount, int margin, bool afterWrite, ref string verdict, ref string note)
	{
		int probeCore = ((coreCount > 1) ? 1 : 0);
		uint bareArg = (uint)probeCore;
		uint shiftedArg = (uint)probeCore << 20;
		uint[] bareResponse;
		uint[] shiftedResponse;
		SmuStatus bareStatus = _service!.ReadCurveMarginRaw(bareArg, out bareResponse);
		SmuStatus shiftedStatus = _service.ReadCurveMarginRaw(shiftedArg, out shiftedResponse);
		int? bareMargin = ((bareStatus == SmuStatus.OK) ? RyzenSmuService.DecodeCurveMargin(bareResponse[0]) : ((int?)null));
		int? shiftedMargin = ((shiftedStatus == SmuStatus.OK) ? RyzenSmuService.DecodeCurveMargin(shiftedResponse[0]) : ((int?)null));
		W("  probe core" + probeCore + " (arg0 bare=0x" + bareArg.ToString("X8", CultureInfo.InvariantCulture) + ", shifted=0x" + shiftedArg.ToString("X8", CultureInfo.InvariantCulture) + ")");
		W("    bare    : status=" + DescribeStatus(bareStatus) + " resp=" + HexWords(bareResponse) + " -> CO " + Fmt(bareMargin));
		W("    shifted : status=" + DescribeStatus(shiftedStatus) + " resp=" + HexWords(shiftedResponse) + " -> CO " + Fmt(shiftedMargin));
		F("  arg0 编码探针（核" + probeCore + "）：裸核号 " + DescribeStatus(bareStatus) + " -> CO " + Fmt(bareMargin)
			+ "；core<<20 " + DescribeStatus(shiftedStatus) + " -> CO " + Fmt(shiftedMargin));
		if (afterWrite)
		{
			bool bareOk = bareStatus == SmuStatus.OK;
			bool shiftedOk = shiftedStatus == SmuStatus.OK;
			bool bareMatches = bareMargin.HasValue && bareMargin.Value == margin;
			bool shiftedMatches = shiftedMargin.HasValue && shiftedMargin.Value == margin;
			if (bareOk && !shiftedOk)
			{
				verdict = "BARE";
				note = "bare core index accepted, core<<20 rejected";
			}
			else if (!bareOk && shiftedOk)
			{
				verdict = "SHIFTED";
				note = "core<<20 accepted, bare core index rejected";
			}
			else if (bareOk && shiftedOk && bareMatches && !shiftedMatches)
			{
				verdict = "BARE";
				note = "both accepted, but only the bare core index returns the written value";
			}
			else if (bareOk && shiftedOk && shiftedMatches && !bareMatches)
			{
				verdict = "SHIFTED";
				note = "both accepted, but only core<<20 returns the written value";
			}
			else if (bareOk && shiftedOk && bareMatches && shiftedMatches)
			{
				verdict = "UNDETERMINED";
				note = "both encodings return the written value: the SMU seems to mask the extra bits, so the encoding cannot be told apart this way";
			}
			else
			{
				verdict = "UNDETERMINED";
				note = "neither encoding clearly matches the written value (see raw response above)";
			}
		}
	}

	private static bool AllReadable(List<int?> values)
	{
		if (values.Count == 0)
		{
			return false;
		}
		foreach (int? value in values)
		{
			if (!value.HasValue)
			{
				return false;
			}
		}
		return true;
	}

	private static string HexWords(uint[] words)
	{
		StringBuilder stringBuilder = new StringBuilder("[");
		for (int i = 0; i < words.Length; i++)
		{
			if (i > 0)
			{
				stringBuilder.Append(' ');
			}
			stringBuilder.Append("0x").Append(words[i].ToString("X8", CultureInfo.InvariantCulture));
		}
		return stringBuilder.Append(']').ToString();
	}

	private static string DescribeStatus(SmuStatus status)
	{
		return status + "(0x" + ((uint)status).ToString("X2", CultureInfo.InvariantCulture) + ")";
	}

	private static string Fmt(int? value)
	{
		return value.HasValue ? value.Value.ToString(CultureInfo.InvariantCulture) : "?";
	}

	private static string DescribeCurveCommand((bool Psmu, uint Command)? command, int margin)
	{
		if (!command.HasValue)
		{
			return "none for this codename";
		}
		return (command.Value.Psmu ? "PSMU 0x" : "MP1 0x") + command.Value.Command.ToString("X2", CultureInfo.InvariantCulture)
			+ " (all cores, arg=0x" + ((uint)(margin & 0xFFFFF)).ToString("X8", CultureInfo.InvariantCulture) + ")";
	}

	// 控制台：只输出 ASCII（cmd 默认代码页下中文会乱码）；文件：中文 + 英文都能有。
	private static void W(string line)
	{
		Report.AppendLine(line);
		try
		{
			Console.WriteLine(line);
		}
		catch (Exception)
		{
		}
	}

	private static void F(string line)
	{
		Report.AppendLine(line);
	}

	private static string WriteReportFile(string? requestedOut)
	{
		string path = ResolveReportPath(requestedOut);
		try
		{
			File.WriteAllText(path, Report.ToString(), new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
			return path;
		}
		catch (Exception ex)
		{
			try
			{
				string fallback = Path.Combine(Path.GetTempPath(), "SMU-CO-roundtrip-fallback.txt");
				File.WriteAllText(fallback, Report.ToString(), new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
				Console.WriteLine("[WARN] could not write " + path + " (" + ex.Message + "); wrote " + fallback);
				return fallback;
			}
			catch (Exception ex2)
			{
				Console.WriteLine("[WARN] report file write failed: " + ex2.Message);
				return "";
			}
		}
	}

	private static string ResolveReportPath(string? requestedOut)
	{
		if (!string.IsNullOrWhiteSpace(requestedOut))
		{
			return Path.GetFullPath(requestedOut);
		}
		string directory = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
		if (string.IsNullOrEmpty(directory) || !Directory.Exists(directory))
		{
			directory = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
		}
		if (string.IsNullOrEmpty(directory) || !Directory.Exists(directory))
		{
			directory = Path.GetTempPath();
		}
		return Path.Combine(directory, "SMU-CO-roundtrip-" + DateTime.Now.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture) + ".txt");
	}

	// 被 cmd/.bat 直接启动时，WinExe 默认不挂到父控制台 -> Console.WriteLine 会丢。
	// 挂上去以后重新取一次标准输出流，这样用户在命令行里也能看到结论。
	internal static void TryAttachParentConsole()
	{
		try
		{
			if (AttachConsole(AttachParentProcess))
			{
				StreamWriter streamWriter = new StreamWriter(Console.OpenStandardOutput(), Console.OutputEncoding)
				{
					AutoFlush = true
				};
				Console.SetOut(streamWriter);
			}
		}
		catch (Exception)
		{
		}
	}
}
