# -*- coding: utf-8 -*-
p = r'D:\openLM\更新日志.txt'
b = open(p, 'rb').read()
t = b.decode('utf-8', errors='replace')

# 找乱码区边界：乱码特征字符 ʮ（GBK 十八 的 'ʮ' 是乱码代表）
idx = t.find('ʮ')
print('first mojibake char at utf8-char index:', idx)
if idx > 0:
    # 该位置的原始字节
    # 用字节定位：从文件开头数 UTF-8 字符到 idx
    # 简化：直接在字节流里找 'ʮ' 的 UTF-8 编码位置
    pass

# 更直接：找乱码起始行 'ʮ�ˡ��ڴ�ռ���Ż���'（十八、后台占用优化）
start_marker = 'ʮ�ˡ��ڴ�ռ���Ż���'
si = t.find(start_marker)
print('mojibake block start at:', si)
# 乱码区结束：下一个正常中文标题（十三、睡眠省电）
end_marker = '十三、睡眠省电'
ei = t.find(end_marker, si)
print('block end at:', ei, 'block utf8 chars:', ei - si)

# 把乱码区字节按 GBK 解码试试
# 先算字节偏移：utf8 前 si 个字符的字节数
bsi = len(t[:si].encode('utf-8'))
bei = len(t[:ei].encode('utf-8'))
seg = b[bsi:bei]
for enc in ('gbk', 'gb18030', 'big5'):
    try:
        dec = seg.decode(enc)
        print(f'--- {enc} decode OK, first 300 chars ---')
        print(dec[:300])
        break
    except Exception as e:
        print(f'{enc} failed: {e}')
