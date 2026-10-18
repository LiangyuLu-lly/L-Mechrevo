// 2026-10-17（平台声明式 · 第 1 步自检）：
// ⭐ 验 `BaolongdaProfile` 的**事实值**与**纯函数**（身份委托 / 亮度上限 / 温度来源顺序）。
//
// ⭐ 为什么放在这里（本仓惯例）：`tools\dev\<name>-selftest\` 把**生产源文件 link 进来**编成 exe，
//    用断言跑自检（见 `fancurve-selftest` / `board-whitelist-probe`）。
// ⚠️ 本自检**只验纯逻辑**：⛔ 不碰硬件、⛔ 不读注册表（BoardWhitelist.Matches 接受字符串入参）。
//
// 跑法：dotnet run --project tools\dev\bdprofile-selftest  （退出码 0 = 全过）

using System;
using MechrevoLite.Gpu;
using MechrevoLite.Hardware;

internal static class Program
{
	private static int _pass;
	private static int _fail;

	private static int Main()
	{
		Console.WriteLine("=== BaolongdaProfile 自检（平台声明式 · 第 1 步：事实表）===");

		Console.WriteLine();
		Console.WriteLine("-- 1. 风扇事实（百分比分母 / 手动上限 / 通道）--");
		Eq("百分比分母 = 6000 RPM（原厂 40 系 ÷60 口径）", 6000, BaolongdaProfile.FanPercentDenominatorRpm);
		Eq("手动转速协议上限 = 60（x100RPM）", 60, BaolongdaProfile.FanManualMaxUnits100Rpm);
		Eq("FanType 0 = 大扇/GPU", 0, BaolongdaProfile.FanTypeLarge);
		Eq("FanType 1 = 小扇/CPU", 1, BaolongdaProfile.FanTypeSmall);
		True("分母与手动上限自洽（60×100=6000）",
			BaolongdaProfile.FanManualMaxUnits100Rpm * 100 == BaolongdaProfile.FanPercentDenominatorRpm);

		Console.WriteLine();
		Console.WriteLine("-- 2. 功耗墙 / 温度墙范围 --");
		Eq("PL 下限 = 5W", 5, BaolongdaProfile.PlMinWatts);
		Eq("PL1 上限 = 120W", 120, BaolongdaProfile.Pl1MaxWatts);
		Eq("PL2 上限 = 150W", 150, BaolongdaProfile.Pl2MaxWatts);
		Eq("温度墙下限 = 60℃", 60, BaolongdaProfile.TccMinCelsius);
		Eq("温度墙上限 = 105℃", 105, BaolongdaProfile.TccMaxCelsius);
		Eq("未设置哨兵 = 0", 0, BaolongdaProfile.Unset);
		True("范围自洽（下限 < 上限）",
			BaolongdaProfile.PlMinWatts < BaolongdaProfile.Pl1MaxWatts
			&& BaolongdaProfile.PlMinWatts < BaolongdaProfile.Pl2MaxWatts
			&& BaolongdaProfile.TccMinCelsius < BaolongdaProfile.TccMaxCelsius);

		Console.WriteLine();
		// ⭐⭐ 2026-10-19（机主实测纠正）：40 系键盘亮度**只有 0/1/2/3**（最大 3），
		//   原"Gen40/50 = 4"会让 UI 拖到固件不支持的档位 ⇒ 「最大亮度不能用」❌
		//   ⇒ 现在全系统一 3（若将来某机型实测有第 5 档，由运行时自校准覆盖本常量）
		Console.WriteLine("-- 3. 键盘灯亮度上限（全系统一 3；40 系实测也只有 0~3）--");
		Eq("Gen30 ⇒ 3", 3, BaolongdaProfile.BrightnessMax(DgpuGenerationKind.Gen30));
		Eq("Gen40 ⇒ 3（机主实测 0~3，不再是 4）", 3, BaolongdaProfile.BrightnessMax(DgpuGenerationKind.Gen40));
		Eq("Gen50 ⇒ 3", 3, BaolongdaProfile.BrightnessMax(DgpuGenerationKind.Gen50));
		Eq("Unknown ⇒ 3（保守方向）", 3, BaolongdaProfile.BrightnessMax(DgpuGenerationKind.Unknown));
		Eq("NoDgpu ⇒ 3", 3, BaolongdaProfile.BrightnessMax(DgpuGenerationKind.NoDgpu));

		Console.WriteLine();
		Console.WriteLine("-- 4. 身份委托（判据本体在 BoardWhitelist，⛔ 不复制）--");
		True("MRID6（30 系）命中", BaolongdaProfile.MatchesBoard("MRID6"));
		True("MRID6-23（40 系）命中", BaolongdaProfile.MatchesBoard("MRID6-23"));
		True("MRID6-99（变体）命中", BaolongdaProfile.MatchesBoard("MRID6-99"));
		True("小写 mrid6-23 也命中（大小写不敏感）", BaolongdaProfile.MatchesBoard("mrid6-23"));
		True("两侧空白先裁掉", BaolongdaProfile.MatchesBoard("  MRID6  "));
		True("MRID60 不命中（假想板号）", !BaolongdaProfile.MatchesBoard("MRID60"));
		True("MRID6A 不命中（假想板号）", !BaolongdaProfile.MatchesBoard("MRID6A"));
		True("GM5HG0A（同方开发机）不命中", !BaolongdaProfile.MatchesBoard("GM5HG0A"));
		True("空串不命中（安全方向）", !BaolongdaProfile.MatchesBoard(""));
		True("null 不命中（安全方向）", !BaolongdaProfile.MatchesBoard(null));

		Console.WriteLine();
		Console.WriteLine("-- 5. 温度来源顺序（声明）--");
		Eq("CPU 来源 3 档", 3, BaolongdaProfile.CpuTempSources.Length);
		Eq("GPU 来源 3 档", 3, BaolongdaProfile.GpuTempSources.Length);
		Eq("CPU 第 1 档 = 厂商（功能号 22）", BaolongdaProfile.TempSource.Vendor, BaolongdaProfile.CpuTempSources[0]);
		Eq("CPU 最后 1 档 = ACPI 热区", BaolongdaProfile.TempSource.Acpi, BaolongdaProfile.CpuTempSources[2]);
		Eq("GPU 第 1 档 = 厂商（宝龙达没有）", BaolongdaProfile.TempSource.Vendor, BaolongdaProfile.GpuTempSources[0]);
		Eq("GPU 最后 1 档 = NVML", BaolongdaProfile.TempSource.Nvml, BaolongdaProfile.GpuTempSources[2]);
		string desc = BaolongdaProfile.DescribeTemperatureSources();
		True("人读描述含 CPU 顺序", desc.Contains("CPU=[厂商→LHM→ACPI热区]"), desc);
		True("人读描述含 GPU 顺序", desc.Contains("GPU=[厂商→LHM→NVML]"), desc);

		Console.WriteLine();
		Console.WriteLine(_fail == 0
			? $"✅ 全部通过：PASS={_pass} FAIL=0"
			: $"❌ 有失败：PASS={_pass} FAIL={_fail}");
		return _fail == 0 ? 0 : 1;
	}

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

	private static void Eq(string name, BaolongdaProfile.TempSource expected, BaolongdaProfile.TempSource actual)
	{
		Eq(name, (int)expected, (int)actual);
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
