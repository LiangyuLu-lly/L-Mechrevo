# -*- coding: utf-8 -*-
"""看群友版 ToastForm.ShowFailure 实现与 DisplayNative 结构区"""
import re

t = open(r'D:\openLM\source\MechrevoLite.Helpers\ToastForm.cs', encoding='utf-8', errors='replace').read()
for m in re.finditer(r'(?:public|private|internal|protected)\s+(?:static\s+)?(?:void|Task|bool)\s+(ShowFailure|RunToast)\s*\(', t):
    start = m.start(); depth = 0; i = start
    while i < len(t):
        if t[i] == '{': depth += 1
        elif t[i] == '}':
            depth -= 1
            if depth == 0: break
        i += 1
    print('=== ToastForm.' + m.group(1) + ' ===')
    print(t[start:i+1])
    print()

d = open(r'D:\openLM\source\MechrevoLite.Display\DisplayNative.cs', encoding='utf-8', errors='replace').read()
print('=== DisplayNative 尾部 ===')
print(d[-2500:])
