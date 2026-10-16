# -*- coding: utf-8 -*-
import os, sys
sys.stdout.reconfigure(encoding='utf-8', errors='replace')
d = r'D:\openLM\work\2026-10-13\r23_unpack'
p2 = os.path.join(d, '{app}', 'Docs', '\u66f4\u65b0\u65e5\u5fd7.txt')
b = open(p2, 'rb').read()
print('Docs changelog bytes', len(b))
print('has 0.291.7.6-beta22.7.6 :', b'0.291.7.6-beta22.7.6' in b)
print('has 0.291.7.6 beta22.7.6 :', '0.291.7.6 beta22.7.6'.encode('utf-8') in b)
se = os.path.join(d, 'LM-0.291.7.6-beta22.7.6-setup.exe')
sb = open(se, 'rb').read()
print('setup size', len(sb))
for m in ['LM-NET-BROWSER', '\u7b2c2\u6863 explorer', 'ShellExecAsOriginalUser', 'explorer.exe']:
    print(repr(m), 'utf8?', m.encode('utf-8') in sb, 'utf16le?', m.encode('utf-16-le') in sb)
# CompiledCode.bin 依赖：先看 iss 重建文件里 [Code] 是什么
iss = open(os.path.join(d, 'install_script.iss'), 'rb').read()
print('rebuilt iss bytes', len(iss), 'has explorer.exe?', b'explorer.exe' in iss)
