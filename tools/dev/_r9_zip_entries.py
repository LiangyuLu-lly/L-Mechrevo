# -*- coding: utf-8 -*-
"""看 r8 源码包里都有哪些“非 source”条目（决定 r9 的条目集合）。"""
import sys, zipfile, os
sys.stdout.reconfigure(encoding="utf-8", errors="replace")
Z8 = r"D:\openLM\dist\LM-0.291.6-beta22.6-source-20261010-r8.zip"
TOP8 = "LM-0.291.6-beta22.6-source-20261010-r8/"
z = zipfile.ZipFile(Z8)
files = [n[len(TOP8):] for n in z.namelist() if n.startswith(TOP8) and not n.endswith("/")]
z.close()
other = sorted(f for f in files if not f.lower().startswith("source/"))
print("r8 文件总数 =", len(files), " 其中非 source =", len(other))
for f in other:
    p = os.path.join(r"D:\openLM", f.replace("/", "\\"))
    print("   %-60s disk=%s" % (f, ("%d B" % os.path.getsize(p)) if os.path.isfile(p) else "MISSING"))
print("\ninstaller-src/files 下的条目:")
for f in sorted(x for x in files if x.lower().startswith("installer-src/files")):
    print("   " + f)
