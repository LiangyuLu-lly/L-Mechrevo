# -*- coding: utf-8 -*-
t = open(r'D:\openLM\修改日志.txt', encoding='utf-8', errors='replace').read()
print('mod:', '设置页 5 项 UI/文案调整' in t)
t2 = open(r'D:\openLM\更新日志.txt', encoding='utf-8', errors='replace').read()
print('upd:', '设置页调整' in t2)
