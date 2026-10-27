using System.Collections.Generic;

namespace MechrevoLite.Hardware;

public static class HardcodeDispositions
{
	public static readonly IReadOnlyList<HardcodeEntry> Items = new HardcodeEntry[22]
	{
		new HardcodeEntry("C1", "YAOSHI 机型串是限充的唯一 allowlist", HardcodeDisposition.MatrixGated, new int[3] { 91, 96, 100 }, "T7：改由 FeatureMatrix（服务画像）+ F3 判定；强制开关语义保留。"),
		new HardcodeEntry("C2", "ITE 键盘 VID 0x048D / PID 0x600B 加分", HardcodeDisposition.KeptWithReason, new int[2] { 12, 13 }, "外设身份，不是笔记本机型；命中是软加分（usage-page 评分兜底），换 PID 仍可匹配。"),
		new HardcodeEntry("C3", "HID usage page 0xFF03 / interface 1 / report 尺寸配比", HardcodeDisposition.KeptWithReason, new int[8] { 184, 187, 188, 189, 190, 191, 192, 193 }, "同一键盘设备的协议选择器；非机型判据，无对应能力位。"),
		new HardcodeEntry("C4", "ITE8291 全 104 键 LED 映射", HardcodeDisposition.KeptWithReason, new int[3] { 20, 23, 50 }, "设备布局而非机型；紧凑键盘会点亮错误格，但需要设备侧证据才能逐机型化。"),
		new HardcodeEntry("C5", "MaxCPU / MaxTotal = 210 W 外圈钳位", HardcodeDisposition.KeptWithReason, new int[2] { 55, 56 }, "仅安全上界；逐机型可调范围来自运行时 Fan/Status，数值不参与支持判定。"),
		new HardcodeEntry("C6", "APVersionCheck > 23 数值代理决定热切换", HardcodeDisposition.MatrixGated, new int[3] { 252, 253, 276 }, "删除；替换 = GpuHotSwapSwitchSupport && lgpuHotSwapSwitchStatus（缺一不提供）。真机写入证据见 t9-hotswap-values.json（BLOCKED-HW）。"),
		new HardcodeEntry("C7", "IsEcoBootFix 的 ASUS 机型串", HardcodeDisposition.KeptWithReason, new int[2] { 508, 510 }, "ASUS 残留谓词；在机械革命机型上恒假。消费点清理归 T29，本任务保留并在 T29 证据中逐条处置。"),
		new HardcodeEntry("C8", "IsManualModeRequired 的 G733 串", HardcodeDisposition.KeptWithReason, new int[2] { 517, 520 }, "同 C7（T29 清理）。"),
		new HardcodeEntry("C9", "IsResetRequired 的 GA403*/FA507XV 串", HardcodeDisposition.KeptWithReason, new int[2] { 523, 525 }, "同 C7（T29 清理）。"),
		new HardcodeEntry("C10", "IsFanRequired 的 19 个 ASUS 机型串", HardcodeDisposition.KeptWithReason, new int[2] { 528, 530 }, "同 C7（T29 清理）。"),
		new HardcodeEntry("C11", "IsModeReapplyRequired 的 FA401/GA403 串", HardcodeDisposition.KeptWithReason, new int[2] { 533, 535 }, "同 C7（T29 清理）。"),
		new HardcodeEntry("C12", "IsStandardModeFix 的 FX506HC/FA808U 串", HardcodeDisposition.KeptWithReason, new int[2] { 538, 540 }, "同 C7（T29 清理）。"),
		new HardcodeEntry("C13", "IsShutdownReset 的 FX507Z 串", HardcodeDisposition.KeptWithReason, new int[2] { 543, 545 }, "同 C7（T29 清理）。"),
		new HardcodeEntry("C14", "NvidiaSmi 默认 GPU 功耗：GU605/GA605 -> 125 W", HardcodeDisposition.MatrixGated, new int[2] { 9, 11 }, "T8：ASUS 名单删除；默认功耗只透传运行时 GpuTgpMaximum，无报告 = -1（未知）。"),
		new HardcodeEntry("C15", "NvidiaSmi 默认 GPU 功耗：GA403 -> 90 W", HardcodeDisposition.MatrixGated, new int[2] { 9, 12 }, "同 C14。"),
		new HardcodeEntry("C16", "NvidiaSmi 默认 GPU 功耗：FA607 -> 140 W，else 175 W", HardcodeDisposition.MatrixGated, new int[3] { 9, 13, 14 }, "同 C14：不再有 175 W 兜底。"),
		new HardcodeEntry("C17", "水冷风扇灯要求设备名/固件含 LCT22002", HardcodeDisposition.KeptWithReason, new int[3] { 79, 80, 81 }, "外设身份（协议能力），不是笔记本机型，且是刻意的设备协议判据。"),
		new HardcodeEntry("C18", "水泵/风扇档位与电压常量（45/60/90 等）", HardcodeDisposition.KeptWithReason, new int[10] { 28, 34, 35, 578, 579, 580, 581, 582, 583, 584 }, "设备协议常量，与笔记本机型无关，低风险。"),
		new HardcodeEntry("C19", "风扇切换灵敏度范围来自 RamFan1p5 单字节规格", HardcodeDisposition.KeptWithReason, new int[4] { 474, 480, 481, 482 }, "支撑判据刻意只用运行时 FanSwitchSpeedSeen（NumericSettingTests 锁定）；逐机型门控归 T21。"),
		new HardcodeEntry("C20", "默认曲线一刀切（Gaming/Office/Turbo JSON）", HardcodeDisposition.MatrixGated, new int[4] { 1178, 1180, 1197, 1201 }, "T8：服务画像显式否掉风扇设置（FanSettingsSupport=0）时不套用内置曲线；逐机型表归 Wave C。"),
		new HardcodeEntry("C21", "灯带子灯可见性不复刻 CCUWinUI 的 BIOS_PROJECT_ID 过滤", HardcodeDisposition.KeptWithReason, new int[8] { 283, 284, 285, 286, 287, 288, 289, 290 }, "刻意的分歧：改用设备自报位，已有注释与测试说明。"),
		new HardcodeEntry("C22", "IsMechrevo 品牌门存在但无消费点", HardcodeDisposition.KeptWithReason, new int[4] { 222, 307, 308, 309 }, "品牌判定保留为诊断用途；无启动门控消费点（勘查 UNKNOWN#1）。")
	};
}
