# -*- coding: utf-8 -*-
"""提取群友版 SettingsForm 关键区域"""
import re

s = open(r'D:\openLM\source\MechrevoLite\SettingsForm.cs', encoding='utf-8', errors='replace').read()

def dump(tag, text, limit=None):
    print('===== ' + tag + ' =====')
    print(text[:limit] if limit else text)
    print()

# 1) 字段区：找 _lastPerformanceLayout 附近
i = s.find('_lastPerformanceLayout')
dump('字段区', s[i-200:i+400])

# 2) VisualiseGPUMode 完整
for m in re.finditer(r'public void VisualiseGPUMode\(', s):
    start = m.start(); depth = 0; i = start
    while i < len(s):
        if s[i] == '{': depth += 1
        elif s[i] == '}':
            depth -= 1
            if depth == 0: break
        i += 1
    dump('VisualiseGPUMode full', s[start:i+1])
    break

# 3) RefreshGpuModeUiStateAsync 完整
for m in re.finditer(r'(?:private|public|internal|protected)[^\n]*RefreshGpuModeUiStateAsync\(', s):
    start = m.start(); depth = 0; i = start
    while i < len(s):
        if s[i] == '{': depth += 1
        elif s[i] == '}':
            depth -= 1
            if depth == 0: break
        i += 1
    dump('RefreshGpuModeUiStateAsync full', s[start:i+1])
    break
