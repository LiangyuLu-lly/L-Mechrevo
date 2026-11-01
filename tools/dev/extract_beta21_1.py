# -*- coding: utf-8 -*-
"""提取 beta21 关键实现段：UefiDisplayModeDiagnostics、Program GpuRestartVerifier、GPUModeControl GpuRowLayout、MechrevoHw GpuRowLayout、SettingsForm 完整方法"""
import re

# 1) UefiDisplayModeDiagnostics 全文
p = r'D:\openLM\beta21\decomp\MechrevoLite.Gpu\UefiDisplayModeDiagnostics.cs'
s = open(p, encoding='utf-8', errors='replace').read()
print('=== UefiDisplayModeDiagnostics.cs ===')
print(s)
print()

# 2) Program.cs GpuRestartVerifier
p = r'D:\openLM\beta21\decomp\MechrevoLite\Program.cs'
s = open(p, encoding='utf-8', errors='replace').read()
print('=== Program.cs GpuRestartVerifier ===')
for m in re.finditer(r'GpuRestartVerifier', s):
    print('...', s[max(0, m.start() - 200):m.end() + 250].replace('\n', ' '))
    print('---')
