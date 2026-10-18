// ⭐⭐ 2026-10-19（30 系「FN 能调亮度、WMI 18 只读」· EC 取证工具 v2：快速扫描 + 自适应专注）：
// `--kbd-ec-probe [秒数]` —— **只读**扫描 EC RAM，找出按 Fn 调亮度时**变化的地址**。
//
// v1 实测教训（机主报告）：
//   · 旧路径单字节 50~80ms（250ms 超时 + 稳定性重试）⇒ 60s 只扫 5 轮、按 Fn 采不到；
//   · 一半读取失败 ⇒ 候选恒空。
// v2 改法：
//   ① 新增 EcPortIo.TryReadByteFast / TryReadBytesFast：**单次事务、不重试**、4ms 短超时，只在探针里用；
//   ② 分块轮转（16B/块，单轮预算 180ms）⇒ 优先区间能快速反复覆盖；
//   ③ 一旦发现某地址变化 ⇒ **立刻切"专注模式"**，只每 50ms 读这些地址，直到结束；
//   ④ 报告记录：每轮耗时/失败数/所有变化的时间点（便于和 Fn 按键时刻对齐）。
//
// ⚠️ 平台闸：仅宝龙达 30/40 系可跑（板号白名单 + GPU 代数）；其它机型打印拒绝并正常退出（exit 0）。
// ⚠️ 全程只读：仅 EcPortIo.TryReadBytesFast → 端口 0x62/0x66、命令 0x92；⛔ 0 写入、⛔ 不碰 WMI 写。

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
/// ⭐ 只读 EC 取证（v2 · 快速扫描）：分块轮转 + 变化地址自适应专注，用于定位 FN 调亮度的寄存器 ✅。
/// <para>⚠️ 全程只读；⛔ 不写 EC / WMI，⛔ 不依赖 GCU/宝龙达 WMI 实例 ✅。</para>
/// </summary>
internal static class KeyboardEcProbe
{
	/// <summary>⭐ 单块字节数（16B ⇒ 快速路径下单块通常 &lt;100ms）✅。</summary>
	private const int ChunkBytes = 16;

	/// <summary>⭐ 扫描模式单轮预算（毫秒）；到点就让出，下一轮继续 ✅。</summary>
	private const int ScanCycleBudgetMs = 180;

	/// <summary>⭐ 快速读单步超时（毫秒）；失败立即下一个地址（不重试）✅。</summary>
	private const int FastTimeoutMs = 4;

	/// <summary>⭐ 专注模式轮询间隔（毫秒）✅。</summary>
	private const int FocusPollMs = 50;

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
	/// <para>⚠️ 只调 <see cref="EcPortIo.TryReadBytesFast" />；⛔ 零写入 ✅。</para>
	/// </summary>
	internal static string Run(int seconds)
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
			File.WriteAllText(path, "lm-kbd-ec-probe v2 开始 " + DateTime.Now.ToString("HH:mm:ss") + "\r\n", Encoding.UTF8);
		}
		catch (Exception ex)
		{
			Logger.WriteError("[kbd-ec-probe] 桌面报告写不了：" + ex.Message);
			return "";
		}

		// ── ① 平台闸：仅宝龙达 30/40 系 ──
		bool baolongda = BaolongdaBackend.IsBaolongdaModelWhitelisted(out string boardNote);
		// ⚠️ 完全限定名（本文件 CMake/命名空间短名解析在编译期曾报 CS0103；明确到命名空间最稳）
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

		// ── ② 优先区间（按机主要求排序；先小范围、能跟上 Fn）──
		(int Start, int EndExclusive, string Name)[] ranges = new (int, int, string)[5]
		{
			(0x0790, 0x0800, "0x0790–0x07FF（键盘背光常见区 · 第一优先）"),
			(0x1800, 0x1900, "0x1800–0x18FF（第二优先）"),
			(0x8300, 0x8400, "0x8300–0x83FF（电池/风扇/键盘块 A · 第三优先）"),
			(0x8400, 0x8500, "0x8400–0x84FF（电池/风扇/键盘块 B · 扩展）"),
			(0x1000, 0x1100, "0x1000–0x10FF（扩展）")
		};
		List<(int Start, int Count)> chunks = new List<(int, int)>();
		foreach ((int start, int endExclusive, string _) in ranges)
		{
			for (int address = start; address < endExclusive; address += ChunkBytes)
			{
				chunks.Add((address, Math.Min(ChunkBytes, endExclusive - address)));
			}
		}
		L("用法提示：**请连续快速按 Fn 调亮/调暗各 5 次（每次间隔 1 秒），最好亮→暗→亮→暗**。");
		L("⭐ 请先等约 2 秒（让探针建立基线），再开始按 Fn。");
		L("只读保证：仅 EcPortIo.TryReadBytesFast（端口 0x62/0x66 · 命令 0x92）⇒ ⛔ 0 写入、⛔ 不碰 WMI 写。");
		L($"快速路径：单字节单次事务、不重试、{FastTimeoutMs}ms 短超时；扫描 {ChunkBytes}B/块、单轮预算 {ScanCycleBudgetMs}ms；");
		L($"发现变化后进入专注模式：每 {FocusPollMs}ms 读一次变化地址，直到结束。");
		L("平台：" + platform);
		L("区间：" + string.Join(" · ", ranges.Select(((int Start, int EndExclusive, string Name) r) => r.Name)));
		L("");

		Dictionary<int, EcAddressStat> stats = new Dictionary<int, EcAddressStat>(2048);
		List<int> focus = new List<int>();
		bool focusMode = false;
		int chunkCursor = 0;
		int cycles = 0;
		int readFailureBytes = 0;
		int totalChanges = 0;
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
					// ── 专注模式：只盯变化地址（每 50ms 一次）──
					foreach (int address in focus.ToArray())
					{
						if (!EcPortIo.TryReadByteFast(address, out int value, FastTimeoutMs))
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
					long waitFocus = FocusPollMs - cycleClock.ElapsedMilliseconds;
					if (waitFocus > 0)
					{
						Thread.Sleep((int)waitFocus);
					}
					continue;
				}
				// ── 扫描模式：块轮转，单轮预算 180ms ──
				while (true)
				{
					(int start, int count) = chunks[chunkCursor];
					chunkCursor = (chunkCursor + 1) % chunks.Count;
					EcPortIo.TryReadBytesFast(start, count, out int[] values, FastTimeoutMs);
					for (int i = 0; i < values.Length; i++)
					{
						int value = values[i];
						if (value < 0)
						{
							readFailureBytes++;
							failuresThisCycle++;
							continue;
						}
						int address = start + i;
						string stamp = DateTime.Now.ToString("HH:mm:ss.fff");
						if (Observe(stats, address, value, stamp, L))
						{
							changesThisCycle++;
							totalChanges++;
						}
					}
					scannedBytes += count;
					// ⭐ 一旦发现变化 ⇒ 立刻切专注模式（把当前所有变化地址加入 watchlist）✅
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

		// ── ③ 汇总：变化次数 ≥2；值域 0..3 / 0..10 的优先；附时间点 ──
		L("");
		L($"扫描结束：{seconds}s · 周期={cycles} · 总变化={totalChanges} · 读取失败字节={readFailureBytes}");
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
			+ $"（周期={cycles} 变化={totalChanges} 失败字节={readFailureBytes} 专注={focusMode}）");
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
