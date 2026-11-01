# -*- coding: utf-8 -*-
"""查群友版 SettingsForm：VisualiseGPUButtons 签名、ApplySnapshot、RefreshDeviceCapabilities GPU 行、ShowGpuRestartPromptAsync"""
import re

s = open(r'D:\openLM\source\MechrevoLite\SettingsForm.cs', encoding='utf-8', errors='replace').read()

# VisualiseGPUButtons 签名
for m in re.finditer(r'(?:private|public|internal|protected)[^\n]*VisualiseGPUButtons\(', s):
    print('=== VisualiseGPUButtons 签名 ===')
    print(s[m.start():m.start()+300])
    break

# ApplySnapshot 局部函数
for m in re.finditer(r'void ApplySnapshot\(\)', s):
    print('=== ApplySnapshot ===')
    start = m.start(); depth = 0; i = start
    while i < len(s):
        if s[i] == '{': depth += 1
        elif s[i] == '}':
            depth -= 1
            if depth == 0: break
        i += 1
    print(s[start:i+1])
    break

# RefreshDeviceCapabilities GPU 行
for m in re.finditer(r'RefreshDeviceCapabilities\(\)', s):
    start = m.start(); depth = 0; i = start
    while i < len(s):
        if s[i] == '{': depth += 1
        elif s[i] == '}':
            depth -= 1
            if depth == 0: break
        i += 1
    body = s[start:i+1]
    # 只打 GPU 相关行
    lines = body.split('\n')
    print('=== RefreshDeviceCapabilities GPU 相关行 ===')
    for j, ln in enumerate(lines):
        if any(k in ln for k in ('GPU', 'Gpu', 'gpu', 'panelGPU', 'HideGPUModes', 'VisualiseGPUButtons')):
            print('%4d| %s' % (j, ln))
    break
