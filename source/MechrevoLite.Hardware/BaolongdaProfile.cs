// ⭐⭐ 2026-10-17（平台声明式 · **第 1 步：只放事实，不放行为**）：
// 「宝龙达平台」的**单一事实表** —— 身份 / 数值范围 / 通道语义 / 温度来源顺序 / 百分比分母。
//
// ⭐⭐ 为什么要有这个文件（用户 2026-10-17：「所谓声明式架构是什么 · 我们也可以对不同平台分离吗」）：
//   现状是 **同一件事在几十处各判一遍** —— `UsesBaolongda` 77 处 / `IsGcuConnected` 74 处 /
//   `UsesLegacyMifs` 17 处 ⇒ 改一处漏一处。本仓已经踩过三次同类事故：
//     · 功能号 22 被当成"整机是否接管"的判据 ⇒ 30 系整机退回 legacy
//     · 功能号 21 的**读值**被当成"最大转速" ⇒ 百分比 40% vs 官方 33%
//     · 亮度档位 3/4 按代数分叉，散在换算函数里
//   ⇒ 第 1 步先把**事实**收拢到一处；**行为**（端口 I/O、延时、重试、回读校验）仍留在各自实现里。
//
// ⚠️⚠️ **本文件的纪律**：
//   ① ⛔ **不放任何行为**（不读注册表、不读 WMI、不打日志、不写硬件）—— 纯常量 + 纯函数 ⇒ 可单测；
//   ② ⛔ **不重复**协议层已有的定义：功能号枚举在 `BaolongdaWmi.Function`，板号判据在 `BoardWhitelist`
//      ⇒ 这里**只引用，不抄一份**（抄一份就又多一处要同步的事实）；
//   ③ 任何"按代数不同"的值，一律写成**纯函数**（如 `BrightnessMax(gen)`），⛔ 不要散在调用点的 if 里。
//
// 许可/署名：与本树同一实现，见 THIRD-PARTY-NOTICES.md 与 LICENSES\。

using System;
using MechrevoLite.Gpu;

namespace MechrevoLite.Hardware;

/// <summary>
/// ⭐⭐ 宝龙达平台的**声明式事实表**（第 1 步：身份 / 范围 / 语义 / 来源顺序）。
/// <para>⚠️ 本类**只有事实**：⛔ 不碰硬件、⛔ 不打日志、⛔ 不读注册表 ⇒ 由
/// <see cref="BoardWhitelist" />（身份）与 <c>BaolongdaWmi</c>（协议）分别负责各自的部分。</para>
/// <para>⭐ 使用方：<c>MechrevoHw</c> 的常量与换算、<c>HardwareControl</c> 的日志（来源顺序）、
/// 以及自检 <c>tools\dev\bdprofile-selftest</c> ✅。</para>
/// </summary>
internal static class BaolongdaProfile
{
	// ══════════════════════════════════════════════════════════════════
	// ① 身份（⭐ 只做**委托**：判据本体在 BoardWhitelist，⛔ 不在这里复制）
	// ══════════════════════════════════════════════════════════════════

	/// <summary>⭐ 这块主板是不是宝龙达（⭐ 委托 <see cref="BoardWhitelist.Matches" /> ✅）。</summary>
	internal static bool MatchesBoard(string baseBoardProduct)
	{
		return BoardWhitelist.Matches(baseBoardProduct);
	}

	// ══════════════════════════════════════════════════════════════════
	// ② 风扇（功能号 13 读 · 20/21 写）
	// ══════════════════════════════════════════════════════════════════

	/// <summary>
	/// ⭐⭐ 转速**百分比**的分母（RPM ✅）：⭐ **40 系**口径 = **RPM ÷ 60** ⇒ 100% = 6000 ✅。
	/// <para>⚠️ 为什么不是"读功能号 21"：原厂把功能号 21 的**读值**当**手动转速设定值**
	/// （⛔ 不是最大转速）⇒ 拿它当分母会随"上次手动设过多少"漂移 ❌（详见
	/// <c>docs\notes\0.291.9.8-风扇曲线修复与开关-20261017.md</c> §七）。</para>
	/// <para>⭐⭐ 2026-10-18（机主新决定）：⭐ 本常量**只作为 40 系别名保留**（⛔ 避免大面积改动）；
	/// ⭐ **按代际取值请用 <see cref="FanPercentDenominatorRpmFor" />**（30 系已改为 **4900**）✅</para>
	/// </summary>
	internal const int FanPercentDenominatorRpm = 6000;

	/// <summary>
	/// ⭐⭐ 2026-10-18（机主新决定 · 30 系风扇基准）：⭐ 百分比**分母按代际** ——
	/// **30 系 = 4900 RPM · 40 系 = 6000 RPM** ✅。
	/// <para>⭐ 依据：⭐ <b>机主实测：30 系 3500 RPM 压不住；原厂上限约 140%（4900 RPM）
	/// ⇒ 以其为 100% 基准</b> ✅（⭐ 3500 只是旧版原厂控制台的 ÷35 显示口径，⛔ 不代表散热需求）。</para>
	/// <para>⚠️ 本函数是**纯函数**（⛔ 不读硬件）；⭐ 非宝龙达由调用方先判 <c>UsesBaolongda</c> ✅。</para>
	/// </summary>
	internal static int FanPercentDenominatorRpmFor(DgpuGenerationKind generation)
	{
		return (generation == DgpuGenerationKind.Gen30) ? FanTrueMaxUnits30Series * 100 : FanPercentDenominatorRpm;
	}

	/// <summary>
	/// ⭐⭐ 2026-10-18（机主 30 系 · 按开源蛟龙口径放宽）：⭐ 手动转速的**档位上限**（⭐ 功能号 21 的入参单位
	/// = `x100 RPM`）⇒ **68 × 100 = 6800 RPM** ✅。
	/// <para>⭐ 依据：参考自 **JiaolongControl (MIT)** 的 `MAX_FAN_BYTE = 68`（68 × 100 = 6800 RPM），
	/// ⭐ 只取这个**数值事实**，⛔ 未复制代码 ✅。</para>
	/// <para>⚠️ 只放宽上限，⛔ **不改"永不写超协议字节范围"的原则**（功能号 21 的 InData 是 byte ⇒ ≤255）✅；
	/// ⭐ 实际能不能到 68 仍**以功能号 13 回读为准**（被固件夹回就把实测值当本机上限）✅。</para>
	/// </summary>
	internal const int FanManualMaxUnits100Rpm = 68;

	/// <summary>
	/// ⭐⭐ 2026-10-18（机主 30 系 · 按开源蛟龙做法加 RPM 斜坡）：⭐ 曲线闭环**涨**速上限 = **800 RPM/s** ✅。
	/// <para>⭐ 来源：参考自 **JiaolongControl (MIT)** 的 `MaxRampUpRpmPerSec = 800.0`
	/// —— ⭐ 仅取**数值与"按 tick 限速"的思路**，⛔ 未复制代码 ✅。</para>
	/// <para>⭐ 换算：每 tick 最多涨 `800 × tick秒 ÷ 100` 档（3 s tick ⇒ **24 档**）；
	/// ⭐ 强冷**不走斜坡**（用户要立刻狂转）✅。</para>
	/// </summary>
	internal const int FanCurveRampUpRpmPerSec = 800;

	/// <summary>
	/// ⭐⭐ 2026-10-18（机主 30 系 · 按开源蛟龙做法加 RPM 斜坡）：⭐ 曲线闭环**降**速上限 = **150 RPM/s** ✅。
	/// <para>⭐ 来源：参考自 **JiaolongControl (MIT)** 的 `MaxRampDownRpmPerSec = 150.0`
	/// —— ⭐ 仅取**数值与"按 tick 限速"的思路**，⛔ 未复制代码 ✅。</para>
	/// <para>⭐ 换算：每 tick 最多降 `150 × tick秒 ÷ 100` 档（3 s tick ⇒ 4 档，⭐ 取整**保守**）；
	/// ⭐ 降得慢是为防抖/护噪音 ✅。</para>
	/// </summary>
	internal const int FanCurveRampDownRpmPerSec = 150;

	/// <summary>
	/// ⭐⭐ 2026-10-18（机主新决定）：⭐ 30 系**真实满转档位** = **49**（⭐ 4900 RPM = 100% ✅）。
	/// <para>⭐ 依据：⭐ <b>机主实测：30 系 3500 RPM 压不住；原厂上限约 140%（4900 RPM）
	/// ⇒ 以其为 100% 基准</b> ✅（⛔ 不是"超频"，⭐ 4900 就是这台 30 系该有的满转）。</para>
	/// <para>⚠️ 旧实现把 30 系满转当 3500（原厂 ÷35 显示口径）⇒ 强冷/曲线都只到 100% ❌。</para>
	/// </summary>
	internal const int FanTrueMaxUnits30Series = 49;

	/// <summary>⭐ 40 系**真实满转档位** = **60**（⭐ 6000 RPM，⭐ 原厂口径 100% ✅）。</summary>
	internal const int FanTrueMaxUnits40Series = 60;

	/// <summary>⭐ `FanType 0` = 大扇 / GPU（⭐ owner 已确认 · ⚠️ 语义仍待真机交叉验证 ✅）。</summary>
	internal const int FanTypeLarge = 0;

	/// <summary>⭐ `FanType 1` = 小扇 / CPU（⭐ 同上 ✅）。</summary>
	internal const int FanTypeSmall = 1;

	// ══════════════════════════════════════════════════════════════════
	// ③ 功耗墙 / 温度墙（功能号 23 · ⚠️ 只写不读）
	// ══════════════════════════════════════════════════════════════════

	/// <summary>⭐ PL1 / PL2 的合理下限（W ✅）—— ⭐ 防手滑，⛔ 不是协议限制 ✅。</summary>
	internal const int PlMinWatts = 5;

	/// <summary>⭐ PL1 允许上限（W ✅）。</summary>
	internal const int Pl1MaxWatts = 120;

	/// <summary>⭐ PL2 允许上限（W ✅）。</summary>
	internal const int Pl2MaxWatts = 150;

	/// <summary>⭐ 温度墙合理下限（℃ ✅）。</summary>
	internal const int TccMinCelsius = 60;

	/// <summary>⭐ 温度墙合理上限（℃ ✅）。</summary>
	internal const int TccMaxCelsius = 105;

	/// <summary>⭐ "这一项未设置"（⭐ 传 ≤0 表示跳过 · ⛔ 不拿编造值凑满三项 ✅）。</summary>
	internal const int Unset = 0;

	// ⭐⭐ 2026-10-19（30 系 · SMU 功耗**时间窗**）：
	//   30 系没有 WMI 功能号 23 ⇒ PL/温度墙/时间窗都走 SMU（`RyzenSmuService.SetStapmTimeSeconds`）。
	//   ⚠️ 这两个常数只是 **UI 可操作区间 + 下发前校验区间**（同一来源，避免"UI 能拖、校验却拒"），
	//      ⛔ **不是** SMU 协议限制（协议没有公开范围）。
	//   ⚠️ 下限取 1 秒：0 秒的语义未验证（可能被固件当成"立即生效/非法"）⇒ ⛔ 不提供 0。
	//   ⚠️ 上限取 600 秒：覆盖常见的 STAPM 时间窗（RyzenAdj 档默认写在 280 秒这一档）；
	//      该上限只约束**界面/校验**，不在服务层夹取（服务层原样下发、由调用方负责取值合理）。
	internal const int StapmTimeMinSeconds = 1;

	/// <summary>⭐ 30 系「功耗时间窗（STAPM）」的 UI/校验上限（秒）—— ⚠️ 见上面注释：不是协议限制。</summary>
	internal const int StapmTimeMaxSeconds = 600;

	// ══════════════════════════════════════════════════════════════════
	// ④ 键盘灯（功能号 16/17/18）
	// ══════════════════════════════════════════════════════════════════

	/// <summary>
	/// ⭐⭐ 键盘灯亮度**上限**（⭐ 界面口径）—— ⭐ 按 GPU 代数（⭐ 唯一的分叉点 ✅）。
	/// <para>⭐ 依据：40 系原厂把枚举定义到 `Level_4` 且实际使用 0~4；30 系只确认到 0~3
	/// ⇒ **Gen40/Gen50 = 4 · 其余 = 3** ✅。</para>
	/// <para>⚠️ 30 系仍属保守取值（待真机确认 30 系是否也支持 4）。</para>
	/// </summary>
	internal static int BrightnessMax(DgpuGenerationKind generation)
	{
		// ⭐⭐ 2026-10-18（机主实测纠正）：⭐ 40 系键盘亮度只有 **0/1/2/3** 四档（最大 = 3）⚠️
		//   ⚠️ 原来这里给 Gen40/50 返回 4 ⇒ UI 能拖到「4」但固件不支持 ⇒ 表现为「最大亮度不能用」❌
		//   ⭐ 现在统一 3（与 30 系一致）；若将来某机型实测有第 5 档，再用运行时探测替换本常量 ✅
		return 3;
	}

	/// <summary>
	/// ⭐⭐ 2026-10-17：键盘灯（功能号 16）**本平台实际可写**的模式值 —— ⭐ 只有 1（自动循环）与 2（固定色）✅。
	/// <para>⭐ 依据：40 系真机（MRID6-23）实测**写模式 3 后回读仍为 2** ⇒ ⭐ 固件不接受模式 3 ❌；
	/// 原厂枚举里的 3 也只是定义、从不写入 ⚠️ ⇒ ⭐ 按能力位铁律**不提供该选项** ✅。</para>
	/// <para>⚠️ 0（关）不作为"写模式"使用：⭐ 关灯 ＝ 写亮度（功能号 18）＝ 0 ✅。</para>
	/// </summary>
	internal static bool IsKeyboardModeWritable(byte mode)
	{
		return mode == 1 || mode == 2;
	}

	/// <summary>
	/// ⭐⭐ 2026-10-17：⭐ 宝龙达键盘灯**可写**的效果 Id ✅
	/// （⭐ 与 <see cref="IsKeyboardModeWritable" /> 同一口径：Off 走亮度 0、Single→2、Rainbow→1；
	/// ⛔ 不含 Manual／模式 3）。
	/// </summary>
	internal static readonly string[] KeyboardEffectIds = { "Off", "Single", "Rainbow" };

	/// <summary>
	/// ⭐ 2026-10-17：把任意已保存的效果 Id 收敛到**本机真能写**的那三项 ✅。
	/// <para>⚠️ 旧配置里的 <c>Manual</c>（模式 3，本机写不进）⇒ ⭐ 固定色 <c>Single</c>
	/// （模式 2 ＋ 用户颜色），⛔ 不再假装支持「自定义颜色」✅；空/未知值同样收敛到 <c>Single</c> ✅。</para>
	/// </summary>
	internal static string NormalizeKeyboardEffect(string effect)
	{
		if (!string.IsNullOrWhiteSpace(effect))
		{
			foreach (string id in KeyboardEffectIds)
			{
				if (string.Equals(id, effect, StringComparison.OrdinalIgnoreCase))
				{
					return id;
				}
			}
		}
		return "Single";
	}

	// ══════════════════════════════════════════════════════════════════
	// ⑤ 温度来源顺序（⭐ **声明**：引擎按此实现，日志按此打印，⛔ 不改变行为）
	// ══════════════════════════════════════════════════════════════════

	/// <summary>⭐ 温度来源（⭐ 声明用 · ⚠️ 执行顺序仍由 <c>HardwareControl.ApplySensorPriority</c> 实现）。</summary>
	internal enum TempSource
	{
		/// <summary>⭐ 厂商接口（⭐ CPU = 功能号 22；⭐ GPU = 宝龙达**没有** ⚠️）。</summary>
		Vendor,

		/// <summary>⭐ LM 自带的 LHM（⭐ GPU 支路在我们这里被主动关掉，防 NVML 原生崩溃 ⚠️）。</summary>
		Lhm,

		/// <summary>⭐ 加固过的 NVML（⭐ NVIDIA 机型 GPU 温度的**实际唯一实时来源** ✅）。</summary>
		Nvml,

		/// <summary>⭐ ACPI 热区（⭐ 免驱动兜底，⭐ 封装温度口径 ⚠️）。</summary>
		Acpi
	}

	/// <summary>
	/// ⭐⭐ CPU 温度来源顺序（声明）：⭐ 厂商（功能号 22）→ LHM → **ACPI 热区** ✅
	/// <para>⚠️ 30 系的 22 返回 0（无效）、LHM 读核心温度要驱动 ⇒ ACPI 是最后一档。</para>
	/// </summary>
	internal static readonly TempSource[] CpuTempSources =
	{
		TempSource.Vendor,
		TempSource.Lhm,
		TempSource.Acpi
	};

	/// <summary>
	/// ⭐⭐ GPU 温度来源顺序（声明）：⭐ 厂商（宝龙达**没有**）→ LHM（GPU 支路已关）→ **NVML** ✅
	/// <para>⚠️ 所以实际只有 NVML 一条实时来源 ⇒ 它取不到就只能显示「—」（⛔ 不谎报）。</para>
	/// </summary>
	internal static readonly TempSource[] GpuTempSources =
	{
		TempSource.Vendor,
		TempSource.Lhm,
		TempSource.Nvml
	};

	/// <summary>⭐ 把声明的人读描述出来（⭐ 用于启动日志 / 诊断包，⛔ 不参与任何判断 ✅）。</summary>
	internal static string DescribeTemperatureSources()
	{
		return "CPU=[" + Join(CpuTempSources) + "] · GPU=[" + Join(GpuTempSources) + "]";

		static string Join(TempSource[] sources)
		{
			string[] names = new string[sources.Length];
			for (int i = 0; i < sources.Length; i++)
			{
				names[i] = sources[i] switch
				{
					TempSource.Vendor => "厂商",
					TempSource.Lhm => "LHM",
					TempSource.Nvml => "NVML",
					TempSource.Acpi => "ACPI热区",
					_ => sources[i].ToString()
				};
			}
			return string.Join("→", names);
		}
	}
}
