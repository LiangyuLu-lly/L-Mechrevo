using PawnIO;

namespace MechrevoLite.Hardware;

// ==============================================================================================
// 2026-10-10 fmax: 一次探针（"检测能力"）里得到的 FMax（PBO 最大频率）结果。
//
// * Capability    —— 代号 -> 能力分类（RyzenSmuService.ClassifyFMax），决定 UI 能不能写/能不能读；
// * Current       —— **只读**回读到的值（= response[0] & 0xFFFFF，与参考实现的打包掩码一致）；
//                    null = 读不到（没有读命令 / 读失败），**绝不用 0 假装读到**；
// * RawResponse   —— response[0] 原始值。参数语义未确认（绝对 MHz vs 偏移），所以原始值一路带到
//                    UI 与日志里，让用户自己比对；
// * Detail        —— 给"技术详情"折叠区的一行英文说明（含 SMU 固件状态码）。
//
// 只在 Probe() 里产生一次，**绝不轮询**。
// 2026-10-10 fmax-persist：改前这里写的是"FMax 不持久化、不开机自动应用"—— 用户决定要「保存/读取」
// 与开机自动应用之后，那两句已经不成立，改成：**探针本身只读**（本记录也只承载只读回读结果）；
// 持久化/自动应用/「重置」恢复都发生在 RuntimeCpuTuning 的写入路径上，探针只负责"第一次成功读取时
// 把原值记进 cpu_fmax_baseline"这一件事。
// ==============================================================================================
internal sealed record CpuFMaxProbe(
	FMaxCapability Capability,
	decimal? Current,
	uint RawResponse,
	string Detail);
