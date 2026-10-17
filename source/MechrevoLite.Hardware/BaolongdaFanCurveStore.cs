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
// 许可/署名：与本树同一实现，见 THIRD-PARTY-NOTICES.md 与 LICENSES\。

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
/// <para>⚠️ ⭐ <b>默认曲线是【占位值】</b> ⚠️（⭐ <see cref="DefaultRpms" /> ✅）——
/// ⭐ 因为宝龙达**没有可靠的通道 max RPM** ⚠️ ⇒ ⭐ 给不出"⭐ 正确"⭐ 的默认 ✅。
/// ⭐ 但 <see cref="Enabled" /> **默认 false** ✅ ⇒ ⭐ 这条占位曲线**不会被静默应用** ✅。</para>
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
	/// ⚠️ ⭐ 默认转速下界（RPM ✅）—— ⭐ **占位值** ⚠️（⭐ 等真机确认通道 max 后再定 ✅）。
	/// <para>⭐ 刻意**不从 0 起** ✅：⭐ 风扇长期停转对笔记本散热不利 ⚠️（⭐ 与既有"⭐ 最低 20%"⭐ 同一考虑 ✅）。</para>
	/// </summary>
	internal const int DefaultMinRpm = 1200;

	/// <summary>
	/// ⚠️ ⭐ 默认转速上界（RPM ✅）—— ⭐ **占位值** ⚠️，⭐ 刻意**保守**（⭐ 远低于协议上限 6000 ✅）。
	/// <para>⭐ 宁可低：⭐ 上限猜大了会让风扇狂转 ⚠️（⭐ 而猜小只是偏保守 ✅）。</para>
	/// </summary>
	internal const int DefaultMaxRpm = 4200;

	/// <summary>⭐ 软件闭环是否启用 ⚠️（⭐ 默认 **false** —— ⭐ 通道归属未确认前不许启用 ✅）。</summary>
	internal bool Enabled { get; init; }

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
	/// <para>⭐ 为 true 时 ⭐ <see cref="Enabled" /> **必为 false** ✅、⭐ 且曲线是**默认占位值** ✅
	/// ⇒ ⭐ ⛔ **不会被应用** ✅。⭐ 供 UI 如实告知用户"⭐ 存档没迁移、⭐ 请重新设一次" ✅。</para>
	/// <para>⭐ 具体原因见 <see cref="StatusNote" /> ✅ —— ⭐ ⛔ 不是"⭐ 没启用"⭐ 这么含糊 ⚠️。</para>
	/// </summary>
	internal bool IsUnreadableArchive { get; init; }

	/// <summary>⭐ 读档时的**如实说明**（⭐ 空 = ⭐ 没有特殊情况 ✅）—— ⭐ 供 UI/日志展示 ✅。</summary>
	internal string StatusNote { get; init; } = string.Empty;

	/// <summary>⭐ 默认温度点（⭐ 40~95℃ 均匀铺开 ✅ —— ⭐ 与既有曲线的常见形状一致 ✅）。</summary>
	internal static int[] DefaultTemperatures()
	{
		int[] array = new int[PointCount];
		for (int i = 0; i < PointCount; i++)
		{
			// ⭐ 40℃ 起、⭐ 每点 +3~4℃ ⇒ ⭐ 最后一点 ≈ 95℃ ✅
			array[i] = 40 + (int)Math.Round(i * (95.0 - 40.0) / (PointCount - 1));
		}
		return array;
	}

	/// <summary>
	/// ⚠️ ⭐ 默认转速曲线（RPM ✅）—— ⭐ ⭐ **这是占位值，⭐ 不是"⭐ 正确值"** ⚠️
	/// <para>⭐ 形状：⭐ <see cref="DefaultMinRpm" /> → ⭐ <see cref="DefaultMaxRpm" /> 线性铺开 ✅
	/// （⭐ 40℃ 给 1200 RPM、⭐ 95℃ 给 4200 RPM ✅）。</para>
	/// <para>⚠️ 之所以只能给占位：⭐ 宝龙达**没有可靠的通道 max RPM** ⚠️
	/// ⇒ ⭐ 编不出"⭐ 按百分比换算"⭐ 的等价物 ✅。⭐ 等真机测出各通道转速范围后再收敛 ✅。</para>
	/// <para>⭐ 安全性：⭐ <see cref="Enabled" /> 默认 false ✅ ⇒ ⭐ 本曲线**不会**被静默应用 ✅。</para>
	/// </summary>
	internal static int[] DefaultRpms()
	{
		int[] array = new int[PointCount];
		for (int i = 0; i < PointCount; i++)
		{
			array[i] = DefaultMinRpm + (int)Math.Round(i * (DefaultMaxRpm - DefaultMinRpm) / (double)(PointCount - 1));
		}
		return array;
	}

	/// <summary>⭐ 深拷贝（⭐ 数组类型是可变引用 ⇒ ⭐ 传出去前必须拷贝，⛔ 免得外部改到内部状态 ✅）。</summary>
	internal BaolongdaFanCurveSettings Clone(bool? enabledOverride = null)
	{
		return new BaolongdaFanCurveSettings
		{
			Enabled = enabledOverride ?? Enabled,
			FormatVersion = FormatVersion,
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

	/// <summary>⭐ 读一次设置（⭐ 读不到/坏掉 ⇒ ⭐ 返回**默认**（⭐ `Enabled=false` ✅），⭐ 不外抛 ✅）。</summary>
	internal static BaolongdaFanCurveSettings Load()
	{
		try
		{
			string path = GetPath();
			if (!File.Exists(path))
			{
				return new BaolongdaFanCurveSettings();
			}
			Dictionary<string, string> map = ReadMap(path);

			// ⭐⭐ 先判版本：⭐ 认不出来就**不解释**（⭐ 这一条是第 1 步的核心要求 ✅）
			bool hasFormat = map.TryGetValue("format", out string formatRaw);
			int version = BaolongdaFanCurveSettings.LegacyDutyFormatVersion;
			if (hasFormat
				&& !int.TryParse(formatRaw, NumberStyles.Integer, CultureInfo.InvariantCulture, out version))
			{
				return Unreadable(BaolongdaFanCurveSettings.LegacyDutyFormatVersion, $"format=「{formatRaw}」不是整数");
			}
			if (!hasFormat)
			{
				return Unreadable(BaolongdaFanCurveSettings.LegacyDutyFormatVersion,
					"文件里没有 format 版本号 ⇒ 判定为旧的占空比格式（占空比无法换算成 RPM）");
			}
			if (version != BaolongdaFanCurveSettings.CurrentFormatVersion)
			{
				return Unreadable(version, $"format={version}（本程序只认 {BaolongdaFanCurveSettings.CurrentFormatVersion}）");
			}

			// ⭐⭐ v2：⭐ **严格**解析 ✅ —— ⭐ 任何一个出现的值读不懂 ⇒ ⭐ 整份算读不懂 ⚠️（⛔ 不猜 ✅）
			List<string> problems = new List<string>();
			if (!TryParseInt(map, "hysteresis", FanCurveEngine.DefaultHysteresisC, out int hysteresis, out string e1)) { problems.Add(e1); }
			if (!TryParseInt(map, "intervalms", BaolongdaFanCurveSettings.DefaultIntervalMs, out int interval, out string e2)) { problems.Add(e2); }
			if (!TryParseInt(map, "minrpm", BaolongdaFanCurveSettings.DefaultMinRpm, out int minRpm, out string e3)) { problems.Add(e3); }
			if (!TryParseInt(map, "maxrpm", BaolongdaFanCurveSettings.DefaultMaxRpm, out int maxRpm, out string e4)) { problems.Add(e4); }
			if (!TryParseCurve(map, "ch0temp", out int[] ch0Temp, out string e5)) { problems.Add(e5); }
			if (!TryParseCurve(map, "ch0rpm", out int[] ch0Rpm, out string e6)) { problems.Add(e6); }
			if (!TryParseCurve(map, "ch1temp", out int[] ch1Temp, out string e7)) { problems.Add(e7); }
			if (!TryParseCurve(map, "ch1rpm", out int[] ch1Rpm, out string e8)) { problems.Add(e8); }
			if (problems.Count > 0)
			{
				return Unreadable(version, string.Join("；", problems));
			}

			return new BaolongdaFanCurveSettings
			{
				// ⚠️ 默认 false：⭐ 文件里没写 `enabled=1` 就是**不启用** ✅（⭐ 安全默认 ✅）
				Enabled = map.TryGetValue("enabled", out string e) && e == "1",
				FormatVersion = BaolongdaFanCurveSettings.CurrentFormatVersion,
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
			return new BaolongdaFanCurveSettings();
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
			sb.Append("format=").Append(BaolongdaFanCurveSettings.CurrentFormatVersion).AppendLine();
			sb.Append("enabled=").Append(settings.Enabled ? "1" : "0").AppendLine();
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
				+ $" · ⭐ ch0={Join(settings.Channel0Rpms)} · ⭐ ch1={Join(settings.Channel1Rpms)}）");
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
	private static BaolongdaFanCurveSettings Unreadable(int detectedVersion, string reason)
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
		return new BaolongdaFanCurveSettings
		{
			Enabled = false,
			FormatVersion = detectedVersion,
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
