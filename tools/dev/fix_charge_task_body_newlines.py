# -*- coding: utf-8 -*-
"""
修正：ChargeTaskLocationBody 的多行文案
上一步 patch_charge_task_location_fix.py 里把换行写成了 '\\n'（Python 非 raw 字符串），
落进 resx 变成字面的反斜杠+n，MessageBox 会原样显示。
本脚本把这两个 resx 里 ChargeTaskLocationBody 的值做一次"字面 \\n -> 真换行"修正。
"""
import io
import re
import sys

FILES = [
    r'D:\openLM\source\MechrevoLite.Properties.Strings.zh-CN.resx',
    r'D:\openLM\source\MechrevoLite.Properties.Strings.resx',
]

PATTERN = re.compile(r'(<data name="ChargeTaskLocationBody" xml:space="preserve">\s*<value>)(.*?)(</value>)', re.S)

fixed_any = False
for path in FILES:
    raw = io.open(path, 'rb').read()
    bom = raw.startswith(b'\xef\xbb\xbf')
    text = raw.decode('utf-8-sig')
    crlf = '\r\n' in text
    if crlf:
        text = text.replace('\r\n', '\n')

    m = PATTERN.search(text)
    if not m:
        print('SKIP（找不到 ChargeTaskLocationBody）: ' + path)
        continue
    body = m.group(2)
    if '\\n' not in body:
        print('SKIP（已是真换行）: ' + path)
        continue
    new_body = body.replace('\\n', '\n')
    text = text[:m.start(2)] + new_body + text[m.end(2):]

    out = text.replace('\n', '\r\n') if crlf else text
    data = out.encode('utf-8')
    if bom:
        data = b'\xef\xbb\xbf' + data
    io.open(path, 'wb').write(data)
    fixed_any = True
    print('修正 %s（%d 个 \\n -> 真换行，值内行数 %d）' % (path, body.count('\\n'), new_body.count('\n') + 1))

sys.exit(0 if fixed_any else 0)
