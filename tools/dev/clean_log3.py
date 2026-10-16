# -*- coding: utf-8 -*-
p = r'D:\openLM\修改日志.txt'
t = open(p, encoding='utf-8').read()
old = '''   CanOfferIgpuOnly || (CanOfferGpuModeSwitch && SupportsDgpuDirect)
   收紧为仅 CanOfferIgpuOnly。原先的兜底分支会把蛟龙 16s 这类
   "双显双模（混合+独显直连、无核显模式）"机型误判为支持核显模式。
   主界面与托盘均已收紧为同一判断（CanOfferIgpuOnly）。
'''
n = t.count(old)
print('匹配:', n)
assert n == 1
open(p, 'w', encoding='utf-8', newline='\n').write(t.replace(old, ''))
print('OK')
