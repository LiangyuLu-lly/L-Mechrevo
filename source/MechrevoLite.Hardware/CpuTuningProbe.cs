using System.Collections.Generic;

namespace MechrevoLite.Hardware;

// 2026-10-10 fmax: 新增一个**可选**尾参（默认 null = 与旧行为完全一致，位置参数调用不受影响）：
//   * FMax —— FMax（PBO 最大频率）能力 + **只读**回读结果（见 CpuFMaxProbe）。
//     它**不是**下拉列表里的一个 CpuTuningOption：FMax 在 CPU 调节页上是一行独立控件，
//     所以不进 Options，也就不改动负压表 / 固定频率那几个选项的既有行为。
internal sealed record CpuTuningProbe(
	string Cpu,
	string StatusKey,
	string Detail,
	IReadOnlyList<CpuTuningOption> Options,
	CpuFMaxProbe? FMax = null);
