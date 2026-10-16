# -*- coding: utf-8 -*-
"""0.291.7.5 -> 0.291.7.6 版本号定点替换（读 -> 断言 -> 写 TEMP -> 校验 -> 替换）。
⚠️ 只做【唯一锚点】定点替换；每次替换后断言命中次数恰好 1。"""
import hashlib, os, shutil, sys

BAK = r'D:\openLM\backup\repack\2026-10\20261013_2115_pre_repack_beta2276\version_files'
os.makedirs(BAK, exist_ok=True)

def sha(b):
    return hashlib.sha256(b).hexdigest().upper()

def patch(path, edits, expect_bom=False, expect_crlf=None):
    raw = open(path, 'rb').read()
    before = sha(raw)
    shutil.copy2(path, os.path.join(BAK, os.path.basename(path)))
    s = raw.decode('utf-8')
    for old, new, n in edits:
        c = s.count(old)
        assert c == n, '%s: anchor %r count=%d expected=%d' % (path, old[:60], c, n)
        s = s.replace(old, new)
    out = s.encode('utf-8')
    assert (out[:3] == b'\xef\xbb\xbf') == expect_bom, path + ' BOM changed'
    if expect_crlf is not None:
        assert out.count(b'\r\n') == expect_crlf, '%s CRLF %d != %d' % (path, out.count(b'\r\n'), expect_crlf)
    tmp = path + '.tmp-write'
    open(tmp, 'wb').write(out)
    chk = open(tmp, 'rb').read()
    assert chk == out and sha(chk) == sha(out), 'TEMP verify failed'
    os.replace(tmp, path)
    print('OK  %-52s %d B -> %d B  %s -> %s' % (os.path.basename(path), len(raw), len(out), before[:16], sha(out)[:16]))

# ---------------- 1. source\Properties\AssemblyInfo.cs（noBOM / LF / 19 行） ----------------
patch(r'D:\openLM\source\Properties\AssemblyInfo.cs', [
    ('[assembly: AssemblyFileVersion("0.291.7.5")]',
     '[assembly: AssemblyFileVersion("0.291.7.6")]', 1),
    ('[assembly: AssemblyInformationalVersion("0.291.7.5-beta22.7.5")]',
     '[assembly: AssemblyInformationalVersion("0.291.7.6-beta22.7.6")]', 1),
    ('[assembly: AssemblyVersion("0.291.7.5")]',
     '[assembly: AssemblyVersion("0.291.7.6")]', 1),
], expect_bom=False, expect_crlf=0)

# ---------------- 2. installer-src\install_script.iss（noBOM / CRLF） ----------------
patch(r'D:\openLM\installer-src\install_script.iss', [
    ('AppVerName=L-Mechrevo 0.291.7.5 beta22.7.5',
     'AppVerName=L-Mechrevo 0.291.7.6 beta22.7.6', 1),
    ('AppVersion=0.291.7.5', 'AppVersion=0.291.7.6', 1),
    ('OutputBaseFilename=LM-0.291.7.5-beta22.7.5-setup',
     'OutputBaseFilename=LM-0.291.7.6-beta22.7.6-setup', 1),
    ('VersionInfoVersion=0.291.7.5', 'VersionInfoVersion=0.291.7.6', 1),
    ('VersionInfoProductVersion=0.291.7.5', 'VersionInfoProductVersion=0.291.7.6', 1),
    ('UninstallDisplayName=L-Mechrevo 0.291.7.5 beta22.7.5',
     'UninstallDisplayName=L-Mechrevo 0.291.7.6 beta22.7.6', 1),
], expect_bom=False)

# ---------------- 3. installer-src\打包须知.txt（noBOM / CRLF） ----------------
patch(r'D:\openLM\installer-src\打包须知.txt', [
    ('主 / GCU 包 = 0.291.7.5-beta22.7.5', '主 / GCU 包 = 0.291.7.6-beta22.7.6', 1),
], expect_bom=False)

print('ALL-DONE')
