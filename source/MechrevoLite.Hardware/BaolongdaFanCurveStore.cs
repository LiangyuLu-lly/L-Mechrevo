// 2026-10-16（0.291.9 beta22.9 · 宝龙达适配 · "全部接入"第 4 步 · 风扇曲线 · ⭐ 只做存储）：
// 2026-10-16（0.291.9.4 · ⭐ **第 1 步：数据模型改造** ✅ —— ⭐ 本文件大改，⭐ 见下 ⚠️）：
//   ⭐ ① ⭐ 曲线单位由「⭐ 占空比 %」⭐ 改为 ⭐ **RPM** ✅（⭐ 见 `FanCurveEngine.cs` 的理由 ✅）
//   ⭐ ② ⭐ 通道改**中性命名** `Channel0` / ⭐ `Channel1` ✅（⭐ 对应协议 `FanType 0/1` ✅）
//       ⚠️ 因为**哪个是 CPU、⭐ 哪个是 GPU 尚未真机确认** ⚠️ ⇒ ⭐ ⛔ 不再叫 Cpu*/Gpu* ✅
//   ⭐ ③ ⭐ ⭐ **拆掉 `ModeId` / ⭐ `FirmwareSlot` 这条错误的耦合** ✅ —— ⭐ 见下"⭐ 为什么要改"⚠️
//   ⭐ ④ ⭐ 存档加**版本号** `format=2` ✅ ⇒ ⭐ 旧格式（⭐ 占空比 ✅）**读不懂就不启用** ✅
//   ⭐ ⑤ ⭐ ⭐ **曲线按"⭐ 通道"⭐ 整体校验** ✅ —— ⚠️ 见下"⭐ 为什么必须整体校验"⚠️
//
// ⭐⭐ 宝龙达**软件闭环风扇曲线**的本地持久化 ✅
//
// ⭐⭐ 为什么需要它（⭐ 用户 2026-10-16 原话：「四 宝龙达是风扇曲线是吗 能不能接我们的曲线里面」）：
//    ⭐ 我们的风扇曲线（`FanCurveForm` / `PerfModeStore.CpuFanDuty`）⭐ 是"⭐ **上传表给 GCU 固件**"的 ✅
//       ⇒ ⭐ 固件拿到表后**自己按温度调**，⭐ 我们退出后它照样在跑 ✅
//    ⚠️ 而宝龙达协议（⭐ 功能号 20/21）⭐ **只有**「⭐ 手动开关 ＋ ⭐ 固定转速」，⭐ **没有曲线表** ⚠️
//       （⭐ 三份开源参考实现里也都没有 ✅）
//    ⇒ ⭐ ⭐ 所以宝龙达只能做 ⭐ **软件闭环**：⭐ **我们的程序读温度 → ⭐ 查曲线 → ⭐ 写功能号 21** ✅
//
// ⭐⭐ 为什么要改（⭐ 第 1 步的"⭐ 去耦合"⚠️ —— ⭐ 这条是**纠正一个错误模型** ✅）：
//    ⚠️ 本文件原来靠"⭐ `PerfModeService.RecordFanCurves` ⭐ 在 `Route != FirmwareSlot` 时跳过"⭐
//       来论证"⭐ 所以要另做一个存储"⚠️ —— ⭐ 而且带了一个 `ModeId`（⭐ "⭐ 曲线是在哪个档位下存的"⚠️）✅
//    ⭐ ⭐ 参考实现显示 ⭐ ⭐ **这个耦合本身是错的** ❌：
//       ⭐ 蛟龙把 `CpuFanCurve` / ⭐ `GpuFanCurve` 存在**应用自己的配置**里 ✅、
//          ⭐ 由 `AutoFanControl` 轮询温度写转速 ✅
//       ⭐ 斗战者的 `fan-curve.json` 同样是应用自己的配置 ✅、⭐ 走 `FanCurveService` 定时器 ✅
//       ⇒ ⭐ ⭐ ⭐ **真实生态里【没有"⭐ 固件槽"这个概念】** ✅ ⇒ ⭐ **风扇曲线是纯软件功能** ✅
//    ⇒ ⭐ ⭐ ⭐ **正确关系**（⭐ 见 `docs\notes\风扇曲线与自定义模式-方案-20261016.md` §5 ✅）：
//       ⭐ 「⭐ 档位」（⭐ 静音/平衡/狂暴/自定义 ✅）→ ⭐ 后端**模式切换**（⭐ 功能号 8 / 23 ✅）
//       ⭐ 「⭐ 风扇曲线」                      → ⭐ **独立软件引擎**（⭐ 功能号 20 / 21 ✅）
//       ⭐ ⭐ **两者正交**：⭐ 切档不换曲线、⭐ 曲线不随档位存 ✅
//    ⇒ ⭐ 所以本文件**删掉 `ModeId`** ✅、⭐ 并**不再引用** `FirmwareSlot` ⚠️
//       ⚠️ ⭐⭐ **但它【没有】动 `PerfModeService`** ⚠️ —— ⭐ 那边的 `FirmwareSlot` 硬门槛是**独立排期** ✅
//          （⭐ 方案文档 §5 明确要求别混进同一个改动 ✅）
//
// ⭐⭐ 为什么必须"⭐ 按通道整体校验"⚠️（⭐ 这条是**自检发现的真 bug** ✅）：
//    ⚠️ 原来的写法是**并列两个数组各自兜底** ⚠️（⭐ `chXtemp` 坏了就用默认温度、
//       ⭐ 而 `chXrpm` 若是好的就照用 ✅）⇒ ⭐ ⭐ **会拼出一条"⭐ 默认温度 × ⭐ 用户转速"⭐ 的怪曲线** ❌
//       （⭐ 例如 40℃ 配 21 RPM ⚠️ —— ⭐ 两条数组本来描述的是**同一条曲线** ✅）
//    ⇒ ⭐ ⭐ ⭐ **一条曲线 ＝ ⭐ 一个整体** ✅ ⇒ ⭐ **任一数组读不懂 ⇒ ⭐ 这一份存档就算读不懂** ✅
//       ⛔ **绝不"⭐ 半个用默认、⭐ 半个用用户的"** ⚠️（⭐ 那正是"⭐ 猜"⭐ ❌）
//
// ⚠️ 本轮**只做存储 ＋ ⭐ 数据模型 ＋ ⭐ 决策器**（⭐ 用户要求 ⭐ 第 1 步 ＋ ⭐ 第 2 步 ✅）
//    ⇒ ⭐ ⛔ **不开** `FanSettings` 能力位（⭐ 因为⚠️ 风扇两通道归属未确认 ⇒ ⭐ 会违反铁律 ✅）
//    ⇒ ⭐ ⛔ **不下发任何转速**（⭐ 真机未验证功能号 20/21 的写入 ⚠️ ⇒ ⭐ 见方案 §6.2 ✅）
//
// 2026-10-19（0.291.9.13 · 宝龙达风扇曲线 · ⭐ 默认曲线校准 ＋ ⭐ 双风扇联动开关）：
//   ⭐ ① ⭐ 默认温度点：⭐ **40~100℃ · 16 点均匀**（每点 +4℃）✅
//   ⭐ ② ⭐ 默认 RPM：⭐ **按该机型"满转"乘百分比形状**生成 ✅
//        · 40 系（板号 `MRID6-23`）⭐ 满转 **6000 RPM**（原厂口径 `rpm / 60f`；真机 1965 RPM ⇒ 33% 已验证）✅
//        · 30 系（板号裸 `MRID6` · 6800H）⭐ 满转 **4900 RPM**（⭐⭐ 2026-10-18 机主新决定：
//          **机主实测 3500 RPM 压不住；原厂上限约 140%（4900 RPM）⇒ 以其为 100% 基准** ✅）
//        · 百分比形状锚点：⭐ 40℃≈30% · ⭐ 70℃≈58% · ⭐ 90℃≈73% · ⭐ 100%=100%（线性插值）✅
//   ⚠️⚠️ **重要定性**：这条默认曲线是【我们软件的默认起点】，⛔ **不是厂商下发的曲线数据** ⚠️
//        —— 宝龙达协议（功能号 20/21）**根本没有曲线表** ✅（只有"手动开关 ＋ 固定转速"）
//        ⇒ ⭐ 机主可以随时在「风扇曲线」窗口自行调整，⭐ 存档里的值永远优先 ✅
//   ⭐ ③ ⭐ 新增 `syncenabled`（双风扇 **0.85 联动**开关，默认 **true**）✅
//        ⚠️ 旧存档（v2 早期）没有这个键 ⇒ ⭐ 读出时按 **true** 处理（向后兼容，⛔ 不当作坏档）✅
//
// ⭐ 算法/数值形状来源（⭐ 只取"做法/形状/系数"，⛔ 未复制任何代码）：
//   参考自 斗战者(GPL-3.0)/开源蛟龙(MIT)，仅取数值形状与算法思路，未复制代码 ✅
//     · 斗战者 12 点 40~100℃、RPM 1900→4400 的"低温柔和、高温陡升"形状；
//     · 蛟龙 `AutoFanControl` 的双风扇 0.85 联动（`SharedHeatPipeSyncRatio`）。
//   ⚠️ 本文件只保留"形状锚点/联动系数"这几个数字，全部实现为本仓自写 ✅
//   ⚠️ 数值定性：⭐ 这些是**我们软件的默认起点**，⛔ **不是厂商数据**，⭐ 用户可自行调整 ✅

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

namespace MechrevoLite.Hardware;

/// <summary>
/// ⭐⭐ 宝龙达**软件闭环风扇曲线**的设置快照 ✅
/// <para>⭐ <b>曲线形状与既有风扇曲线对齐</b>：⭐ 每条通道 ⭐ **16 个点** ✅
/// —— ⭐ 每点 = ⭐ (温度 ℃, ⭐ **转速 RPM** ✅)，⭐ 与 `MechrevoHw.CpuCurveUpT` 的点数同形状
/// ⇒ ⭐ 将来要复用 `FanCurveForm` 的编辑界面时**只需换单位** ✅。</para>
/// <para>⭐⭐ <b>通道是【中性】的</b>：⭐ <see cref="Channel0Temperatures" /> / ⭐ <see cref="Channel1Temperatures" />
/// 对应协议里的 ⭐ **`FanType 0` / ⭐ `FanType 1`** ✅。
/// ⚠️ <b>哪个是 CPU、⭐ 哪个是 GPU 尚未真机确认</b> ⚠️（⭐ 功能号 13 读到两个通道，⭐ 但归属没定 ✅）
/// ⇒ ⭐ ⛔ 本类型**不出现** Cpu/Gpu 字样 ✅ —— ⭐ 等 `--fan-watch` 确认后，
/// ⭐ 在**外面**加一层语义映射 ✅（⭐ ⛔ 不回来改数据模型 ⚠️）。</para>
/// <para>⭐⭐ <b>默认曲线已按代际校准</b>（⭐ 见 <see cref="DefaultRpms(int)" /> ✅）：
/// ⭐ 温度 **40~100℃ · 16 点均匀** · ⭐ RPM = **本机满转 × 百分比形状** ✅
/// （40 系 6000 RPM / 30 系 4900 RPM）。
/// ⚠️ 它是**我们软件的默认起点**，⛔ **不是厂商数据** ⚠️ —— 宝龙达协议没有曲线表，
/// ⭐ 用户随时可在「风扇曲线」窗口自行调整（⭐ 存档值永远优先）✅。</para>
/// <para>⭐ 安全性：<see cref="Enabled" /> **默认 false** ✅ ⇒ ⭐ 这条默认曲线**不会被静默应用** ✅。</para>
/// <para>⭐⭐ <b>双风扇联动</b>：<see cref="SyncEnabled" />（默认 true）—— 打开时两路按
/// <c>max(自己, (int)(对方 × 0.85))</c> 互相带动，适配共用热管 ✅。</para>
/// </summary>
internal sealed class BaolongdaFanCurveSettings
{
	/// <summary>⭐ 点数（⭐ 与既有曲线一致 = 16 ✅）。</summary>
	internal const int PointCount = 16;

	/// <summary>⭐⭐ 本程序**能解释**的存档版本号 ✅（⭐ 见 <see cref="BaolongdaFanCurveStore" /> 的兼容策略 ✅）。</summary>
	internal const int CurrentFormatVersion = 2;

	/// <summary>
	/// ⭐ 旧的（⭐ 2026-10-16 之前 ✅）格式版本号 ⚠️ —— ⭐ 单位是**占空比** ✅、⭐ 且带 `modeid` ✅。
	/// <para>⚠️ 它**无法**被解释成 RPM ⚠️（⭐ 缺 max RPM 这个分母 ✅）⇒ ⭐ ⭐ **读到它就不启用曲线** ✅。</para>
	/// <para>⭐ 也是"⭐ 文件里没有 <c>format</c> 键"⭐ 时的**推定版本** ✅。</para>
	/// </summary>
	internal const int LegacyDutyFormatVersion = 1;

	/// <summary>⭐ 默认轮询周期（ms ✅）—— ⭐ 蛟龙 1000 / ⭐ 斗战者 5000 ✅ ⇒ ⭐ 取蛟龙的 1000 ✅。</summary>
	internal const int DefaultIntervalMs = 1000;

	/// <summary>
	/// ⭐ 默认转速下界（RPM ✅）—— ⭐ 沿用既有口径 **1200 RPM** ✅（⭐ 刻意不从 0 起：长期停转不利于散热）。
	/// <para>⭐ 2026-10-18（机主新基准）：⭐ 30 系满转改为 **4900** ⇒ 默认曲线首点 = **1470 RPM**（&gt; 本下界）
	/// ⇒ ⭐ 不会再被抬升；⭐ 40 系首点 1800 也高于下界 ✅—— ⭐ 这是**安全下界**，⛔ 不是曲线算错 ✅。</para>
	/// </summary>
	internal const int DefaultMinRpm = 1200;

	/// <summary>⭐ 默认温度点起点（℃）✅。</summary>
	internal const int DefaultTemperatureStartC = 40;

	/// <summary>⭐ 默认温度点终点（℃）✅。</summary>
	internal const int DefaultTemperatureEndC = 100;

	/// <summary>
	/// ⭐ 40 系（板号 `MRID6-23`）的**满转** = **6000 RPM** ✅
	/// <para>⭐ 依据：原厂 40 系控制台把风扇百分比算作 `rpm / 60f` ⇒ 100% = 6000 RPM
	/// （真机 1965 RPM ⇒ 33% 已验证）✅</para>
	/// </summary>
	internal const int FullRpm40Series = 6000;

	/// <summary>
	/// ⭐⭐ 2026-10-18（机主新决定）：⭐ 30 系（板号裸 `MRID6` · 6800H）的**满转** = **4900 RPM** ✅
	/// <para>⭐ 依据：⭐ **机主实测：30 系 3500 RPM 压不住；原厂上限约 140%（4900 RPM）
	/// ⇒ 以其为 100% 基准** ✅（⛔ 不是厂商曲线数据；⭐ 3500 只是旧版原厂 ÷35 显示口径）✅</para>
	/// </summary>
	internal const int FullRpm30Series = 4900;

	/// <summary>
	/// ⭐ 没有硬件上下文（自检 / 未接后端）时的默认满转 —— 按 **40 系 6000 RPM** 处理 ✅。
	/// <para>⚠️ 真正的下发路径**必须**由调用方把本机满转传进来：
	/// <c>MechrevoHw.LoadBaolongdaFanCurve()</c> 传 <c>FanRpmMaxForPercent</c>（40 系 6000 / 30 系 4900）✅
	/// —— ⭐ 复用与 <c>MechrevoHw.FanRpmMaxForPercent</c> **同一条代际判据** ✅。</para>
	/// </summary>
	internal const int DefaultFullRpm = FullRpm40Series;

	/// <summary>⭐ 默认转速上界（RPM ✅）＝ <see cref="DefaultFullRpm" /> ✅（⭐ 默认曲线 100 ℃ 正好到满转）。</summary>
	internal const int DefaultMaxRpm = DefaultFullRpm;

	/// <summary>⭐ 软件闭环是否启用 ⚠️（⭐ 默认 **false** —— ⭐ 通道归属未确认前不许启用 ✅）。</summary>
	internal bool Enabled { get; init; }

	/// <summary>
	/// ⭐⭐ **双风扇 0.85 联动**开关（⭐ 默认 **true** ✅）—— ⭐ 只对宝龙达软件闭环有意义 ✅。
	/// <para>打开：两路各算完后按 ⭐ <c>max(自己, (int)(对方 × 0.85))</c> 互相带动 ✅
	/// （⭐ 适配共用热管：一侧热起来时另一侧不能还慢悠悠 ✅）。关闭：两路**各算各的** ✅。</para>
	/// <para>⚠️ <b>向后兼容</b>：旧的 v2 存档里**没有** <c>syncenabled</c> 键 ⇒ ⭐ 读出时按 **true** 处理 ✅
	/// （⛔ 不当作坏档、⛔ 不要求用户重设曲线 ✅）。</para>
	/// </summary>
	internal bool SyncEnabled { get; init; } = true;

	/// <summary>⭐ 存档版本号（⭐ 读出来时是文件里声明的值；⭐ 新建时是 <see cref="CurrentFormatVersion" /> ✅）。</summary>
	internal int FormatVersion { get; init; } = CurrentFormatVersion;

	/// <summary>⭐ 回差（℃ ✅）—— ⭐ 默认 3 ✅（⭐ 斗战者口径 ✅）。</summary>
	internal int HysteresisC { get; init; } = FanCurveEngine.DefaultHysteresisC;

	/// <summary>⭐ 轮询周期（ms ✅）。</summary>
	internal int IntervalMs { get; init; } = DefaultIntervalMs;

	/// <summary>⭐ 转速下界（RPM ✅）—— ⭐ 决策器用它夹紧 ✅。</summary>
	internal int MinRpm { get; init; } = DefaultMinRpm;

	/// <summary>⭐ 转速上界（RPM ✅）—— ⭐ 决策器用它夹紧 ✅。</summary>
	internal int MaxRpm { get; init; } = DefaultMaxRpm;

	/// <summary>⭐ 通道 0（⭐ 协议 `FanType 0` ✅）的温度点（⭐ 16 个 ✅）。</summary>
	internal int[] Channel0Temperatures { get; init; } = DefaultTemperatures();

	/// <summary>⭐ 通道 0 的转速（⭐ 16 个 · ⭐ RPM ✅）。</summary>
	internal int[] Channel0Rpms { get; init; } = DefaultRpms();

	/// <summary>⭐ 通道 1（⭐ 协议 `FanType 1` ✅）的温度点（⭐ 16 个 ✅）。</summary>
	internal int[] Channel1Temperatures { get; init; } = DefaultTemperatures();

	/// <summary>⭐ 通道 1 的转速（⭐ 16 个 · ⭐ RPM ✅）。</summary>
	internal int[] Channel1Rpms { get; init; } = DefaultRpms();

	/// <summary>
	/// ⭐⭐ 这份存档**读不懂**吗 ⚠️（⭐ 旧占空比格式 / ⭐ 版本号不认识 / ⭐ 某个值坏掉 ✅）。
	/// <para>⭐ 为 true 时 ⭐ <see cref="Enabled" /> **必为 false** ✅、⭐ 且曲线是**按代际校准的默认值** ✅
	/// ⇒ ⭐ ⛔ **不会被应用** ✅。⭐ 供 UI 如实告知用户"⭐ 存档没迁移、⭐ 请重新设一次" ✅。</para>
	/// <para>⭐ 具体原因见 <see cref="StatusNote" /> ✅ —— ⭐ ⛔ 不是"⭐ 没启用"⭐ 这么含糊 ⚠️。</para>
	/// </summary>
	internal bool IsUnreadableArchive { get; init; }

	/// <summary>⭐ 读档时的**如实说明**（⭐ 空 = ⭐ 没有特殊情况 ✅）—— ⭐ 供 UI/日志展示 ✅。</summary>
	internal string StatusNote { get; init; } = string.Empty;

	/// <summary>
	/// ⭐ 默认温度点：⭐ **<see cref="DefaultTemperatureStartC" />~<see cref="DefaultTemperatureEndC" />℃ ·
	/// 16 点均匀**（每点 +4℃）✅
	/// <para>⭐ 与既有曲线同为 16 点 ⇒ 存储格式、界面滑点都不变 ✅。</para>
	/// </summary>
	internal static int[] DefaultTemperatures()
	{
		int[] array = new int[PointCount];
		for (int i = 0; i < PointCount; i++)
		{
			array[i] = DefaultTemperatureStartC
				+ i * (DefaultTemperatureEndC - DefaultTemperatureStartC) / (PointCount - 1);
		}
		return array;
	}

	/// <summary>
	/// ⭐ 默认转速曲线（RPM ✅）—— ⭐ 无硬件上下文时按 <see cref="DefaultFullRpm" />（40 系 6000）生成 ✅。
	/// <para>⚠️ 带硬件路径请用 <see cref="DefaultRpms(int)" /> 传本机满转（30 系 4900）✅。</para>
	/// </summary>
	internal static int[] DefaultRpms()
	{
		return DefaultRpms(DefaultFullRpm);
	}

	/// <summary>
	/// ⭐⭐ 默认转速曲线（RPM ✅）：⭐ **本机满转 × 百分比形状** ✅
	/// <para>⭐ 形状锚点（<see cref="DefaultRpmPercent" />）：⭐ 40℃≈30% · ⭐ 70℃≈58% ·
	/// ⭐ 90℃≈73% · ⭐ 100%=100%（⭐ 线性插值 ✅）。</para>
	/// <para>⚠️ <paramref name="fullRpm" /> ≤ 0 ⇒ 回退到 <see cref="DefaultFullRpm" />（40 系）⚠️；
	/// 真正下发路径必须传本机满转（40 系 <see cref="FullRpm40Series" /> / 30 系 <see cref="FullRpm30Series" />）✅。</para>
	/// <para>⚠️ 这是**我们软件的默认起点**，⛔ **不是厂商数据** —— 宝龙达协议没有曲线表 ✅
	/// ⇒ 用户可在「风扇曲线」窗口自行调整（⭐ 存档值永远优先）✅。</para>
	/// </summary>
	internal static int[] DefaultRpms(int fullRpm)
	{
		int full = (fullRpm > 0) ? fullRpm : DefaultFullRpm;
		int[] temperatures = DefaultTemperatures();
		int[] array = new int[PointCount];
		for (int i = 0; i < PointCount; i++)
		{
			double percent = DefaultRpmPercent(temperatures[i]);
			// ⭐ 明确用 AwayFromZero：中点（如 30 系 4900 × 59.5% = 2915.5）向上取整，⛔ 不用银行家舍入（结果更直观）✅
			int rpm = (int)Math.Round(full * percent / 100.0, MidpointRounding.AwayFromZero);
			array[i] = Math.Clamp(rpm, 0, full);
		}
		return array;
	}

	/// <summary>
	/// ⭐⭐ 默认 RPM 的**百分比形状**（0~100 ✅）—— ⭐ 锚点线性插值：
	/// 40℃ = 30% · 70℃ = 58% · 90℃ = 73% · 100℃ = 100% ✅
	/// <para>⭐ 形状取自 斗战者(GPL-3.0) 12 点曲线（40~100℃、1900→4400 RPM）的
	/// "低温柔和、高温陡升"特征，⭐ 仅取**数值形状**，⛔ 未复制代码 ✅（⛔ 不是厂商数据）。</para>
	/// <para>⭐ 边界：≤40℃ ⇒ 30%（⛔ 不外推到更低）· ≥100℃ ⇒ 100%（⛔ 不超 100%）✅。</para>
	/// </summary>
	internal static double DefaultRpmPercent(int tempC)
	{
		if (tempC <= DefaultTemperatureStartC)
		{
			return 30.0;
		}
		if (tempC >= DefaultTemperatureEndC)
		{
			return 100.0;
		}
		if (tempC <= 70)
		{
			return 30.0 + (tempC - 40) * (58.0 - 30.0) / (70 - 40);
		}
		if (tempC <= 90)
		{
			return 58.0 + (tempC - 70) * (73.0 - 58.0) / (90 - 70);
		}
		return 73.0 + (tempC - 90) * (100.0 - 73.0) / (DefaultTemperatureEndC - 90);
	}

	/// <summary>
	/// ⭐⭐ 生成一份**默认曲线**（⭐ 已按代际校准 ✅）：⭐ 温度 40~100℃/16 点、RPM 按本机满转 × 形状 ✅。
	/// <para>⭐ 存档不存在 / 读不懂时用本方法返回（⭐ <c>Enabled=false</c> ⇒ ⛔ 不会被静默应用 ✅）。</para>
	/// <para>⚠️ <paramref name="fullRpm" /> ≤ 0 ⇒ 回退到 <see cref="DefaultFullRpm" />（40 系）⚠️。</para>
	/// </summary>
	internal static BaolongdaFanCurveSettings CreateDefault(int fullRpm)
	{
		int bandMax = (fullRpm > 0) ? fullRpm : DefaultFullRpm;
		int[] temperatures = DefaultTemperatures();
		int[] rpms = DefaultRpms(bandMax);
		return new BaolongdaFanCurveSettings
		{
			Enabled = false,
			FormatVersion = CurrentFormatVersion,
			SyncEnabled = true,
			HysteresisC = FanCurveEngine.DefaultHysteresisC,
			IntervalMs = DefaultIntervalMs,
			MinRpm = DefaultMinRpm,
			MaxRpm = bandMax,
			Channel0Temperatures = (int[])temperatures.Clone(),
			Channel0Rpms = (int[])rpms.Clone(),
			Channel1Temperatures = (int[])temperatures.Clone(),
			Channel1Rpms = (int[])rpms.Clone()
		};
	}

	/// <summary>⭐ 深拷贝（⭐ 数组类型是可变引用 ⇒ ⭐ 传出去前必须拷贝，⛔ 免得外部改到内部状态 ✅）。</summary>
	internal BaolongdaFanCurveSettings Clone(bool? enabledOverride = null)
	{
		return new BaolongdaFanCurveSettings
		{
			Enabled = enabledOverride ?? Enabled,
			FormatVersion = FormatVersion,
			SyncEnabled = SyncEnabled,
			HysteresisC = HysteresisC,
			IntervalMs = IntervalMs,
			MinRpm = MinRpm,
			MaxRpm = MaxRpm,
			Channel0Temperatures = (int[])Channel0Temperatures.Clone(),
			Channel0Rpms = (int[])Channel0Rpms.Clone(),
			Channel1Temperatures = (int[])Channel1Temperatures.Clone(),
			Channel1Rpms = (int[])Channel1Rpms.Clone(),
			IsUnreadableArchive = IsUnreadableArchive,
			StatusNote = StatusNote
		};
	}

	/// <summary>⭐ 拷贝一份、只改 <see cref="Enabled" /> ✅（⭐ 供界面「启用/关闭软件闭环」开关用 ✅）。</summary>
	internal BaolongdaFanCurveSettings WithEnabled(bool enabled)
	{
		return Clone(enabled);
	}

	/// <summary>
	/// ⭐ 拷贝一份、只改 <see cref="SyncEnabled" /> ✅（⭐ 供「风扇曲线」窗口的「双风扇联动」开关用 ✅）。
	/// <para>⚠️ 曲线 / 启用态 / 上下界 / 回差**全部原样** ✅（⛔ 改一个开关不顺手改别的）。</para>
	/// </summary>
	internal BaolongdaFanCurveSettings WithSyncEnabled(bool syncEnabled)
	{
		return new BaolongdaFanCurveSettings
		{
			Enabled = Enabled,
			FormatVersion = FormatVersion,
			SyncEnabled = syncEnabled,
			HysteresisC = HysteresisC,
			IntervalMs = IntervalMs,
			MinRpm = MinRpm,
			MaxRpm = MaxRpm,
			Channel0Temperatures = (int[])Channel0Temperatures.Clone(),
			Channel0Rpms = (int[])Channel0Rpms.Clone(),
			Channel1Temperatures = (int[])Channel1Temperatures.Clone(),
			Channel1Rpms = (int[])Channel1Rpms.Clone(),
			IsUnreadableArchive = IsUnreadableArchive,
			StatusNote = StatusNote
		};
	}
}

/// <summary>
/// ⭐⭐ 宝龙达软件风扇曲线的存储（⭐ 照 `LightingSettingsStore` 的风格：⭐ 纯文件、⭐ 可覆盖目录 ✅）
/// <para>⭐ 文件：⭐ <c>%APPDATA%\MechrevoLite\fan_curve_bd.cfg</c> ✅
/// （⭐ 与 `keyboard.cfg` / ⭐ `lightbar.cfg` / ⭐ `logolight.cfg` 同目录 ⇒ ⭐ 安装/卸载一起带走 ✅）</para>
/// <para>⚠️ 全程 `try/catch` 吞异常：⭐ 存储坏掉**绝不能**影响启动或运行 ✅（⭐ 与既有 Store 同款约定 ✅）。</para>
/// <para>⭐⭐ <b>读档策略</b>（⭐ 用户要求「⭐ 不要猜、⭐ 不要静默迁移」✅）：</para>
/// <list type="bullet">
/// <item>⭐ 文件里**没有** <c>format</c> 键 ⇒ ⭐ 判定为**旧的占空比格式**（⭐ v1 ✅）⇒ ⭐ ⛔ **不解释** ✅</item>
/// <item>⭐ <c>format</c> 不是 2（⭐ 含不是整数 ✅）⇒ ⭐ 同样**不解释** ✅（⭐ 将来版本号变了也不会误读 ✅）</item>
/// <item>⭐ ⭐ **v2 里任何一个出现的值读不懂 ⇒ ⭐ 整份存档算读不懂** ✅
/// （⭐ 曲线按"⭐ 通道"⭐ 整体校验 ⚠️ —— ⭐ 见文件头"⭐ 为什么必须整体校验"✅）</item>
/// <item>⭐ 不解释时：⭐ **曲线停在关闭状态** ✅ ＋ ⭐ 打一条**默认级别可见**的日志 ✅
/// （⭐ `Logger.WriteError` ＋ ⭐ `TryMarkChanged` 去重 ⇒ ⭐ 不刷屏 ✅）</item>
/// <item>⭐⭐ **⛔ 绝不静默迁移、⭐ ⛔ 绝不"⭐ 猜一个 RPM 出来"** ✅（⭐ 缺 max RPM 这个分母，
/// 占空比 → ⭐ RPM 的换算**在数学上就不成立** ⚠️）</item>
/// </list>
/// </summary>
internal static class BaolongdaFanCurveStore
{
	/// <summary>⭐ 目录覆盖变量（⭐ 与 `LightingSettingsStore` 用同一个 —— ⭐ 便于测试与迁移 ✅）。</summary>
	internal const string ConfigDirectoryOverrideVariable = "LMECHREVO_LIGHT_CFG_DIR";

	private const string FileName = "fan_curve_bd.cfg";

	/// <summary>
	/// ⭐ 读一次设置（⭐ 读不到/坏掉 ⇒ ⭐ 返回**默认**（⭐ `Enabled=false` ✅），⭐ 不外抛 ✅）。
	/// <para>⭐⭐ 参数 <paramref name="fullRpm" /> = ⭐ **本机风扇满转**（40 系 6000 / 30 系 4900）✅
	/// —— ⭐ **存档不存在 / 读不懂**时用它生成校准过的默认曲线 ✅。
	/// ⚠️ 传 ≤0 ⇒ 按 40 系 <see cref="BaolongdaFanCurveSettings.DefaultFullRpm" /> 处理（⭐ 仅供自检/无硬件路径）✅。</para>
	/// </summary>
	internal static BaolongdaFanCurveSettings Load(int fullRpm = 0)
	{
		int bandMax = (fullRpm > 0) ? fullRpm : BaolongdaFanCurveSettings.DefaultFullRpm;
		try
		{
			string path = GetPath();
			if (!File.Exists(path))
			{
				return BaolongdaFanCurveSettings.CreateDefault(bandMax);
			}
			Dictionary<string, string> map = ReadMap(path);

			// ⭐⭐ 先判版本：⭐ 认不出来就**不解释**（⭐ 这一条是第 1 步的核心要求 ✅）
			bool hasFormat = map.TryGetValue("format", out string formatRaw);
			int version = BaolongdaFanCurveSettings.LegacyDutyFormatVersion;
			if (hasFormat
				&& !int.TryParse(formatRaw, NumberStyles.Integer, CultureInfo.InvariantCulture, out version))
			{
				return Unreadable(BaolongdaFanCurveSettings.LegacyDutyFormatVersion, $"format=「{formatRaw}」不是整数", bandMax);
			}
			if (!hasFormat)
			{
				return Unreadable(BaolongdaFanCurveSettings.LegacyDutyFormatVersion,
					"文件里没有 format 版本号 ⇒ 判定为旧的占空比格式（占空比无法换算成 RPM）", bandMax);
			}
			if (version != BaolongdaFanCurveSettings.CurrentFormatVersion)
			{
				return Unreadable(version, $"format={version}（本程序只认 {BaolongdaFanCurveSettings.CurrentFormatVersion}）", bandMax);
			}

			// ⭐⭐ v2：⭐ **严格**解析 ✅ —— ⭐ 任何一个出现的值读不懂 ⇒ ⭐ 整份算读不懂 ⚠️（⛔ 不猜 ✅）
			List<string> problems = new List<string>();
			if (!TryParseInt(map, "hysteresis", FanCurveEngine.DefaultHysteresisC, out int hysteresis, out string e1)) { problems.Add(e1); }
			if (!TryParseInt(map, "intervalms", BaolongdaFanCurveSettings.DefaultIntervalMs, out int interval, out string e2)) { problems.Add(e2); }
			if (!TryParseInt(map, "minrpm", BaolongdaFanCurveSettings.DefaultMinRpm, out int minRpm, out string e3)) { problems.Add(e3); }
			if (!TryParseInt(map, "maxrpm", bandMax, out int maxRpm, out string e4)) { problems.Add(e4); }
			// ⭐ 双风扇联动：⭐ 旧存档**没有这个键** ⇒ ⭐ 默认 **1（开）**，⛔ 不当作坏档 ✅（向后兼容）
			if (!TryParseInt(map, "syncenabled", 1, out int syncEnabled, out string e0)) { problems.Add(e0); }
			if (!TryParseCurve(map, "ch0temp", out int[] ch0Temp, out string e5)) { problems.Add(e5); }
			if (!TryParseCurve(map, "ch0rpm", out int[] ch0Rpm, out string e6)) { problems.Add(e6); }
			if (!TryParseCurve(map, "ch1temp", out int[] ch1Temp, out string e7)) { problems.Add(e7); }
			if (!TryParseCurve(map, "ch1rpm", out int[] ch1Rpm, out string e8)) { problems.Add(e8); }
			if (problems.Count > 0)
			{
				return Unreadable(version, string.Join("；", problems), bandMax);
			}

			return new BaolongdaFanCurveSettings
			{
				// ⚠️ 默认 false：⭐ 文件里没写 `enabled=1` 就是**不启用** ✅（⭐ 安全默认 ✅）
				Enabled = map.TryGetValue("enabled", out string e) && e == "1",
				FormatVersion = BaolongdaFanCurveSettings.CurrentFormatVersion,
				SyncEnabled = syncEnabled != 0,
				HysteresisC = hysteresis,
				IntervalMs = interval,
				MinRpm = minRpm,
				MaxRpm = maxRpm,
				Channel0Temperatures = ch0Temp,
				Channel0Rpms = ch0Rpm,
				Channel1Temperatures = ch1Temp,
				Channel1Rpms = ch1Rpm
			};
		}
		catch (Exception ex)
		{
			Logger.WriteLine("宝龙达风扇曲线：⭐ 读取失败（按默认值处理）：" + ex.Message);
			return BaolongdaFanCurveSettings.CreateDefault(bandMax);
		}
	}

	/// <summary>⭐ 写一次设置（⭐ 失败只记日志 ✅）。⭐ 总是写成**当前版本** ✅。</summary>
	internal static bool Save(BaolongdaFanCurveSettings settings)
	{
		if (settings == null)
		{
			return false;
		}
		try
		{
			string path = GetPath();
			string directory = Path.GetDirectoryName(path);
			if (!string.IsNullOrEmpty(directory))
			{
				Directory.CreateDirectory(directory);
			}
			StringBuilder sb = new StringBuilder();
			sb.AppendLine("# 宝龙达软件风扇曲线（⭐ 由 L-Mechrevo 自动生成，⭐ 手改请谨慎 ✅）");
			sb.AppendLine("# ⚠️ 数值是**我们软件记的**（⭐ 机器读不回来）—— 见 docs\\notes 里第 4 步说明 ✅");
			sb.AppendLine("# ⭐ 单位是 RPM（⛔ 不是占空比）✅；⭐ 通道 0/1 对应协议 FanType 0/1（⭐ 哪个是 CPU/GPU 待确认 ⚠️）✅");
			sb.AppendLine("# syncenabled=1 ⇒ 双风扇 0.85 联动（共用热管）；旧版本存档没有该键 ⇒ 按 1 处理 ✅");
			sb.Append("format=").Append(BaolongdaFanCurveSettings.CurrentFormatVersion).AppendLine();
			sb.Append("enabled=").Append(settings.Enabled ? "1" : "0").AppendLine();
			sb.Append("syncenabled=").Append(settings.SyncEnabled ? "1" : "0").AppendLine();
			sb.Append("hysteresis=").Append(settings.HysteresisC).AppendLine();
			sb.Append("intervalms=").Append(settings.IntervalMs).AppendLine();
			sb.Append("minrpm=").Append(settings.MinRpm).AppendLine();
			sb.Append("maxrpm=").Append(settings.MaxRpm).AppendLine();
			sb.Append("ch0temp=").AppendLine(Join(settings.Channel0Temperatures));
			sb.Append("ch0rpm=").AppendLine(Join(settings.Channel0Rpms));
			sb.Append("ch1temp=").AppendLine(Join(settings.Channel1Temperatures));
			sb.Append("ch1rpm=").AppendLine(Join(settings.Channel1Rpms));
			File.WriteAllText(path, sb.ToString(), new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
			Logger.WriteLine($"宝龙达风扇曲线：⭐ 已保存（⭐ format={BaolongdaFanCurveSettings.CurrentFormatVersion} · ⭐ enabled={settings.Enabled}"
				+ $" · ⭐ 双风扇联动={settings.SyncEnabled} · ⭐ ch0={Join(settings.Channel0Rpms)} · ⭐ ch1={Join(settings.Channel1Rpms)}）");
			return true;
		}
		catch (Exception ex)
		{
			Logger.WriteLine("宝龙达风扇曲线：⭐ 保存失败：" + ex.Message);
			return false;
		}
	}

	/// <summary>⭐ 删掉设置（⭐ 恢复"从未配置"状态 ✅）。</summary>
	internal static void Clear()
	{
		try
		{
			string path = GetPath();
			if (File.Exists(path))
			{
				File.Delete(path);
				Logger.WriteLine("宝龙达风扇曲线：⭐ 已清除本地配置 ✅");
			}
		}
		catch (Exception ex)
		{
			Logger.WriteLine("宝龙达风扇曲线：⭐ 清除失败：" + ex.Message);
		}
	}

	/// <summary>⭐ 设置文件是否存在（⭐ 诊断用 ✅）。</summary>
	internal static bool Exists()
	{
		try
		{
			return File.Exists(GetPath());
		}
		catch
		{
			return false;
		}
	}

	/// <summary>
	/// ⭐⭐ 读不懂的存档 ⇒ ⭐ **不启用 ＋ ⭐ 如实说** ✅（⭐ ⛔ 不猜、⭐ ⛔ 不迁移 ⚠️）。
	/// <para>⭐ 日志用 ⭐ <see cref="Logger.WriteError" />（⭐ 默认「仅错误」级别也可见 ✅）
	/// ＋ ⭐ <see cref="Logger.TryMarkChanged" /> 去重（⭐ 读档可能被调多次 ⇒ ⭐ 状态没变就不重复打 ✅）。</para>
	/// </summary>
	private static BaolongdaFanCurveSettings Unreadable(int detectedVersion, string reason, int fullRpm)
	{
		try
		{
			string message = "宝龙达风扇曲线：⚠️ 本地存档【无法解释】⇒ ⭐ 按「不启用」处理"
				+ "（⛔ 不猜、⛔ 不静默迁移）：" + reason
				+ "；⭐ 曲线停在关闭状态，⛔ 不会下发任何转速 ✅（⭐ 要启用请重新设一次曲线 ✅）";
			if (Logger.TryMarkChanged("baolongda-fancurve-archive-unreadable", message))
			{
				Logger.WriteError(message);
			}
		}
		catch
		{
		}
		// ⭐ 读不懂时给"⭐ 按代际校准过"的默认曲线（仍 Enabled=false ⇒ ⛔ 不会被静默应用 ✅）
		BaolongdaFanCurveSettings fallback = BaolongdaFanCurveSettings.CreateDefault(fullRpm);
		return new BaolongdaFanCurveSettings
		{
			Enabled = false,
			FormatVersion = detectedVersion,
			SyncEnabled = fallback.SyncEnabled,
			HysteresisC = fallback.HysteresisC,
			IntervalMs = fallback.IntervalMs,
			MinRpm = fallback.MinRpm,
			MaxRpm = fallback.MaxRpm,
			Channel0Temperatures = fallback.Channel0Temperatures,
			Channel0Rpms = fallback.Channel0Rpms,
			Channel1Temperatures = fallback.Channel1Temperatures,
			Channel1Rpms = fallback.Channel1Rpms,
			IsUnreadableArchive = true,
			StatusNote = reason
		};
	}

	private static Dictionary<string, string> ReadMap(string path)
	{
		Dictionary<string, string> map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
		foreach (string line in File.ReadAllLines(path))
		{
			if (line.Length == 0 || line[0] == '#')
			{
				continue;
			}
			int eq = line.IndexOf('=');
			if (eq > 0)
			{
				map[line.Substring(0, eq).Trim()] = line.Substring(eq + 1).Trim();
			}
		}
		return map;
	}

	/// <summary>
	/// ⭐ 读一个**可选**整数参数 ✅：⭐ 缺省 ⇒ ⭐ 用默认 ✅ · ⭐ **出现但读不懂 ⇒ ⭐ 报错**（⭐ 由调用方升级为"⭐ 整份读不懂"✅）。
	/// <para>⚠️ 为什么"⭐ 出现但坏掉"⭐ 不能静默兜底 ⚠️：⭐ 例如 `maxrpm` 写坏了，
	/// ⭐ 静默用默认就会**悄悄改掉夹紧范围** ⚠️ ⇒ ⭐ 那是"⭐ 猜" ✅。</para>
	/// </summary>
	private static bool TryParseInt(Dictionary<string, string> map, string key, int fallback, out int value, out string error)
	{
		value = fallback;
		error = null;
		if (!map.TryGetValue(key, out string raw) || string.IsNullOrWhiteSpace(raw))
		{
			return true;
		}
		if (!int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out value))
		{
			value = fallback;
			error = $"{key}=「{raw}」不是整数";
			return false;
		}
		return true;
	}

	/// <summary>
	/// ⭐ 解析一条 **16 点曲线** ✅：⭐ 缺省 / ⭐ 点数不对 / ⭐ 有非整数 ⇒ ⭐ 返回 false ＋ ⭐ 具体原因 ✅。
	/// <para>⚠️ <b>必须整条一起校验</b> ⚠️：⭐ 温度数组与转速数组描述的是**同一条曲线** ✅
	/// ⇒ ⭐ ⛔ 绝不允许"⭐ 一个用默认、⭐ 一个用用户的"⭐（⭐ 那会拼出怪曲线 ✅）。</para>
	/// </summary>
	private static bool TryParseCurve(Dictionary<string, string> map, string key, out int[] values, out string error)
	{
		values = null;
		error = null;
		if (!map.TryGetValue(key, out string raw) || string.IsNullOrWhiteSpace(raw))
		{
			error = $"缺少 {key}（或为空）";
			return false;
		}
		string[] parts = raw.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
		if (parts.Length != BaolongdaFanCurveSettings.PointCount)
		{
			error = $"{key} 点数={parts.Length}（应为 {BaolongdaFanCurveSettings.PointCount}）";
			return false;
		}
		int[] array = new int[parts.Length];
		for (int i = 0; i < parts.Length; i++)
		{
			if (!int.TryParse(parts[i], NumberStyles.Integer, CultureInfo.InvariantCulture, out array[i]))
			{
				error = $"{key} 第 {i} 项「{parts[i]}」不是整数";
				return false;
			}
		}
		values = array;
		return true;
	}

	private static string Join(int[] values)
	{
		if (values == null || values.Length == 0)
		{
			return string.Empty;
		}
		string[] parts = new string[values.Length];
		for (int i = 0; i < values.Length; i++)
		{
			parts[i] = values[i].ToString(CultureInfo.InvariantCulture);
		}
		return string.Join(",", parts);
	}

	private static string GetPath()
	{
		return Path.Combine(GetConfigDirectory(), FileName);
	}

	/// <summary>⭐ 配置目录（⭐ 与 `LightingSettingsStore` 完全同源 ✅ —— ⭐ 同一个覆盖变量 ✅）。</summary>
	private static string GetConfigDirectory()
	{
		try
		{
			string environmentVariable = Environment.GetEnvironmentVariable(ConfigDirectoryOverrideVariable);
			if (!string.IsNullOrWhiteSpace(environmentVariable))
			{
				return environmentVariable;
			}
		}
		catch
		{
		}
		return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "MechrevoLite");
	}
}
