# -*- coding: utf-8 -*-
"""
2026-10-17（接手修复 · 0.291.9.8 · 宝龙达风扇曲线）—— 第 2/2 部分：
  ② `BaolongdaFanCurveSettings.WithEnabled()`（⭐ 界面开关要"只改 Enabled"的拷贝）
  ③ `FanCurveForm` 补「启用软件闭环（本机无固件曲线表）」复选框（待办 #1）

⚠️ 第 1/2 部分（`MechrevoHw.cs` 的两处误插 ＋ 决策器接线）见
   `bd_fan_curve_fix_20261017.py`（已跑过，别重复跑）。

行尾：
  - BaolongdaFanCurveStore.cs：纯 LF
  - FanCurveForm.cs：纯 CRLF（⇒ ⛔ 不能用 edit 工具直接改）
"""
BASE = r"D:\openLM\source"


def read(path):
    with open(path, "rb") as f:
        data = f.read()
    assert not data.startswith(b"\xef\xbb\xbf"), "有 BOM，脚本假定无 BOM: " + path
    return data.decode("utf-8")


def write(path, text):
    with open(path, "wb") as f:
        f.write(text.encode("utf-8"))


def must(cond, msg):
    if not cond:
        raise SystemExit("FAIL: " + msg)


# ══════════════════════════════════════════════════════════════════════
# 2) BaolongdaFanCurveStore.cs（纯 LF）
# ══════════════════════════════════════════════════════════════════════
P_STORE = BASE + r"\MechrevoLite.Hardware\BaolongdaFanCurveStore.cs"
st = read(P_STORE)
must("\r\n" not in st, "BaolongdaFanCurveStore.cs 不是纯 LF")

OLD_CLONE = (
    "\tinternal BaolongdaFanCurveSettings Clone()\n"
    "\t{\n"
    "\t\treturn new BaolongdaFanCurveSettings\n"
    "\t\t{\n"
    "\t\t\tEnabled = Enabled,"
)
NEW_CLONE = (
    "\tinternal BaolongdaFanCurveSettings Clone(bool? enabledOverride = null)\n"
    "\t{\n"
    "\t\treturn new BaolongdaFanCurveSettings\n"
    "\t\t{\n"
    "\t\t\tEnabled = enabledOverride ?? Enabled,"
)
must(st.count(OLD_CLONE) == 1, "Clone 锚点不唯一")
st = st.replace(OLD_CLONE, NEW_CLONE)

OLD_TAIL = (
    "\t\t\tStatusNote = StatusNote\n"
    "\t\t};\n"
    "\t}\n"
    "}\n"
)
NEW_TAIL = (
    "\t\t\tStatusNote = StatusNote\n"
    "\t\t};\n"
    "\t}\n"
    "\n"
    '\t/// <summary>⭐ 拷贝一份、只改 <see cref="Enabled" /> ✅（⭐ 供界面「启用/关闭软件闭环」开关用 ✅）。</summary>\n'
    "\tinternal BaolongdaFanCurveSettings WithEnabled(bool enabled)\n"
    "\t{\n"
    "\t\treturn Clone(enabled);\n"
    "\t}\n"
    "}\n"
)
must(st.count(OLD_TAIL) == 1, "Clone 结尾锚点不唯一")
st = st.replace(OLD_TAIL, NEW_TAIL)
write(P_STORE, st)
print("[OK] BaolongdaFanCurveStore.cs -> WithEnabled()")

# ══════════════════════════════════════════════════════════════════════
# 3) FanCurveForm.cs（纯 CRLF ⇒ 归一/还原）
# ══════════════════════════════════════════════════════════════════════
P_FORM = BASE + r"\MechrevoLite\FanCurveForm.cs"
fm = read(P_FORM)
must("\r\n" in fm and fm.replace("\r\n", "").count("\n") == 0, "FanCurveForm.cs 不是纯 CRLF")
fm = fm.replace("\r\n", "\n")

# 3.1 字段
OLD_F = "\tprivate RCheckBox? _respectiveChk;\n"
NEW_F = (
    "\tprivate RCheckBox? _respectiveChk;\n"
    "\n"
    "\t// 2026-10-17（接手修复 · 待办 #1）：宝龙达软件闭环的启用开关\n"
    "\t//   原来保存曲线必然 Enabled=true 且没有关闭入口 ⇒ 机主只能手改 cfg 才能停掉\n"
    "\tprivate RCheckBox? _bdEnableChk;\n"
    "\n"
    "\tprivate bool _syncingBdEnable;\n"
)
must(fm.count(OLD_F) == 1, "字段锚点不唯一")
fm = fm.replace(OLD_F, NEW_F)

# 3.2 控件（放在"独立控制"提示后面；仅宝龙达可见）
OLD_C = "\t\tflowLayoutPanel.Controls.Add(_respectiveHint);\n"
NEW_C = (
    "\t\tflowLayoutPanel.Controls.Add(_respectiveHint);\n"
    "\t\t// 2026-10-17（接手修复 · 待办 #1）：宝龙达的「启用软件闭环」开关\n"
    "\t\t//   宝龙达没有固件曲线表 ⇒ 曲线只能由我们的程序按温度下发\n"
    "\t\t//   ⇒ 必须有个关得掉的入口（关 = 两通道归零 + 关手动开关 = 交回固件）\n"
    "\t\t_bdEnableChk = new RCheckBox\n"
    "\t\t{\n"
    '\t\t\tText = "启用软件闭环（本机无固件曲线表）",\n'
    "\t\t\tChecked = false,\n"
    "\t\t\tForeColor = UiVisualStyle.Text,\n"
    "\t\t\tBackColor = UiVisualStyle.Window,\n"
    "\t\t\tAutoSize = true,\n"
    "\t\t\tVisible = false,\n"
    "\t\t\tMargin = new Padding(D(12), D(4), 0, 0),\n"
    "\t\t\tCursor = Cursors.Hand\n"
    "\t\t};\n"
    "\t\t_bdEnableChk.CheckedChanged += (object? _, EventArgs _) =>\n"
    "\t\t{\n"
    "\t\t\tif (_syncingBdEnable || !(Program.hw?.UsesBaolongda ?? false))\n"
    "\t\t\t{\n"
    "\t\t\t\treturn;\n"
    "\t\t\t}\n"
    "\t\t\tToggleBaolongdaCurveEnable(_bdEnableChk.Checked);\n"
    "\t\t};\n"
    "\t\tflowLayoutPanel.Controls.Add(_bdEnableChk);\n"
)
must(fm.count(OLD_C) == 1, "控件插入锚点不唯一")
fm = fm.replace(OLD_C, NEW_C)

# 3.3 刷新时同步开关状态 + 如实显示"读不懂"
OLD_S = (
    '\t\t\t\t\t_status.Text = "风扇曲线：软件闭环（宝龙达）"\n'
    '\t\t\t\t\t\t+ (s.Enabled ? " · 已启用" : " · 未启用")\n'
    '\t\t\t\t\t\t+ $" · 范围 {s.MinRpm}~{s.MaxRpm} RPM";\n'
    "\t\t\t\t\treturn;\n"
)
NEW_S = (
    '\t\t\t\t\t_status.Text = "风扇曲线：软件闭环（宝龙达）"\n'
    '\t\t\t\t\t\t+ (s.Enabled ? " · 已启用" : " · 未启用")\n'
    '\t\t\t\t\t\t+ $" · 范围 {s.MinRpm}~{s.MaxRpm} RPM"\n'
    '\t\t\t\t\t\t+ (s.IsUnreadableArchive ? " · " + s.StatusNote : "");\n'
    "\t\t\t\t\tif (_bdEnableChk != null)\n"
    "\t\t\t\t\t{\n"
    "\t\t\t\t\t\t_syncingBdEnable = true;\n"
    "\t\t\t\t\t\t_bdEnableChk.Visible = true;\n"
    "\t\t\t\t\t\t_bdEnableChk.Enabled = !s.IsUnreadableArchive;\n"
    "\t\t\t\t\t\t_bdEnableChk.Checked = s.Enabled;\n"
    "\t\t\t\t\t\t_syncingBdEnable = false;\n"
    "\t\t\t\t\t}\n"
    "\t\t\t\t\treturn;\n"
)
must(fm.count(OLD_S) == 1, "状态行锚点不唯一")
fm = fm.replace(OLD_S, NEW_S)

# 3.4 开关方法（插在 OnCurveUpdated 之后）
OLD_M = (
    '\t\t\tLogger.WriteLine("Fan curve refresh failed: " + ex.GetType().Name + " " + ex.Message);\n'
    "\t\t}\n"
    "\t}\n"
)
NEW_M = OLD_M + (
    "\n"
    "\t// 2026-10-17（接手修复 · 待办 #1）：宝龙达软件闭环的「启用 / 关闭交回固件」开关\n"
    "\t//   勾选 ⇒ 3 s 定时器持续按曲线下发（功能号 20/21）\n"
    "\t//   取消 ⇒ SaveBaolongdaFanCurve(Enabled=false) ⇒ 两通道写 0 + 关手动开关 = 交回固件\n"
    "\tprivate void ToggleBaolongdaCurveEnable(bool enabled)\n"
    "\t{\n"
    "\t\tMechrevoHw hw = Program.hw;\n"
    "\t\tif (hw == null)\n"
    "\t\t{\n"
    "\t\t\treturn;\n"
    "\t\t}\n"
    "\t\ttry\n"
    "\t\t{\n"
    "\t\t\tBaolongdaFanCurveSettings s = hw.LoadBaolongdaFanCurve();\n"
    "\t\t\tbool ok = hw.SaveBaolongdaFanCurve(s.WithEnabled(enabled));\n"
    "\t\t\t_status.Text = enabled\n"
    '\t\t\t\t? (ok ? "风扇曲线：已启用软件闭环（关闭本窗后仍持续生效）" : "风扇曲线：启用失败")\n'
    '\t\t\t\t: (ok ? "风扇曲线：已关闭，交回固件自动控制" : "风扇曲线：关闭失败");\n'
    "\t\t\tRaiseFanCurveSaved(confirmed: ok);\n"
    "\t\t}\n"
    "\t\tcatch (Exception ex)\n"
    "\t\t{\n"
    '\t\t\tLogger.WriteLine("宝龙达风扇曲线开关失败: " + ex.Message);\n'
    '\t\t\t_status.Text = "风扇曲线：开关失败";\n'
    "\t\t}\n"
    "\t}\n"
)
must(fm.count(OLD_M) == 1, "开关方法插入锚点不唯一")
fm = fm.replace(OLD_M, NEW_M)

# 3.5 保存曲线时不再强制启用（尊重开关状态）
OLD_E = "\t\t\t\t\tEnabled = true,\n\t\t\t\t\tHysteresisC = prev.HysteresisC,"
NEW_E = "\t\t\t\t\tEnabled = prev.Enabled,\n\t\t\t\t\tHysteresisC = prev.HysteresisC,"
must(fm.count(OLD_E) == 1, "Enabled 锚点不唯一")
fm = fm.replace(OLD_E, NEW_E)

OLD_K = (
    "\t\t\t\tbool ok = hw.SaveBaolongdaFanCurve(next);\n"
    '\t\t\t\t_status.Text = ok ? ("风扇曲线已保存并启用 · " + DateTime.Now.ToString("HH:mm:ss")) : "风扇曲线保存失败";\n'
)
NEW_K = (
    "\t\t\t\tbool ok = hw.SaveBaolongdaFanCurve(next);\n"
    '\t\t\t\t// 保存不再等于启用（2026-10-17）⇒ 如实说清"存了但没启用"，别让机主以为在跑\n'
    "\t\t\t\t_status.Text = !ok\n"
    '\t\t\t\t\t? "风扇曲线保存失败"\n'
    "\t\t\t\t\t: (next.Enabled\n"
    '\t\t\t\t\t\t? ("风扇曲线已保存并启用 · " + DateTime.Now.ToString("HH:mm:ss"))\n'
    '\t\t\t\t\t\t: ("风扇曲线已保存（未启用：勾选「启用软件闭环」后才会下发）· " + DateTime.Now.ToString("HH:mm:ss")));\n'
)
must(fm.count(OLD_K) == 1, "保存提示锚点不唯一")
fm = fm.replace(OLD_K, NEW_K)

out = fm.replace("\n", "\r\n")
must(out.count("\r\n") == out.count("\n"), "还原 CRLF 后仍有裸 LF")
write(P_FORM, out)
print("[OK] FanCurveForm.cs -> 启用/关闭复选框")
print("DONE")
