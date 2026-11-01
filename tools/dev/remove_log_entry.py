# -*- coding: utf-8 -*-
p = r'D:\openLM\修改日志.txt'
t = open(p, encoding='utf-8').read()
marker = '【2026-10-03】更新日志.txt 修复'
idx = t.find(marker)
if idx == -1:
    print('marker not found')
else:
    before = t[:idx].rstrip('\n') + '\n'
    open(p, 'w', encoding='utf-8', newline='\n').write(before)
    t2 = open(p, encoding='utf-8').read()
    print('删除成功; marker 仍存在:', marker in t2)
    print('尾部最后一行:', t2.rstrip('\n').split('\n')[-1])
