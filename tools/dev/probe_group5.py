# -*- coding: utf-8 -*-
"""查群友版 Program.cs 启动区、GPUModeControl.InitGPUMode、Strings.cs 结构"""
import re

# 1) Program.cs：找 Application.Idle / deferredInitialization
s = open(r'D:\openLM\source\MechrevoLite\Program.cs', encoding='utf-8', errors='replace').read()
print('=== Program.cs Application.Idle / deferred ===')
for m in re.finditer(r'Application\.Idle|deferredInitialization|ReportUpdateOutcome|StartOverlay', s):
    print('...', s[max(0, m.start()-150):m.end()+150].replace('\n', ' '))
    print('---')

# 2) GPUModeControl.InitGPUMode
g = open(r'D:\openLM\source\MechrevoLite.Gpu\GPUModeControl.cs', encoding='utf-8', errors='replace').read()
for m in re.finditer(r'public void InitGPUMode\(', g):
    start = m.start(); depth = 0; i = start
    while i < len(g):
        if g[i] == '{': depth += 1
        elif g[i] == '}':
            depth -= 1
            if depth == 0: break
        i += 1
    body = g[start:i+1]
    print('=== 群友版 InitGPUMode (len %d) ===' % len(body))
    print(body[:3500])
    break
