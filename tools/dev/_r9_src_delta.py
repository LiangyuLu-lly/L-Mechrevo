# -*- coding: utf-8 -*-
"""廉价佐证：本轮 4 个源文件的规模变化 + 关键词计数（r8 包内 vs 磁盘）。"""
import sys, io, re, zipfile
sys.stdout.reconfigure(encoding="utf-8", errors="replace")
Z8 = r"D:\openLM\dist\LM-0.291.6-beta22.6-source-20261010-r8.zip"
TOP8 = "LM-0.291.6-beta22.6-source-20261010-r8/"
TARGETS = [
    "source/PawnIO/RyzenSmuService.cs",
    "source/MechrevoLite/CpuTuningForm.cs",
    "source/MechrevoLite.Hardware/RuntimeCpuTuning.cs",
    "source/MechrevoLite.UI/UiAuditRunner.cs",
    "source/MechrevoLite.Hardware/CpuTuningOption.cs",
]
PATTERNS = ["SetPsmMarginSingleCore", "PerCoreWrite", "perCoreWrite", "CpuTuneFreqNote",
            "FixedAllCore", "AllCoreCo"]
z = zipfile.ZipFile(Z8)
for rel in TARGETS:
    a = z.read(TOP8 + rel)
    b = io.open(r"D:\openLM\\" + rel.replace("/", "\\"), "rb").read()
    print("== %s\n   r8 %d B -> 磁盘 %d B (+%d)" % (rel, len(a), len(b), len(b) - len(a)))
    at, bt = a.decode("utf-8", "replace"), b.decode("utf-8", "replace")
    for p in PATTERNS:
        ca, cb = at.count(p), bt.count(p)
        if ca or cb:
            print("     %-24s r8=%-3d 磁盘=%-3d" % (p, ca, cb))
z.close()
