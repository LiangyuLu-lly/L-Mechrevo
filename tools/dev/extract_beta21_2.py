# -*- coding: utf-8 -*-
"""提取 beta21：Program GpuRestartVerifier + SettingsForm 完整 VisualiseGPUMode/ResolveHighlightedGpuMode/GpuRowStatusText/_gpuRowLayout 初始化"""
import re

# 1) Program.cs
s = open(r'D:\openLM\beta21\decomp\MechrevoLite\Program.cs', encoding='utf-8', errors='replace').read()
print('=== Program.cs GpuRestartVerifier ===')
for m in re.finditer(r'GpuRestartVerifier', s):
    print(s[max(0, m.start() - 300):m.end() + 350])
    print('---END---')

# 2) SettingsForm 完整方法
s = open(r'D:\openLM\beta21\decomp\MechrevoLite\SettingsForm.cs', encoding='utf-8', errors='replace').read()
for name in ('ResolveHighlightedGpuMode', 'GpuRowStatusText'):
    for m in re.finditer(r'(?:private|public|internal|protected)[^\n]*' + name + r'\(', s):
        print('=== ' + name + ' full ===')
        # 找方法体结束（下一个 'private' 或 'public' 同缩进方法，简单用大括号平衡）
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
        print('---END---')
        break
