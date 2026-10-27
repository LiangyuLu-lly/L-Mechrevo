// 来源：_upstream\0.290.6\decomp\MechrevoLite.Hardware\LegacyMifsSnapshot.cs（作者 0.290.6 实现）。
// 用途：老机型 MICommonInterface（见 LegacyMifsWmi）读回的性能档快照 —— 档位 + 单调递增版本号。
// ⚠️ 2026-10-15 C-1 第 1 步：纯增量底座。
// ✅ 2026-10-15 C-1 第 2 步（本轮）：已接线 —— MechrevoHw.RefreshLegacyModeAsync 消费本快照。

namespace MechrevoLite.Hardware;

internal readonly record struct LegacyMifsSnapshot(int OperatingMode, long Version);
