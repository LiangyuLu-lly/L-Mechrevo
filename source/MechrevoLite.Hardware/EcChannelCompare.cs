// 2026-10-18（宝龙达 · 风扇接管）：⭐⭐ **EC 双通道只读对照** —— LPC/SuperIO 与 ACPI EC ✅
//
// ⭐⭐ 背景（机主 2026-10-18）：LPC/SuperIO 通道（`EcLpcIo`，PawnIO LpcIO 模块）**读成功**：
//    `0x2000=0x55 · 0xC411=0x23 · 0xC834/0xC835=0x14`；但**写失败**（写 0x1060 后回读仍 0x00）。
//    ⇒ ⭐ 需要判断：这是"ioctl 被模块拒绝"还是"EC 不认这个写序列"，
//      以及 **ACPI EC（`EcPortIo`，0x62/0x66 · XRAM mailbox 命令 0x92）能否读到同一批地址**。
//
// ⭐ 本类只做**只读对照**（⛔ 不写任何 EC 字节、⛔ 不解锁、⛔ 不发 0x93/0x81）：
//    · 用 `EcPortIo.TryReadByte`（XRAM mailbox）尝试读同一批地址；
//    · 若 ACPI EC 能读到 `0x2000 == 0x55` ⇒ 说明**两条通道都能读** ⇒ 写可能应走 ACPI EC
//      （⭐ 下一步再验证写，**本轮不写**）。
//
// 许可/署名：与本树同一实现，见 THIRD-PARTY-NOTICES.md 与 LICENSES\。

using System;

namespace MechrevoLite.Hardware;

/// <summary>
/// ⭐⭐ EC 双通道**只读**对照（LPC/SuperIO `EcLpcIo` vs ACPI EC `EcPortIo`）✅。
/// </summary>
internal static class EcChannelCompare
{
	private static readonly int[] Addresses = { 0x2000, 0x1060, 0xC411, 0xC83C, 0xC83D, 0xB20, 0xC834, 0xC835 };

	private static readonly string[] Names =
	{
		"芯片 ID（期望 0x55）",
		"解锁寄存器",
		"EC 版本",
		"Fan1 目标",
		"Fan2 目标",
		"接管掩码",
		"Fan1 转速字节",
		"Fan2 转速字节"
	};

	/// <summary>
	/// ⭐ 只读记录 ACPI EC 通道对同一批地址的读数（⭐ 失败如实写原因；⛔ 不写任何 EC 数据）✅。
	/// </summary>
	internal static void LogAcpiReadOnly(Action<string> log)
	{
		log("── ACPI EC 只读对照（EcPortIo · 0x62/0x66 · XRAM mailbox 命令 0x92）──");
		log("ACPI 通道状态：" + EcPortIo.DescribeChannel());
		bool id55 = false;
		for (int i = 0; i < Addresses.Length; i++)
		{
			int address = Addresses[i];
			bool ok = EcPortIo.TryReadByte(address, out int value);
			if (ok)
			{
				log("  ACPI EC 0x" + address.ToString("X4") + "（" + Names[i] + "）= 0x" + value.ToString("X2"));
				if (address == 0x2000 && value == 0x55)
				{
					id55 = true;
				}
			}
			else
			{
				log("  ACPI EC 0x" + address.ToString("X4") + "（" + Names[i] + "）= ⛔ 读不到 · "
					+ EcPortIo.LastReadFailure);
			}
		}
		log(id55
			? "  ⇒ ⭐ ACPI EC 能读到 0x2000=0x55 ⇒ **两条通道都能读** ⇒ 写优先试 ACPI EC（⭐ 下一步验证，⛔ 本轮不写）"
			: "  ⇒ ACPI EC 读 0x2000 不是 0x55 / 读不到 ⇒ ⭐ 仅记录（⛔ 本轮不写；等下一轮决定写通道）");
	}
}
