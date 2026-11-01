# -*- coding: utf-8 -*-
p = r'D:\openLM\更新日志.txt'
b = open(p, 'rb').read()
t = b.decode('utf-8', errors='replace')
si = t.find('ʮ�ˡ��ڴ�ռ���Ż���')
ei = t.find('十三、睡眠省电', si)
bsi = len(t[:si].encode('utf-8'))
bei = len(t[:ei].encode('utf-8'))
seg = b[bsi:bei]
print('block bytes:', len(seg))
# 简单的 GBK 渐进解码统计：统计 2 字节窗口中首字节在 GBK 范围的占比
total2 = 0
gbk_ok = 0
for i in range(0, len(seg) - 1):
    c = seg[i]
    if c >= 0x81:
        total2 += 1
        if 0x40 <= seg[i+1] <= 0xFE and seg[i+1] != 0x7F:
            gbk_ok += 1
print('GBK 首字节出现次数:', total2, '其中后字节合法:', gbk_ok, '占比: %.1f%%' % (100.0*gbk_ok/max(total2,1)))
# 看段尾 500 字节的原始内容（可能有一段可读）
print('--- tail 400 bytes repr ---')
print(seg[-400:].hex())
# 尝试整体用 gb18030 宽松解码（errors=replace）看可读率
dec = seg.decode('gb18030', errors='replace')
import unicodedata
repl = dec.count('\ufffd')
print('gb18030 replace 解码替换字符数:', repl, '/', len(dec))
print('--- gb18030 解码预览（前 2000 字符）---')
print(dec[:2000])
