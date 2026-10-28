# -*- coding: utf-8 -*-
"""Compare the current source files with the pre-change backups (hash table for the log/report)."""
import hashlib
import io
import os

PAIRS = [
    (r"D:\openLM\backup\20261005_2700_pre_percore\RyzenSmuService.cs", r"D:\openLM\source\PawnIO\RyzenSmuService.cs"),
    (r"D:\openLM\backup\20261005_2700_pre_percore\RuntimeCpuTuning.cs", r"D:\openLM\source\MechrevoLite.Hardware\RuntimeCpuTuning.cs"),
    (r"D:\openLM\backup\20261005_2700_pre_percore\CpuTuningOption.cs", r"D:\openLM\source\MechrevoLite.Hardware\CpuTuningOption.cs"),
    (r"D:\openLM\backup\20261005_2700_pre_percore\CpuTuningForm.cs", r"D:\openLM\source\MechrevoLite\CpuTuningForm.cs"),
    (r"D:\openLM\backup\20261005_2700_pre_percore\ModelScopedConfig.cs", r"D:\openLM\source\MechrevoLite\ModelScopedConfig.cs"),
    (r"D:\openLM\backup\20261005_2700_pre_percore\Program.cs", r"D:\openLM\source\MechrevoLite\Program.cs"),
    (r"D:\openLM\backup\20261005_2700_pre_percore\properties\Strings.cs", r"D:\openLM\source\MechrevoLite.Properties\Strings.cs"),
    (r"D:\openLM\backup\20261005_2700_pre_percore\properties\MechrevoLite.Properties.Strings.resx", r"D:\openLM\source\MechrevoLite.Properties.Strings.resx"),
    (r"D:\openLM\backup\20261005_2700_pre_percore\properties\MechrevoLite.Properties.Strings.zh-CN.resx", r"D:\openLM\source\MechrevoLite.Properties.Strings.zh-CN.resx"),
    (r"D:\openLM\backup\20261005_2700_pre_percore\smu-co-tool\CoreWriteRunner.cs", r"D:\openLM\tools\smu-co-tool\src\CoreWriteRunner.cs"),
    (r"D:\openLM\backup\20261005_2700_pre_percore\uvshot\Program.cs", r"D:\openLM\tools\uvshot\Program.cs"),
]


def sha(path):
    with io.open(path, "rb") as handle:
        return hashlib.sha256(handle.read()).hexdigest()


lines = []
for before, after in PAIRS:
    hb = sha(before)
    ha = sha(after)
    lines.append("%s\n   before %s\n   after  %s\n   %s\n"
                 % (os.path.basename(after), hb, ha, "CHANGED" if hb != ha else "UNCHANGED"))

extra = [
    r"D:\openLM\source\MechrevoLite.UI\UiAuditRunner.cs",
    r"D:\openLM\tools\smu-co-tool\src\ReadProbeRunner.cs",
]
for path in extra:
    lines.append("%s\n   after  %s\n   (no pre-change copy taken / not modified by decision)\n" % (os.path.basename(path), sha(path)))

out = r"D:\openLM\backup\20261005_2700_pre_percore\post-change-hashes.txt"
with io.open(out, "w", encoding="utf-8", newline="") as handle:
    handle.write("".join(lines))
print("".join(lines))
print("written to " + out)
