# -*- coding: utf-8 -*-
"""列出群友版关键文件的方法/属性/能力清单"""
import re, os

def methods(path, title):
    s = open(path, encoding='utf-8', errors='replace').read()
    print('=====', title, '=====')
    pat = re.compile(r'^\s{1,4}(?:public|private|internal|protected)\s+(?:static\s+|readonly\s+|async\s+|sealed\s+|override\s+)*(?:[A-Za-z<>\[\],\?\. ]+)\s+([A-Za-z_][A-Za-z0-9_]*)\s*\(', re.M)
    seen = []
    for m in pat.finditer(s):
        name = m.group(1)
        if name not in seen:
            seen.append(name)
    # 属性
    pat2 = re.compile(r'^\s{1,4}(?:public|private|internal|protected)\s+(?:static\s+)*(?:[A-Za-z<>\[\],\?\. ]+)\s+([A-Za-z_][A-Za-z0-9_]*)\s*\{', re.M)
    props = []
    for m in pat2.finditer(s):
        name = m.group(1)
        if name not in props and name not in seen:
            props.append(name)
    print('METHODS:', ', '.join(seen))
    print('PROPS:', ', '.join(props))
    print('len:', len(s))
    print()

methods(r'D:\openLM\source\MechrevoLite\SettingsForm.cs', '群友版 SettingsForm.cs')
methods(r'D:\openLM\source\MechrevoLite.Hardware\MechrevoHw.cs', '群友版 MechrevoHw.cs')
methods(r'D:\openLM\source\MechrevoLite.Hardware\MechrevoService.cs', '群友版 MechrevoService.cs')
methods(r'D:\openLM\source\MechrevoLite.Gpu\GPUModeControl.cs', '群友版 GPUModeControl.cs')
