# -*- coding: utf-8 -*-
"""加 NoticeCallback 字段到 ToastForm"""
import re
p = r'D:\openLM\source\MechrevoLite.Helpers\ToastForm.cs'
t = open(p, encoding='utf-8', errors='replace').read()
# 找 FailureCallback 声明行
m = re.search(r'^\s*(?:public|internal|private|protected)\s+static\s+event[^\n]*FailureCallback[^\n]*;', t, re.M)
print('decl:', m.group(0) if m else 'NOT FOUND')
if m and 'NoticeCallback' not in t.split('\n')[t[:m.start()].count('\n')]:
    line = m.group(0)
    newline = line + '\n\n' + re.sub(r'FailureCallback', 'NoticeCallback', line)
    t = t.replace(line, newline, 1)
    open(p, 'w', encoding='utf-8', newline='').write(t)
    print('OK added NoticeCallback')
else:
    print('SKIP')
