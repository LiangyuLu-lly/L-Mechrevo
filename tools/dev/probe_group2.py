# -*- coding: utf-8 -*-
"""查群友版：CanOffer* 属性、ProcessHelper、GpuApplyResult、Service 方法、SwitchGpuModeFromUi、GpuSwitchPolicy.Resolve 签名"""
import os, re

root = r'D:\openLM\source'

# 1) 各属性/类型
for kw in ('CanOfferNvPreferredGpu', 'CanOfferIgpuMuxTarget', 'CanOfferIgpuOnly', 'CanOfferGpuModeSwitch',
           'CanOfferGpuHotSwap', 'ProcessHelper', 'GpuApplyResult', 'CancelPendingGpuRestartAsync',
           'SetNvPreferredGpuAsync', 'GpuSwitchPlan', 'GpuSwitchPolicy', 'DgpuGeneration', 'GpuSwitchResult',
           'EvidenceMark', 'OemDisplayModeEncoding', 'IsUserAdministrator'):
    files = []
    for dp, dn, fn in os.walk(root):
        if 'bin' in dp or 'obj' in dp:
            continue
        for f in fn:
            if not f.endswith('.cs'):
                continue
            p = os.path.join(dp, f)
            try:
                s = open(p, encoding='utf-8', errors='replace').read()
            except Exception:
                continue
            if kw in s:
                files.append(os.path.relpath(p, root))
    print(kw, '->', files if files else 'MISSING')

# 2) 群友版 SwitchGpuModeFromUi 完整
s = open(r'D:\openLM\source\MechrevoLite\SettingsForm.cs', encoding='utf-8', errors='replace').read()
for m in re.finditer(r'(?:private|public|internal|protected)[^\n]*SwitchGpuModeFromUi\(', s):
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
    print('===== 群友版 SwitchGpuModeFromUi =====')
    print(s[start:i + 1])
    break
