namespace PawnIO;

public enum SmuStatus : uint
{
	OK = 1u,
	Failed = 255u,
	UnknownCmd = 254u,
	CmdRejectedPrereq = 253u,
	CmdRejectedBusy = 252u,

	// ⭐⭐ 2026-10-19（30 系 SMU 功耗墙）：**L-Mechrevo 私有状态，不是 SMU 固件返回值** ✅
	//   ⭐ 语义：⭐ "本代号/本家族**没有**可用的命令号" ⚠️ ⇒ ⭐ 一个寄存器都**没有**写 ✅
	//   ⭐ 与 Failed / UnknownCmd 的区别：
	//      * Failed          = 命令发出去了、但 SMU 回了 0xFF（真失败）；
	//      * UnknownCmd 0xFE = 命令发出去了、固件回"不认识这条命令"（实测出来的"不支持"）；
	//      * NotSupported    = ⭐ **我们主动不发**（表里没有依据 ⇒ 不猜命令号）✅
	//   ⚠️ 取值 0xF9 只是"SMU 固件永远不会返回"的一个空闲值（与 ryzen_smu 用户态库的
	//      SMU_Return_Unsupported 同值；这里只借用"不冲突"这一性质，不表示固件语义）。
	NotSupported = 249u
}
