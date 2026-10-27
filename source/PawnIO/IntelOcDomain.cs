namespace PawnIO;

// ==============================================================================================
// 2026-10-10 intel（⑦B）：Intel OC 邮箱的一个"域"（domain）的当前值与能力快照。
//
// 来源：上游（作者 0.290.6）`PawnIO\IntelOcDomain.cs`（509 B）**逐字移植**（含位布局），
//      协议来源 kitsunyan/intel-undervolt（GPL-3.0），署名见 THIRD-PARTY-NOTICES.md §1.3。
//
//   * Capabilities 位布局（由 IntelOcMailbox.Command(1, domain, 0) 读回）：
//       bit10 (0x400) -> 支持电压偏移（VoltageSupported）
//       bit8  (0x100) -> 支持倍频（RatioSupported）+ 低 8 位 = 最大倍频
//   * Value 位布局（由 IntelOcMailbox.Command(16, domain, 0) 读回）：
//       低 8 位   -> 当前倍频（Ratio）
//       位 21..31 -> 11 位补码电压偏移（OffsetMv，见 IntelOcMailbox.DecodeOffset）
//
// ⚠️ 本文件由**作者**编写（L-Mechrevo 上游），我们合并进来；与 CpuInfo.IsAMD 无关，
//    只有 RuntimeCpuTuning 的 Intel 分支才会构造它。
// ==============================================================================================
internal sealed record IntelOcDomain(int Id, uint Value, uint Capabilities)
{
	public bool VoltageSupported => (Capabilities & 0x400) != 0;

	public bool RatioSupported
	{
		get
		{
			if ((Capabilities & 0x100) != 0 && MaximumRatio >= Ratio)
			{
				return Ratio > 0;
			}
			return false;
		}
	}

	public int MaximumRatio => (int)(Capabilities & 0xFF);

	public int Ratio => (int)(Value & 0xFF);

	public decimal OffsetMv => IntelOcMailbox.DecodeOffset(Value);
}
