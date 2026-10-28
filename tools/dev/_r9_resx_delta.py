# -*- coding: utf-8 -*-
"""列出本轮（r8 源码包 -> 当前磁盘）Strings.resx / Strings.zh-CN.resx 的新增与改动键。"""
import sys, re, zipfile, io
sys.stdout.reconfigure(encoding="utf-8", errors="replace")

Z8 = r"D:\openLM\dist\LM-0.291.6-beta22.6-source-20261010-r8.zip"
TOP8 = "LM-0.291.6-beta22.6-source-20261010-r8/"
PAIRS = [
    (TOP8 + "source/MechrevoLite.Properties.Strings.resx",
     r"D:\openLM\source\MechrevoLite.Properties.Strings.resx"),
    (TOP8 + "source/MechrevoLite.Properties.Strings.zh-CN.resx",
     r"D:\openLM\source\MechrevoLite.Properties.Strings.zh-CN.resx"),
    (TOP8 + "source/MechrevoLite.Properties/Strings.cs",
     r"D:\openLM\source\MechrevoLite.Properties\Strings.cs"),
    (TOP8 + "source/MechrevoLite/CpuTuningForm.cs",
     r"D:\openLM\source\MechrevoLite\CpuTuningForm.cs"),
    (TOP8 + "source/PawnIO/RyzenSmuService.cs",
     r"D:\openLM\source\PawnIO\RyzenSmuService.cs"),
    (TOP8 + "source/MechrevoLite.Hardware/RuntimeCpuTuning.cs",
     r"D:\openLM\source\MechrevoLite.Hardware\RuntimeCpuTuning.cs"),
    (TOP8 + "source/MechrevoLite.UI/UiAuditRunner.cs",
     r"D:\openLM\source\MechrevoLite.UI\UiAuditRunner.cs"),
    (TOP8 + "THIRD-PARTY-NOTICES.md", r"D:\openLM\THIRD-PARTY-NOTICES.md"),
    (TOP8 + "ATTRIBUTION-PATCH-PLAN.md", r"D:\openLM\ATTRIBUTION-PATCH-PLAN.md"),
    (TOP8 + "source/MechrevoLite/AboutLicensesForm.cs",
     r"D:\openLM\source\MechrevoLite\AboutLicensesForm.cs"),
    (TOP8 + "installer-src/install_script.iss",
     r"D:\openLM\installer-src\install_script.iss"),
    (TOP8 + "source/MechrevoLite.Hardware/CpuTuningOption.cs",
     r"D:\openLM\source\MechrevoLite.Hardware\CpuTuningOption.cs"),
]


def keys(t):
    out = {}
    for m in re.finditer(r'<data name="([^"]+)"[^>]*>\s*<value>(.*?)</value>', t, re.S):
        out[m.group(1)] = m.group(2)
    return out


z = zipfile.ZipFile(Z8)
have = set(z.namelist())
for zn, disk in PAIRS:
    name = zn.split("/")[-1]
    if zn not in have:
        print("== %-36s 不在 r8 包内（本身为 r8 之后新增）" % name)
        continue
    a = z.read(zn).decode("utf-8-sig", "replace")
    b = io.open(disk, encoding="utf-8-sig", errors="replace").read()
    if a == b:
        print("== %-36s 与 r8 相同" % name)
        continue
    if name.endswith(".resx"):
        ka, kb = keys(a), keys(b)
        add = [k for k in kb if k not in ka]
        chg = [k for k in kb if k in ka and ka[k] != kb[k]]
        print("== %s  新增 %d 键 / 改动 %d 键" % (name, len(add), len(chg)))
        for k in add:
            print("   + %s" % k)
            print("       %s" % kb[k].replace("\n", " ")[:170])
        for k in chg:
            print("   ~ %s" % k)
            print("       OLD %s" % ka[k].replace("\n", " ")[:150])
            print("       NEW %s" % kb[k].replace("\n", " ")[:150])
    else:
        print("== %s  内容不同（r8 %d B -> 现在 %d B）" % (name, len(a.encode()), len(b.encode())))
z.close()
