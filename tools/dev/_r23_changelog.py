# -*- coding: utf-8 -*-
r"""r23（0.291.7.6）在两份 更新日志.txt 顶部（倒叙）插入新一节，两份逐字节一致。
   读 -> 断言（两份当前必须逐字节相同）-> 写 TEMP -> 校验 -> 替换。"""
import hashlib, os, shutil

ROOT_LOG = r'D:\openLM\更新日志.txt'
PKG_LOG = r'D:\openLM\installer-src\files\更新日志.txt'
BAK = r'D:\openLM\backup\repack\2026-10\20261013_2115_pre_repack_beta2276\version_files'
os.makedirs(BAK, exist_ok=True)

ANCHOR = '二十九、0.291.7.5 beta22.7.5（2026-10-13）'
NEW = (
    '三十、0.291.7.6 beta22.7.6（2026-10-13）\r\n'
    '────────────────────────────────────────────\r\n'
    '一、修复\r\n'
    '1. 修复点「下载」有时不打开浏览器（现在会以你的身份打开）。\r\n'
    '\r\n'
)


def sha(b):
    return hashlib.sha256(b).hexdigest().upper()


def read(p):
    return open(p, 'rb').read()


def patch(p, bak_name):
    raw = read(p)
    shutil.copy2(p, os.path.join(BAK, bak_name))
    s = raw.decode('utf-8')
    assert s.startswith(ANCHOR), 'anchor not at top: ' + p
    assert NEW not in s, 'already inserted: ' + p
    out = (NEW + s).encode('utf-8')
    tmp = p + '.tmp-write'
    open(tmp, 'wb').write(out)
    chk = open(tmp, 'rb').read()
    assert chk == out
    os.replace(tmp, p)
    print('OK %-46s %d B -> %d B  sha %s' % (p, len(raw), len(out), sha(out)))
    return sha(out)


a = read(ROOT_LOG)
b = read(PKG_LOG)
assert a == b, '两份改前不是逐字节一致'
print('改前两份一致，%d B / sha %s' % (len(a), sha(a)))
h1 = patch(ROOT_LOG, 'root_更新日志.txt')
h2 = patch(PKG_LOG, 'files_更新日志.txt')
a2 = read(ROOT_LOG)
b2 = read(PKG_LOG)
assert a2 == b2, '两份改后不一致'
assert sha(a2) == h1 == h2
# 断言新节在最顶部，且旧节紧随其后
s = a2.decode('utf-8')
assert s.startswith('三十、0.291.7.6 beta22.7.6（2026-10-13）'), s[:60]
assert s.index(ANCHOR) > 0 and s.index(ANCHOR) < 400, 'old section not right after new one'
assert '0.291.7.6 beta22.7.6' in s and '点「下载」' in s
print('改后两份逐字节一致，%d B / sha %s' % (len(a2), sha(a2)))
print('ALL-DONE')
