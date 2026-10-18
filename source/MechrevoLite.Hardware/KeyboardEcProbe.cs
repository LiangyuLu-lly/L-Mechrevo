// ⭐⭐ 2026-10-19（30 系「FN 能调亮度」· EC 取证 v3：ACPI/LPC 双通道 + 测速 + 窄范围）：
// `--kbd-ec-probe [秒数] [--lpc]` —— **只读**扫描 EC RAM，找出按 Fn 调亮度时**变化的地址**。
//
// v2 实测（ACPI 通道）：54 轮/60s · 16B/轮 · 单轮 730~1460ms · 失败 21 · 总变化 0
//   ⇒ 单字节 ~45ms、全量一遍 ~50s，跟不上 Fn ⇒ 0 变化**不能证明 EC 没有亮度字节**。
// v3：
//   ① 新增 `--lpc`（走现有 EcLpcIo：端口 0x4E/0x4F 读路径）；LPC 打开失败 ⇒ 自动回退 ACPI 并记日志；
//   ② 先测速：32 次采样，报告「LPC 通道单字节平均耗时 = …ms」；>5ms ⇒ 直接提示“LPC 也跟不上”，提前结束；
//   ③ 收窄到最可能的 3 段（约 560B）：0x0790–0x07FF / 0x1800–0x18FF / 0x8300–0x83BF；
//   ④ 单轮预算 ≤300ms（按实测速度分块轮转）；发现变化 ⇒ 专注模式（每 50ms 盯变化地址）；
//   ⑤ 报告额外记录：通道/覆盖段/平均轮耗时/失败数，以及 LPC 与 ACPI 同地址视图对照（判断 banked）。
//
// ⚠️ 平台闸：仅宝龙达 30/40 系可跑（板号白名单 + GPU 代数）；其它机型打印拒绝并正常退出（exit 0）。
// ⚠️ 全程只读：ACPI 走 EcPortIo.TryReadByteFast（0x92）；LPC 走 EcLpcIo.TryReadByte（只读）；
//    ⛔ 0 写入、⛔ 不碰 WMI 写、⛔ 不改任何状态。

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using MechrevoLite.Gpu;

namespace MechrevoLite.Hardware;

/// <summary>
/// ⭐ 只读 EC 取证（v3 · 双通道）：ACPI/LPC 可切换、先测速、窄范围快速轮转，用于定位 FN 调亮度的寄存器 ✅。
/// <para>⚠️ 全程只读；⛔ 不写 EC / WMI，⛔ 不依赖 GCU/宝龙达 WMI 实例 ✅。</para>
/// </summary>
internal static class KeyboardEcProbe
{
	/// <summary>⭐ 单块字节数 ✅。</summary>
	private const int ChunkBytes = 16;

	/// <summary>⭐ 单轮预算（毫秒；机主要求 ≤300ms）✅。</summary>
	private const int ScanCycleBudgetMs = 300;

	/// <summary>⭐ ACPI 快速读单步超时（毫秒；仅探针用）✅。</summary>
	private const int FastTimeoutMs = 4;

	/// <summary>⭐ 专注模式轮询间隔（毫秒）✅。</summary>
	private const int FocusPollMs = 50;

	/// <summary>⭐ LPC 测速阈值：超过则提示“也跟不上”并提前结束 ✅。</summary>
	private const double LpcTooSlowMs = 5.0;

	/// <summary>⭐ 单个地址的变化统计（报告用）✅。</summary>
	private sealed class EcAddressStat
	{
		internal int Address;

		internal int LastValue = -1;

		internal int Changes;

		/// <summary>⭐ 值序列（初始值 + 每次变化后的值；最多 12 个）✅。</summary>
		internal readonly List<int> Values = new List<int>(12);

		/// <summary>⭐ 变化时间点（HH:mm:ss.fff；最多 12 个）✅。</summary>
		internal readonly List<string> Times = new List<string>(12);
	}

	/// <summary>
	/// ⭐ 运行只读 EC 取证（返回桌面报告路径）✅。
	/// <para>⚠️ 只读；LPC 走 <see cref="EcLpcIo" />、ACPI 走 <see cref="EcPortIo.TryReadByteFast" /> ✅。</para>
	/// </summary>
	/// <param name="seconds">采样时长（秒）✅。</param>
	/// <param name="useLpc">true ⇒ LPC（0x4E/0x4F）；false ⇒ ACPI（0x92）✅。</param>
	internal static string Run(int seconds, bool useLpc)
	{
		seconds = Math.Clamp(seconds, 10, 300);
		string path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
			"lm-kbd-ec-probe-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".txt");
		void L(string text)
		{
			try
			{
				File.AppendAllText(path, text + "\r\n", Encoding.UTF8);
			}
			catch
			{
			}
			try
			{
				Logger.WriteLine("[kbd-ec-probe] " + text);
			}
			catch
			{
			}
		}
		try
		{
			File.WriteAllText(path, "lm-kbd-ec-probe v3 开始 " + DateTime.Now.ToString("HH:mm:ss") + "\r\n", Encoding.UTF8);
		}
		catch (Exception ex)
		{
			Logger.WriteError("[kbd-ec-probe] 桌面报告写不了：" + ex.Message);
			return "";
		}

		// ── ① 平台闸：仅宝龙达 30/40 系 ──
		bool baolongda = BaolongdaBackend.IsBaolongdaModelWhitelisted(out string boardNote);
		// ⚠️ 完全限定名（短名解析在编译期曾报 CS0103）
		DgpuGenerationKind generation = MechrevoLite.Gpu.GpuGenerationProvider.Current().Generation;
		string platform = $"宝龙达={baolongda} · 代数={generation} · {boardNote}";
		bool is30or40 = generation == DgpuGenerationKind.Gen30 || generation == DgpuGenerationKind.Gen40;
		if (!baolongda || !is30or40)
		{
			L("⛔ 拒绝：本命令仅宝龙达 30/40 系可用（只读 EC 取证）。");
			L(platform);
			L("⇒ 未读取 EC、未写任何字节；报告：" + path);
			Logger.WriteError("[kbd-ec-probe] 拒绝（非宝龙达 30/40 系）：" + platform + " ⇒ 报告 " + path);
			return path;
		}

		// ── ② 通道选择：LPC 打开失败 ⇒ 自动回退 ACPI ──
		string channelName = useLpc ? "LPC" : "ACPI";
		bool requestedLpc = useLpc;
		if (useLpc)
		{
			bool lpcOpened = EcLpcIo.TryReadByte(0x2000, out int lpcProbeValue)
				|| EcLpcIo.TryReadByte(0xC411, out lpcProbeValue);
			if (!lpcOpened)
			{
				useLpc = false;
				channelName = "ACPI（LPC 打开失败 ⇒ 已自动回退）";
				L("⚠️ LPC 通道打开失败（0x2000/0xC411 均读不到）⇒ 自动回退 ACPI ✅");
			}
			else
			{
				L($"LPC 通道已打开（probe 0x2000/0xC411 = {lpcProbeValue}）✅");
			}
		}
		bool ReadOne(int address, out int value)
		{
			if (useLpc)
			{
				return EcLpcIo.TryReadByte(address, out value);
			}
			return EcPortIo.TryReadByteFast(address, out value, FastTimeoutMs);
		}

		// ── ③ 先测速（32 次）──
		int sampleCount = 32;
		long speedStart = Stopwatch.GetTimestamp();
		int speedOk = 0;
		for (int i = 0; i < sampleCount; i++)
		{
			int address = 0x0790 + i * 7 % 112; // ⭐ 在 0x0790–0x07FF 内取样，贴近实际扫描地址 ✅
			if (ReadOne(address, out int sampleValue) && sampleValue >= 0)
			{
				speedOk++;
			}
		}
		double avgMs = Stopwatch.GetElapsedTime(speedStart).TotalMilliseconds / sampleCount;
		L($"[kbd-ec-probe] {channelName} 通道单字节平均耗时 = {avgMs:F2}ms（采样 {sampleCount} 次，成功 {speedOk}）");
		if (requestedLpc && useLpc && avgMs > LpcTooSlowMs)
		{
			L($"⛔ LPC 也跟不上（单字节 {avgMs:F2}ms > {LpcTooSlowMs:F1}ms）⇒ 提前结束，不浪费 {seconds} 秒。");
			L($"报告：{path}");
			Logger.WriteError($"[kbd-ec-probe] LPC 太慢（{avgMs:F2}ms/B）⇒ 提前结束；报告 {path}");
			return path;
		}

		// ── ④ 窄范围（只扫最可能的 3 段，约 560B）──
		(int Start, int EndExclusive, string Name)[] ranges = new (int, int, string)[3]
		{
			(0x0790, 0x0800, "0x0790–0x07FF（键盘背光常见区）"),
			(0x1800, 0x1900, "0x1800–0x18FF"),
			(0x8300, 0x83C0, "0x8300–0x83BF")
		};
		List<(int Start, int Count)> chunks = new List<(int, int)>();
		foreach ((int start, int endExclusive, string _) in ranges)
		{
			for (int address = start; address < endExclusive; address += ChunkBytes)
			{
				chunks.Add((address, Math.Min(ChunkBytes, endExclusive - address)));
			}
		}
		string coverage = string.Join(" · ", ranges.Select(((int Start, int EndExclusive, string Name) r) => r.Name));
		L("用法提示：**请连续快速按 Fn 调亮/调暗各 5 次（每次间隔 1 秒），最好亮→暗→亮→暗**。");
		L("⭐ 请先等约 2 秒（让探针建立基线），再开始按 Fn。");
		L($"通道={channelName} · 覆盖段={coverage}");
		L("只读保证：ACPI=EcPortIo.TryReadByteFast（0x92）· LPC=EcLpcIo.TryReadByte（0x4E/0x4F 只读）；⛔ 0 写入、⛔ 不碰 WMI 写。");
		L($"扫描：{ChunkBytes}B/块 · 单轮预算 {ScanCycleBudgetMs}ms；发现变化 ⇒ 专注模式每 {FocusPollMs}ms 盯变化地址。");
		L("平台：" + platform);

		// ── ⑤ LPC 与 ACPI 视图对照（判断是否 banked；各读一次）──
		bool viewMismatch = false;
		L("两通道视图对照（同地址各读一次；仅当请求 LPC 时）：");
		if (requestedLpc && useLpc)
		{
			int[] compareAddresses = new int[3] { 0x2000, 0x0790, 0x1800 };
			foreach (int address in compareAddresses)
			{
				bool okLpc = EcLpcIo.TryReadByte(address, out int lpcValue);
				bool okAcpi = EcPortIo.TryReadByteFast(address, out int acpiValue, FastTimeoutMs);
				L($"  0x{address:X4}: LPC={(okLpc ? lpcValue.ToString() : "fail")} · ACPI={(okAcpi ? acpiValue.ToString() : "fail")}");
				if (okLpc && okAcpi && lpcValue != acpiValue)
				{
					viewMismatch = true;
				}
			}
		}
		else
		{
			L("  （当前走 ACPI，未对比）");
		}
		L(viewMismatch
			? "⚠️ **LPC 与 ACPI 视图不一致**（可能 banked / 不同地址空间）⇒ 若要写，需先确认 bank 切换"
			: "✓ LPC 与 ACPI 视图一致（或未对比）");
		L("");

		// ── ⑥ 扫描 + 专注 ──
		Dictionary<int, EcAddressStat> stats = new Dictionary<int, EcAddressStat>(2048);
		List<int> focus = new List<int>();
		bool focusMode = false;
		int chunkCursor = 0;
		int cycles = 0;
		int readFailureBytes = 0;
		int totalChanges = 0;
		long totalCycleMs = 0;
		DateTime deadline = DateTime.UtcNow.AddSeconds(seconds);
		try
		{
			while (DateTime.UtcNow < deadline)
			{
				cycles++;
				Stopwatch cycleClock = Stopwatch.StartNew();
				int scannedBytes = 0;
				int failuresThisCycle = 0;
				int changesThisCycle = 0;
				if (focusMode)
				{
					foreach (int address in focus.ToArray())
					{
						if (!ReadOne(address, out int value) || value < 0)
						{
							failuresThisCycle++;
							continue;
						}
						string stamp = DateTime.Now.ToString("HH:mm:ss.fff");
						if (Observe(stats, address, value, stamp, L))
						{
							changesThisCycle++;
							totalChanges++;
						}
					}
					L($"[cycle] 第 {cycles} 轮（专注）：盯 {focus.Count} 个地址 · 用时 {cycleClock.ElapsedMilliseconds}ms"
						+ $" · 失败 {failuresThisCycle} · 本轮变化 {changesThisCycle} · 累计变化 {totalChanges}");
					totalCycleMs += cycleClock.ElapsedMilliseconds;
					long waitFocus = FocusPollMs - cycleClock.ElapsedMilliseconds;
					if (waitFocus > 0)
					{
						Thread.Sleep((int)waitFocus);
					}
					continue;
				}
				while (true)
				{
					(int start, int count) = chunks[chunkCursor];
					chunkCursor = (chunkCursor + 1) % chunks.Count;
					for (int i = 0; i < count; i++)
					{
						int address = start + i;
						if (!ReadOne(address, out int value) || value < 0)
						{
							readFailureBytes++;
							failuresThisCycle++;
							continue;
						}
						string stamp = DateTime.Now.ToString("HH:mm:ss.fff");
						if (Observe(stats, address, value, stamp, L))
						{
							changesThisCycle++;
							totalChanges++;
						}
					}
					scannedBytes += count;
					if (changesThisCycle > 0)
					{
						focusMode = true;
						focus = stats.Values.Where((EcAddressStat s) => s.Changes > 0).Select((EcAddressStat s) => s.Address).ToList();
						L($"[focus] 发现变化 ⇒ 切专注模式：盯 {focus.Count} 个地址（"
							+ string.Join(", ", focus.Select((int a) => "0x" + a.ToString("X4"))) + "）");
						break;
					}
					if (cycleClock.ElapsedMilliseconds >= ScanCycleBudgetMs)
					{
						break;
					}
				}
				L($"[cycle] 第 {cycles} 轮（扫描）：块游标 {chunkCursor}/{chunks.Count} · 扫 {scannedBytes}B"
					+ $" · 用时 {cycleClock.ElapsedMilliseconds}ms · 失败 {failuresThisCycle} · 本轮变化 {changesThisCycle}"
					+ $" · 累计变化 {totalChanges}");
				totalCycleMs += cycleClock.ElapsedMilliseconds;
				long waitScan = ScanCycleBudgetMs - cycleClock.ElapsedMilliseconds;
				if (waitScan > 0)
				{
					Thread.Sleep((int)waitScan);
				}
			}
		}
		catch (Exception ex)
		{
			L("扫描异常（已吞掉）：" + ex.GetType().Name + " " + ex.Message);
		}

		// ── ⑦ 汇总 ──
		double avgCycleMs = cycles > 0 ? (double)totalCycleMs / cycles : 0.0;
		L("");
		L($"扫描结束：{seconds}s · 周期={cycles} · 总变化={totalChanges} · 读取失败字节={readFailureBytes}");
		L($"通道={channelName} · 覆盖段={coverage} · 平均轮耗时={avgCycleMs:F1}ms · 失败数={readFailureBytes}");
		List<EcAddressStat> candidates = stats.Values
			.Where((EcAddressStat s) => s.Changes >= 2)
			.OrderByDescending((EcAddressStat s) => IsSmallRange(s) ? 1 : 0)
			.ThenByDescending((EcAddressStat s) => s.Changes)
			.ThenBy((EcAddressStat s) => s.Address)
			.ToList();
		L("候选（按相关性排序：变化次数≥2；值域 0..3 / 0..10 优先）：");
		if (candidates.Count == 0)
		{
			L("（没有变化≥2 的地址：可能 FN 走 WMI/另一块 EC RAM，或采样窗口内未按 Fn）");
		}
		int shown = 0;
		foreach (EcAddressStat candidate in candidates)
		{
			if (shown >= 40)
			{
				break;
			}
			string sequence = string.Join("→", candidate.Values.Select((int v) => "0x" + v.ToString("X2")));
			string times = string.Join(" · ", candidate.Times);
			string rangeHint = IsSmallRange(candidate) ? " · 值域 0..3/0..10 ⭐" : "";
			L($"0x{candidate.Address:X4} 变化 {candidate.Changes} 次 · 值序列 {sequence}{rangeHint}");
			L($"  ↳ 变化时刻：{times}");
			shown++;
		}
		L("");
		L("⭐ 判读：优先看**值域 0..3 / 0..10** 且变化次数与 Fn 按键次数吻合的地址");
		L("   （例如按亮 5 次、暗 5 次 ⇒ 变化约 10 次；且每次只在 0..3/0..10 间切换）。");
		L("报告：" + path);
		Logger.WriteError("[kbd-ec-probe] 报告：" + path
			+ $"（通道={channelName} 周期={cycles} 平均轮耗时={avgCycleMs:F1}ms 变化={totalChanges}"
			+ $" 失败字节={readFailureBytes} 专注={focusMode} 视图不一致={viewMismatch}）");
		return path;
	}

	/// <summary>
	/// ⭐ 观测一个地址：首次记录初始值（不记变化）；之后不同才记变化（含时间点）✅。
	/// <returns>是否记录了一次变化 ✅。</returns>
	/// </summary>
	private static bool Observe(Dictionary<int, EcAddressStat> stats, int address, int value, string stamp, Action<string> log)
	{
		if (!stats.TryGetValue(address, out EcAddressStat stat))
		{
			stat = new EcAddressStat { Address = address, LastValue = value };
			stat.Values.Add(value);
			stats[address] = stat;
			return false;
		}
		if (stat.LastValue == value)
		{
			return false;
		}
		int oldValue = stat.LastValue;
		stat.LastValue = value;
		stat.Changes++;
		stat.Values.Add(value);
		if (stat.Values.Count > 12)
		{
			stat.Values.RemoveAt(0);
		}
		stat.Times.Add(stamp);
		if (stat.Times.Count > 12)
		{
			stat.Times.RemoveAt(0);
		}
		log($"{stamp} 0x{address:X4}: 0x{oldValue:X2} → 0x{value:X2} · 变化次数={stat.Changes}");
		return true;
	}

	/// <summary>⭐ 值域是否落在 0..3 或 0..10（FN 亮度最可能的值域）✅。</summary>
	private static bool IsSmallRange(EcAddressStat stat)
	{
		if (stat.Values.Count == 0)
		{
			return false;
		}
		bool all03 = true;
		bool all010 = true;
		foreach (int value in stat.Values)
		{
			if (value < 0 || value > 3)
			{
				all03 = false;
			}
			if (value < 0 || value > 10)
			{
				all010 = false;
			}
			if (!all03 && !all010)
			{
				return false;
			}
		}
		return all03 || all010;
	}
}
