# -*- coding: utf-8 -*-
"""查群友版 csproj 引用与 NvAPIWrapper"""
import os, re

for dp, dn, fn in os.walk(r'D:\openLM\source'):
    if 'bin' in dp or 'obj' in dp:
        continue
    for f in fn:
        if not f.endswith('.csproj'):
            continue
        p = os.path.join(dp, f)
        s = open(p, encoding='utf-8', errors='replace').read()
        print('===', os.path.relpath(p, r'D:\openLM\source'), '===')
        for m in re.finditer(r'PackageReference\s+Include="([^"]+)"', s):
            print('pkg:', m.group(1))
        for m in re.finditer(r'ProjectReference\s+Include="([^"]+)"', s):
            print('proj:', m.group(1))

# 全源码找 NvAPIWrapper / NvidiaSmi
root = r'D:\openLM\source'
for kw in ('NvAPIWrapper', 'NvidiaSmi', 'NvmlHelper'):
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
