# -*- coding: utf-8 -*-
"""提取抓包全部 JSON 键"""
import re
from collections import Counter

for name in ('gcu_probe_out.log', 'GCU_capture_yaoshi_50.log'):
    blob = open(r'D:\openLM\patches\\' + name, encoding='utf-8', errors='replace').read()
    keys = re.findall(r'"([A-Za-z][A-Za-z0-9_]*)"\s*:', blob)
    c = Counter(keys)
    print('=' * 50)
    print(name, '键总数', len(c))
    for k, v in c.most_common(120):
        print(f'  {k} x{v}')
