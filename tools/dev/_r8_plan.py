# -*- coding: utf-8 -*-
"""推导 r7 源码包的文件选取规则，并列出 r8 相对 r7 的新增/缺失（只读，不写包）"""
import sys, os, zipfile, collections
sys.stdout.reconfigure(encoding="utf-8", errors="replace")

Z7 = r"D:\openLM\dist\LM-0.291.6-beta22.6-source-20261010-r7.zip"
TOP7 = "LM-0.291.6-beta22.6-source-20261010-r7/"
ROOT = r"D:\openLM"

z = zipfile.ZipFile(Z7)
r7 = set()
for n in z.namelist():
    if n.endswith("/"):
        continue
    r7.add(n[len(TOP7):].replace("/", "\\"))
z.close()

r7_src = sorted(x for x in r7 if x.lower().startswith("source\\"))
exts = collections.Counter(os.path.splitext(x)[1].lower() for x in r7_src)
print("r7 source 文件数 =", len(r7_src))
print("r7 source 扩展名分布:", dict(exts))

ALLOW_EXT = set(exts)

# 磁盘上按同样规则应纳入的文件（排除 bin/obj）
disk_src = []
for dp, dn, fn in os.walk(os.path.join(ROOT, "source")):
    low = dp.lower()
    if "\\bin" in low or "\\obj" in low:
        continue
    dn[:] = [d for d in dn if d.lower() not in ("bin", "obj")]
    for f in fn:
        if os.path.splitext(f)[1].lower() in ALLOW_EXT:
            full = os.path.join(dp, f)
            disk_src.append(os.path.relpath(full, ROOT))
disk_src = sorted(disk_src)
print("磁盘同规则 source 文件数 =", len(disk_src))

s7 = set(x.lower() for x in r7_src)
sd = set(x.lower() for x in disk_src)
new = sorted(x for x in disk_src if x.lower() not in s7)
gone = sorted(x for x in r7_src if x.lower() not in sd)
print("\n=== r8 新增（磁盘有 / r7 无）: %d ===" % len(new))
for x in new:
    print("   + %s  (%d B)" % (x, os.path.getsize(os.path.join(ROOT, x))))
print("\n=== r7 有 / 磁盘无: %d ===" % len(gone))
for x in gone:
    print("   - %s" % x)

# 非 source 部分
other7 = sorted(x for x in r7 if not x.lower().startswith("source\\"))
print("\n=== r7 非 source 条目: %d ===" % len(other7))
for x in other7:
    disk = os.path.join(ROOT, x)
    print("   %-46s disk=%s" % (x, ("%d B" % os.path.getsize(disk)) if os.path.isfile(disk) else "MISSING"))
