# -*- coding: utf-8 -*-
"""确认 Silent/Balanced 等资源字符串的实际取值，排查'平衡/静音显示互换'"""
import io
import re
import sys

sys.stdout.reconfigure(encoding="utf-8", errors="replace")

FILES = [
    r'D:\openLM\source\MechrevoLite.Properties.Strings.zh-CN.resx',
    r'D:\openLM\source\MechrevoLite.Properties.Strings.resx',
]
KEYS = ['Silent', 'Balanced', 'Turbo', 'SilentTurbo', 'Office', 'Gaming',
        'SilentShort', 'BalancedShort', 'TurboShort']

for f in FILES:
    try:
        t = io.open(f, encoding='utf-8-sig').read()
    except Exception as e:
        print('SKIP %s (%s)' % (f, e))
        continue
    print('===== %s =====' % f.split('\\')[-1])
    for key in KEYS:
        m = re.search(r'<data name="' + re.escape(key) + r'"[^>]*>\s*<value>(.*?)</value>', t, re.S)
        if m:
            print('  %-14s = %s' % (key, m.group(1).strip()))
