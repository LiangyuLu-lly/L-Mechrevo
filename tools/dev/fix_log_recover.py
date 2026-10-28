# -*- coding: utf-8 -*-
p = r'D:\openLM\更新日志.txt'
b = open(p, 'rb').read()
t = b.decode('utf-8', errors='replace')
si = t.find('ʮ�ˡ��ڴ�ռ���Ż���')
ei = t.find('十三、睡眠省电', si)
bsi = len(t[:si].encode('utf-8'))
bei = len(t[:ei].encode('utf-8'))
seg = b[bsi:bei]
dec = seg.decode('gb18030', errors='replace')
open(r'D:\openLM\tools\mojibake_recovered.txt', 'w', encoding='utf-8').write(dec)
# 找损坏区（替换字符）位置
idx = [i for i, ch in enumerate(dec) if ch == '\ufffd']
print('replacement char positions:', idx[:50])
print('total:', len(idx))
# 损坏区间的可读上下文
for i in idx[:10]:
    ctx = dec[max(0, i-60):i+60]
    print('--- ctx @', i, '---')
    print(repr(ctx))
