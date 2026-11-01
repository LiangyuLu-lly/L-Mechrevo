# -*- coding: utf-8 -*-
"""2026-10-05 uifix（用户四条反馈）: 改文案 + 删掉并进底部按钮排的旧键。

文本级操作（与前几轮 add_percore_resx.py 同一套做法：只碰 <data> 块，不做全量 XML 重写）：
  1) 用正则替换指定键的整个 <data> 块（值里含换行也安全）；
  2) 删除已经没有任何代码引用的 4 个键；
  3) 新增 4 个键（保底机制状态行 / 提示 / tooltip、自动应用警告的详细 tooltip）。

同时同步 Strings.cs（强类型属性）：新键追加属性，被删的键去掉属性。

运行：py D:\\openLM\\tools\\uifix_resx.py
"""
import io
import os
import re
import sys

EN = r"D:\openLM\source\MechrevoLite.Properties.Strings.resx"
ZH = r"D:\openLM\source\MechrevoLite.Properties.Strings.zh-CN.resx"
CS = r"D:\openLM\source\MechrevoLite.Properties\Strings.cs"

# ---------------------------------------------------------------- 变更后的值
CHANGED_EN = {
    "CpuTuneResetAllCores": "Reset",
    "CpuTuneScope": (
        "Applies to the CPU across all modes for this boot. Apply writes and saves the current values; "
        "Reset zeroes the CO of every core and exits fixed-clock mode."),
    "CpuTuneAutoApplyWarning": (
        "With this on, every startup really writes these CO values (not a read-only check); it lasts for "
        "this boot only and the firmware starts over after a reboot. Leave it off unless you are sure."),
    "CpuTuneAutoApplyConfirm": (
        "Turn startup auto-apply on?\n\n"
        "Every L-Mechrevo start would really write these values into the SMU (not a read-only check); a bad "
        "value can freeze or restart the PC.\n\n"
        "Will write: {0}\n"
        "Current baseline: {1}\n\n"
        "It lasts for this boot only, the firmware starts over after a reboot. Continue?"),
    "CpuTuneResetConfirm": (
        "Reset writes 0 to every core and then sends one all-core CO 0 fallback, and it exits fixed-clock "
        "mode.\n\n"
        "It clears every core, including offsets that already existed before you opened this window, plus the "
        "saved per-core values (so startup auto-apply cannot replay what you just cleared).\n\n"
        "Will write: {0}\n"
        "Current baseline: {1}\n\n"
        "Continue?"),
    "CpuTuneResetConfirmTitle": "Reset every core to CO 0?",
    "CpuTuneResetDone": "Reset finished: every core was written to CO 0 and fixed-clock mode was exited.",
}

CHANGED_ZH = {
    "CpuTuneResetAllCores": "重置",
    "CpuTuneScope": (
        "本次开机全局生效，切换性能模式时保留。「应用」写入并保存当前值；"
        "「重置」把所有核心 CO 清零并退出固定频率。"),
    "CpuTuneAutoApplyWarning": (
        "打开后每次开机都会真的写入这些 CO 值（不是只读）；只对本次开机有效，重启由固件重来。"
        "不确定就不要打开。"),
    "CpuTuneAutoApplyConfirm": (
        "确定打开「开机自动应用」？\n\n"
        "以后每次 L-Mechrevo 启动，都会在你来得及干预之前，把这些值真的写进 SMU（不是只读）。"
        "值不合适可能导致死机或重启。\n\n"
        "将写入：{0}\n"
        "当前基线：{1}\n\n"
        "只对本次开机有效，重启由固件重来。继续？"),
    "CpuTuneResetConfirm": (
        "「重置」把每个核心写成 0，再补发一次全核 CO 0 兜底，并退出固定频率。\n\n"
        "它会把所有核心清零 —— 包括你打开这个窗口之前就有的偏移，以及已保存的每核值"
        "（免得「开机自动应用」下次又把刚清掉的偏移写回来）。\n\n"
        "将写入：{0}\n"
        "当前基线：{1}\n\n"
        "继续？"),
    "CpuTuneResetConfirmTitle": "把所有核心的 CO 重置为 0？",
    "CpuTuneResetDone": "重置完成：各核心 CO 已写 0，固定频率已退出。",
}

ADDED_EN = [
    ("CpuTuneAutoApplyWarningTip",
     "Details: the write is a real SMU command and the SMU state can be read back afterwards; a bad offset can "
     "freeze or restart the PC. The switch only does something on desktop Vermeer (the per-core write path "
     "measured on a Ryzen 7 5700X); elsewhere the switch and the buttons stay disabled. Turning it on saves "
     "the values currently in the boxes, and those saved values are what a startup replays."),
    ("CpuTuneGuardSkipped",
     "The last shutdown was not clean, so startup auto-apply was skipped this boot. Switch: Custom mode -> CPU tuning."),
    ("CpuTuneGuardSkippedTip",
     "Windows saw no clean exit last time (crash, power loss or a forced kill), so this start did not write the "
     "saved per-core CO values into the SMU - a possibly unstable curve is not re-applied. To turn the feature "
     "off: Custom mode -> CPU tuning -> clear \"Apply these CO values at startup\". The line disappears after one "
     "clean shutdown."),
    ("CpuTuneGuardSkipToast", "Unclean shutdown: CO not auto-applied"),
]

ADDED_ZH = [
    ("CpuTuneAutoApplyWarningTip",
     "细节：写入是真实的 SMU 命令，之后可以回读 SMU 当前状态；偏移不合适可能死机或重启。"
     "这个开关只在桌面 Vermeer 上有意义（每核写路径已在 Ryzen 7 5700X 上实测），"
     "其它机器上开关与按钮保持置灰。设为「开」时会把输入框里当前的值一并保存，开机重放的就是这一份。"),
    ("CpuTuneGuardSkipped",
     "上次非正常关机，本次未自动应用。开关：自定义模式 → CPU 调节。"),
    ("CpuTuneGuardSkippedTip",
     "上次没有正常退出或关机（崩溃、断电、被任务管理器强制结束等），所以这次启动没有把保存的每核 CO 值写进 SMU "
     "—— 避免把可能不稳定的偏移再写一遍。想关掉它：自定义模式 → CPU 调节 → 取消「开机自动应用这些 CO 值」。"
     "正常关机一次以后，这行提示就不会再出现。"),
    ("CpuTuneGuardSkipToast", "上次非正常关机，未自动应用 CO"),
]

# 已经没有任何代码引用的旧键（「逐核应用」并进「应用」、「保存设置」并进「应用」、
# 「恢复」改名并合并成「重置」，成功文案统一用 CpuTuneResetDone）。
REMOVED = ["CpuTunePerCoreApply", "CpuTuneSaveSettings", "CpuTuneRestore", "CpuTuneRestored"]

BLOCK = '<data name="%s" xml:space="preserve">\n    <value>%s</value>\n  </data>'


def replace_value(text, key, value):
    pattern = re.compile(r'<data name="%s" xml:space="preserve">.*?</data>' % re.escape(key), re.S)
    match = pattern.search(text)
    if not match:
        raise SystemExit("FATAL: key not found: " + key)
    return text[:match.start()] + (BLOCK % (key, value)) + text[match.end():]


def remove_key(text, key):
    pattern = re.compile(r'[ \t]*<data name="%s" xml:space="preserve">.*?</data>\n' % re.escape(key), re.S)
    match = pattern.search(text)
    if not match:
        raise SystemExit("FATAL: key to remove not found: " + key)
    return text[:match.start()] + text[match.end():]


def add_key(text, key, value):
    if ('name="%s"' % key) in text:
        raise SystemExit("FATAL: key already present: " + key)
    idx = text.rfind("</root>")
    if idx < 0:
        raise SystemExit("FATAL: </root> not found")
    return text[:idx] + ("    " + BLOCK % (key, value) + "\n") + text[idx:]


def patch(path, changed, added, removed):
    with io.open(path, "r", encoding="utf-8", newline="") as handle:
        text = handle.read()
    for key, value in changed.items():
        text = replace_value(text, key, value)
    for key in removed:
        text = remove_key(text, key)
    for key, value in added:
        text = add_key(text, key, value)
    with io.open(path, "w", encoding="utf-8", newline="") as handle:
        handle.write(text)
    print("patched %s: changed=%d removed=%d added=%d"
          % (os.path.basename(path).encode("ascii", "replace").decode(), len(changed), len(removed), len(added)))


def patch_strings_cs():
    with io.open(CS, "r", encoding="utf-8", newline="") as handle:
        text = handle.read()
    removed = 0
    for key in REMOVED:
        line = '\tinternal static string %s => ResourceManager.GetString("%s", resourceCulture);\n' % (key, key)
        if line not in text:
            # CpuTuneRestore / CpuTuneRestored 从来就没有强类型属性（一直走 Localized(key)）。
            print("skip (no property): " + key)
            continue
        text = text.replace(line, "", 1)
        removed += 1
    idx = text.rfind("}")
    if idx < 0:
        raise SystemExit("FATAL: Strings.cs closing brace not found")
    block = []
    for key, _ in ADDED_EN:
        if ('"%s"' % key) in text:
            print("skip (property present): " + key)
            continue
        block.append('\tinternal static string %s => ResourceManager.GetString("%s", resourceCulture);\n' % (key, key))
    text = text[:idx] + "".join(block) + text[idx:]
    with io.open(CS, "w", encoding="utf-8", newline="") as handle:
        handle.write(text)
    print("patched Strings.cs: removed=%d added=%d" % (removed, len(block)))


if __name__ == "__main__":
    if len(sys.argv) > 1 and sys.argv[1] == "cs-only":
        patch_strings_cs()
        sys.exit(0)
    patch(EN, CHANGED_EN, ADDED_EN, REMOVED)
    patch(ZH, CHANGED_ZH, ADDED_ZH, REMOVED)
    patch_strings_cs()
    sys.exit(0)
