// 2026-10-16（0.291.9.4 · 宝龙达风扇曲线 · 第 2 步自检）：
// ⭐⭐ 风扇曲线【决策器】的单元测试 ✅ —— ⭐ 纯逻辑，⛔ 不碰硬件、⭐ 不下发任何转速 ✅
//
// ⭐ 为什么放在这里（⭐ 本仓的约定 ✅）：⭐ `tools\dev\<name>-selftest\` 是既有惯例 ✅
//    （⭐ 见 `kblight-selftest` / ⭐ `igpu-selftest` ✅）：
//    ⭐ 把**生产源文件 link 进来**编成一个 exe ⇒ ⭐ 用断言跑自检 ✅
//    ⇒ ⭐ ⛔ 不引入测试框架（⭐ 本仓没有测试工程 ✅）、⭐ ⛔ 不影响主工程构建与发布产物 ✅
//
// ⭐ 跑法：⭐ `dotnet run --project tools\dev\fancurve-selftest` ✅（⭐ 退出码 0 = 全过 ✅）
//
// ⚠️ 这里**只测算法** ⚠️：⭐ 曲线插值 / ⭐ 钳位 / ⭐ 换算 / ⭐ 回差防抖 ✅
//    ⛔ 不测"⭐ 功能号 20/21 的写入是否被固件接受"⚠️（⭐ 那必须真机验 ✅ —— 方案文档 §6.2 ✅）
//
// 许可/署名：与本树同一实现，见 THIRD-PARTY-NOTICES.md 与 LICENSES\。

using System;
using MechrevoLite.Hardware;

internal static class Program
{
	private static int _pass;
	private static int _fail;

	private static int Main()
	{
		Console.WriteLine("=== FanCurveEngine / FanCurveDecider 自检（第 2 步：决策器）===");

		Console.WriteLine();
		Console.WriteLine("-- 1. 曲线插值（线性）--");
		TestInterpolation();

		Console.WriteLine();
		Console.WriteLine("-- 2. 钳位（曲线定义域 → 配置区间 → 协议上限）--");
		TestClamping();

		Console.WriteLine();
		Console.WriteLine("-- 3. 边界与坏输入 --");
		TestEdges();

		Console.WriteLine();
		Console.WriteLine("-- 4. BuildPoints（并列数组 → 点集）--");
		TestBuildPoints();

		Console.WriteLine();
		Console.WriteLine("-- 5. RPM → 协议档位（RPM/100）--");
		TestUnits();

		Console.WriteLine();
		Console.WriteLine("-- 6. 回差防抖（升温即写、降温需过回差）--");
		TestHysteresis();

		Console.WriteLine();
		Console.WriteLine("-- 7. 空曲线必须【不下发】--");
		TestEmptyCurve();

		Console.WriteLine();
		Console.WriteLine("-- 8. 存档格式（⭐ 第 1 步验收点：旧格式 ⇒ 不启用 ＋ 如实告知）--");
		TestStore();

		Console.WriteLine();
		Console.WriteLine(_fail == 0
			? $"✅ 全部通过：PASS={_pass} FAIL=0"
			: $"❌ 有失败：PASS={_pass} FAIL={_fail}");
		return _fail == 0 ? 0 : 1;
	}

	// ══════════════════════════════════════════════════════════════════
	// ⭐ 测试用的标准曲线：(40℃,1000) (60℃,2000) (80℃,3000) ✅
	// ══════════════════════════════════════════════════════════════════
	private static FanCurvePoint[] Standard()
	{
		return FanCurveEngine.BuildPoints(
			new[] { 40, 60, 80 },
			new[] { 1000, 2000, 3000 });
	}

	private static void TestInterpolation()
	{
		FanCurvePoint[] c = Standard();
		// ⭐ 中点：⭐ 50℃ 正好在 (40,1000) 与 (60,2000) 中间 ⇒ ⭐ 1500 ✅
		Eq("50℃ ⇒ 1500（两等距点中点）", 1500, FanCurveEngine.ResolveRpm(c, 50, 0, 6000));
		// ⭐ 1/4 处：⭐ 45℃ ⇒ 1000 + 5×1000/20 = 1250 ✅
		Eq("45℃ ⇒ 1250（1/4 处）", 1250, FanCurveEngine.ResolveRpm(c, 45, 0, 6000));
		// ⭐ 70℃ 在 (60,2000) 与 (80,3000) 中间 ⇒ ⭐ 2500 ✅
		Eq("70℃ ⇒ 2500（另一段中点）", 2500, FanCurveEngine.ResolveRpm(c, 70, 0, 6000));
		// ⭐ 恰好落在点上 ⇒ ⭐ 就是那个点的值（⭐ 不因为插值而漂移 ✅）
		Eq("40℃ ⇒ 1000（正好第一点）", 1000, FanCurveEngine.ResolveRpm(c, 40, 0, 6000));
		Eq("60℃ ⇒ 2000（正好第二点）", 2000, FanCurveEngine.ResolveRpm(c, 60, 0, 6000));
		Eq("80℃ ⇒ 3000（正好第三点）", 3000, FanCurveEngine.ResolveRpm(c, 80, 0, 6000));
		// ⭐ 单点曲线 ⇒ ⭐ 任何温度都是它 ✅
		FanCurvePoint[] one = FanCurveEngine.BuildPoints(new[] { 50 }, new[] { 2500 });
		Eq("单点曲线 @20℃ ⇒ 2500", 2500, FanCurveEngine.ResolveRpm(one, 20, 0, 6000));
		Eq("单点曲线 @99℃ ⇒ 2500", 2500, FanCurveEngine.ResolveRpm(one, 99, 0, 6000));
	}

	private static void TestClamping()
	{
		FanCurvePoint[] c = Standard();
		// ⭐ ⛔ 不外推：⭐ 低于第一点 ⇒ ⭐ 取第一点；⭐ 高于最后一点 ⇒ ⭐ 取最后一点 ✅
		Eq("30℃（低于首点）⇒ 1000（不外推）", 1000, FanCurveEngine.ResolveRpm(c, 30, 0, 6000));
		Eq("95℃（高于末点）⇒ 3000（不外推）", 3000, FanCurveEngine.ResolveRpm(c, 95, 0, 6000));
		Eq("0℃ ⇒ 1000（不外推）", 1000, FanCurveEngine.ResolveRpm(c, 0, 0, 6000));
		Eq("-40℃ ⇒ 1000（负温度也不外推）", 1000, FanCurveEngine.ResolveRpm(c, -40, 0, 6000));

		// ⭐ 配置区间（band）夹紧 ✅
		Eq("band 下界 1200 把 1000 抬到 1200", 1200, FanCurveEngine.ResolveRpm(c, 40, 1200, 4200));
		Eq("band 上界 4200 把 5000 压到 4200", 4200,
			FanCurveEngine.ResolveRpm(FanCurveEngine.BuildPoints(new[] { 40, 80 }, new[] { 1000, 5000 }), 80, 1200, 4200));

		// ⭐ band 反着写（min > max）⇒ ⭐ 内部纠正，⭐ 不抛异常 ✅
		Eq("band 反写 [4200,1200] 仍夹到 1200", 1200, FanCurveEngine.ResolveRpm(c, 40, 4200, 1200));

		// ⭐ 协议上限 6000：⭐ 就算 band 允许 9999，⭐ 也不能超过 6000 ✅
		Eq("协议上限把 9000 压到 6000", 6000,
			FanCurveEngine.ResolveRpm(FanCurveEngine.BuildPoints(new[] { 40, 80 }, new[] { 1000, 9000 }), 80, 0, 9999));
		// ⭐ 插值结果越界也要被夹（⭐ 曲线中间段高于 band 上界 ✅）
		Eq("插值结果也受 band 上界约束", 4200,
			FanCurveEngine.ResolveRpm(FanCurveEngine.BuildPoints(new[] { 40, 60 }, new[] { 1000, 8000 }), 60, 0, 4200));
	}

	private static void TestEdges()
	{
		// ⭐ 手改出来的"⭐ 两点同温"⚠️ ⇒ ⭐ 绝不除零、⭐ 绝不抛 ✅
		FanCurvePoint[] dup = FanCurveEngine.BuildPoints(
			new[] { 40, 60, 60, 80 },
			new[] { 1000, 2000, 3000, 4000 });
		Eq("重复温度：点数保留为 4", 4, dup.Length);
		int at60 = FanCurveEngine.ResolveRpm(dup, 60, 0, 6000);
		True($"重复温度 @60℃ 不抛异常且在 [2000,3000]（实际 {at60}）", at60 >= 2000 && at60 <= 3000);
		int at70 = FanCurveEngine.ResolveRpm(dup, 70, 0, 6000);
		Eq("重复温度之后仍能正常插值 @70℃ ⇒ 3500", 3500, at70);

		// ⭐ 温度乱序输入 ⇒ ⭐ BuildPoints 排序后结果正确 ✅
		FanCurvePoint[] unsorted = FanCurveEngine.BuildPoints(
			new[] { 80, 40, 60 },
			new[] { 3000, 1000, 2000 });
		Eq("乱序输入 @50℃ ⇒ 1500（已排序）", 1500, FanCurveEngine.ResolveRpm(unsorted, 50, 0, 6000));

		// ⭐ 极值不炸 ✅
		Eq("int.MaxValue 温度 ⇒ 末点值", 3000, FanCurveEngine.ResolveRpm(Standard(), int.MaxValue, 0, 6000));
		Eq("int.MinValue 温度 ⇒ 首点值", 1000, FanCurveEngine.ResolveRpm(Standard(), int.MinValue, 0, 6000));
	}

	private static void TestBuildPoints()
	{
		Eq("长度不一致 ⇒ 空", 0, FanCurveEngine.BuildPoints(new[] { 40, 60 }, new[] { 1000 }).Length);
		Eq("null ⇒ 空", 0, FanCurveEngine.BuildPoints(null, null).Length);
		Eq("温度 null ⇒ 空", 0, FanCurveEngine.BuildPoints(null, new[] { 1000 }).Length);
		Eq("空数组 ⇒ 空", 0, FanCurveEngine.BuildPoints(new int[0], new int[0]).Length);

		// ⭐ 丢弃不可解释的点（⭐ 温度 ≤ 0 / ⭐ 转速 < 0 ✅）—— ⭐ ⛔ 不猜、⭐ 不补默认 ✅
		Eq("温度 ≤ 0 的点被丢弃", 2, FanCurveEngine.BuildPoints(new[] { 0, -5, 40, 60 }, new[] { 500, 500, 1000, 2000 }).Length);
		Eq("转速 < 0 的点被丢弃", 2, FanCurveEngine.BuildPoints(new[] { 40, 60, 80 }, new[] { -1, 2000, 3000 }).Length);
		Eq("全部非法 ⇒ 空", 0, FanCurveEngine.BuildPoints(new[] { 0, -1 }, new[] { 100, 200 }).Length);

		// ⭐ 排序是升序 ✅
		FanCurvePoint[] sorted = FanCurveEngine.BuildPoints(new[] { 80, 40, 60 }, new[] { 3000, 1000, 2000 });
		True("结果按温度升序", sorted[0].TempC == 40 && sorted[1].TempC == 60 && sorted[2].TempC == 80,
			$"实际 {sorted[0].TempC},{sorted[1].TempC},{sorted[2].TempC}");

		// ⭐ 转速 0 是**合法**的（⭐ 只丢负数 ✅）
		Eq("转速 0 保留（只丢负数）", 2, FanCurveEngine.BuildPoints(new[] { 40, 60 }, new[] { 0, 2000 }).Length);
	}

	private static void TestUnits()
	{
		Eq("2500 RPM ⇒ 档位 25", 25, FanCurveEngine.ToUnits100Rpm(2500));
		Eq("999 RPM ⇒ 档位 9（向下取整）", 9, FanCurveEngine.ToUnits100Rpm(999));
		Eq("0 RPM ⇒ 档位 0", 0, FanCurveEngine.ToUnits100Rpm(0));
		Eq("-5 RPM ⇒ 档位 0（负数不当成越界）", 0, FanCurveEngine.ToUnits100Rpm(-5));
		Eq("6000 RPM ⇒ 档位 60（正好上限）", 60, FanCurveEngine.ToUnits100Rpm(6000));
		Eq("9999 RPM ⇒ 档位 60（夹到上限）", 60, FanCurveEngine.ToUnits100Rpm(9999));
		True("协议上限常量自洽（60×100=6000）", FanCurveEngine.ProtocolMaxRpm == 6000);
	}

	private static void TestHysteresis()
	{
		FanCurvePoint[] c = Standard();
		FanCurveDecider d = new FanCurveDecider();

		// ⭐ 第一次调用 ⇒ ⭐ 一定写 ✅
		int? first = d.Decide(c, 70, 0, 6000, 3);
		Eq("首次调用 ⇒ 写 2500", 2500, first.Value);
		True("首次之后 HasLast=true", d.HasLast);
		Eq("LastTempC 记录为 70", 70, d.LastTempC);
		Eq("LastRpm 记录为 2500", 2500, d.LastRpm);

		// ⭐ 目标不变 ⇒ ⭐ 不写 ✅
		Null("同一温度再调 ⇒ 不写", d.Decide(c, 70, 0, 6000, 3));

		// ⭐ 降温但**不足回差** ⇒ ⭐ 不写 ✅
		//    70℃ → 68℃：⭐ 目标 2400 < 2500（降），⭐ 温差 2 < 3 ⇒ ⭐ 不写
		Null("降温 2℃（< 回差 3）⇒ 不写", d.Decide(c, 68, 0, 6000, 3));
		// ⭐ 而且**被拒时不得改状态**（⭐ 否则回差会被自己吃掉 ✅）
		Eq("被拒后 LastTempC 仍为 70", 70, d.LastTempC);
		Eq("被拒后 LastRpm 仍为 2500", 2500, d.LastRpm);

		// ⭐ 再降一点，⭐ 累计温差 ≥ 回差 ⇒ ⭐ 写 ✅
		//    70℃ → 66℃：⭐ 目标 2300，⭐ 温差 4 ≥ 3 ⇒ ⭐ 写
		int? down = d.Decide(c, 66, 0, 6000, 3);
		Eq("降温 4℃（≥ 回差）⇒ 写 2300", 2300, down.Value);
		Eq("写后 LastTempC 更新为 66", 66, d.LastTempC);

		// ⭐ 升温**不需要**过回差 ⇒ ⭐ 立刻写 ✅
		//    66℃ → 67℃：⭐ 目标 2350 > 2300 ⇒ ⭐ 写
		int? up = d.Decide(c, 67, 0, 6000, 3);
		Eq("升温 1℃ ⇒ 立刻写 2350", 2350, up.Value);

		// ⭐ 回差 = 0 ⇒ ⭐ 关闭防抖：⭐ 只要转速变了就写 ✅
		FanCurveDecider d0 = new FanCurveDecider();
		d0.Decide(c, 70, 0, 6000, 0);
		int? sensitive = d0.Decide(c, 69, 0, 6000, 0);
		Eq("回差=0 ⇒ 降温 1℃ 也写 2450", 2450, sensitive.Value);

		// ⭐ 负回差 ⇒ ⭐ 同样视为关闭防抖（⭐ 不出现"永远不写"的死区 ✅）
		FanCurveDecider dn = new FanCurveDecider();
		dn.Decide(c, 70, 0, 6000, -5);
		int? neg = dn.Decide(c, 69, 0, 6000, -5);
		Eq("回差<0 ⇒ 同样立即写 2450", 2450, neg.Value);

		// ⭐ Reset ⇒ ⭐ 下次一定写 ✅（⭐ 睡眠恢复/曲线被改过时用 ✅）
		FanCurveDecider dr = new FanCurveDecider();
		dr.Decide(c, 70, 0, 6000, 3);
		dr.Reset();
		True("Reset 后 HasLast=false", !dr.HasLast);
		int? afterReset = dr.Decide(c, 70, 0, 6000, 3);
		Eq("Reset 后再调 ⇒ 又写 2500", 2500, afterReset.Value);

		// ⭐ 夹紧也要在决策器里生效（⭐ band 上界 ✅）
		FanCurveDecider db = new FanCurveDecider();
		int? banded = db.Decide(c, 80, 0, 2000, 3);
		Eq("决策器也受 band 上界约束（3000 → 2000）", 2000, banded.Value);
	}

	private static void TestEmptyCurve()
	{
		// ⭐⭐ 空曲线 ⇒ ⭐ **必须返回 null**（⭐ 即"⭐ 不下发"⚠️）——
		//    ⛔ 绝不能把"⭐ 没有曲线"⭐ 当成"⭐ 用最低转速"⭐ ❌
		FanCurveDecider d = new FanCurveDecider();
		Null("空曲线 ⇒ 不写", d.Decide(Array.Empty<FanCurvePoint>(), 60, 1200, 4200, 3));
		Null("null 曲线 ⇒ 不写", d.Decide(null, 60, 1200, 4200, 3));
		True("空曲线后 HasLast 仍为 false（没落笔）", !d.HasLast);
		True("空曲线时 LastRpm 仍是 0", d.LastRpm == 0);

		// ⭐ 纯函数层：⭐ 空曲线返回 band 下界的夹紧值（⭐ 契约如此 ✅ —— ⭐ 但调用方**必须先判空** ✅）
		Eq("ResolveRpm 空曲线 ⇒ band 下界 1200", 1200, FanCurveEngine.ResolveRpm(Array.Empty<FanCurvePoint>(), 60, 1200, 4200));
		Eq("ResolveRpm null ⇒ band 下界 1200", 1200, FanCurveEngine.ResolveRpm(null, 60, 1200, 4200));
	}

	// ══════════════════════════════════════════════════════════════════
	// ⭐ 8. 存档格式（⭐ 第 1 步的验收点 ⚠️）
	//    ⭐ 要证的**不是**"⭐ 没启用"⚠️，⭐ 而是"⭐ 读不懂 ⇒ ⭐ 不启用 ＋ ⭐ 【如实告知】"✅
	//    （⭐ 方案文档 §4.2 / ⭐ 用户要求「⭐ 不要猜、⭐ 不要静默迁移」✅）
	// ══════════════════════════════════════════════════════════════════
	private static string _tempDir;

	private static void TestStore()
	{
		_tempDir = System.IO.Path.Combine(
			System.IO.Path.GetTempPath(),
			"lm-fancurve-selftest-" + Guid.NewGuid().ToString("N"));
		System.IO.Directory.CreateDirectory(_tempDir);
		Environment.SetEnvironmentVariable(BaolongdaFanCurveStore.ConfigDirectoryOverrideVariable, _tempDir);

		// ---- 用例 1：文件不存在 ⇒ ⭐ 干净默认，⭐ 且**不是**旧存档 ⚠️ ----
		DeleteConfig();
		Logger.Reset();
		BaolongdaFanCurveSettings fresh = BaolongdaFanCurveStore.Load();
		True("无存档 ⇒ Enabled=false", !fresh.Enabled);
		True("无存档 ⇒ 不标记为旧存档", !fresh.IsUnreadableArchive);
		Eq("无存档 ⇒ FormatVersion=2", BaolongdaFanCurveSettings.CurrentFormatVersion, fresh.FormatVersion);
		Eq("无存档 ⇒ 点数 16", BaolongdaFanCurveSettings.PointCount, fresh.Channel0Rpms.Length);
		Eq("默认 band 下界 = 1200", BaolongdaFanCurveSettings.DefaultMinRpm, fresh.MinRpm);
		// 2026-10-19（默认曲线校准）：band 上界 = 满转（无硬件上下文按 40 系 6000）
		Eq("默认 band 上界 = 满转 6000（40 系）", BaolongdaFanCurveSettings.FullRpm40Series, fresh.MaxRpm);
		Eq("默认温度首点 = 40℃", 40, fresh.Channel0Temperatures[0]);
		Eq("默认温度末点 = 100℃", 100, fresh.Channel0Temperatures[15]);
		Eq("默认 RPM 首点 = 1800（40 系 30%）", 1800, fresh.Channel0Rpms[0]);
		Eq("默认 RPM 末点 = 6000（40 系 100%）", 6000, fresh.Channel0Rpms[15]);
		True("默认双风扇联动 = 开", fresh.SyncEnabled);
		Eq("默认回差 = 3", FanCurveEngine.DefaultHysteresisC, fresh.HysteresisC);
		Eq("无存档 ⇒ 不该打错误日志", 0, Logger.Errors.Count);

		// ---- 用例 1b：30 系（满转 3500）⇒ 默认曲线按 3500 校准 ----
		BaolongdaFanCurveSettings fresh30 = BaolongdaFanCurveStore.Load(BaolongdaFanCurveSettings.FullRpm30Series);
		Eq("30 系 ⇒ band 上界 3500", BaolongdaFanCurveSettings.FullRpm30Series, fresh30.MaxRpm);
		Eq("30 系 ⇒ RPM 首点 = 1050（30%）", 1050, fresh30.Channel0Rpms[0]);
		Eq("30 系 ⇒ RPM 末点 = 3500（100%）", 3500, fresh30.Channel0Rpms[15]);

		// ---- 用例 2：**旧格式**（⭐ 占空比 ＋ ⭐ modeid ＋ ⭐ 没有 format ✅）⇒ ⭐ 不启用 ＋ ⭐ 如实告知 ----
		//    ⚠️ 注意 `enabled=1`：⭐ 旧存档是**开着**的 ⚠️ ⇒ ⭐ 更要证明它**不会**被启用 ✅
		WriteConfig("# legacy\nenabled=1\nmodeid=balanced\n"
			+ "cputemp=" + Csv16(40) + "\ncpuduty=" + Csv16(20) + "\n"
			+ "gputemp=" + Csv16(40) + "\ngpuduty=" + Csv16(20) + "\n");
		Logger.Reset();
		BaolongdaFanCurveSettings legacy = BaolongdaFanCurveStore.Load();
		True("旧格式（即使 enabled=1）⇒ ⭐ 仍不启用", !legacy.Enabled);
		True("旧格式 ⇒ 标记 IsUnreadableArchive", legacy.IsUnreadableArchive);
		True("旧格式 ⇒ StatusNote 非空（可给 UI 显示）", legacy.StatusNote.Length > 0);
		Eq("旧格式 ⇒ 不该静默迁移（曲线退回默认值）", 1800, legacy.Channel0Rpms[0]);
		Logged("旧格式 ⇒ 打了【默认级别可见】的日志", "无法解释");

		// ⭐ 再读一次 ⇒ ⭐ 去重生效（⭐ 不刷屏 ✅）
		BaolongdaFanCurveStore.Load();
		Eq("重复读旧存档 ⇒ 日志只打一次（去重）", 1, Logger.Errors.Count);

		// ---- 用例 3：未来版本号 / ⭐ 版本号不是整数 ⇒ ⭐ 同样"读不懂就停" ✅ ----
		WriteConfig("format=99\nenabled=1\n");
		Logger.Reset();
		BaolongdaFanCurveSettings future = BaolongdaFanCurveStore.Load();
		True("format=99 ⇒ 不启用", !future.Enabled);
		True("format=99 ⇒ 标记 IsUnreadableArchive", future.IsUnreadableArchive);
		Logged("format=99 ⇒ 有可见日志", "format=99");

		WriteConfig("format=abc\nenabled=1\n");
		Logger.Reset();
		BaolongdaFanCurveSettings broken = BaolongdaFanCurveStore.Load();
		True("format=abc ⇒ 不启用", !broken.Enabled);
		True("format=abc ⇒ 标记 IsUnreadableArchive", broken.IsUnreadableArchive);
		Logged("format=abc ⇒ 有可见日志", "不是整数");

		// ---- 用例 4：v2 往返（⭐ 保存 → ⭐ 读回 ✅）----
		BaolongdaFanCurveSettings saved = new BaolongdaFanCurveSettings
		{
			Enabled = true,
			SyncEnabled = false,
			HysteresisC = 5,
			IntervalMs = 2000,
			MinRpm = 900,
			MaxRpm = 5100,
			Channel0Temperatures = Ramp16(30),
			Channel0Rpms = Ramp16(1500),
			Channel1Temperatures = Ramp16(35),
			Channel1Rpms = Ramp16(1800)
		};
		True("Save 成功", BaolongdaFanCurveStore.Save(saved));
		True("存档里写了 format=2", ReadConfig().Contains("format=2"));
		True("存档里写了 syncenabled=0", ReadConfig().Contains("syncenabled=0"));
		Logger.Reset();
		BaolongdaFanCurveSettings back = BaolongdaFanCurveStore.Load();
		True("往返 ⇒ Enabled 保持 true", back.Enabled);
		True("往返 ⇒ 不是旧存档", !back.IsUnreadableArchive);
		True("往返 ⇒ 双风扇联动 = false", !back.SyncEnabled);
		Eq("往返 ⇒ HysteresisC", 5, back.HysteresisC);
		Eq("往返 ⇒ IntervalMs", 2000, back.IntervalMs);
		Eq("往返 ⇒ MinRpm", 900, back.MinRpm);
		Eq("往返 ⇒ MaxRpm", 5100, back.MaxRpm);
		Eq("往返 ⇒ ch0 温度首点", 30, back.Channel0Temperatures[0]);
		Eq("往返 ⇒ ch0 转速首点", 1500, back.Channel0Rpms[0]);
		Eq("往返 ⇒ ch1 温度首点", 35, back.Channel1Temperatures[0]);
		Eq("往返 ⇒ ch1 转速首点", 1800, back.Channel1Rpms[0]);
		Eq("往返 ⇒ ch0 温度末点", 30 + 15, back.Channel0Temperatures[15]);
		Eq("往返 ⇒ ch1 转速末点", 1800 + 15, back.Channel1Rpms[15]);
		Eq("往返 ⇒ 不该有错误日志", 0, Logger.Errors.Count);

		// ---- 用例 4b：WithEnabled —— 界面开关"只改 Enabled、⛔ 不动曲线" ✅（2026-10-17 接手修复）----
		BaolongdaFanCurveSettings off = saved.WithEnabled(false);
		True("WithEnabled(false) ⇒ Enabled=false", !off.Enabled);
		Eq("WithEnabled(false) ⇒ 曲线原样（ch0 温度首点）", 30, off.Channel0Temperatures[0]);
		Eq("WithEnabled(false) ⇒ 转速上限原样", 5100, off.MaxRpm);
		Eq("WithEnabled(false) ⇒ 回差原样", 5, off.HysteresisC);
		True("WithEnabled(false) ⇒ 原对象不被改动（仍是 true）", saved.Enabled);
		BaolongdaFanCurveSettings on = off.WithEnabled(true);
		True("WithEnabled(true) ⇒ 能再打开", on.Enabled);
		True("WithEnabled(true) ⇒ 也不是旧存档", !on.IsUnreadableArchive);

		// ---- 用例 4b-2：WithSyncEnabled —— 只改双风扇联动，⛔ 不动别的 ✅ ----
		BaolongdaFanCurveSettings syncOn = saved.WithSyncEnabled(true);
		True("WithSyncEnabled(true) ⇒ SyncEnabled=true", syncOn.SyncEnabled);
		True("WithSyncEnabled ⇒ 启用态原样", syncOn.Enabled);
		Eq("WithSyncEnabled ⇒ 曲线原样（ch0 转速首点）", 1500, syncOn.Channel0Rpms[0]);
		Eq("WithSyncEnabled ⇒ 上下界原样", 5100, syncOn.MaxRpm);
		True("WithSyncEnabled ⇒ 原对象不被改动（仍是 false）", !saved.SyncEnabled);

		// ---- 用例 4c：旧 v2 存档**没有** syncenabled ⇒ ⭐ 按 true（向后兼容）✅ ----
		WriteConfig("format=2\nenabled=0\nch0temp=" + Csv16(40) + "\nch0rpm=" + Csv16(11) + "\n"
			+ "ch1temp=" + Csv16(40) + "\nch1rpm=" + Csv16(21) + "\n");
		Logger.Reset();
		BaolongdaFanCurveSettings oldV2 = BaolongdaFanCurveStore.Load();
		True("旧 v2 缺 syncenabled ⇒ 按 true（向后兼容）", oldV2.SyncEnabled);
		True("旧 v2 缺 syncenabled ⇒ 不当作坏档", !oldV2.IsUnreadableArchive);
		Eq("旧 v2 缺 syncenabled ⇒ 不该有错误日志", 0, Logger.Errors.Count);

		// ---- 用例 5：v2 但某条曲线点数不对 ⇒ ⭐ **整份读不懂** ✅（⭐ 自检发现的设计修正 ⚠️）----
		//    ⚠️ 为什么不是"⭐ 该条退回默认、⭐ 别的照用"⚠️：
		//    ⭐ 温度数组与转速数组描述的是**同一条曲线** ✅ ⇒ ⭐ 一个用默认、⭐ 一个用用户的
		//    ⭐ ⭐ **会拼出怪曲线**（⭐ 例如 40℃ 配 21 RPM ⚠️）⇒ ⭐ 那就是"⭐ 猜" ❌
		WriteConfig("format=2\nenabled=1\nhysteresis=4\n"
			+ "ch0temp=" + Csv16(40) + "\nch0rpm=" + Csv16(11) + "\n"
			+ "ch1temp=1,2,3\nch1rpm=" + Csv16(21) + "\n");
		Logger.Reset();
		BaolongdaFanCurveSettings partial = BaolongdaFanCurveStore.Load();
		True("点数错 ⇒ 整份不启用（⛔ 不半个用默认半个用用户的）", !partial.Enabled);
		True("点数错 ⇒ 标记 IsUnreadableArchive", partial.IsUnreadableArchive);
		Logged("点数错 ⇒ 可见日志点名 ch1temp 点数", "ch1temp 点数=3");
		Eq("点数错 ⇒ 好的那条也不被采用（曲线回默认值）", 1800, partial.Channel0Rpms[0]);

		// ⭐ 配对问题：⭐ 温度好、⭐ 转速坏 ⇒ ⭐ 同样整份读不懂（⭐ ⛔ 绝不拼"⭐ 默认温度 × ⭐ 用户转速"✅）
		WriteConfig("format=2\nenabled=1\n"
			+ "ch0temp=" + Csv16(40) + "\nch0rpm=1,2,3\n"
			+ "ch1temp=" + Csv16(40) + "\nch1rpm=" + Csv16(21) + "\n");
		Logger.Reset();
		BaolongdaFanCurveSettings halfPair = BaolongdaFanCurveStore.Load();
		True("转速坏 ⇒ 整份不启用（不拿默认温度配用户转速）", !halfPair.Enabled);
		Logged("转速坏 ⇒ 可见日志点名 ch0rpm", "ch0rpm 点数=3");

		// ⭐ 参数坏掉也不能静默兜底（⭐ 否则会**悄悄改掉夹紧范围**⚠️）
		WriteConfig("format=2\nenabled=1\nmaxrpm=abc\n"
			+ "ch0temp=" + Csv16(40) + "\nch0rpm=" + Csv16(11) + "\n"
			+ "ch1temp=" + Csv16(40) + "\nch1rpm=" + Csv16(21) + "\n");
		Logger.Reset();
		BaolongdaFanCurveSettings badParam = BaolongdaFanCurveStore.Load();
		True("参数坏 ⇒ 整份不启用", !badParam.Enabled);
		Logged("参数坏 ⇒ 可见日志点名 maxrpm", "maxrpm=「abc」不是整数");

		// ⭐ 缺 `ch0temp` 键 ⇒ ⭐ 也算读不懂（⭐ v2 的四条曲线是**必备**的 ✅）
		WriteConfig("format=2\nenabled=1\n"
			+ "ch0rpm=" + Csv16(11) + "\n"
			+ "ch1temp=" + Csv16(40) + "\nch1rpm=" + Csv16(21) + "\n");
		Logger.Reset();
		True("缺 ch0temp 键 ⇒ 整份不启用", !BaolongdaFanCurveStore.Load().Enabled);
		Logged("缺键 ⇒ 可见日志点名 ch0temp", "缺少 ch0temp");

		// ---- 用例 6：v2 且 enabled=0 ⇒ ⭐ 不启用（⭐ 安全默认 ✅）----
		WriteConfig("format=2\nenabled=0\nch0temp=" + Csv16(40) + "\nch0rpm=" + Csv16(11) + "\n"
			+ "ch1temp=" + Csv16(40) + "\nch1rpm=" + Csv16(21) + "\n");
		Logger.Reset();
		True("v2 且 enabled=0 ⇒ 不启用", !BaolongdaFanCurveStore.Load().Enabled);

		Environment.SetEnvironmentVariable(BaolongdaFanCurveStore.ConfigDirectoryOverrideVariable, null);
	}

	private static string ConfigFile()
	{
		return System.IO.Path.Combine(_tempDir, "fan_curve_bd.cfg");
	}

	private static void WriteConfig(string text)
	{
		System.IO.File.WriteAllText(ConfigFile(), text);
	}

	private static string ReadConfig()
	{
		return System.IO.File.ReadAllText(ConfigFile());
	}

	private static void DeleteConfig()
	{
		if (System.IO.File.Exists(ConfigFile()))
		{
			System.IO.File.Delete(ConfigFile());
		}
	}

	/// <summary>⭐ 16 个递增数（⭐ 便于验证"⭐ 位置对不对"✅）。</summary>
	private static int[] Ramp16(int start)
	{
		int[] a = new int[BaolongdaFanCurveSettings.PointCount];
		for (int i = 0; i < a.Length; i++)
		{
			a[i] = start + i;
		}
		return a;
	}

	/// <summary>⭐ 16 个递增数的 CSV ✅。</summary>
	private static string Csv16(int start)
	{
		return string.Join(",", Ramp16(start));
	}

	private static void Logged(string name, string needle)
	{
		bool found = false;
		foreach (string e in Logger.Errors)
		{
			if (e.Contains(needle))
			{
				found = true;
				break;
			}
		}
		True(name, found, $"错误日志 {Logger.Errors.Count} 条，未含「{needle}」");
	}

	// ══════════════════════════════════════════════════════════════════
	// ⭐ 断言小工具（⭐ 本仓没有测试框架 ⇒ ⭐ 自己来 ✅）
	// ══════════════════════════════════════════════════════════════════
	private static void Eq(string name, int expected, int actual)
	{
		if (expected == actual)
		{
			_pass++;
			Console.WriteLine("  PASS  " + name);
		}
		else
		{
			_fail++;
			Console.WriteLine($"  FAIL  {name}  ⇒ 期望={expected} 实际={actual}");
		}
	}

	private static void Null(string name, int? value)
	{
		if (value == null)
		{
			_pass++;
			Console.WriteLine("  PASS  " + name);
		}
		else
		{
			_fail++;
			Console.WriteLine($"  FAIL  {name}  ⇒ 期望=null 实际={value.Value}");
		}
	}

	private static void True(string name, bool condition, string detail = "")
	{
		if (condition)
		{
			_pass++;
			Console.WriteLine("  PASS  " + name);
		}
		else
		{
			_fail++;
			Console.WriteLine($"  FAIL  {name}  {(detail.Length > 0 ? "⇒ " + detail : string.Empty)}");
		}
	}
}
