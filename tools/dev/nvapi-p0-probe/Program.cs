using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Principal;
using System.Threading;
using MechrevoLite.Gpu;
using NvAPIWrapper.GPU;
using NvAPIWrapper.Native;
using NvAPIWrapper.Native.GPU;
using NvAPIWrapper.Native.GPU.Structures;
using NvAPIWrapper.Native.Interfaces.GPU;

namespace NvApiP0Probe;

/// <summary>
/// 独立 NVAPI P-States 20 探针（宝龙达 GPU 核心/显存偏移验证用）。
/// <para>⚠️ 默认【只读】：只调 <c>NvAPI_GPU_GetPstates20</c>（经 NvAPIWrapper），不写任何偏移。</para>
/// <para>⭐ <c>--write</c> 流程（硬约束 ±100 MHz）：+50 → 回读 → 一致才 +100 → 回读 →
/// <b>finally 里强制归零 0/0 → 回读确认</b>（归零失败会再写一次）⇒ 退出码反映结果。</para>
/// <para>⭐ <c>--interop</c> / <c>--interop-write</c>：链接主工程的 <c>NvapiPStates.cs</c> 原文件，
/// 实测「手写 NVAPI 类 + NvAPIWrapper 同一进程」的互操作（本机曾复现 Unload 导致 wrapper
/// <c>EnumPhysicalGPUs</c> 0xC0000005；修正后 10/10 一致）。</para>
/// <para>⭐ <c>--reset</c> 只归零；<c>--apply N</c> 应用 +N/+N 并**故意不归零**（人工验证用，验证后手动 --reset）；
/// <c>--log PATH</c> 指定输出文件（提权时无法重定向 stdout，必须靠它）。</para>
/// <para>⚠️ 普通读/写模式用 NvAPIWrapper（与主工程同一 DLL）；interop 模式才用主工程手写结构体类。</para>
/// </summary>
internal static class Program
{
	private const int HardLimitMhz = 100;

	private static readonly List<string> Lines = new List<string>();

	private static string _logPath;

	private static int Main(string[] args)
	{
		bool write = HasFlag(args, "--write");
		bool reset = HasFlag(args, "--reset");
		bool interop = HasFlag(args, "--interop");
		bool interopWrite = HasFlag(args, "--interop-write");
		_logPath = ArgValue(args, "--log");
		int delta = 50;
		string deltaText = ArgValue(args, "--delta");
		if (deltaText != null && int.TryParse(deltaText, out int parsed))
		{
			delta = Math.Abs(parsed);
		}
		delta = Math.Clamp(delta, 1, HardLimitMhz / 2);
		int apply = -1;
		string applyText = ArgValue(args, "--apply");
		if (applyText != null && int.TryParse(applyText, out int applyParsed) && applyParsed != 0)
		{
			apply = Math.Clamp(Math.Abs(applyParsed), 1, HardLimitMhz);
		}
		try
		{
			Log($"=== nvapi-p0-probe ===  PID={Environment.ProcessId} · admin={IsElevated()} · {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
			Log($"args = {(args.Length == 0 ? "(none)" : string.Join(" ", args))}");
			if (apply > 0)
			{
				if (write || reset || interop || interopWrite)
				{
					Log("[args] --apply 不能与其他模式同时用");
					return 3;
				}
				return RunApply(apply);
			}
			if (interop || interopWrite)
			{
				return RunInterop(interopWrite);
			}
			return Run(write, reset, delta);
		}
		catch (Exception ex)
		{
			Log("[FATAL] " + ex);
			return 2;
		}
	}

	private static int Run(bool write, bool reset, int delta)
	{
		if (write && reset)
		{
			Log("[args] --write 与 --reset 不能同时用");
			return 3;
		}
		Log("mode = " + (write ? $"WRITE sequence (+{delta} then +{2 * delta} if first ok, forced reset in finally)" : (reset ? "RESET core/mem -> 0/0" : "READ-ONLY")));
		PhysicalGPU[] gpus = PhysicalGPU.GetPhysicalGPUs();
		if (gpus == null || gpus.Length == 0)
		{
			Log("[gpu] 枚举不到物理 GPU ⇒ 停止");
			return 4;
		}
		Log($"[gpu] PhysicalGPU.GetPhysicalGPUs() => {gpus.Length} 个");
		for (int i = 0; i < gpus.Length; i++)
		{
			Log($"  [{i}] {gpus[i].FullName} · SystemType={gpus[i].SystemType} · Handle={gpus[i].Handle}");
		}
		PhysicalGPU gpu = gpus.FirstOrDefault((PhysicalGPU g) => g.SystemType == SystemType.Laptop) ?? gpus[0];
		Log("[gpu] 选用：" + gpu.FullName);
		Log("");
		Log("--- 只读：P-States 20 ---");
		IPerformanceStates20Info info = GPUApi.GetPerformanceStates20(gpu.Handle);
		Log($"IsEditable = {info.IsEditable}");
		foreach (KeyValuePair<PerformanceStateId, IPerformanceStates20ClockEntry[]> kv in info.Clocks.OrderBy((KeyValuePair<PerformanceStateId, IPerformanceStates20ClockEntry[]> p) => p.Key))
		{
			Log("  " + kv.Key + ":");
			foreach (IPerformanceStates20ClockEntry e in kv.Value)
			{
				PerformanceStates20ParameterDelta d = e.FrequencyDeltaInkHz;
				Log($"    {DomainName(e.DomainId),-8} editable={e.IsEditable,-5} current={d.DeltaValue / 1000.0,8:0.###} MHz  range={d.DeltaRange.Minimum / 1000.0,8:0.###}..{d.DeltaRange.Maximum / 1000.0,8:0.###} MHz");
			}
		}
		Log("");
		if (!TryReadP0(gpu.Handle, out int coreNow, out int memNow, out string readErr))
		{
			Log("[P0] 读不到：" + readErr);
			return 5;
		}
		Log($"[P0] 当前 core={coreNow} MHz · mem={memNow} MHz");
		if (!write && !reset)
		{
			Log("");
			Log("[read-only] 完成：未发出任何写调用。");
			return 0;
		}
		if (!IsElevated())
		{
			Log("[write] ⚠️ 当前进程不是管理员 ⇒ NVAPI 写会失败（NVAPI_INVALID_USER_PRIVILEGE），未发出任何写调用。");
			return 6;
		}
		Log("");
		if (reset)
		{
			bool resetOk = ResetCoreMem(gpu.Handle);
			if (!resetOk)
			{
				Log("⚠️ 归零未确认 ⇒ 再写一次 0/0");
				resetOk = ResetCoreMem(gpu.Handle);
			}
			Log(resetOk ? "[reset] 归零已确认 0/0 ✅" : "[reset] ⛔ 归零后回读仍不是 0/0 ⇒ 必须人工复核！");
			return resetOk ? 0 : 7;
		}
		return WriteSequence(gpu.Handle, delta);
	}

	/// <summary>
	/// ⭐ 互操作实测（主工程风险点，已复现过一次 0xC0000005）：`NvapiPStates`（手写结构体）与
	/// `NvAPIWrapper`（LM 里 NvidiaGpuControl 用、缓存函数指针、从不 Initialize）在**同一进程**
	/// 共用一份 nvapi64 状态。
	/// <para>2026-10-18 旧版「每次 Initialize/Unload」在本探针里稳定复现：
	/// raw Close(Unload) 后 wrapper 下次 `EnumPhysicalGPUs()` 直接 0xC0000005。
	/// 修正 = raw 类只 Initialize 一次、永不 Unload；本模式就是回归测试。</para>
	/// <para>只读：wrapper 读 → raw 读 → 交替 10 轮；写入（--interop-write，需管理员）：
	/// raw Set(+50/+50) → wrapper 读 → raw 读 → finally raw Set(0/0) → wrapper 读。</para>
	/// <para>退出码：0 = 全部一致且不崩；10 = 只读互操作不一致；8 = raw 写后 wrapper 读不到；
	/// 9 = raw 写失败；7 = 归零未确认；6 = 写入需要管理员。</para>
	/// </summary>
	private static int RunInterop(bool doWrite)
	{
		Log("=== interop: NvapiPStates(raw) vs NvAPIWrapper（同一进程；前者只 Initialize 一次、永不 Unload，后者缓存函数指针）===");
		int failures = 0;
		Log("--- 1) NvAPIWrapper 读 P0（LM 的 NvidiaGpuControl 同路径）---");
		bool w1Ok = TryReadWrapperP0(out int w1Core, out int w1Mem, out string w1Err);
		Log(w1Ok ? $"[wrapper] core={w1Core} mem={w1Mem}" : "[wrapper] 失败: " + w1Err);
		if (!w1Ok)
		{
			failures++;
		}
		Log("--- 2) NvapiPStates(raw) 读 P0 ---");
		bool r1Ok = NvapiPStates.TryGetP0Offsets(out int r1Core, out int r1Mem);
		if (r1Ok)
		{
			string range = NvapiPStates.TryGetP0OffsetRange(out int cMin, out int cMax, out int mMin, out int mMax)
				? $" range core={cMin}..{cMax} mem={mMin}..{mMax}"
				: " range=读取失败(" + NvapiPStates.LastError + ")";
			Log($"[raw] core={r1Core} mem={r1Mem}{range}");
		}
		else
		{
			Log("[raw] 失败: " + NvapiPStates.LastError);
			failures++;
		}
		if (w1Ok && r1Ok && (w1Core != r1Core || w1Mem != r1Mem))
		{
			Log($"[MISMATCH] wrapper({w1Core},{w1Mem}) != raw({r1Core},{r1Mem})");
			failures++;
		}
		Log("--- 3) 交替 10 轮：raw 每次 Initialize→…→Close，wrapper 立刻在同一进程读 ---");
		for (int i = 0; i < 10; i++)
		{
			bool rawOk = NvapiPStates.TryGetP0Offsets(out int rawCore, out int rawMem);
			string rawErr = NvapiPStates.LastError;
			bool wraOk = TryReadWrapperP0(out int wraCore, out int wraMem, out string wraErr);
			bool same = rawOk && wraOk && rawCore == wraCore && rawMem == wraMem;
			if (!same)
			{
				failures++;
			}
			Log($"  [{i}] raw={(rawOk ? rawCore + "/" + rawMem : "FAIL:" + rawErr)} · wrapper={(wraOk ? wraCore + "/" + wraMem : "FAIL:" + wraErr)} ⇒ {(same ? "一致 ✅" : "⚠️ 不一致/失败")}");
		}
		if (!doWrite)
		{
			Log(failures == 0 ? "[interop] 只读互操作：10 轮全部一致、无崩溃 ✅" : $"[interop] ⚠️ 只读互操作有 {failures} 轮失败");
			Log(failures == 0 ? "EXIT=0" : "EXIT=10");
			return failures == 0 ? 0 : 10;
		}
		Log("");
		if (!IsElevated())
		{
			Log("[interop-write] ⚠️ 需要管理员，未发出写调用。");
			return 6;
		}
		bool setOk = false;
		bool wrapperSaw = false;
		bool resetOk = false;
		try
		{
			if (NvapiPStates.TryGetP0Offsets(out int baseCore, out int baseMem) && (baseCore != 0 || baseMem != 0))
			{
				Log($"[interop-write] 进入时偏移非 0（{baseCore}/{baseMem}）⇒ 先 raw 归零");
				if (!NvapiPStates.TrySetP0Offsets(0, 0, out int pc, out int pm))
				{
					Log("[interop-write] ⚠️ 前置归零失败: " + NvapiPStates.LastError + " ⇒ 不写");
					return 7;
				}
				Log($"[interop-write] 前置归零回读 core={pc} mem={pm}");
			}
			Log("");
			Log("--- 4) raw Set(+50/+50) ---");
			setOk = NvapiPStates.TrySetP0Offsets(50, 50, out int ac, out int am);
			Log($"[raw] Set core=50 mem=50 ⇒ ok={setOk} · raw 回读 core={ac} mem={am}"
				+ (string.IsNullOrEmpty(NvapiPStates.LastError) ? "" : " · " + NvapiPStates.LastError));
			Log("--- 5) 紧接着 NvAPIWrapper 读 P0（期望 50/50，验证 Unload 是否把 wrapper 打死）---");
			wrapperSaw = TryReadWrapperP0(out int wc, out int wm, out string wErr2);
			Log(wrapperSaw ? $"[wrapper] core={wc} mem={wm} ⇒ {(wc == 50 && wm == 50 ? "50/50 ✅（raw 与 wrapper 互不干扰）" : "⚠️ 与 raw 写入不一致")}" : "[wrapper] 读失败: " + wErr2);
		}
		finally
		{
			Log("");
			Log("--- finally: raw Set(0/0) + wrapper 复核 ---");
			resetOk = NvapiPStates.TrySetP0Offsets(0, 0, out int zc, out int zm);
			Log($"[raw] reset ⇒ ok={resetOk} · raw 回读 core={zc} mem={zm}"
				+ (string.IsNullOrEmpty(NvapiPStates.LastError) ? "" : " · " + NvapiPStates.LastError));
			bool wrapperZero = TryReadWrapperP0(out int wzc, out int wzm, out string wzErr);
			Log(wrapperZero ? $"[wrapper] core={wzc} mem={wzm}" : "[wrapper] 读失败: " + wzErr);
			resetOk = resetOk && zc == 0 && zm == 0 && wrapperZero && wzc == 0 && wzm == 0;
			Log(resetOk ? "[interop-write] 归零经 raw + wrapper 双路确认 0/0 ✅" : "[interop-write] ⛔ 归零未确认（raw/wrapper 见上）");
		}
		Log("");
		if (!setOk)
		{
			Log("EXIT=9（raw 写未被接受）");
			return 9;
		}
		if (!wrapperSaw)
		{
			Log("EXIT=8（raw 写成功但 wrapper 读失败 ⇒ 互操作有问题）");
			return 8;
		}
		if (!resetOk)
		{
			Log("EXIT=7（归零未确认）");
			return 7;
		}
		Log("EXIT=0（raw 写 + wrapper 读 + 归零双路确认，同一进程互不干扰）");
		return 0;
	}

	/// <summary>用 NvAPIWrapper（LM 的 NvidiaGpuControl 同路径）读 P0 core/mem 偏移（MHz）。</summary>
	private static bool TryReadWrapperP0(out int core, out int mem, out string error)
	{
		core = 0;
		mem = 0;
		error = null;
		try
		{
			PhysicalGPU[] gpus = PhysicalGPU.GetPhysicalGPUs();
			PhysicalGPU gpu = gpus.FirstOrDefault((PhysicalGPU g) => g.SystemType == SystemType.Laptop) ?? gpus.FirstOrDefault();
			if (gpu == null)
			{
				error = "wrapper 枚举不到 GPU";
				return false;
			}
			IPerformanceStates20Info info = GPUApi.GetPerformanceStates20(gpu.Handle);
			if (!info.Clocks.TryGetValue(PerformanceStateId.P0_3DPerformance, out IPerformanceStates20ClockEntry[] clocks))
			{
				error = "wrapper 读不到 P0";
				return false;
			}
			IPerformanceStates20ClockEntry coreEntry = clocks.FirstOrDefault((IPerformanceStates20ClockEntry c) => c.DomainId == PublicClockDomain.Graphics);
			IPerformanceStates20ClockEntry memEntry = clocks.FirstOrDefault((IPerformanceStates20ClockEntry c) => c.DomainId == PublicClockDomain.Memory);
			if (coreEntry == null || memEntry == null)
			{
				error = "wrapper P0 缺 Graphics/Memory";
				return false;
			}
			core = (int)Math.Round(coreEntry.FrequencyDeltaInkHz.DeltaValue / 1000.0, MidpointRounding.AwayFromZero);
			mem = (int)Math.Round(memEntry.FrequencyDeltaInkHz.DeltaValue / 1000.0, MidpointRounding.AwayFromZero);
			return true;
		}
		catch (Exception ex)
		{
			error = ex.GetType().Name + ": " + ex.Message;
			return false;
		}
	}

	/// <summary>
	/// +delta → 回读；完全一致才 +2*delta → 回读；**无论成功失败，finally 里归零**。
	/// 退出码：0 = 两档写+回读都一致且归零确认；8 = 第一档成功但第二档未确认；
	/// 9 = 第一档未确认；7 = 归零未确认（最高优先级报警）；2 = 异常。
	/// </summary>
	private static int WriteSequence(PhysicalGPUHandle handle, int delta)
	{
		bool firstOk = false;
		bool secondOk = false;
		bool resetOk = false;
		int lastCore = 0;
		int lastMem = 0;
		if (!TryReadP0(handle, out int curCore, out int curMem, out string err))
		{
			Log("[write] 进入时读不到 P0：" + err + " ⇒ 不写");
			return 5;
		}
		if (curCore != 0 || curMem != 0)
		{
			Log($"⚠️ 进入时偏移非 0（core={curCore} mem={curMem}）⇒ 先归零再测");
			if (!ResetCoreMem(handle))
			{
				Log("[write] ⚠️ 前置归零没确认 ⇒ 不做写入测试（避免在未知基线上叠加）");
				return 7;
			}
		}
		try
		{
			Log("");
			Log("--- 用例 1：core=+" + delta + " mem=+" + delta + " MHz（绝对偏移）---");
			firstOk = TryWriteCase(handle, delta, out lastCore, out lastMem);
			if (firstOk)
			{
				Log("✅ +" + delta + " 回读完全一致 ⇒ 按授权继续用例 2");
				Log("");
				Log("--- 用例 2：core=+" + (2 * delta) + " mem=+" + (2 * delta) + " MHz（不超过 ±" + HardLimitMhz + "）---");
				secondOk = TryWriteCase(handle, 2 * delta, out lastCore, out lastMem);
			}
			else
			{
				Log("⛔ +" + delta + " 回读不一致 ⇒ 按纪律**不加大**，跳过用例 2（实际回读 core=" + lastCore + " mem=" + lastMem + "）");
			}
		}
		finally
		{
			Log("");
			Log("--- finally：强制归零 core=0 mem=0（无论上面成败）---");
			resetOk = ResetCoreMem(handle);
			if (!resetOk)
			{
				Log("⚠️⚠️ 归零未被回读确认 ⇒ 立即再写一次 0/0");
				Log("--- 第二次归零 ---");
				resetOk = ResetCoreMem(handle);
			}
			Log(resetOk ? "[reset] 归零已确认 0/0 ✅" : "[reset] ⛔ 归零后回读仍不是 0/0 ⇒ 必须人工复核！");
		}
		Log("");
		Log("=== 汇总 ===");
		Log($"  第 1 档 +{delta} MHz: {(firstOk ? "写+回读一致 ✅" : "未确认 ❌")}");
		Log($"  第 2 档 +{2 * delta} MHz: {(firstOk ? (secondOk ? "写+回读一致 ✅" : "未确认 ❌") : "未测试（前一档未一致）")}");
		Log($"  归零 0/0: {(resetOk ? "确认 ✅" : "未确认 ⛔")}");
		if (!resetOk)
		{
			Log("EXIT=7（归零未确认）");
			return 7;
		}
		if (!firstOk)
		{
			Log("EXIT=9（第一档未确认，归零已确认）");
			return 9;
		}
		if (!secondOk)
		{
			Log("EXIT=8（第一档成功，第二档未确认，归零已确认）");
			return 8;
		}
		Log("EXIT=0（两档写+回读都一致，归零已确认）");
		return 0;
	}

	/// <summary>
	/// ⭐ 人工验证模式：写入 core/mem = +N / +N MHz 并回读确认，**故意不自动归零**
	/// （保持偏移供机主用外部工具/体感验证）。验证完请运行 <c>--reset</c>。
	/// <para>⚠️ 若写入未被回读确认，仍会立即归零兜底：不把未知状态留给机主。</para>
	/// <para>退出码：0=已应用并回读一致；9=未确认（已归零兜底）；6=需要管理员；5=读不到 P0。</para>
	/// </summary>
	private static int RunApply(int mhz)
	{
		Log($"mode = APPLY core/mem = +{mhz} MHz（**不自动归零**，供人工验证；验证后请运行 --reset）");
		PhysicalGPU[] gpus = PhysicalGPU.GetPhysicalGPUs();
		if (gpus == null || gpus.Length == 0)
		{
			Log("[gpu] 枚举不到物理 GPU ⇒ 停止");
			return 4;
		}
		PhysicalGPU gpu = gpus.FirstOrDefault((PhysicalGPU g) => g.SystemType == SystemType.Laptop) ?? gpus[0];
		Log("[gpu] 选用：" + gpu.FullName);
		if (!TryReadP0(gpu.Handle, out int coreNow, out int memNow, out string err))
		{
			Log("[P0] 读不到：" + err);
			return 5;
		}
		Log($"[P0] 写前 core={coreNow} mem={memNow} MHz");
		if (!IsElevated())
		{
			Log("[apply] ⚠️ 当前进程不是管理员 ⇒ 不写（NVAPI 写会被拒绝）。");
			return 6;
		}
		if (coreNow != 0 || memNow != 0)
		{
			Log($"⚠️ 进入时偏移非 0（{coreNow}/{memNow}）⇒ 先归零再做本用例");
			if (!ResetCoreMem(gpu.Handle))
			{
				Log("[apply] ⚠️ 前置归零未确认 ⇒ 不写");
				return 7;
			}
		}
		Log("");
		Log($"--- apply：core=+{mhz} mem=+{mhz} MHz（单次 SetPstates20，两域一起）---");
		bool ok = TryWriteAndVerify(gpu.Handle, mhz, "apply(+" + mhz + ")", out int readCore, out int readMem);
		if (!ok)
		{
			Log("⚠️ 写入未被回读确认 ⇒ 立即归零兜底（不把未知状态留给机主）");
			bool zero = ResetCoreMem(gpu.Handle);
			Log(zero ? "[apply] 已归零 0/0 ✅（本次 apply 失败）" : "[apply] ⛔ 归零也未确认，请人工复核！");
			return 9;
		}
		Log($"[apply] ✅ 已应用并回读确认 core={readCore} mem={readMem} MHz");
		Log("⚠️ 本模式故意不归零：偏移保持到驱动复位/重启；人工验证完请运行 --reset 归零。");
		Log("EXIT=0");
		return 0;
	}

	private static bool TryWriteCase(PhysicalGPUHandle handle, int targetMhz, out int readCore, out int readMem)
	{
		return TryWriteAndVerify(handle, targetMhz, "case +" + targetMhz, out readCore, out readMem);
	}

	/// <summary>写入 core=mem=targetMhz 并多次回读，只有完全一致才返回 true（回读值通过 out 返回）。</summary>
	private static bool TryWriteAndVerify(PhysicalGPUHandle handle, int targetMhz, string tag, out int readCore, out int readMem)
	{
		readCore = 0;
		readMem = 0;
		if (Math.Abs(targetMhz) > HardLimitMhz)
		{
			Log($"⛔ 目标 {targetMhz} MHz 超出硬约束 ±{HardLimitMhz} ⇒ 拒绝写入");
			return false;
		}
		try
		{
			WriteP0(handle, targetMhz, targetMhz);
			Log($"  [{tag}] SetPstates20(core={targetMhz}, mem={targetMhz}) 调用成功（驱动已接受，无异常）");
		}
		catch (Exception ex)
		{
			Log($"  [{tag}] SetPstates20 抛异常 ⇒ 写入未被接受：{ex.Message}");
			return false;
		}
		int[] delays = new int[6] { 100, 150, 250, 500, 1000, 2000 };
		for (int i = 0; i < delays.Length; i++)
		{
			Thread.Sleep(delays[i]);
			if (!TryReadP0(handle, out readCore, out readMem, out string err))
			{
				Log($"  [{tag}] 第 {i + 1} 次回读失败：{err}");
				continue;
			}
			bool match = readCore == targetMhz && readMem == targetMhz;
			Log($"  [{tag}] 第 {i + 1} 次回读 core={readCore} mem={readMem}（期望 {targetMhz}/{targetMhz}）⇒ {(match ? "匹配 ✅" : "未匹配")}");
			if (match)
			{
				return true;
			}
		}
		return false;
	}

	private static bool ResetCoreMem(PhysicalGPUHandle handle)
	{
		try
		{
			WriteP0(handle, 0, 0);
			Log("  [reset] SetPstates20(core=0, mem=0) 调用成功（无异常）");
		}
		catch (Exception ex)
		{
			Log("  [reset] SetPstates20 抛异常：" + ex.Message);
			return false;
		}
		int[] delays = new int[6] { 100, 150, 250, 500, 1000, 2000 };
		for (int i = 0; i < delays.Length; i++)
		{
			Thread.Sleep(delays[i]);
			if (!TryReadP0(handle, out int core, out int mem, out string err))
			{
				Log($"  [reset] 第 {i + 1} 次回读失败：{err}");
				continue;
			}
			bool match = core == 0 && mem == 0;
			Log($"  [reset] 第 {i + 1} 次回读 core={core} mem={mem} ⇒ {(match ? "0/0 ✅" : "未归零")}");
			if (match)
			{
				return true;
			}
		}
		return false;
	}

	private static void WriteP0(PhysicalGPUHandle handle, int coreMhz, int memMhz)
	{
		PerformanceStates20ClockEntryV1 coreEntry = new PerformanceStates20ClockEntryV1(PublicClockDomain.Graphics, new PerformanceStates20ParameterDelta(coreMhz * 1000));
		PerformanceStates20ClockEntryV1 memEntry = new PerformanceStates20ClockEntryV1(PublicClockDomain.Memory, new PerformanceStates20ParameterDelta(memMhz * 1000));
		PerformanceStates20ClockEntryV1[] clocks = new PerformanceStates20ClockEntryV1[2] { coreEntry, memEntry };
		PerformanceStates20InfoV1.PerformanceState20[] pstates = new PerformanceStates20InfoV1.PerformanceState20[1]
		{
			new PerformanceStates20InfoV1.PerformanceState20(PerformanceStateId.P0_3DPerformance, clocks, Array.Empty<PerformanceStates20BaseVoltageEntryV1>())
		};
		// ⚠️ 关键：第二个参数是"时钟项个数"，必须等于 clocks.Length（2）。
		//   原来写死 1u + 载荷 2 项 ⇒ 驱动只处理第 1 项（Graphics），Memory 被静默丢弃
		//   （本机实测：core +50 生效、mem 恒 0）。见 2026-10-18 探针日志。
		GPUApi.SetPerformanceStates20(performanceStates20Info: new PerformanceStates20InfoV1(pstates, (uint)clocks.Length, 0u), physicalGPUHandle: handle);
	}

	private static bool TryReadP0(PhysicalGPUHandle handle, out int core, out int mem, out string error)
	{
		core = 0;
		mem = 0;
		error = null;
		try
		{
			IPerformanceStates20Info info = GPUApi.GetPerformanceStates20(handle);
			if (!info.Clocks.TryGetValue(PerformanceStateId.P0_3DPerformance, out IPerformanceStates20ClockEntry[] clocks))
			{
				error = "P0 不存在";
				return false;
			}
			IPerformanceStates20ClockEntry coreEntry = clocks.FirstOrDefault((IPerformanceStates20ClockEntry c) => c.DomainId == PublicClockDomain.Graphics);
			IPerformanceStates20ClockEntry memEntry = clocks.FirstOrDefault((IPerformanceStates20ClockEntry c) => c.DomainId == PublicClockDomain.Memory);
			if (coreEntry == null || memEntry == null)
			{
				error = "P0 缺 Graphics/Memory 时钟项";
				return false;
			}
			core = (int)Math.Round(coreEntry.FrequencyDeltaInkHz.DeltaValue / 1000.0, MidpointRounding.AwayFromZero);
			mem = (int)Math.Round(memEntry.FrequencyDeltaInkHz.DeltaValue / 1000.0, MidpointRounding.AwayFromZero);
			return true;
		}
		catch (Exception ex)
		{
			error = ex.Message;
			return false;
		}
	}

	private static bool HasFlag(string[] args, string name)
	{
		return args.Any((string a) => a.Equals(name, StringComparison.OrdinalIgnoreCase));
	}

	private static string ArgValue(string[] args, string name)
	{
		for (int i = 0; i < args.Length - 1; i++)
		{
			if (args[i].Equals(name, StringComparison.OrdinalIgnoreCase))
			{
				return args[i + 1];
			}
		}
		return null;
	}

	private static bool IsElevated()
	{
		try
		{
			using WindowsIdentity identity = WindowsIdentity.GetCurrent();
			return new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
		}
		catch
		{
			return false;
		}
	}

	private static string DomainName(PublicClockDomain d)
	{
		return d switch
		{
			PublicClockDomain.Graphics => "Graphics",
			PublicClockDomain.Memory => "Memory",
			PublicClockDomain.Processor => "Processor",
			PublicClockDomain.Video => "Video",
			_ => d.ToString()
		};
	}

	/// <summary>控制台 + `--log` 文件 + LM 日志（best-effort）。提权进程无法重定向 stdout，所以文件是唯一可靠出口。</summary>
	private static void Log(string line)
	{
		Console.WriteLine(line);
		Lines.Add(line);
		if (_logPath != null)
		{
			try
			{
				File.AppendAllText(_logPath, line + Environment.NewLine);
			}
			catch (Exception ex)
			{
				Console.WriteLine("[log] 写日志失败: " + ex.Message);
			}
		}
		AppendLmLog(line);
	}

	/// <summary>把关键行同步写进 LM 的 log.txt（同一个用户配置目录；失败不影响探针）。</summary>
	private static void AppendLmLog(string line)
	{
		try
		{
			string dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "MechrevoLite");
			if (!Directory.Exists(dir))
			{
				return;
			}
			string file = Path.Combine(dir, "log.txt");
			using FileStream fs = new FileStream(file, FileMode.Append, FileAccess.Write, FileShare.ReadWrite | FileShare.Delete);
			using StreamWriter sw = new StreamWriter(fs);
			sw.WriteLine($"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}: [nvapi-p0-probe] {line}");
		}
		catch
		{
			// best-effort
		}
	}
}
