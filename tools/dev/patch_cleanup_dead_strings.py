# -*- coding: utf-8 -*-
r"""
清理 3 个死字符串：Optimized / OptimizedGPUTooltip / AutoShort
================================================================================
实测：三者在 .cs 里引用数均为 0（AutoShort 的唯一使用者 ButtonOptimized_Click 已随 AUTO 残余清理删除）。
用正则删除 resx 的 <data> 块（AutoShort 块内含尾随空格，精确匹配不可靠），
并删掉 Strings.cs 的访问器。删完核对每个文件各删 3 处。
"""
import io
import re
import sys

CS = r'D:\openLM\source\MechrevoLite.Properties\Strings.cs'
RESX = [r'D:\openLM\source\MechrevoLite.Properties.Strings.resx',
        r'D:\openLM\source\MechrevoLite.Properties.Strings.zh-CN.resx']
KEYS = ('Optimized', 'OptimizedGPUTooltip', 'AutoShort')


def main():
    apply_ = '--apply' in sys.argv

    # --- resx：正则删 <data> 块 ---
    for path in RESX:
        data = io.open(path, 'rb').read()
        pat = re.compile(rb'<data name="(?:' + b'|'.join(k.encode() for k in KEYS) + rb')"[^>]*>.*?</data>\r?\n', re.S)
        found = len(pat.findall(data))
        new = pat.sub(b'', data)
        print("  %-46s 命中 %d 处%s" % (path.split('\\')[-1], found, '' if found == 3 else '  <- 期望 3，需检查'))
        if apply_ and found == 3:
            io.open(path, 'wb').write(new)

    # --- Strings.cs：删访问器行（含紧邻的 /// 注释行）---
    data = io.open(CS, 'rb').read()
    text = data.decode('utf-8')
    nl = '\r\n' if '\r\n' in text else '\n'
    lines = text.split(nl)
    out, removed = [], []
    for i, l in enumerate(lines):
        if any(('GetString("%s"' % k) in l for k in KEYS) and 'internal static string' in l:
            removed.append(l.strip())
            # 顺手删掉紧邻其上的 /// 注释行
            while out and out[-1].strip().startswith('///'):
                out.pop()
            continue
        out.append(l)
    print("  %-46s 命中 %d 处%s" % ('Strings.cs', len(removed), '' if len(removed) == 3 else '  <- 期望 3，需检查'))
    for r in removed:
        print("      - " + r[:100])
    if apply_ and len(removed) == 3:
        io.open(CS, 'wb').write(nl.join(out).encode('utf-8'))

    if not apply_:
        print('\n（dry-run，未写盘。加 --apply 执行）')
    return 0


if __name__ == '__main__':
    sys.exit(main())
