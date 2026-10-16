# -*- coding: utf-8 -*-
"""确认 r9 源码包内含本轮要求的条目（带顶层前缀的精确查询）。"""
import sys, zipfile
sys.stdout.reconfigure(encoding="utf-8", errors="replace")
Z = r"D:\openLM\dist\LM-0.291.6-beta22.6-source-20261010-r9.zip"
TOP = "LM-0.291.6-beta22.6-source-20261010-r9/"
NEED = [
    "source/PawnIO/RyzenSmuService.cs",
    "source/MechrevoLite/CpuTuningForm.cs",
    "source/MechrevoLite.Hardware/RuntimeCpuTuning.cs",
    "source/MechrevoLite.UI/UiAuditRunner.cs",
    "source/MechrevoLite.Hardware/CpuTuningOption.cs",
    "source/PawnIO/IntelOcMailbox.cs",
    "source/PawnIO/IntelOcDomain.cs",
    "source/LMechrevo.IntelMSR.bin",
    "source/MechrevoLite.Properties.Strings.resx",
    "source/MechrevoLite.Properties.Strings.zh-CN.resx",
    "source/MechrevoLite.Properties/Strings.cs",
    "source/MechrevoLite/AboutLicensesForm.cs",
    "THIRD-PARTY-NOTICES.md",
    "ATTRIBUTION-PATCH-PLAN.md",
    "installer-src/install_script.iss",
    "installer-src/files/THIRD_PARTY_NOTICES,1.txt",
    "installer-src/files/THIRD_PARTY_NOTICES,2.txt",
    "build.ps1",
    "SOURCE-README.md",
]
BAD = ["bin/", "obj/", "lm_pkg_files/", "diagnostics/", "installer-src/files/GCU/payload/",
       "修改日志", "抓包", "crash", ".dmp"]
z = zipfile.ZipFile(Z)
names = set(z.namelist())
print("总条目 %d" % len(names))
miss = 0
for n in NEED:
    full = TOP + n
    ok = full in names
    if not ok:
        miss += 1
    print("   %-52s %s  %s B" % (n, ok, z.getinfo(full).file_size if ok else "-"))
print("缺失 %d 项" % miss)
print("\n禁入项扫描：")
hit = [n for n in names if any(b in n for b in BAD)]
print("   %d 项 %s" % (len(hit), hit[:10]))
z.close()
