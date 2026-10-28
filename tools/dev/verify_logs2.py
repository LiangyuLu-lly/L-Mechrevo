# -*- coding: utf-8 -*-
t = open(r'D:\openLM\修改日志.txt', encoding='utf-8', errors='replace').read()
print('mod:', '更正：诊断“显卡热切换”判断收窄' in t or '诊断"显卡热切换"判断收窄' in t)
t2 = open(r'D:\openLM\更新日志.txt', encoding='utf-8', errors='replace').read()
print('upd:', '诊断显卡热切换判断修正（更正）' in t2)
