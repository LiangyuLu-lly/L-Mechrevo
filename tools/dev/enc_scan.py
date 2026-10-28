# -*- coding: utf-8 -*-
p = r'D:\openLM\更新日志.txt'
b = open(p, 'rb').read()
print('file size:', len(b))
print('first 8 bytes hex:', b[:8].hex())
lines = b.split(b'\r\n')
print('total lines:', len(lines))
# 每行判定编码：utf8 严格 / gbk 严格
def dec_ok(ln, enc):
    try:
        ln.decode(enc)
        return True
    except Exception:
        return False
cnt_utf8 = cnt_gbk = cnt_both = cnt_none = 0
for ln in lines:
    u = dec_ok(ln, 'utf-8')
    g = dec_ok(ln, 'gbk')
    if u and g: cnt_both += 1
    elif u: cnt_utf8 += 1
    elif g: cnt_gbk += 1
    else: cnt_none += 1
print('utf8-only lines:', cnt_utf8, 'gbk-only:', cnt_gbk, 'both:', cnt_both, 'neither:', cnt_none)
# 打印前 12 行与关键行的判定
for i in range(0, 14):
    ln = lines[i]
    u = dec_ok(ln, 'utf-8'); g = dec_ok(ln, 'gbk')
    print(i, 'u' if u else '-', 'g' if g else '-', repr(ln[:50]))
# 找 18 开头行（GBK 十八：0xCA 0xAE? 或 utf8）
for i, ln in enumerate(lines):
    if '0.289.4.0' in ln.decode('gbk', errors='ignore') or b'0.289.4.0' in ln:
        print('found 0.289.4.0 at line', i, 'utf8ok:', dec_ok(ln,'utf-8'), 'gbkok:', dec_ok(ln,'gbk'), 'hex head:', ln[:40].hex())
        break
# 睡眠省电行
for i, ln in enumerate(lines):
    if b'\xe7\x9d\xa1\xe7\x9c\xa0' in ln:  # utf8 睡眠
        print('utf8 睡眠 at line', i)
        break
# GBK 睡眠省电: 鐫＄湢鐪佺數
for i, ln in enumerate(lines):
    if b'\xc7\xd7\xc3\xdf' in ln:  # GBK 睡眠
        print('gbk 睡眠 at line', i)
        break
