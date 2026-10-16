# -*- coding: utf-8 -*-
p = r'D:\openLM\更新日志.txt'
b = open(p, 'rb').read()
print('size:', len(b))
print('BOM:', b[:3] == b'\xef\xbb\xbf')
t = b.decode('utf-8')  # 严格解码，异常即报错
print('strict utf-8 decode: OK')
print('CR count (应 0):', b.count(b'\r'))
print('LF count:', b.count(b'\n'))
# 段落编号检查
import re
nums = re.findall(r'^([一二三四五六七八九十]+|[0-9]+)、', t, re.M)
print('编号序列:', ' '.join(nums[:50]))
