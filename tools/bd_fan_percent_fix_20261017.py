# -*- coding: utf-8 -*-
r"""
2026-10-17（接手 · 0.291.9.8 未发布版内追加）：
宝龙达**转速百分比**改用原厂口径 —— 分母 6000，不再读功能号 21 当 max。

依据（原厂 40 系控制台 v0.3.15 · `蛟龙游戏控制中心.dll` 反编译）：
    FanSpeed.Text = _CPUGPUFanSpeed.Item1 + " RPM";      // 功能号 13: [4..5] 小端 = RPM
    float num = (float)_CPUGPUFanSpeed.Item1 / 60f;      // ⭐ 分母 = 60 ⇒ 100% = 6000 RPM
    FANProgressBar.Value = (int)Math.Round(num);
    FANTextBlock.Text = num.ToString("F0") + "%";

真机对照（MRID6-23 / 4070，同一条时间线）：
    官方 1965 RPM ⇒ 33%（1965/6000 = 32.75）
    LM  1996 RPM ⇒ 40%（旧分母 5000，来自"读功能号 21 = 50"）❌

三个文件都是纯 LF / 无 BOM（改前已核对）。
"""
import sys

sys.stdout.reconfigure(encoding="utf-8", errors="replace")
BASE = r"D:\openLM\source"


def read(path):
    with open(path, "rb") as f:
        data = f.read()
    assert not data.startswith(b"\xef\xbb\xbf"), "有 BOM: " + path
    text = data.decode("utf-8")
    assert "\r\n" not in text, "不是纯 LF: " + path
    return text


def write(path, text):
    assert "\r\n" not in text
    with open(path, "wb") as f:
        f.write(text.encode("utf-8"))


def must(c, msg):
    if not c:
        raise SystemExit("FAIL: " + msg)


# ══════════════════════════════════════════════════════════════════════
# 1) MechrevoHw.cs：删掉"读功能号 21 当分母"的整套逻辑，换成原厂常量
# ══════════════════════════════════════════════════════════════════════
P_HW = BASE + r"\MechrevoLite.Hardware\MechrevoHw.cs"
hw = read(P_HW)
lines = hw.split("\n")

START = "\tprivate int _baolongdaFanRpmMaxCache;"
END = "\tpublic bool FanRpmMaxIsReference => _baolongdaFanRpmMaxIsReference;"
si = [i for i, ln in enumerate(lines) if ln == START]
ei = [i for i, ln in enumerate(lines) if ln == END]
must(len(si) == 1 and len(ei) == 1, "MechrevoHw 锚点不唯一")
s, e = si[0], ei[0]
must(e > s, "锚点顺序异常")

NEW_HW = [
    "\t// ⭐⭐ 2026-10-17（接手 · 按**原厂口径**修正转速百分比）：⭐ 100% 对应的 RPM ✅",
    "\t//",
    "\t//  ⭐⭐ 依据：原厂 40 系控制台 `蛟龙游戏控制中心.dll`（v0.3.15 · Bitland 签名）反编译 ——",
    "\t//     FanSpeed.Text = _CPUGPUFanSpeed.Item1 + \" RPM\";   // 功能号 13：[4..5] 小端 = RPM",
    "\t//     float num = (float)_CPUGPUFanSpeed.Item1 / 60f;   // ⭐ 分母 = 60 ⇒ 100% = 6000 RPM",
    "\t//     FANProgressBar.Value = (int)Math.Round(num);",
    "\t//     FANTextBlock.Text = num.ToString(\"F0\") + \"%\";",
    "\t//  真机对照（40 系 `MRID6-23` / 4070）：官方 1965 RPM ⇒ **33%**（1965 ÷ 6000 = 32.75 ✅）。",
    "\t//",
    "\t//  ⭐ 6000 不是猜的：功能号 21 的**写**口径是 `x100 RPM`、协议上限 60",
    "\t//    （原厂手动转速范围 22~58 ⇒ 2200~5800 RPM）⇒ 60 × 100 = 6000 就是这套编码的满刻度，",
    "\t//    原厂也正好拿它当百分比分母 ✅。",
    "\t//",
    "\t//  ⚠️⚠️ **为什么不再读功能号 21**：原厂把它的读数当作**手动转速设定值**",
    "\t//    （`MaxFanSpeed.Text = value`，再由它反算滑块位置 `Set_MaxFanSpeedSlider`），",
    "\t//    ⛔ 从来不是\"最大转速\"。旧实现把读数（真机 = 50）当分母 ⇒ 5000",
    "\t//    ⇒ 1996 RPM 显示 **40%**，而官方同一时刻是 **33%** ❌ —— 这就是\"和官方对不上\"的根因。",
    "\t//",
    "\t//  ⚠️ 30 系仍待确认：旧版 30 系原厂控制台（v0.2.35）用的是 `rpm / 35f`（⇒ 3500 RPM），",
    "\t//    但那是另一套年代／可能不同模具的包 ⇒ ⛔ 暂不套用；30 系真机请对照官方控制台百分比，",
    "\t//    若不一致就把这里按代数拆开（⚠️ 唯一改动点）✅",
    "\tinternal const int BaolongdaFanRpmAtFullPercent = 6000;",
    "",
    "\t/// <summary>",
    "\t/// ⭐⭐ 2026-10-17（接手 · 按原厂口径修正）：⭐ 风扇百分比的**分母**（RPM ✅）。",
    "\t/// <para>依据与原委见 <see cref=\"BaolongdaFanRpmAtFullPercent\" /> —— 原厂 40 系是 `rpm ÷ 60`，",
    "\t/// ⛔ 不是\"读功能号 21 当 max\"（那个读数是**手动转速设定值** ⚠️）。</para>",
    "\t/// <para>⚠️ 非宝龙达 ⇒ 返回 0（⭐ 分母由各机型自己的 duty 口径负责 ✅）。</para>",
    "\t/// </summary>",
    "\tpublic int FanRpmMaxForPercent => UsesBaolongda ? BaolongdaFanRpmAtFullPercent : 0;",
]
lines[s:e + 1] = NEW_HW
write(P_HW, "\n".join(lines))
print("[OK] MechrevoHw.cs -> 分母改成原厂常量 6000（删掉功能号 21 读 max 的整套逻辑）")

# ══════════════════════════════════════════════════════════════════════
# 2) SettingsForm.cs：同步注释与判据（去掉 !FanRpmMaxIsReference）
# ══════════════════════════════════════════════════════════════════════
P_SF = BASE + r"\MechrevoLite\SettingsForm.cs"
sf = read(P_SF)
slines = sf.split("\n")

S_MARK = '\t\t// ⭐⭐ 2026-10-16（0.291.9.1 · 修"⭐ 宝龙达机型百分比恒为 <c>0%</c>"⭐）：'
s2 = [i for i, ln in enumerate(slines) if ln.startswith(S_MARK)]
must(len(s2) == 1, "SettingsForm 注释锚点不唯一")
start = s2[0]
# 结尾：else 分支最后一行 + 收尾大括号
else_line = [i for i in range(start, start + 80)
             if slines[i] == "\t\t\tduty = isCpu ? hw.CpuFanDuty : hw.GpuFanDuty;"]
must(len(else_line) == 1, "SettingsForm else 锚点不唯一")
end = else_line[0] + 1
must(slines[end] == "\t\t}", "SettingsForm 收尾大括号位置异常")

NEW_SF = [
    "\t\t// ⭐⭐ 2026-10-17（接手 · 按原厂口径修正转速百分比）：⭐ 宝龙达的百分比 = ⭐ RPM ÷ 6000 ✅",
    "\t\t//   依据：原厂 40 系控制台 v0.3.15 的 `float num = rpm / 60f`（⭐ 完整代码与真机对照见",
    "\t\t//   `MechrevoHw.BaolongdaFanRpmAtFullPercent` 的注释）。",
    "\t\t//   ⚠️ 旧实现先读功能号 21 当分母 —— 原厂把那个读数当**手动转速设定值**、⛔ 不是最大转速",
    "\t\t//      ⇒ 真机读到 50 ⇒ 分母 5000 ⇒ 1996 RPM 显示 40%，而官方是 33% ❌ 已弃用。",
    "\t\t//   ⭐ 真机对照（MRID6-23 / 4070）：官方 1965 RPM ⇒ 33%（1965 ÷ 6000 = 32.75 ✅）。",
    "\t\t//   ⚠️ 对 GCU 机型：`UsesBaolongda` 为假 ⇒ 走 else ⇒ 判据一字未改（含 duty=0 照旧显示 0%）✅",
    "\t\tint? duty;",
    "\t\tif (hw.UsesBaolongda)",
    "\t\t{",
    "\t\t\tint rpmMaxForPercent = hw.FanRpmMaxForPercent;",
    "\t\t\tint rpmNow = isCpu ? hw.CpuFanRpm : hw.GpuFanRpm;",
    "\t\t\tduty = (rpmMaxForPercent > 0 && rpmNow > 0)",
    "\t\t\t\t? (int?)Math.Min(100, (int)Math.Round(rpmNow * 100.0 / rpmMaxForPercent))",
    "\t\t\t\t: (int?)null;",
    "\t\t}",
    "\t\telse",
    "\t\t{",
    "\t\t\tduty = isCpu ? hw.CpuFanDuty : hw.GpuFanDuty;",
    "\t\t}",
]
slines[start:end + 1] = NEW_SF
write(P_SF, "\n".join(slines))
print("[OK] SettingsForm.cs -> 同步注释与判据")

# ══════════════════════════════════════════════════════════════════════
# 3) DiagnosticBaolongdaInfo.cs：功能号 21 的标签/说明改成已知语义
# ══════════════════════════════════════════════════════════════════════
P_DG = BASE + r"\MechrevoLite.Diagnostics\DiagnosticBaolongdaInfo.cs"
dg = read(P_DG)

OLD_C = """\t\t// ⭐⭐ 2026-10-16（宝龙达 · 转速百分比）：⭐ **补试读功能号 21**（`MaxFanSpeed`）✅ ——
\t\t//   ⚠️ 核查发现本节**从未试读过它**（只读了 8/13/20/22/23）⇒ ⭐ "功能号 21 能不能读到
\t\t//   **本机最大转速**"此前**无人知道** ⚠️。⭐ 它是风扇百分比**分母**的第一候选 ✅。
\t\t//   ⚠️ 写是双字节 `[FanType, RPM÷100]`（⭐ 见 `TrySetMaxFanSpeed` ✅）⇒ ⭐ **读的语义未验证**
\t\t//   ⇒ ⭐ 所以下面把**原始字节**也打出来，让诊断包自行判断 ⛔（⭐ 不猜 ✅）。
\t\tAppendOne(sb, wmi, BaolongdaWmi.Function.MaxFanSpeed, "21", "风扇最大转速");"""
NEW_C = """\t\t// ⭐⭐ 2026-10-17（接手）：⭐ 功能号 21 的**读语义已由原厂包确认** ——
\t\t//   ⛔ **不是"最大转速"**，⭐ 而是**手动转速设定值**：原厂 `MaxFanSpeed.Text = value`、
\t\t//   再由它反算滑块位置（`Set_MaxFanSpeedSlider`）⇒ 读值 = 上次写下去的 `x100 RPM` ✅。
\t\t//   ⇒ ⭐ 我们**不再**拿它当百分比分母（真机读到 50 ⇒ 曾被当成 5000 RPM，导致 40% vs 官方 33% ❌）。
\t\t//   下面仍把原始字节打出来，⭐ 供核对"手动设定值到底是多少" ✅。
\t\tAppendOne(sb, wmi, BaolongdaWmi.Function.MaxFanSpeed, "21", "风扇手动转速设定值（⚠️ 读=上次设的转速，不是最大转速）");"""
must(dg.count(OLD_C) == 1, "Diagnostic 注释+标签锚点不唯一")
dg = dg.replace(OLD_C, NEW_C)

OLD_RAW = """\t\t\t\tsb.AppendLine($"     · 功能号 21 原始 4 字节 = {mfsRaw[0]},{mfsRaw[1]},{mfsRaw[2]},{mfsRaw[3]}"
\t\t\t\t\t+ $" ⇒ 单字节={mfsRaw[0]} · 小端16位={(mfsRaw[1] << 8) + mfsRaw[0]}"
\t\t\t\t\t+ $" · 大端16位={(mfsRaw[0] << 8) + mfsRaw[1]}"
\t\t\t\t\t+ " · ⚠️ 也可能是 RPM÷100 口径 ⚠️ ⇒ **语义待判定**（⭐ 不猜 ✅）");"""
NEW_RAW = """\t\t\t\tsb.AppendLine($"     · 功能号 21 原始 4 字节 = {mfsRaw[0]},{mfsRaw[1]},{mfsRaw[2]},{mfsRaw[3]}"
\t\t\t\t\t+ $" ⇒ 单字节={mfsRaw[0]}（= 手动设定 {mfsRaw[0] * 100} RPM）"
\t\t\t\t\t+ $" · 小端16位={(mfsRaw[1] << 8) + mfsRaw[0]} · 大端16位={(mfsRaw[0] << 8) + mfsRaw[1]}"
\t\t\t\t\t+ " · ⭐ 原厂口径：⛔ 不是最大转速 ⇒ 已经不拿它当百分比分母 ✅");"""
must(dg.count(OLD_RAW) == 1, "Diagnostic 原始字节锚点不唯一")
dg = dg.replace(OLD_RAW, NEW_RAW)

OLD_HINT = '\t\t\t\tsb.AppendLine("     · 功能号 21 只读到 1 字节或读不到"\n\t\t\t\t\t+ " ⇒ ⚠️ 不能据此推 max（⭐ 若读不到就只能退化为**参考分母** ✅）");'
NEW_HINT = '\t\t\t\tsb.AppendLine("     · 功能号 21 只读到 1 字节或读不到"\n\t\t\t\t\t+ " ⇒ ⚠️ 这只影响\\"手动设定值\\"的显示；⭐ 百分比分母是常量 6000、与本项无关 ✅");'
must(dg.count(OLD_HINT) == 1, "Diagnostic 兜底锚点不唯一")
dg = dg.replace(OLD_HINT, NEW_HINT)

write(P_DG, dg)
print("[OK] DiagnosticBaolongdaInfo.cs -> 功能号 21 语义改标")
print("DONE")
