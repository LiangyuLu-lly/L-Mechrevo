# -*- coding: utf-8 -*-
# 追加 420W 供电编码修复到修改日志
import io

log = r'D:\openLM\修改日志.txt'
entry = '''

==== 2026-10-04 供电编码 0x58=420W 修复 ====

- 证据：耀世 5080 机主（420W 圆口电源）EC 供电采样日志 raw=0x5A，&0x78 掩码 = 0x58，
  此前识别表无此编码，显示"未知（不在表内）"。
- 修复：PowerInputDecoder.TryDecodeAdapterWatts 的 switch 增加 88(0x58) => 420。
- 影响：LM 电池行/悬浮窗供电显示与诊断包供电采样均走同一识别表，现正确显示"圆口 420W"。
'''

t = io.open(log, encoding='utf-8').read()
if '0x58=420W' not in t:
    t = t.rstrip('\n') + '\n' + entry
    io.open(log, 'w', encoding='utf-8', newline='').write(t)
    print('APPENDED')
else:
    print('ALREADY PRESENT')

t2 = io.open(log, encoding='utf-8').read()
print('size now:', len(t2.encode('utf-8')))
