# -*- coding: utf-8 -*-
"""查：panelGPU.Visible 设置处、ShowGpuRestartPromptAsync、beta21 VisualiseGPUButtons 签名"""
import re

s = open(r'D:\openLM\source\MechrevoLite\SettingsForm.cs', encoding='utf-8', errors='replace').read()
for m in re.finditer(r'panelGPU\.Visible', s):
    print('panelGPU.Visible:', s[max(0, m.start()-160):m.end()+160].replace('\n', ' '))
    print('---')

for m in re.finditer(r'(?:private|public|internal|protected)[^\n]*ShowGpuRestartPromptAsync\(', s):
    start = m.start(); depth = 0; i = start
    while i < len(s):
        if s[i] == '{': depth += 1
        elif s[i] == '}':
            depth -= 1
            if depth == 0: break
        i += 1
    print('=== ShowGpuRestartPromptAsync full ===')
    print(s[start:i+1])
    break

b = open(r'D:\openLM\beta21\decomp\MechrevoLite\SettingsForm.cs', encoding='utf-8', errors='replace').read()
for m in re.finditer(r'(?:private|public|internal|protected)[^\n]*VisualiseGPUButtons\(', b):
    print('=== beta21 VisualiseGPUButtons ===')
    print(b[m.start():m.start()+800])
    break
