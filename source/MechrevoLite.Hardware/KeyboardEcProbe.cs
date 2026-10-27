// ⭐⭐ 2026-10-19（30 系「FN 能调亮度」· EC 取证 v5：缩两段 + 5s 基线 + 报告 finally 落盘）：
// `--kbd-ec-probe [秒数] [--lpc]` —— **只读**扫描 EC RAM，找出按 Fn 调亮度时**变化的地址**。
//
// v4 实测教训：基线仅 2s，而全量扫完 ~10s ⇒ 大部分地址在基线期没被采过、没被标噪声；
//   之后 0x83xx 等电池/风扇遥测持续抖动，把候选淹没；报告还因被关闭而没写完。
// v5：
//   ① **只扫两段**（去掉 0x83xx 噪声源）：0x0790–0x07FF（112B）+ 0x1800–0x18FF（256B）≈368B；
//   ② **基线 5 秒**（完整多轮）⇒ 基线期内任何变化过的地址永久标噪声；
//   ③ 之后每轮只记录**非噪声**地址变化，日志 `[cand] 0xADDR: 0x.. → 0x.. · 变化 N 次 · 时刻 …`；
//   ④ WMI18 关联保留（±300ms），只对非噪声地址；
//   ⑤ **报告三段在 finally 里写**（Ctrl+C/异常也落盘），③ 段固定有 `（无 / 列表）`；
//   ⑥ 提示：「先等 5 秒不要动 → 然后一下一下按 Fn（每次间隔 1 秒）亮 5 次、暗 5 次」。
//
// ⚠️ 平台闸：仅宝龙达 30/40 系可跑（板号白名单 + GPU 代数）；其它机型打印拒绝并正常退出（exit 0）。
// ⚠️ 全程只读：ACPI=EcPortIo.TryReadByteFast（0x92）· LPC=EcLpcIo.TryReadByte（0x4E/0x4F）·
//    WMI18=BaolongdaWmi.TryReadByte（只发 Get）；⛔ 0 写入、⛔ 不碰 WMI 写、⛔ 不改任何状态。

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
/// ⭐ 只读 EC 取证（v5 · 两段 + 5s 基线 + finally 落盘）：ACPI/LPC 双通道 ✅。
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

	/// <summary>⭐ LPC 测速阈值：超过则提示“也跟不上”并提前结束 ✅。</summary>
	private const double LpcTooSlowMs = 5.0;

	/// <summary>⭐ 基线期 5 秒（完整多轮）⇒ 期间变化过的地址永久排除 ✅。</summary>
	private const int BaselineMs = 5000;

	/// <summary>⭐ WMI18 轮询间隔（毫秒）✅。</summary>
	private const int WmiPollMs = 100;

	/// <summary>⭐ WMI18 变化与 EC 变化的时间相关窗口（±300ms）✅。</summary>
	private const int CorrelationWindowMs = 300;

	/// <summary>⭐ 单个地址的变化统计（报告用）✅。</summary>
	private sealed class EcAddressStat
	{
		internal int Address;

		internal int LastValue = -1;

		internal int Changes;

		/// <summary>⭐ 值序列（初始值 + 每次变化后的值；最多 12 个）✅。</summary>
		internal readonly List<int> Values = new List<int>(12);

		/// <summary>⭐ 变化时间点（HH:mm:ss.fff；最多 12 个）✅。</summary>
		internal readonly List<string> TimeTexts = new List<string>(12);

		/// <summary>⭐ 变化时间（完整 DateTime；用于相关性）✅。</summary>
		internal readonly List<DateTime> Times = new List<DateTime>(12);
	}

	/// <summary>⭐ 运行只读 EC 取证（返回桌面报告路径）✅。</summary>
	internal static string Run(int seconds, bool useLpc)
	{
		seconds = Math.Clamp(seconds, 10, 300);
		string path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
			"lm-kbd-ec-probe-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".txt");
		object logLock = new object();
		void L(string text)
		{
			lock (logLock)
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
		}
		try
		{
			File.WriteAllText(path, "lm-kbd-ec-probe v5 开始 " + DateTime.Now.ToString("HH:mm:ss") + "\r\n", Encoding.UTF8);
		}
		catch (Exception ex)
		{
			Logger.WriteError("[kbd-ec-probe] 桌面报告写不了：" + ex.Message);
			return "";
		}

		// ── ① 平台闸：仅宝龙达 30/40 系 ──
		bool baolongda = BaolongdaBackend.IsBaolongdaModelWhitelisted(out string boardNote);
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
			int address = 0x0790 + i * 7 % 112;
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

		// ── ④ 只扫两段（去掉 0x83xx 噪声源；约 368B）──
		(int Start, int EndExclusive, string Name)[] ranges = new (int, int, string)[2]
		{
			(0x0790, 0x0800, "0x0790–0x07FF（键盘背光常见区）"),
			(0x1800, 0x1900, "0x1800–0x18FF")
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
		L("用法提示：**先等 5 秒不要动 → 然后一下一下按 Fn（每次间隔 1 秒）亮 5 次、暗 5 次**。");
		L($"基线期：开头 {BaselineMs}ms 内**任何**变化过的地址 ⇒ 噪声，永久排除。");
		L($"通道={channelName} · 覆盖段={coverage}");
		L("只读保证：ACPI=EcPortIo.TryReadByteFast（0x92）· LPC=EcLpcIo.TryReadByte（0x4E/0x4F 只读）"
			+ " · WMI18=BaolongdaWmi 只读 Get；⛔ 0 写入、⛔ 不碰 WMI 写。");
		L($"扫描：{ChunkBytes}B/块 · 单轮预算 {ScanCycleBudgetMs}ms；扫描跑满 {seconds}s（不因单个变化停扫）。");
		L($"WMI18 关联：每 {WmiPollMs}ms 只读一次功能号 18；18 变化时打 `[corr]`；非噪声 EC 变化在 ±{CorrelationWindowMs}ms 内 ⇒ 候选。");
		L("平台：" + platform);

		// ── ⑤ LPC 与 ACPI 视图对照（判断是否 banked）──
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

		// ── ⑥ 数据容器（扫描/关联/报告共用；finally 里也可见）──
		Dictionary<int, EcAddressStat> stats = new Dictionary<int, EcAddressStat>(2048);
		HashSet<int> noise = new HashSet<int>();
		List<(DateTime Time, int Address)> ecChanges = new List<(DateTime, int)>();
		List<(DateTime Time, int From, int To)> wmi18Changes = new List<(DateTime, int, int)>();
		object wmiLock = new object();
		CancellationTokenSource wmiCts = new CancellationTokenSource();
		Thread wmiThread = null;
		BaolongdaWmi wmi = null;
		int baselineNoiseChanges = 0;
		int chunkCursor = 0;
		int cycles = 0;
		int readFailureBytes = 0;
		int totalChanges = 0;
		long totalCycleMs = 0;
		int abortRequested = 0;
		DateTime baselineEnd = DateTime.UtcNow.AddMilliseconds(BaselineMs);
		DateTime deadline = DateTime.UtcNow.AddSeconds(seconds);
		ConsoleCancelEventHandler cancelHandler = null;

		// ── ⑦ WMI18 轮询线程（只读 Get；每 100ms）──
		try
		{
			wmi = BaolongdaWmi.TryCreate();
		}
		catch (Exception ex)
		{
			L("⚠️ 创建 WMI 实例异常（WMI18 关联不可用）：" + ex.Message);
		}
		if (wmi == null)
		{
			L("⚠️ 拿不到宝龙达 WMI 实例 ⇒ **WMI18 关联不可用**（EC 扫描仍继续）。");
		}
		else
		{
			wmiThread = new Thread((ThreadStart)delegate
			{
				int last = -1;
				while (!wmiCts.IsCancellationRequested)
				{
					try
					{
						if (wmi.TryReadByte(BaolongdaWmi.Function.RgbKeyboardBrightness, out byte value))
						{
							int current = value;
							if (last >= 0 && current != last)
							{
								DateTime now = DateTime.Now;
								lock (wmiLock)
								{
									wmi18Changes.Add((now, last, current));
								}
								L($"[corr] {now:HH:mm:ss.fff} WMI18: {last} → {current}");
							}
							last = current;
						}
					}
					catch
					{
					}
					Thread.Sleep(WmiPollMs);
				}
			})
			{
				IsBackground = true,
				Name = "kbd-ec-probe-wmi18"
			};
			wmiThread.Start();
		}

		// ⭐ 观测：基线期内变化 ⇒ 噪声；之后噪声地址忽略；非噪声变化 ⇒ 记录 `[cand]` ✅
		bool ObserveAddress(int address, int value, DateTime now)
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
			bool baseline = now < baselineEnd;
			if (baseline)
			{
				noise.Add(address);
				baselineNoiseChanges++;
				stat.Changes++;
				stat.Values.Add(value);
				if (stat.Values.Count > 12)
				{
					stat.Values.RemoveAt(0);
				}
				stat.TimeTexts.Add(now.ToString("HH:mm:ss.fff"));
				stat.Times.Add(now);
				if (stat.TimeTexts.Count > 12)
				{
					stat.TimeTexts.RemoveAt(0);
					stat.Times.RemoveAt(0);
				}
				L($"[baseline] {now:HH:mm:ss.fff} 0x{address:X4}: 0x{oldValue:X2} → 0x{value:X2}（噪声，永久排除）");
				return true;
			}
			if (noise.Contains(address))
			{
				return false;
			}
			stat.Changes++;
			stat.Values.Add(value);
			if (stat.Values.Count > 12)
			{
				stat.Values.RemoveAt(0);
			}
			stat.TimeTexts.Add(now.ToString("HH:mm:ss.fff"));
			stat.Times.Add(now);
			if (stat.TimeTexts.Count > 12)
			{
				stat.TimeTexts.RemoveAt(0);
				stat.Times.RemoveAt(0);
			}
			ecChanges.Add((now, address));
			L($"[cand] 0x{address:X4}: 0x{oldValue:X2} → 0x{value:X2} · 变化 {stat.Changes} 次"
				+ $" · 时刻 {string.Join(" / ", stat.TimeTexts)}");
			return true;
		}

		// ⭐ 报告三段：**在 finally 里调用**（Ctrl+C/异常也落盘）✅
		void WriteReportSections()
		{
			List<(DateTime Time, int From, int To)> wmiSnapshot;
			lock (wmiLock)
			{
				wmiSnapshot = new List<(DateTime, int, int)>(wmi18Changes);
			}
			Dictionary<int, (int Count, List<string> EcTimes, List<string> WmiTimes)> correlated =
				new Dictionary<int, (int, List<string>, List<string>)>();
			foreach ((DateTime wmiTime, int fromValue, int toValue) in wmiSnapshot)
			{
				_ = fromValue;
				_ = toValue;
				foreach ((DateTime ecTime, int address) in ecChanges)
				{
					if (Math.Abs((ecTime - wmiTime).TotalMilliseconds) > CorrelationWindowMs)
					{
						continue;
					}
					if (!correlated.TryGetValue(address, out (int Count, List<string> EcTimes, List<string> WmiTimes) entry))
					{
						entry = (0, new List<string>(), new List<string>());
					}
					correlated[address] = (entry.Count + 1, entry.EcTimes, entry.WmiTimes);
					entry.EcTimes.Add(ecTime.ToString("HH:mm:ss.fff"));
					entry.WmiTimes.Add(wmiTime.ToString("HH:mm:ss.fff"));
				}
			}
			double avgCycleMs = cycles > 0 ? (double)totalCycleMs / cycles : 0.0;
			L("");
			L($"扫描结束：{seconds}s · 周期={cycles} · 总变化={totalChanges} · 读取失败字节={readFailureBytes}"
				+ (Volatile.Read(ref abortRequested) != 0 ? " · ⚠️ 提前结束（Ctrl+C/关闭）" : ""));
			L($"通道={channelName} · 覆盖段={coverage} · 平均轮耗时={avgCycleMs:F1}ms · 失败数={readFailureBytes}"
				+ $" · 基线噪声地址={noise.Count} · WMI18 变化={wmiSnapshot.Count}");
			L("");
			L("=== ① 噪声地址（基线 5s 内变化过 ⇒ 已永久排除）===");
			List<EcAddressStat> noiseStats = stats.Values.Where((EcAddressStat s) => noise.Contains(s.Address))
				.OrderBy((EcAddressStat s) => s.Address).ToList();
			if (noiseStats.Count == 0)
			{
				L("（无）");
			}
			foreach (EcAddressStat s in noiseStats)
			{
				L($"  0x{s.Address:X4} 基线期变化 {s.Changes} 次 · 值序列 {string.Join("→", s.Values.Select((int v) => "0x" + v.ToString("X2")))}"
					+ $" · 时刻 {string.Join(" / ", s.TimeTexts)}");
			}
			L("");
			L("=== ② 全部变化地址（非噪声）===");
			List<EcAddressStat> changedStats = stats.Values
				.Where((EcAddressStat s) => !noise.Contains(s.Address) && s.Changes > 0)
				.OrderByDescending((EcAddressStat s) => s.Changes)
				.ThenBy((EcAddressStat s) => s.Address)
				.ToList();
			if (changedStats.Count == 0)
			{
				L("（无）");
			}
			int shown = 0;
			foreach (EcAddressStat s in changedStats)
			{
				if (shown >= 80)
				{
					L($"…（共 {changedStats.Count} 个，只列前 80 个）");
					break;
				}
				L($"[cand] 0x{s.Address:X4}: 值序列 {string.Join("→", s.Values.Select((int v) => "0x" + v.ToString("X2")))}"
					+ $" · 变化 {s.Changes} 次 · 时刻 {string.Join(" / ", s.TimeTexts)}");
				shown++;
			}
			L("");
			L("=== ③ 与 WMI18 时间相关的候选 ===");
			if (wmiSnapshot.Count == 0)
			{
				L("（无：WMI18 没有变化/读不到，或窗口内没按 Fn）");
			}
			else if (correlated.Count == 0)
			{
				L("（无：没有非噪声 EC 变化落在任何 WMI18 变化 ±300ms 内）");
			}
			else
			{
				foreach (KeyValuePair<int, (int Count, List<string> EcTimes, List<string> WmiTimes)> pair in
					correlated.OrderByDescending((KeyValuePair<int, (int Count, List<string> EcTimes, List<string> WmiTimes)> p) => p.Value.Count)
						.ThenBy((KeyValuePair<int, (int Count, List<string> EcTimes, List<string> WmiTimes)> p) => p.Key))
				{
					L($"[cand] 0x{pair.Key:X4} 相关 {pair.Value.Count} 次 · EC 时刻 {string.Join(" / ", pair.Value.EcTimes)}"
						+ $" · 对应 WMI18 时刻 {string.Join(" / ", pair.Value.WmiTimes)}");
				}
			}
			L("");
			L("⭐ 判读：以 ③ 为准（即使 EC 值域不是 0..3/0..10，只要与 WMI18 变化时间吻合就值得追）；② 为次优候选；① 已排除。");
			L("报告：" + path);
			Logger.WriteError("[kbd-ec-probe] 报告：" + path
				+ $"（通道={channelName} 周期={cycles} 平均轮耗时={avgCycleMs:F1}ms 变化={totalChanges}"
				+ $" 失败字节={readFailureBytes} 噪声={noise.Count} WMI18变化={wmiSnapshot.Count}"
				+ $" 相关候选={correlated.Count} 视图不一致={viewMismatch}）");
		}

		// ── ⑧ 扫描（跑满时长；基线 5s；Ctrl+C 优雅收尾）──
		try
		{
			try
			{
				cancelHandler = delegate (object sender, ConsoleCancelEventArgs e)
				{
					e.Cancel = true;
					Interlocked.Exchange(ref abortRequested, 1);
					L("⚠️ 收到 Ctrl+C ⇒ 提前结束并写报告（已收集数据保留）");
				};
				Console.CancelKeyPress += cancelHandler;
			}
			catch
			{
			}
			while (DateTime.UtcNow < deadline && Volatile.Read(ref abortRequested) == 0)
			{
				cycles++;
				Stopwatch cycleClock = Stopwatch.StartNew();
				int scannedBytes = 0;
				int failuresThisCycle = 0;
				int changesThisCycle = 0;
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
						if (ObserveAddress(address, value, DateTime.Now))
						{
							changesThisCycle++;
							totalChanges++;
						}
					}
					scannedBytes += count;
					if (cycleClock.ElapsedMilliseconds >= ScanCycleBudgetMs)
					{
						break;
					}
				}
				L($"[cycle] 第 {cycles} 轮：块游标 {chunkCursor}/{chunks.Count} · 扫 {scannedBytes}B"
					+ $" · 用时 {cycleClock.ElapsedMilliseconds}ms · 失败 {failuresThisCycle} · 本轮变化 {changesThisCycle}"
					+ $" · 累计变化 {totalChanges} · 噪声地址 {noise.Count}");
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
		finally
		{
			try
			{
				if (cancelHandler != null)
				{
					Console.CancelKeyPress -= cancelHandler;
				}
			}
			catch
			{
			}
			try
			{
				wmiCts.Cancel();
			}
			catch
			{
			}
			try
			{
				wmiThread?.Join(500);
			}
			catch
			{
			}
			try
			{
				wmi?.Dispose();
			}
			catch
			{
			}
			try
			{
				WriteReportSections();
			}
			catch (Exception ex2)
			{
				L("⚠️ 写最终报告异常（已吞掉）：" + ex2.GetType().Name + " " + ex2.Message);
			}
		}
		return path;
	}
}
