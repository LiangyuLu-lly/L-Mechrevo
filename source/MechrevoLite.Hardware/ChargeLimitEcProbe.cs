namespace MechrevoLite.Hardware;

/// <summary>
/// 2026-10-10（限充真效果判据 · 按上游 Leo-stro/mechrevo-charge-limit 口径）：
/// 一次**只读**探测的结果载体 —— 把限充相关的 EC 寄存器原始值打成一个包，日志/证据结构/诊断包共用同一份口径。
///
/// ⚠️ 本类型只承载"读到了什么"，**不含任何写入语义**：构造它不会写 EC，字段为 <see cref="Unreadable"/> 表示
/// 该地址读不到（通道打不开 / 机型差异 / 寄存器不存在）。⛔ 任何调用方都不得据此改变写入行为。
/// </summary>
internal readonly struct ChargeLimitEcProbe
{
	/// <summary>读不到该地址（-1）。⛔ 不等于 0，也不等于"未生效"。</summary>
	internal const int Unreadable = -1;

	internal ChargeLimitEcProbe(int upper, int lower, int gate, int effect, int storedLimit, int romId, bool channelOpened, string channelNote)
	{
		Upper = upper;
		Lower = lower;
		Gate = gate;
		Effect = effect;
		StoredLimit = storedLimit;
		RomId = romId;
		ChannelOpened = channelOpened;
		ChannelNote = channelNote;
	}

	/// <summary>未探测过的空值（全部不可读）。</summary>
	internal static readonly ChargeLimitEcProbe Empty = new(-1, -1, -1, -1, -1, -1, false, "未探测");

	/// <summary>
	/// 0x07B9 实时充电上限的**原始字节**（0 = 不限）。⚠️ bit7 是固件自己置的"限充已启用"标志
	/// （⭐ 用户实测：上限 80 ⇒ 0xD0、上限 60 ⇒ 0xBC、不限充 ⇒ 0x00），**低 7 位才是百分数**。
	/// ⛔ 本字段照实保留原始值（诊断/日志要能看到标志位），要百分数请用 <see cref="EcChargeLimit.PercentFor"/>。
	/// </summary>
	internal int Upper { get; }

	/// <summary>
	/// 0x07D0 复充下限（**原始值**）。⚠️ 上游 Leo-stro/mechrevo-charge-limit **不写**这个寄存器
	/// （只写 0x07B9），下限由固件 store_limit() 自己推导；LM 自 2026-10-10 起也**只读不改**，
	/// 读它是为了验证"固件推出来的下限是否仍 == 上限 − 5"。
	/// </summary>
	internal int Lower { get; }

	/// <summary>0x07C3 产品线门控（上游：非 4/5 时固件不执行限充控制循环）。</summary>
	internal int Gate { get; }

	/// <summary>0x0742 bit2 = 固件"限充已生效"标志（上游口径的唯一真效果判据）。</summary>
	internal int Effect { get; }

	/// <summary>0x087F 固件存储区（跨重启持久）；上游为 H2RAM 盲区，主机通常读不到。</summary>
	internal int StoredLimit { get; }

	/// <summary>0x0770 ROMID 首字节（上游用它做 4/5 产品线判断）。</summary>
	internal int RomId { get; }

	/// <summary>通道（\\.\ACPIDriver）是否打开成功。</summary>
	internal bool ChannelOpened { get; }

	/// <summary>通道说明或打不开的原因（写进日志/诊断包，便于以后按机型判断）。</summary>
	internal string ChannelNote { get; }

	/// <summary>0x0742 的 bit2 是否置位；null = 该寄存器读不到（⇒ 无法判定）。</summary>
	internal bool? EffectBitSet => (Effect < 0) ? null : ((Effect & EcChargeLimit.EffectBitMask) != 0);

	/// <summary>把原始字节格式化成日志用文本；不可读时明确写"不可读"。</summary>
	internal static string FormatRaw(int raw)
	{
		return (raw < 0) ? "不可读" : ("0x" + raw.ToString("X2"));
	}

	/// <summary>把 0x0742 的原始值与 bit2 一起格式化成"生效 / 未生效 / 无法判定"的依据片段。</summary>
	internal string DescribeEffect()
	{
		if (Effect < 0)
		{
			return "无法判定（0x0742 读不到）";
		}
		bool set = (Effect & EcChargeLimit.EffectBitMask) != 0;
		return $"{(set ? "生效" : "未生效")}（0x0742=0x{Effect:X2}，bit2={(set ? 1 : 0)}）";
	}

	/// <summary>
	/// 一行式摘要（诊断包/日志用）。⭐ 0x07B9 照实打【原始字节】不掩码，并把 bit7（固件"限充已启用"
	/// 标志）与低 7 位（真正的百分数）分别标出 —— ⛔ 不要把标志位藏掉。
	/// </summary>
	internal string DescribeAll()
	{
		string upperNote = (Upper < 0)
			? string.Empty
			: $"（bit7={(((Upper & EcChargeLimit.EnabledFlagMask) != 0) ? "1=已启用限充" : "0")}，低 7 位=0x{(Upper & EcChargeLimit.PercentMask).ToString("X2")}={EcChargeLimit.PercentFor(Upper)}%）";
		return $"0x{EcChargeLimit.UpperRegister:X3}={FormatRaw(Upper)}{upperNote}、0x{EcChargeLimit.LowerRegister:X3}={FormatRaw(Lower)}、"
			+ $"0x{EcChargeLimit.GateRegister:X3}={FormatRaw(Gate)}、0x{EcChargeLimit.EffectRegister:X3}={FormatRaw(Effect)}、"
			+ $"0x{EcChargeLimit.RomIdRegister:X3}={FormatRaw(RomId)}、0x{EcChargeLimit.StoredLimitRegister:X3}={FormatRaw(StoredLimit)}";
	}
}
