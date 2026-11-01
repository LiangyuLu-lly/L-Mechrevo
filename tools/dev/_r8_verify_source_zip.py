# -*- coding: utf-8 -*-
"""r8 源码包核对：逐字节 == 磁盘；含本轮改动；不含禁用物"""
import sys, os, zipfile, hashlib
sys.stdout.reconfigure(encoding="utf-8", errors="replace")

ROOT = r"D:\openLM"
Z = os.path.join(ROOT, "dist", "LM-0.291.6-beta22.6-source-20261010-r8.zip")
TOP = "LM-0.291.6-beta22.6-source-20261010-r8/"
GEN = {"build.ps1", "SOURCE-README.md"}

same = 0
missing = []
diff = []
names = []
zf = zipfile.ZipFile(Z)
for n in zf.namelist():
    if n.endswith("/"):
        continue
    rel = n[len(TOP):].replace("/", "\\")
    names.append(rel)
    if rel in GEN:
        continue
    p = os.path.join(ROOT, rel)
    if not os.path.isfile(p):
        missing.append(rel); continue
    if zf.read(n) == open(p, "rb").read():
        same += 1
    else:
        diff.append(rel)
print("逐字节相同(除生成件) :", same)
print("磁盘无此文件        :", missing)
print("内容不同            :", diff)

MUST = [
    r"source\PawnIO\IntelOcMailbox.cs",
    r"source\PawnIO\IntelOcDomain.cs",
    r"source\LMechrevo.IntelMSR.bin",
    r"source\L-Mechrevo.csproj",
    r"source\MechrevoLite\AboutLicensesForm.cs",
    r"source\MechrevoLite.Properties.Strings.resx",
    r"source\MechrevoLite.Properties.Strings.zh-CN.resx",
    r"source\MechrevoLite.Properties\Strings.cs",
    r"THIRD-PARTY-NOTICES.md",
    r"ATTRIBUTION-PATCH-PLAN.md",
]
print("\n=== 本轮改动是否在包内 ===")
for m in MUST:
    print("   %-52s %s" % (m, "IN" if m in names else "*** MISSING ***"))

print("\n=== 包内 csproj 是否含 IntelMSR EmbeddedResource ===")
t = zf.read(TOP + "source/L-Mechrevo.csproj").decode("utf-8", "replace")
for k in ("LMechrevo.IntelMSR.bin", "EmbeddedResource"):
    print("   %-26s %s" % (k, k in t))
for line in t.splitlines():
    if "IntelMSR" in line:
        print("   |", line.strip())

print("\n=== 包内 IntelMSR.bin 哈希 ===")
print("   ", hashlib.sha256(zf.read(TOP + "source/LMechrevo.IntelMSR.bin")).hexdigest().upper())

print("\n=== 包内新 resx 键 ===")
for f in ("source/MechrevoLite.Properties.Strings.resx", "source/MechrevoLite.Properties.Strings.zh-CN.resx"):
    s = zf.read(TOP + f).decode("utf-8", "replace")
    print("   %-56s AboutLicensesUsePawnIOIntelMsr=%s" % (f, "AboutLicensesUsePawnIOIntelMsr" in s))
print("   Strings.cs 含该键:", "AboutLicensesUsePawnIOIntelMsr" in zf.read(TOP + "source/MechrevoLite.Properties/Strings.cs").decode("utf-8", "replace"))
md = zf.read(TOP + "THIRD-PARTY-NOTICES.md").decode("utf-8", "replace")
print("   THIRD-PARTY-NOTICES.md 含 IntelMSR / §1.2 / §1.3:",
      ("IntelMSR" in md), ("### 1.2" in md), ("### 1.3" in md), "SMUDebugTool" in md)

print("\n=== build.ps1 / SOURCE-README.md ===")
b = zf.read(TOP + "build.ps1").decode("utf-8", "replace")
print("   build.ps1 含当前 DLL 哈希 771EF2A4:", "771EF2A4" in b, "| r8:", "revision r8" in b, "| 卫星哈希 2D2CF581:", "2D2CF581" in b)
r = zf.read(TOP + "SOURCE-README.md").decode("utf-8", "replace")
print("   SOURCE-README r8:", "(r8)" in r or "revision r8" in r, "| 提到 IntelMSR.bin:", "LMechrevo.IntelMSR.bin" in r)

print("\n=== 禁用物检查（应全部 False） ===")
for pat, desc in ((r"GCU\payload", "厂商 payload"), ("lm_pkg_files", "参考 DLL 目录"),
                  ("diagnostics", "diagnostics"), ("/bin/", "构建输出"), ("/obj/", "构建中间产物"),
                  (".log", "日志"), ("config.json", "用户配置"), ("crash", "崩溃转储"),
                  ("抓包", "抓包")):
    hit = [x for x in names if pat.lower().lstrip("\\") in x.lower()]
    print("   %-14s %-6s %s" % (desc, bool(hit), hit[:3]))
zf.close()
