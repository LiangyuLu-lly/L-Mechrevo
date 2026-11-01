// ==============================================================================================
// L-Mechrevo — FMax（PBO 最大频率 / boost limit）能力模型。
//
// 派生声明 / Derived-work notice: 本文件的 `FMaxSpec` 表（Get/Set 命令行 + SMN 邮箱地址组）
// 与 `FMaxCapability` 分类规则，是在阅读下列 GPL-3.0 参考实现后重写的：
//     irusanov/ZenStates-Core  https://github.com/irusanov/ZenStates-Core  (GPL-3.0)
//     irusanov/SMUDebugTool    https://github.com/irusanov/SMUDebugTool    (GPL-3.0)
// 按 GPL-3.0 分发（L-Mechrevo 自身即为 GPL-3.0-only）。
// 许可文本：LICENSES\GPL-3.0.txt · 第三方清单：THIRD-PARTY-NOTICES.md
// ==============================================================================================
namespace PawnIO;

// ==============================================================================================
// 2026-10-10 fmax: FMax（PBO 最大频率 / boost limit）**能力分类**。
//
// 用户决定（原话）：「不支持平台只读取 支持平台可以写入」
//   => 分类结果只决定"能不能写"，**不**决定"能不能显示"：ReadOnly 的平台照样读、照样把值
//      显示给用户，只有「应用」被禁用；None 的平台连读都不做，UI 明写"本机不支持 FMax"。
//
// 这个枚举是**纯数据**，分类逻辑在 RyzenSmuService.ResolveFMax / ClassifyFMax（一张表 + 一个
// 派生属性），不碰驱动、不碰 SMU —— 因此可以脱离硬件单测/人工核对。
// ==============================================================================================
internal enum FMaxCapability
{
	// 该代号既没有读命令也没有写命令（SummitRidge / PinnacleRidge / Colfax / Rome / Undefined ...）。
	None = 0,
	// 只有 Get（Set id = 0）：**只读**。
	ReadOnly = 1,
	// Get + Set 都有：可读可写，写后能回读校验。
	ReadWrite = 2,
	// 只有 Set、没有 Get（ShimadaPeak, Zen5）：**可写，但无法回读校验**。
	WriteNoReadback = 3
}

// ==============================================================================================
// 一个代号上 FMax 的完整"配方"：命令 ID + 邮箱地址组 + 读命令的 arg0。
//
// 地址是 SMN（PCI 配置空间间接访问）地址，与上游 ZenStates-Core 的 RSMU 邮箱是同一组，
// 也就是本仓库**已有的 PSMU 邮箱通路**（SendPsmuRaw -> MailboxRaw 用的就是这些寄存器语义）：
//     ZenStates-Core 为 GPL-3.0（https://github.com/irusanov/ZenStates-Core），与本项目
//     GPL-3.0-only 相容；许可文本见 LICENSES\GPL-3.0.txt。
//   * APU 组     0x3B10A20 / 0x3B10A80 / 0x3B10A88
//                == GetPsmuAddrs(Raven/Renoir/Mobile/Strix*) 那一组；
//   * 桌面组     0x3B10524 / 0x3B10570 / 0x3B10A40
//                == GetPsmuAddrs(Matisse/Raphael) 那一组；
//   * ShimadaPeak 0x3B10924 / 0x3B10970 / 0x3B10A40
//                == GetPsmuAddrs(ShimadaPeak) 那一组。
//
// ⚠️ 地址**故意**在这里显式写出、而**不是**复用 GetPsmuAddrs(CpuFamily)：FMax 的"配方"是**按代号**
//    定的（同一代里 APU0/APU1/桌面 三组地址 + 各代不同的 Get/Set 命令），显式表让"能力分类"、"实际
//    发的命令"、"邮箱地址"三者在同一处、逐代号可核对，不依赖家族标签的正确性。
//
//    历史注记（2026-10-10 fmax-family + fmax-family-guard）：写这段注释时，本仓库的 GetFamily 里
//    Genoa / StormPeak / Bergamo / Turin / TurinD / Mero（以及 Milan / Chagall / Rome / Naples）都落在
//    default -> CpuFamily.Unknown，于是 RyzenSmuService.Initialize() 的 `Family != Unknown` 过不去、
//    服务根本不初始化 —— 那几个代号的能力虽然在这张表里，却永远到不了邮箱。**随后两轮已经把
//    GetFamily 按 RyzenSMU.p k_addridx + 上游 settings 分档补齐**（RyzenSmuService.GetFamily，
//    现在只剩 Undefined 是 Unknown），所以这些代号能真正走到邮箱了。
//    同一轮还加了一道**族驱动命令白名单**（RyzenSmuService.LegacyFamilyCommandsApproved）：补家族
//    只为"能初始化 + FMax 可达"，Stapm/Fast/Slow/Thm/CoGfx 这些按家族选命令的老通路对新补齐的代号
//    **一律不发命令**（返回 Failed+日志），保持改前行为。FMax **不走**那道白名单（见上：配方按代号）。
//    本表的显式地址也仍然原样保留：它依旧是 FMax 地址的唯一来源，不改成 GetPsmuAddrs(Family)，
//    这样 FMax 的正确性不押在家族标签上。
//
// Capability 由命令 ID 派生，**保证"分类"与"实际会发的命令"不可能不一致**：
//   Get=0 & Set=0 -> None；Get=0 & Set!=0 -> WriteNoReadback；Get!=0 & Set=0 -> ReadOnly；否则 ReadWrite。
// ==============================================================================================
internal readonly record struct FMaxSpec(
	uint GetCommand,
	uint SetCommand,
	uint MailboxCommand,
	uint MailboxResponse,
	uint MailboxArgument,
	uint ReadArgument)
{
	internal FMaxCapability Capability
	{
		get
		{
			if (GetCommand == 0)
			{
				return (SetCommand == 0) ? FMaxCapability.None : FMaxCapability.WriteNoReadback;
			}
			return (SetCommand == 0) ? FMaxCapability.ReadOnly : FMaxCapability.ReadWrite;
		}
	}
}
