# -*- coding: utf-8 -*-
r"""
2026-10-17（合规清理）：把**源码注释里的原厂反编译片段**换成"只取事实"的表述。

原因（用户 2026-10-17 原话）：「和官方对齐就行 不要照抄官方代码有法律风险」。
原则（本仓既有红线）：**只取事实（数值 / 功能号 / 顺序 / 常量），⛔ 不抄代码结构、⛔ 不贴反编译原文到源码**。
取证原文只留在 docs\notes（内部记录，不进安装包、不进 GPL 源码分发语境之外的产物）。

本脚本改两个文件的注释（纯 LF / 无 BOM）。
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


def replace_span(text, start_marker, end_marker_exclusive, new_block):
    s = text.find(start_marker)
    must(s >= 0, "找不到起始锚点: " + start_marker[:40])
    e = text.find(end_marker_exclusive, s)
    must(e > s, "找不到结束锚点: " + end_marker_exclusive[:40])
    return text[:s] + new_block + text[e:]


# ══════════════════════════════════════════════════════════════════════
# 1) MechrevoHw.cs —— 去掉 4 行原厂代码原文
# ══════════════════════════════════════════════════════════════════════
P_HW = BASE + r"\MechrevoLite.Hardware\MechrevoHw.cs"
hw = read(P_HW)

NEW_HW_BLOCK = """\t// ⭐⭐ 2026-10-17（接手 · 按**原厂口径**修正转速百分比）：⭐ 100% 对应的 RPM ✅
\t//
\t//  ⭐⭐ 依据（⭐ 只取**事实**：原厂 40 系控制台 v0.3.15 的反编译结论 ——
\t//     ⛔ 源码/注释里**不放**它的代码片段，法律风险；取证原文只留在 docs 的分析记录里）：
\t//     · 采样：功能号 13（`CPUGPUFanSpeed`）—— 我们本来就在读 ✅
\t//     · 折算：RPM **÷ 60** 得到百分比 ⇒ ⭐ **100% = 6000 RPM** ✅
\t//     · 显示：四舍五入成**整数**百分比（`33%`）✅
\t//  真机对照（40 系 `MRID6-23` / 4070）：官方 1965 RPM ⇒ **33%**（1965 ÷ 6000 = 32.75 ✅）。
\t//
\t//  ⭐ 6000 不是猜的：功能号 21 的**写**口径是 `x100 RPM`、协议上限 60
\t//    （原厂手动转速范围 22~58 ⇒ 2200~5800 RPM）⇒ 60 × 100 = 6000 就是这套编码的满刻度 ✅。
\t//
\t//  ⚠️⚠️ **为什么不再读功能号 21**：原厂把它当**手动转速设定值**用 ——
\t//    读回来的就是"上次写下去的那个值"（用来回显/定位手动滑块），⛔ 从来不是"最大转速"。
\t//    旧实现把读数（真机 = 50）当分母 ⇒ 5000 ⇒ 1996 RPM 显示 **40%**，而官方是 **33%** ❌。
\t//  ⭐ 合规说明：本改动**没有照搬原厂的任何实现**，只采用"分母 60"这一个**协议事实** ✅。
\t//
\t//  ⚠️ 30 系仍待确认：旧版 30 系原厂控制台（v0.2.35）用的是**除以 35**（⇒ 3500 RPM），
\t//    但那是另一套年代／可能不同模具的包 ⇒ ⛔ 暂不套用；30 系真机请对照官方控制台百分比，
\t//    若不一致就把这里按代数拆开（⚠️ 唯一改动点）✅
"""
hw = replace_span(hw,
                  "\t// ⭐⭐ 2026-10-17（接手 · 按**原厂口径**修正转速百分比）：⭐ 100% 对应的 RPM ✅",
                  "\tinternal const int BaolongdaFanRpmAtFullPercent = 6000;",
                  NEW_HW_BLOCK)
must("_CPUGPUFanSpeed.Item1" not in hw, "MechrevoHw 里仍残留原厂代码片段")
must("FANTextBlock" not in hw, "MechrevoHw 里仍残留原厂代码片段(FANTextBlock)")
write(P_HW, hw)
print("[OK] MechrevoHw.cs -> 注释改为只取事实（无原厂代码片段）")

# ══════════════════════════════════════════════════════════════════════
# 2) DiagnosticBaolongdaInfo.cs —— 去掉 `MaxFanSpeed.Text = value` 这类原文
# ══════════════════════════════════════════════════════════════════════
P_DG = BASE + r"\MechrevoLite.Diagnostics\DiagnosticBaolongdaInfo.cs"
dg = read(P_DG)

NEW_DG_BLOCK = """\t\t// ⭐⭐ 2026-10-17（接手）：⭐ 功能号 21 的**读语义已由原厂包确认** ——
\t\t//   ⛔ **不是"最大转速"**，⭐ 而是**手动转速设定值**：原厂读回它只用于回显/定位手动转速滑块，
\t\t//   值就是上次写下去的 `x100 RPM` ✅（⭐ 只取事实，⛔ 不贴其代码 —— 法律风险）。
\t\t//   ⇒ ⭐ 我们**不再**拿它当百分比分母（真机读到 50 ⇒ 曾被当成 5000 RPM，导致 40% vs 官方 33% ❌）。
\t\t//   下面仍把原始字节打出来，⭐ 供核对"手动设定值到底是多少" ✅。
"""
dg = replace_span(dg,
                  "\t\t// ⭐⭐ 2026-10-17（接手）：⭐ 功能号 21 的**读语义已由原厂包确认** ——",
                  "\t\tAppendOne(sb, wmi, BaolongdaWmi.Function.MaxFanSpeed, \"21\",",
                  NEW_DG_BLOCK)
must("MaxFanSpeed.Text" not in dg, "Diagnostics 里仍残留原厂代码片段")
must("Set_MaxFanSpeedSlider" not in dg, "Diagnostics 里仍残留原厂代码片段(Set_MaxFanSpeedSlider)")
write(P_DG, dg)
print("[OK] DiagnosticBaolongdaInfo.cs -> 注释改为只取事实")
print("DONE")
