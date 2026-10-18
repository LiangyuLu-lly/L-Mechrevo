// ⭐⭐ 2026-10-19（30 系「FN 能调亮度、WMI 18 只读」· EC 取证工具）：
// `--kbd-ec-probe [秒数]` —— **只读**扫描 EC RAM 若干区间，找出按 Fn 调亮度时**变化的地址**。
//
// 依据（机主实测）：
//   · 官方 30 系控制台 `SetSP_KeyboardLightBrightness` 是**空实现**（只 Debug.WriteLine）；
//   · FN 能调亮度 ⇒ 固件/EC 层有通路；
//   · WMI 功能号 18 在这台机只读 ⇒ 先定位 Fn 改的是 EC RAM 哪个字节；
//   ⇒ 本工具**只读**（仅 EcPortIo.TryReadBytes → 端口 0x62/0x66、命令 0x92/0x80），
//     ⛔ 不调任何 Set、⛔ 不碰 WMI 写、⛔ 不改任何状态。
//
// ⚠️ 平台闸：仅宝龙达 30/40 系可跑（板号白名单 + GPU 代数）；其它机型打印拒绝并正常退出（exit 0）。
//
// 许可/署名：与本树同一实现，见 THIRD-PARTY-NOTICES.md 与 LICENSES\。

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using MechrevoLite.Gpu;
using System.Threading;
using MechrevoLite.Gpu;

namespace MechrevoLite.Hardware;

/// <summary>
/// ⭐ 只读 EC 取证：周期扫描 EC RAM 区间，记录**变化的地址**（用于定位 FN 调亮度的寄存器）✅。
/// <para>⚠️ 全程只读；⛔ 不写 EC / WMI，⛔ 不依赖 GCU/宝龙达 WMI 实例 ✅。</para>
/// </summary>
internal static class KeyboardEcProbe
{
	/// <summary>⭐ 单个地址的变化统计（报告用）✅。</summary>
	private sealed class EcAddressStat
	{
		internal int Address;

		internal int LastValue = -1;

		internal int Changes;

		/// <summary>⭐ 值序列（初始值 + 每次变化后的值；最多保留 12 个）✅。</summary>
		internal readonly List<int> Values = new List<int>(12);
	}

	/// <summary>
	/// ⭐ 运行只读 EC 取证（返回桌面报告路径）✅。
	/// <para>⚠️ 只调 <see cref="EcPortIo.TryReadBytes" />；⛔ 零写入 ✅。</para>
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
			File.WriteAllText(path, "lm-kbd-ec-probe 开始 " + DateTime.Now.ToString("HH:mm:ss") + "\r\n", Encoding.UTF8);
		}
		catch (Exception ex)
		{
			Logger.WriteError("[kbd-ec-probe] 桌面报告写不了：" + ex.Message);
			return "";
		}

		// ── ① 平台闸：仅宝龙达 30/40 系 ──
		bool baolongda = BaolongdaBackend.IsBaolongdaModelWhitelisted(out string boardNote);
		// ⚠️ 用完全限定名：本文件所在命名空间树里 `GpuGenerationProvider` 的短名解析有歧义（编译期 CS0103）
		//    ⇒ 明确写成 `MechrevoLite.Gpu.GpuGenerationProvider`（与 DgpuGenerationKind 同一命名空间）✅
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

		// ── ② 只读扫描区间（按优先级；轮转扫描，保证每轮有进展）──
		(int Start, int EndExclusive, string Name)[] ranges = new (int, int, string)[4]
		{
			(0x8300, 0x8400, "0x8300–0x83FF（电池/风扇/键盘相关块 A）"),
			(0x8400, 0x8500, "0x8400–0x84FF（电池/风扇/键盘相关块 B）"),
			(0x1000, 0x1100, "0x1000–0x10FF"),
			(0x07B0, 0x0800, "0x07B0–0x07FF")
		};
		L("用法提示：**接下来 " + seconds + " 秒内请按 Fn 调亮/调暗各 3~5 次，不要动 LM 界面**。");
		L("只读保证：仅 EcPortIo.TryReadBytes（端口 0x62/0x66 · 命令 0x92/0x80）⇒ ⛔ 0 写入、⛔ 不碰 WMI 写。");
		L("平台：" + platform);
		L("区间：" + string.Join(" · ", ranges.Select(((int Start, int EndExclusive, string Name) r) => r.Name)));
		L("");

		Dictionary<int, EcAddressStat> stats = new Dictionary<int, EcAddressStat>(1024);
		int rangeCursor = 0;
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
				int scannedBytesThisCycle = 0;
				for (int step = 0; step < ranges.Length; step++)
				{
					int index = (rangeCursor + step) % ranges.Length;
					(int start, int endExclusive, _) = ranges[index];
					int count = endExclusive - start;
					bool allOk = EcPortIo.TryReadBytes(start, count, out int[] values);
					if (!allOk)
					{
						readFailureBytes += count;
					}
					for (int i = 0; i < values.Length; i++)
					{
						int value = values[i];
						if (value < 0)
						{
							continue;
						}
						int address = start + i;
						if (!stats.TryGetValue(address, out EcAddressStat stat))
						{
							stat = new EcAddressStat { Address = address, LastValue = value };
							stat.Values.Add(value);
							stats[address] = stat;
							continue;
						}
						if (stat.LastValue != value)
						{
							stat.Changes++;
							totalChanges++;
							stat.Values.Add(value);
							if (stat.Values.Count > 12)
							{
								stat.Values.RemoveAt(0);
							}
							L($"{DateTime.Now:HH:mm:ss.fff} 0x{address:X4}: 0x{stat.LastValue:X2} → 0x{value:X2} · 变化次数={stat.Changes}");
						}
						stat.LastValue = value;
					}
					scannedBytesThisCycle += count;
					rangeCursor = (index + 1) % ranges.Length;
					if (cycleClock.ElapsedMilliseconds >= 250)
					{
						break; // ⭐ 单轮预算 250ms；没扫到的区间下一轮接着扫（每地址仍会被覆盖到）✅
					}
				}
				L($"[cycle] 第 {cycles} 轮：扫 {scannedBytesThisCycle} 字节 · 用时 {cycleClock.ElapsedMilliseconds}ms · 累计变化 {totalChanges}");
				long wait = 250L - cycleClock.ElapsedMilliseconds;
				if (wait > 0)
				{
					Thread.Sleep((int)wait);
				}
			}
		}
		catch (Exception ex)
		{
			L("扫描异常（已吞掉）：" + ex.GetType().Name + " " + ex.Message);
		}

		// ── ③ 汇总：变化次数 ≥2；值域 0..3 / 0..10 的优先 ──
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
			string rangeHint = IsSmallRange(candidate) ? " · 值域 0..3/0..10 ⭐" : "";
			L($"0x{candidate.Address:X4} 变化 {candidate.Changes} 次 · 值序列 {sequence}{rangeHint}");
			shown++;
		}
		L("");
		L("⭐ 判读：优先看**值域 0..3 / 0..10** 且变化次数与 Fn 按键次数吻合的地址");
		L("   （例如按 3 次亮、3 次暗 ⇒ 变化 6 次；且每次只在 0..3/0..10 间切换）。");
		L("报告：" + path);
		Logger.WriteError("[kbd-ec-probe] 报告：" + path + $"（周期={cycles} 变化={totalChanges} 失败字节={readFailureBytes}）");
		return path;
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
