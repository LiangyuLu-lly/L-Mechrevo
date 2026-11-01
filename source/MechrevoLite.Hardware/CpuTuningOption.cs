using System.Collections.Generic;

namespace MechrevoLite.Hardware;

// 2026-10-05 coread: 新增两个**可选**尾参（默认 null = 与旧行为完全一致，位置参数调用不受影响）：
//   * CoreReadback        —— 逐核回读值（CO 步数）。只在 Probe() 里读一次填进来，**绝不轮询**。
//   * ReadbackNoteKey     —— Current 为 null 时 UI 要显示的 resx 键
//                            （"该平台没有回读命令" / "读取失败"；null = 用旧的 CpuTuneNoReadback）。
// 2026-10-05 percore-product: 再新增一个**可选**尾参：
//   * PerCoreWrite        —— 该选项有没有**每核**写路径（命令号见 RyzenSmuService.CurvePerCoreCommand）。
//                            false 时 UI 不给每核输入/重置（= 该代号在 SMU 参考里没有每核写命令，
//                            或 SKU 层级没放行）。
//                            ⚠️ 2026-10-10 percorenolog：「只要支持就开」——⛔ 不再是"只有桌面 Vermeer"，
//                            也⛔ 不再有"已实测 / SMU 声明·未实测"的分档。
//                            仍然只为**展示/门控**服务：真正的写入判定在 RuntimeCpuTuning 里，
//                            CanWrite 与 PerCoreWrite 都要为真才允许写。
internal sealed record CpuTuningOption(
	string Id,
	string LabelKey,
	decimal Minimum,
	decimal Maximum,
	string Unit,
	decimal? Current,
	bool CanWrite,
	IReadOnlyList<int>? CoreReadback = null,
	string? ReadbackNoteKey = null,
	bool PerCoreWrite = false);
