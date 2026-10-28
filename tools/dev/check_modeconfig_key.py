# -*- coding: utf-8 -*-
"""查 ModeConfigShort 的用法与同名中文键"""
import io
import re
import sys
import glob

sys.stdout.reconfigure(encoding="utf-8", errors="replace")

SRC = r'D:\openLM\source'
EN = r'D:\openLM\source\MechrevoLite.Properties.Strings.resx'
ZH = r'D:\openLM\source\MechrevoLite.Properties.Strings.zh-CN.resx'

print("===== code usages of ModeConfigShort =====")
for f in glob.glob(SRC + r'\**\*.cs', recursive=True):
    try:
        lines = io.open(f, encoding='utf-8-sig', errors='replace').read().split('\n')
    except Exception:
        continue
    for i, l in enumerate(lines):
        if 'ModeConfigShort' in l:
            print("  %s:%d  %s" % (f.split('\\')[-1], i + 1, l.strip()))

print("\n===== keys with value configured in zh-CN (line 161 / 4432) =====")
zh = io.open(ZH, encoding='utf-8-sig', errors='replace').read().split('\n')
for n in (161, 4432):
    for j in range(max(0, n - 3), min(len(zh), n + 1)):
        print("  zh-CN %5d|%s" % (j + 1, zh[j].rstrip('\r')))

print("\n===== corresponding en keys (4438-4460) =====")
en = io.open(EN, encoding='utf-8-sig', errors='replace').read().split('\n')
for j in range(4437, min(len(en), 4460)):
    print("  en    %5d|%s" % (j + 1, en[j].rstrip('\r')))

print("\n===== all data-name that equal Config / 配置 in en resx =====")
t = io.open(EN, encoding='utf-8-sig', errors='replace').read()
for m in re.finditer(r'<data name="([^"]+)"[^>]*>\s*<value>([^<]*)</value>', t):
    if m.group(2).strip() in ('Config', 'Cfg', 'Configuration'):
        print("  en  %-34s = %s" % (m.group(1), m.group(2)))
