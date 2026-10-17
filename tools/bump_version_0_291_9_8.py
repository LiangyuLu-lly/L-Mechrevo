# -*- coding: utf-8 -*-
"""0.291.9.7 -> 0.291.9.8 版本串升位（**字节级**替换，不碰行尾）。

⚠️ `installer-src\\install_script.iss` 是 **CRLF + 8 个裸 LF** 的混合文件
（handoff 里点过名）⇒ 这里只做字节替换，⛔ 绝不解码/重写换行。
"""
import sys

sys.stdout.reconfigure(encoding="utf-8", errors="replace")

PAIRS = [(b"0.291.9.7", b"0.291.9.8"), (b"beta22.9.7", b"beta22.9.8")]
FILES = [r"D:\openLM\source\Properties\AssemblyInfo.cs",
         r"D:\openLM\installer-src\install_script.iss"]

total = 0
for p in FILES:
    with open(p, "rb") as f:
        data = f.read()
    before = data
    n = 0
    for old, new in PAIRS:
        c = data.count(old)
        n += c
        data = data.replace(old, new)
    if data == before:
        raise SystemExit("没有发生替换: " + p)
    with open(p, "wb") as f:
        f.write(data)
    print("%-58s 替换 %d 处" % (p.split("\\")[-1], n))
    total += n

# 自检：旧串必须清零
for p in FILES:
    with open(p, "rb") as f:
        left = f.read()
    for old, _ in PAIRS:
        if old in left:
            raise SystemExit("旧串残留: %r 在 %s" % (old, p))

print("✅ 共 %d 处，旧串残留 0" % total)
