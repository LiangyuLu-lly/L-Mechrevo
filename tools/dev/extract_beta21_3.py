# -*- coding: utf-8 -*-
"""提取 beta21 剩余移植素材"""
import re

# 1) SettingsForm：_gpuRowLayout / _gpuSwitchInProgress / _lastGpuModeHint 声明与赋值
s = open(r'D:\openLM\beta21\decomp\MechrevoLite\SettingsForm.cs', encoding='utf-8', errors='replace').read()
print('=== SettingsForm 字段与赋值 ===')
for kw in ('_gpuRowLayout', '_gpuSwitchInProgress', '_lastGpuModeHint'):
    print('---', kw, '---')
    for m in re.finditer(re.escape(kw), s):
        print('...', s[max(0, m.start() - 130):m.end() + 130].replace('\n', ' '))
        print()

# 2) HasPendingGpuRestart 定义（beta21）
print('=== HasPendingGpuRestart ===')
for m in re.finditer(r'(?:private|public|internal|protected)[^\n]*HasPendingGpuRestart\(', s):
    start = m.start()
    depth = 0
    i = start
    while i < len(s):
        if s[i] == '{':
            depth += 1
        elif s[i] == '}':
            depth -= 1
            if depth == 0:
                break
        i += 1
    print(s[start:i + 1])
    print('---')
    break

# 3) VisualiseGPUMode 完整（找 public void VisualiseGPUMode）
print('=== VisualiseGPUMode full ===')
for m in re.finditer(r'public void VisualiseGPUMode\(', s):
    start = m.start()
    depth = 0
    i = start
    while i < len(s):
        if s[i] == '{':
            depth += 1
        elif s[i] == '}':
            depth -= 1
            if depth == 0:
                break
        i += 1
    body = s[start:i + 1]
    print(body)
    print('---LEN', len(body), '---')
    break
