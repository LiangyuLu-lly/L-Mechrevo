# -*- coding: utf-8 -*-
lines = open(r'D:\openLM\tools\wcs_probe.ps1', encoding='utf-8').read().splitlines()
for i, l in enumerate(lines[:14]):
    print(i, repr(l))
closes = [i for i, l in enumerate(lines) if l.strip() == "'@"]
print('close lines:', closes)
