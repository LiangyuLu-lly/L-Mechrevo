# -*- coding: utf-8 -*-
r"""平台声明式 · 第 1 步：把宝龙达的**数值事实**从 MechrevoHw 常量改成引用 `BaolongdaProfile`。

⚠️ 只改"这个数写在哪"，不改任何行为：写入路径的判据/顺序/夹取一行不动。
"""
import sys

sys.stdout.reconfigure(encoding="utf-8", errors="replace")
P = r"D:\openLM\source\MechrevoLite.Hardware\MechrevoHw.cs"

with open(P, "rb") as f:
    raw = f.read()
assert not raw.startswith(b"\xef\xbb\xbf")
t = raw.decode("utf-8")
assert "\r\n" not in t

REPL = [
    ("internal const int BaolongdaFanRpmAtFullPercent = 6000;",
     "internal const int BaolongdaFanRpmAtFullPercent = BaolongdaProfile.FanPercentDenominatorRpm;"),
    ("private const int BaolongdaPlMinWatts = 5;",
     "private const int BaolongdaPlMinWatts = BaolongdaProfile.PlMinWatts;"),
    ("private const int BaolongdaPl1MaxWatts = 120;",
     "private const int BaolongdaPl1MaxWatts = BaolongdaProfile.Pl1MaxWatts;"),
    ("private const int BaolongdaPl2MaxWatts = 150;",
     "private const int BaolongdaPl2MaxWatts = BaolongdaProfile.Pl2MaxWatts;"),
    ("private const int BaolongdaTccMinCelsius = 60;",
     "private const int BaolongdaTccMinCelsius = BaolongdaProfile.TccMinCelsius;"),
    ("private const int BaolongdaTccMaxCelsius = 105;",
     "private const int BaolongdaTccMaxCelsius = BaolongdaProfile.TccMaxCelsius;"),
    ("private const int BaolongdaUnset = 0;",
     "private const int BaolongdaUnset = BaolongdaProfile.Unset;"),
    ("internal const int BaolongdaFanTypeLarge = 0;",
     "internal const int BaolongdaFanTypeLarge = BaolongdaProfile.FanTypeLarge;"),
    ("internal const int BaolongdaFanTypeSmall = 1;",
     "internal const int BaolongdaFanTypeSmall = BaolongdaProfile.FanTypeSmall;"),
    # 亮度上限：把内联分叉收进 profile
    ("""		DgpuGenerationKind gen = Program.hw?.DgpuGeneration ?? DgpuGenerationKind.Unknown;
		int max = (gen == DgpuGenerationKind.Gen40 || gen == DgpuGenerationKind.Gen50) ? 4 : 3;
		return (byte)Math.Clamp(ui0to4, 1, max);""",
     """		DgpuGenerationKind gen = Program.hw?.DgpuGeneration ?? DgpuGenerationKind.Unknown;
		return (byte)Math.Clamp(ui0to4, 1, BaolongdaProfile.BrightnessMax(gen));"""),
    # 启动时把"声明的事实"打一行（只读、仅日志；写入路径不碰）
    ("""		Logger.WriteLine($"Hardware backend: Baolongda ({backend.Wmi.InstancePath}); built-in modes only (本轮)."
			+ $" fans={backend.Wmi.FanCount} modes={backend.Wmi.ModeCount}");""",
     """		Logger.WriteLine($"Hardware backend: Baolongda ({backend.Wmi.InstancePath}); built-in modes only (本轮)."
			+ $" fans={backend.Wmi.FanCount} modes={backend.Wmi.ModeCount}");
		// ⭐⭐ 2026-10-17（平台声明式 · 第 1 步）：⭐ 把"声明的事实"打一行 ✅
		//   ⚠️ 纯日志：⛔ 不改变任何行为；⭐ 以后诊断包里能直接看到"这台机器按什么口径算"✅
		Logger.WriteLine("宝龙达平台事实表（声明）："
			+ $"转速百分比分母={BaolongdaProfile.FanPercentDenominatorRpm} RPM · "
			+ $"手动转速上限={BaolongdaProfile.FanManualMaxUnits100Rpm}（x100RPM） · "
			+ $"功耗墙范围 PL1=[{BaolongdaProfile.PlMinWatts},{BaolongdaProfile.Pl1MaxWatts}]W "
			+ $"PL2=[{BaolongdaProfile.PlMinWatts},{BaolongdaProfile.Pl2MaxWatts}]W "
			+ $"温度墙=[{BaolongdaProfile.TccMinCelsius},{BaolongdaProfile.TccMaxCelsius}]℃ · "
			+ $"温度来源顺序 {BaolongdaProfile.DescribeTemperatureSources()}");"""),
]

for old, new in REPL:
    if t.count(old) != 1:
        raise SystemExit("锚点数量异常（%d 处）：%s" % (t.count(old), old[:60]))
    t = t.replace(old, new)

assert "\r\n" not in t
with open(P, "wb") as f:
    f.write(t.encode("utf-8"))
print("[OK] MechrevoHw.cs 已改为引用 BaolongdaProfile（%d 处）" % len(REPL))
