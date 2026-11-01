# -*- coding: utf-8 -*-
"""r7 源码包逐条与磁盘比对（build.ps1 / SOURCE-README.md 为包内独有，预期例外）"""
import sys, os, zipfile, hashlib
sys.stdout.reconfigure(encoding="utf-8", errors="replace")
zip_path = r"D:\openLM\dist\LM-0.291.6-beta22.6-source-20261010-r7.zip"
top = "LM-0.291.6-beta22.6-source-20261010-r7"
root = r"D:\openLM"
same = 0
missing_on_disk = []
diff = []
for name in zipfile.ZipFile(zip_path).namelist():
    if name.endswith("/"):
        continue
    rel = name[len(top) + 1:]
    if rel.startswith("source/"):
        disk = os.path.join(root, rel.replace("/", "\\"))
    elif rel.startswith("installer-src/"):
        disk = os.path.join(root, rel.replace("/", "\\"))
    else:
        disk = os.path.join(root, rel.replace("/", "\\"))
    if not os.path.isfile(disk):
        missing_on_disk.append(rel)
        continue
    a = zipfile.ZipFile(zip_path).read(name)
    b = open(disk, "rb").read()
    if a == b:
        same += 1
    else:
        diff.append(rel)
print("逐字节相同:", same)
print("磁盘无此文件(包内独有):", missing_on_disk)
print("内容不同:", diff)
