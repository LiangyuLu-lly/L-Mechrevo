# -*- coding: utf-8 -*-
"""2026-10-05 percore-product: 把「每核 CO」相关文案**文本级**插进两个 resx（不做全量 XML 重写）。

教训（见修改日志）：
  * 字符串拼接插在 </root> 之后 -> 多根 XML（MSB3103）；
  * minidom 全量重写会破坏内嵌 xs:schema -> NullReference。
所以这里只做两件事：
  1) 在最后一个 </root> 之前插入新的 <data> 块；
  2) 对 CpuTuneScope 的 value 做一次精确文本替换（改总述：现在有「开机自动应用」开关，默认关）。

运行：py D:\\openLM\\tools\\add_percore_resx.py
"""
import io
import os
import sys

EN = r"D:\openLM\source\MechrevoLite.Properties.Strings.resx"
ZH = r"D:\openLM\source\MechrevoLite.Properties.Strings.zh-CN.resx"

NEW_EN = [
    ("CpuTunePerCoreTitle",
     "Per-core Curve Optimizer (desktop Vermeer only)"),
    ("CpuTuneCoreLabel",
     "Core {0}"),
    ("CpuTunePerCoreApply",
     "Apply per core"),
    ("CpuTuneResetAllCores",
     "Reset (write 0 to every core)"),
    ("CpuTuneSaveSettings",
     "Save settings"),
    ("CpuTuneAutoApply",
     "Apply these CO values at startup"),
    ("CpuTuneAutoApplyWarning",
     "Warning: with startup auto-apply on, L-Mechrevo writes the saved per-core CO values to the SMU "
     "every time it starts. The write is real (the SMU state can be read back afterwards) but it is "
     "still lost on reboot. Leave it off unless you are sure."),
    ("CpuTuneAutoApplyConfirm",
     "Turn startup auto-apply on?\n\n"
     "From now on every L-Mechrevo start writes the per-core CO values shown here to the SMU, before "
     "you can intervene. A bad value can freeze or restart the PC.\n\n"
     "Continue?"),
    ("CpuTuneAutoApplyOn",
     "on"),
    ("CpuTuneAutoApplyOff",
     "off"),
    ("CpuTuneSaved",
     "Settings saved. Startup auto-apply: {0}."),
    ("CpuTuneResetConfirm",
     "Reset writes CO 0 to every core ({1} cores) and then sends the all-core CO 0 fallback.\n\n"
     "It clears ALL cores, including any offset that already existed before you opened this window "
     "(read at entry: {0}). The saved per-core values are cleared as well, so startup auto-apply "
     "cannot replay the values you just cleared.\n\n"
     "Continue?"),
    ("CpuTuneResetConfirmTitle",
     "Reset every core to CO 0?"),
    ("CpuTuneResetDone",
     "Reset finished: every core was written to 0 CO (all-core CO 0 fallback sent as well)."),
    ("CpuTunePerCoreBlocked",
     "Per-core CO is unavailable (not writable)"),
    ("CpuTunePerCoreCodenameOnly",
     "This codename only has the all-core CO write; per-core writes are limited to desktop Vermeer "
     "(measured on a Ryzen 7 5700X)."),
    ("CpuTunePerCoreNotCurve",
     "Per-core CO applies to the all-core Curve Optimizer entry only."),
    ("CpuTunePerCoreBaseline",
     "Read at entry (this boot): {0}"),
    ("CpuTunePerCoreBaselineUnknown",
     "unavailable (no per-core readback on this platform)"),
]

NEW_ZH = [
    ("CpuTunePerCoreTitle",
     "每核 Curve Optimizer（仅桌面 Vermeer）"),
    ("CpuTuneCoreLabel",
     "核 {0}"),
    ("CpuTunePerCoreApply",
     "逐核应用"),
    ("CpuTuneResetAllCores",
     "重置（每核写 0）"),
    ("CpuTuneSaveSettings",
     "保存设置"),
    ("CpuTuneAutoApply",
     "开机自动应用这些 CO 值"),
    ("CpuTuneAutoApplyWarning",
     "警告：打开「开机自动应用」后，L-Mechrevo 每次启动都会把保存的每核 CO 值真的写进 SMU。"
     "写入是真实生效的（之后可回读 SMU 当前状态），但仍然只在本次开机有效、重启即由固件重来。"
     "不确定就不要打开。"),
    ("CpuTuneAutoApplyConfirm",
     "确定打开「开机自动应用」？\n\n"
     "以后每次 L-Mechrevo 启动，都会在你来得及干预之前，把这里显示的每核 CO 值自动写进 SMU。"
     "值写错可能导致死机或重启。\n\n继续？"),
    ("CpuTuneAutoApplyOn",
     "已开"),
    ("CpuTuneAutoApplyOff",
     "已关"),
    ("CpuTuneSaved",
     "设置已保存。开机自动应用：{0}。"),
    ("CpuTuneResetConfirm",
     "「重置」会把每个核心（共 {1} 核）的 CO 写成 0，然后再发一次全核 CO 0 兜底。\n\n"
     "它会把所有核心清零 —— 包括你打开这个窗口之前就已经存在的偏移（进入时实读：{0}）。"
     "同时会把已保存的每核值一并清零，避免「开机自动应用」下次启动又把刚清掉的偏移写回来。\n\n继续？"),
    ("CpuTuneResetConfirmTitle",
     "把所有核心的 CO 重置为 0？"),
    ("CpuTuneResetDone",
     "重置已完成：每个核心都写成了 0 CO（并发了一次全核 CO 0 兜底）。"),
    ("CpuTunePerCoreBlocked",
     "每核 CO 不可用（当前不允许写入）"),
    ("CpuTunePerCoreCodenameOnly",
     "该代号只有全核 CO 写路径；每核写只对桌面 Vermeer 开放（已在 Ryzen 7 5700X 上实测）。"),
    ("CpuTunePerCoreNotCurve",
     "每核 CO 只作用于「全核 Curve Optimizer」这一项。"),
    ("CpuTunePerCoreBaseline",
     "进入时实读（本次开机）：{0}"),
    ("CpuTunePerCoreBaselineUnknown",
     "不可回读（该平台没有每核回读）"),
]

SCOPE_OLD_EN = ("Applies to the CPU across all modes for this boot. Changes are manual and are not "
                "reapplied at startup. Intel restore uses the captured baseline; AMD restore requests "
                "CO 0 and exits fixed-clock mode.")
SCOPE_NEW_EN = ("Applies to the CPU across all modes for this boot. The all-core entry stays manual; "
                "the per-core entry has a startup auto-apply switch that is OFF by default (with it on, "
                "the saved per-core CO values are written at every startup). Intel restore uses the "
                "captured baseline; AMD restore requests CO 0, writes every core back to CO 0 on desktop "
                "Vermeer and exits fixed-clock mode.")

SCOPE_OLD_ZH = ("本次开机全局生效，切换性能模式时保留。仅手动应用，不在开机时自动重放。Intel 恢复检测时的原值；"
                "AMD 恢复 CO 0 并退出固定频率模式。")
SCOPE_NEW_ZH = ("本次开机全局生效，切换性能模式时保留。全核那一项仍是手动应用；每核那一项有「开机自动应用」开关，"
                "且默认关（打开后每次启动都会写入保存的每核值）。Intel 恢复检测时的原值；"
                "AMD 恢复 CO 0（桌面 Vermeer 上会把每个核心写回 0 CO）并退出固定频率模式。")


def insert_before_root(text, pairs):
    # 注意：这两个 resx 的结尾是 "\n</root>"（**没有**缩进、没有尾随换行），所以按裸标记找。
    marker = "</root>"
    idx = text.rfind(marker)
    if idx < 0:
        raise SystemExit("FATAL: </root> not found")
    blocks = []
    for name, value in pairs:
        if ('name="%s"' % name) in text:
            raise SystemExit("FATAL: key already present: " + name)
        blocks.append('    <data name="%s" xml:space="preserve">\n      <value>%s</value>\n    </data>\n'
                      % (name, value))
    return text[:idx] + "".join(blocks) + text[idx:]


def patch(path, pairs, old_scope, new_scope):
    with io.open(path, "r", encoding="utf-8", newline="") as handle:
        text = handle.read()
    if old_scope not in text:
        raise SystemExit("FATAL: scope value not found in " + path)
    text = text.replace(old_scope, new_scope, 1)
    text = insert_before_root(text, pairs)
    with io.open(path, "w", encoding="utf-8", newline="") as handle:
        handle.write(text)
    print("patched %s (%d new keys)" % (os.path.basename(path).encode("ascii", "replace").decode(), len(pairs)))


if __name__ == "__main__":
    patch(EN, NEW_EN, SCOPE_OLD_EN, SCOPE_NEW_EN)
    patch(ZH, NEW_ZH, SCOPE_OLD_ZH, SCOPE_NEW_ZH)
    sys.exit(0)
