# -*- coding: utf-8 -*-
"""
2026-10-17（接手修复 · 0.291.9.8 · 宝龙达风扇曲线）

修三件事（全部是静态可证的缺陷，不需要真机）：
  ① `MechrevoHw` 里 efb652c 那次「回读主体接线」把「宝龙达状态支持清单」整段
     插到了**每一个** `RestoreBaolongdaFanFirmwareControl();` 前面（3 处）⇒
     其中 2 处是误插：`SaveBaolongdaFanCurve`（用户一关曲线就同步跑 14 个功能号
     的只读快照，卡 UI）与 `Dispose`（关机路径白跑一遍 WMI）。只保留启动那处。
  ② `FanCurveDecider`（回差防抖）写好了却**没人用** ⇒ 3 s 定时器每 tick 都拿
     `ResolveRpm` 重算并**无条件重写**两通道（日志每 3 s 一行）。
     现在接上决策器：只在变化时写，另每 30 s 重申一次手动控制权。
  ③ 曲线「启用 / 关闭交回固件」没有界面入口（待办 #1）⇒ 加复选框。

行尾约定（本脚本强制保留，别改成"统一 CRLF"）：
  - MechrevoHw.cs / BaolongdaFanCurveStore.cs：纯 LF
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


def find_lines(lines, needle, what):
    hits = [i for i, ln in enumerate(lines) if needle in ln]
    must(len(hits) == 1, "%s 锚点预期 1 处，实际 %d 处（%s）" % (what, len(hits), needle[:40]))
    return hits[0]


def find_exact(lines, exact, what):
    hits = [i for i, ln in enumerate(lines) if ln == exact]
    must(len(hits) == 1, "%s 锚点（整行）预期 1 处，实际 %d 处" % (what, len(hits)))
    return hits[0]


def find_block(lines, block, what):
    hits = [i for i in range(len(lines) - len(block) + 1) if lines[i:i + len(block)] == block]
    must(len(hits) == 1, "%s 锚点（多行块）预期 1 处，实际 %d 处" % (what, len(hits)))
    return hits[0]


# ══════════════════════════════════════════════════════════════════════
# 1) MechrevoHw.cs（纯 LF）
# ══════════════════════════════════════════════════════════════════════
P_HW = BASE + r"\MechrevoLite.Hardware\MechrevoHw.cs"
hw = read(P_HW)
must("\r\n" not in hw, "MechrevoHw.cs 不是纯 LF —— 先停下，别混行尾")
lines = hw.split("\n")

# ── 1.1 三处"状态清单"误插：只留启动那处 ──────────────────────────────
MARK = '⭐ 启动时把"⭐ 本机哪些功能号是通的"⭐ 打**一次** ✅'
TAIL = "}RestoreBaolongdaFanFirmwareControl();"

markers = [i for i, ln in enumerate(lines) if MARK in ln]
must(len(markers) == 3, "预期 3 处状态清单块，实际 %d" % len(markers))

# 先处理**后面**的，避免前面的删除让索引失效
sites = []
for m in markers:
    end = None
    for j in range(m, min(m + 25, len(lines))):
        if lines[j].strip() == TAIL:
            end = j
            break
    must(end is not None, "找不到块尾（marker 行 %d）" % (m + 1))
    if lines[m - 2].strip() == "if (!settings.Enabled)" and lines[m - 1].strip() == "{":
        site = "A"
        repl = ["\t\t\tRestoreBaolongdaFanFirmwareControl();"]
    elif "否则风扇会**停在我们上次写的转速上**" in lines[m - 1]:
        site = "C"
        repl = ["\t\tRestoreBaolongdaFanFirmwareControl();"]
    elif "本方法**先读再决定**" in lines[m - 1]:
        site = "B"
        repl = ["\t\tRestoreBaolongdaFanFirmwareControl();"]
    else:
        raise SystemExit("无法判定的插入点（marker 行 %d）" % (m + 1))
    sites.append((m, end + 1, site, repl))

for m, end, site, repl in sorted(sites, key=lambda x: x[0], reverse=True):
    print("  移除状态清单块 site=%s（原行 %d..%d）" % (site, m + 1, end))
    lines[m:end] = repl

# ── 1.2 决策器字段 ─────────────────────────────────────────────────
i = find_exact(lines, "\tprivate volatile bool _baolongdaFanCurveApplied;", "决策器字段")
lines[i + 1:i + 1] = [
    "",
    "\t// ⭐⭐ 2026-10-17（接手修复）：两个通道各一个决策器 —— 回差/去重都在这里 ✅",
    "\t//   ⚠️ 此前 `FanCurveDecider` 写好了却**没人调用** ⚠️ ⇒ 3 s 定时器每 tick 都无条件重写",
    "\t//   ⇒ 现在只在目标变化时写，另每 30 s 重申一次手动控制权（⭐ 防固件自己退回自动 ⚠️）",
    "\tprivate readonly FanCurveDecider _baolongdaCurveDecider0 = new FanCurveDecider();",
    "",
    "\tprivate readonly FanCurveDecider _baolongdaCurveDecider1 = new FanCurveDecider();",
    "",
    "\tprivate const int BaolongdaCurveReassertMs = 30000;",
    "",
    "\tprivate long _baolongdaCurveLastWriteTick;",
]

# ── 1.3 一步闭环：ResolveRpm 直算 ⇒ 决策器 + 变化才写 ─────────────────
START = "\t\t\tFanCurvePoint[] channel0Points = FanCurveEngine.BuildPoints(settings.Channel0Temperatures, settings.Channel0Rpms);"
END = '\t\t\t\t+ $" · ⭐ 确认={confirmed} ⚠️（⭐ 通道归属待真机确认 ✅）");'
s = find_exact(lines, START, "闭环起始")
e = find_exact(lines, END, "闭环结尾")
must(e > s and e - s < 40, "闭环块跨度异常")

new_step = [
    "\t\t\tFanCurvePoint[] channel0Points = FanCurveEngine.BuildPoints(settings.Channel0Temperatures, settings.Channel0Rpms);",
    "\t\t\tFanCurvePoint[] channel1Points = FanCurveEngine.BuildPoints(settings.Channel1Temperatures, settings.Channel1Rpms);",
    '\t\t\t// ⚠️ 空曲线 ⇒ **不许写**（⛔ 绝不能把"没有曲线"当成"用最低转速"——那等于瞎转风扇）✅',
    "\t\t\tif (channel0Points.Length == 0 || channel1Points.Length == 0)",
    "\t\t\t{",
    '\t\t\t\tLogger.WriteLineIfChanged("baolongda-fancurve-nopoints",',
    '\t\t\t\t\t"宝龙达风扇曲线：⚠️ 至少一个通道没有可用曲线点 ⇒ ⭐ 本轮不写（⛔ 不拿最低转速顶替 ✅）");',
    "\t\t\t\treturn false;",
    "\t\t\t}",
    "\t\t\t// ⭐⭐ 决策器（回差 3℃）：升温立刻写、降温要过回差、**目标没变就不写** ✅",
    "\t\t\t//   ⚠️ 温度来源的配对沿用改动前：gpuTemp↔ch0 · cpuTemp↔ch1 ✅",
    "\t\t\tint? decided0 = _baolongdaCurveDecider0.Decide(channel0Points, gpuTemp, settings.MinRpm, settings.MaxRpm, settings.HysteresisC);",
    "\t\t\tint? decided1 = _baolongdaCurveDecider1.Decide(channel1Points, cpuTemp, settings.MinRpm, settings.MaxRpm, settings.HysteresisC);",
    "\t\t\tlong nowTick = Environment.TickCount64;",
    "\t\t\tbool reassertDue = (nowTick - _baolongdaCurveLastWriteTick) >= BaolongdaCurveReassertMs;",
    "\t\t\tif (!decided0.HasValue && !decided1.HasValue && !reassertDue)",
    "\t\t\t{",
    "\t\t\t\t// ⭐ 两条通路都没变 ⇒ **本轮一个字节都不写**（⭐ 这是原先缺的那层防抖 ✅）",
    "\t\t\t\treturn false;",
    "\t\t\t}",
    "\t\t\t// ⭐ 没变的那一路用决策器记着的**上次落笔值**（⛔ 不用 ResolveRpm 重算，否则绕过回差 ✅）",
    "\t\t\tint channel0Rpm = decided0 ?? _baolongdaCurveDecider0.LastRpm;",
    "\t\t\tint channel1Rpm = decided1 ?? _baolongdaCurveDecider1.LastRpm;",
    "\t\t\tint channel0Units = FanCurveEngine.ToUnits100Rpm(channel0Rpm);",
    "\t\t\tint channel1Units = FanCurveEngine.ToUnits100Rpm(channel1Rpm);",
    "\t\t\t// ⚠️ 通道顺序必须与 `TrySetFanManualViaBackend(enable, ch0, ch1, ...)` 的约定一致 ✅",
    "\t\t\t//    ⭐ ch0 = `FanType 0` · ⭐ ch1 = `FanType 1` ⚠️（⭐ 语义归属未真机确认 ✅）",
    "\t\t\tif (!TrySetFanManualViaBackend(enable: true, channel0Units, channel1Units, out bool confirmed))",
    "\t\t\t{",
    "\t\t\t\treturn false;",
    "\t\t\t}",
    "\t\t\t_baolongdaCurveLastWriteTick = nowTick;",
    "\t\t\t_baolongdaFanCurveApplied = true;",
    '\t\t\tLogger.WriteLineIfChanged("baolongda-fancurve-step",',
    '\t\t\t\t$"宝龙达风扇曲线（⭐ 软件闭环 · ⭐ 单位 RPM ✅）：⭐ ch0 温度 {gpuTemp}℃ → ⭐ {channel0Rpm} RPM（⭐ 档位 {channel0Units}）"',
    '\t\t\t\t+ $" · ⭐ ch1 温度 {cpuTemp}℃ → ⭐ {channel1Rpm} RPM（⭐ 档位 {channel1Units}）"',
    '\t\t\t\t+ $" · ⭐ 确认={confirmed} · ⭐ 本轮{(reassertDue && !decided0.HasValue && !decided1.HasValue ? "仅重申" : "有变化")}（⭐ 通道归属待真机确认 ⚠️）");',
]
lines[s:e + 1] = new_step

# ── 1.4 保存曲线 ⇒ 决策器清零 ──────────────────────────────────────
OLD_SAVE = [
    "\t\tif (!settings.Enabled)",
    "\t\t{",
    "\t\t\tRestoreBaolongdaFanFirmwareControl();",
    "\t\t}",
    "\t\telse",
    "\t\t{",
    "\t\t\tApplyBaolongdaFanCurveStep();",
    "\t\t}",
]
i = find_block(lines, OLD_SAVE, "保存曲线启用/关闭分支")
NEW_SAVE = [
    "\t\t// ⭐ 曲线被保存（含只改开关）⇒ 决策器清零 ⇒ 下一个 tick **必定重写**",
    '\t\t//   （⛔ 否则会拿旧回差状态压住新曲线，用户会以为"保存了没反应" ⚠️）',
    "\t\t_baolongdaCurveDecider0.Reset();",
    "\t\t_baolongdaCurveDecider1.Reset();",
] + OLD_SAVE
lines[i:i + len(OLD_SAVE)] = NEW_SAVE

# ── 1.5 交回固件 ⇒ 决策器 + 重申计时清零 ──────────────────────────
REST_OLD = [
    "\t\tBaolongdaWmi wmi = _baolongda?.Wmi;",
    "\t\tif (wmi == null)",
    "\t\t{",
    "\t\t\treturn;",
    "\t\t}",
    "\t\ttry",
]
i = find_block(lines, REST_OLD, "RestoreBaolongdaFanFirmwareControl")
NEW_REST = REST_OLD[:-1] + [
    "\t\t// ⭐ 交回固件后**下一次启用必须重写**（⛔ 不能因为\"目标没变\"就跳过首写）✅",
    "\t\t_baolongdaCurveDecider0.Reset();",
    "\t\t_baolongdaCurveDecider1.Reset();",
    "\t\t_baolongdaCurveLastWriteTick = 0;",
    "\t\ttry",
]
lines[i:i + len(REST_OLD)] = NEW_REST

out = "\n".join(lines)
must("\r\n" not in out, "写出前混进了 CRLF")
write(P_HW, out)
print("✅ MechrevoHw.cs 已改（总 %d 行）" % len(lines))

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
print("✅ BaolongdaFanCurveStore.cs 已改（WithEnabled）")

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
    "\t// ⭐ 2026-10-17（接手修复 · 待办 #1）：宝龙达软件闭环的启用开关\n"
    "\t//   ⚠️ 原来保存曲线必然 Enabled=true 且没有关闭入口 ⇒ 机主只能手改 cfg 才能停掉 ⚠️\n"
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
    "\t\t// ⭐ 2026-10-17（接手修复 · 待办 #1）：宝龙达的「启用软件闭环」开关 ✅\n"
    "\t\t//   ⚠️ 宝龙达**没有固件曲线表** ⇒ 曲线只能由我们的程序按温度下发 ⚠️\n"
    "\t\t//   ⇒ 必须有个**关得掉**的入口（关 = 两通道归零 + 关手动开关 = 交回固件）✅\n"
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
    '\t\t\t\t\t\t+ (s.IsUnreadableArchive ? " · ⚠️ " + s.StatusNote : "");\n'
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
    "\t/// 2026-10-17（接手修复 · 待办 #1）：宝龙达软件闭环的「启用 / 关闭交回固件」开关 ✅\n"
    "\t//   ⭐ 勾选 ⇒ 3 s 定时器持续按曲线下发（功能号 20/21）\n"
    "\t//   ⭐ 取消 ⇒ `SaveBaolongdaFanCurve(Enabled=false)` ⇒ 两通道写 0 + 关手动开关 = 交回固件\n"
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
    '\t\t\t\t? (ok ? "风扇曲线：⭐ 已启用软件闭环（关闭本窗后仍持续生效）" : "风扇曲线：⚠️ 启用失败")\n'
    '\t\t\t\t: (ok ? "风扇曲线：⭐ 已关闭 ⇒ 交回固件自动控制" : "风扇曲线：⚠️ 关闭失败");\n'
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

# 3.5 保存曲线时**不再**强制启用（尊重开关状态）
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
    '\t\t\t\t// ⚠️ 保存**不再**等于启用（2026-10-17）⇒ 如实说清"存了但没启用"，⛔ 别让机主以为在跑\n'
    "\t\t\t\t_status.Text = !ok\n"
    '\t\t\t\t\t? "风扇曲线保存失败"\n'
    "\t\t\t\t\t: (next.Enabled\n"
    '\t\t\t\t\t\t? ("风扇曲线已保存并启用 · " + DateTime.Now.ToString("HH:mm:ss"))\n'
    '\t\t\t\t\t\t: ("风扇曲线已保存（⛔ 未启用：勾选「启用软件闭环」后才会下发）· " + DateTime.Now.ToString("HH:mm:ss")));\n'
)
must(fm.count(OLD_K) == 1, "保存提示锚点不唯一")
fm = fm.replace(OLD_K, NEW_K)

out = fm.replace("\n", "\r\n")
must(out.count("\r\n") == out.count("\n"), "还原 CRLF 后仍有裸 LF")
write(P_FORM, out)
print("✅ FanCurveForm.cs 已改（开关 UI）")
print("全部完成")
