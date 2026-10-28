# -*- coding: utf-8 -*-
"""定位「未自动重启系统 / 重启后才会生效」这条提示的资源键与调用点"""
import io
import re
import sys
import glob

sys.stdout.reconfigure(encoding="utf-8", errors="replace")

PATTERNS = ["未自动重启", "重启后才会生效", "5 秒后重启", "秒后重启"]

resx_files = []
for pat in (r'D:\openLM\source\MechrevoLite.Properties.Strings*.resx',
            r'D:\openLM\source\MechrevoLite.Properties\Strings*.resx'):
    resx_files += glob.glob(pat)

keys = set()
for f in resx_files:
    t = io.open(f, encoding='utf-8-sig', errors='replace').read()
    for m in re.finditer(r'<data name="([^"]+)"[^>]*>\s*<value>(.*?)</value>', t, re.S):
        v = m.group(2)
        if any(p in v for p in PATTERNS):
            keys.add(m.group(1))
            print("%-30s = %s" % (m.group(1), v.strip().replace("\n", " ")[:90]))
print("KEYS =", sorted(keys))

# 在源码里找这些键的使用点
src_files = glob.glob(r'D:\openLM\source\**\*.cs', recursive=True)
for k in sorted(keys):
    print("---- 使用点: Strings.%s ----" % k)
    for f in src_files:
        if f.endswith('.group_backup'):
            continue
        try:
            lines = io.open(f, encoding='utf-8-sig', errors='replace').read().split('\n')
        except Exception:
            continue
        for i, l in enumerate(lines):
            if ('Strings.' + k) in l:
                print("  %s:%d  %s" % (f.split('\\')[-1], i + 1, l.strip()[:150]))
