# -*- coding: utf-8 -*-
"""2026-10-05 percore-product: 把 19 个新 CpuTune* 键的属性追加进 Strings.cs（文本级追加）。

Strings.cs 是反编译出来的强类型资源类；本仓库的做法是"resx 加键 + Strings.cs 文本追加属性"
（新键用 CpuTuningForm.Localized(key) 走 ResourceManager，属性只是三件套同步的一部分）。
本次只把属性追加在类体最后一个右花括号之前，不动任何既有成员。
"""
import io

CS = r"D:\openLM\source\MechrevoLite.Properties\Strings.cs"

KEYS = [
    "CpuTunePerCoreTitle",
    "CpuTuneCoreLabel",
    "CpuTunePerCoreApply",
    "CpuTuneResetAllCores",
    "CpuTuneSaveSettings",
    "CpuTuneAutoApply",
    "CpuTuneAutoApplyWarning",
    "CpuTuneAutoApplyConfirm",
    "CpuTuneAutoApplyOn",
    "CpuTuneAutoApplyOff",
    "CpuTuneSaved",
    "CpuTuneResetConfirm",
    "CpuTuneResetConfirmTitle",
    "CpuTuneResetDone",
    "CpuTunePerCoreBlocked",
    "CpuTunePerCoreCodenameOnly",
    "CpuTunePerCoreNotCurve",
    "CpuTunePerCoreBaseline",
    "CpuTunePerCoreBaselineUnknown",
]

with io.open(CS, "r", encoding="utf-8", newline="") as handle:
    text = handle.read()

idx = text.rfind("}")
if idx < 0:
    raise SystemExit("FATAL: class closing brace not found")

block = []
for key in KEYS:
    if ('"%s"' % key) in text:
        raise SystemExit("FATAL: property already present: " + key)
    block.append('\tinternal static string %s => ResourceManager.GetString("%s", resourceCulture);\n' % (key, key))

text = text[:idx] + "".join(block) + text[idx:]
with io.open(CS, "w", encoding="utf-8", newline="") as handle:
    handle.write(text)
print("appended %d properties" % len(KEYS))
