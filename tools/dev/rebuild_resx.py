# -*- coding: utf-8 -*-
"""重建 resx：18.6 原始头（root/schema/resheader）+ 群友版全部 data 块"""
import re

def rebuild(damaged, base186, out):
    d = open(damaged, encoding='utf-8', errors='replace').read()
    b = open(base186, encoding='utf-8', errors='replace').read()
    # 提取损坏文件的全部 data 块
    blocks = re.findall(r'<data\s+name="[^"]*"[^>]*>.*?</data>', d, re.S)
    # 去重（按 name）
    seen = {}
    for blk in blocks:
        m = re.search(r'name="([^"]+)"', blk)
        if m:
            seen[m.group(1)] = blk.strip()
    print('data blocks:', len(blocks), 'unique:', len(seen))
    # 取 18.6 头部：<?xml ... <root ...> 到最后一个 </resheader> 结束
    end = b.rfind('</resheader>')
    assert end > 0
    head = b[:end + len('</resheader>')]
    # 取 18.6 尾部 </root> 前的空白样式
    # 组合
    body = '\n'.join(seen.values())
    out_s = head + '\n' + body + '\n</root>\n'
    open(out, 'w', encoding='utf-8', newline='').write(out_s)
    print('REBUILT', out, 'chars', len(out_s))

rebuild(
    r'D:\openLM\source\MechrevoLite.Properties.Strings.resx',
    r'D:\openLM\archive\refs\L-Mechrevo-18.6-source\source\MechrevoLite.Properties.Strings.resx',
    r'D:\openLM\source\MechrevoLite.Properties.Strings.resx')
rebuild(
    r'D:\openLM\source\MechrevoLite.Properties.Strings.zh-CN.resx',
    r'D:\openLM\archive\refs\L-Mechrevo-18.6-source\source\MechrevoLite.Properties.Strings.zh-CN.resx',
    r'D:\openLM\source\MechrevoLite.Properties.Strings.zh-CN.resx')
print('DONE')
