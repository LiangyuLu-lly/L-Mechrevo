# -*- coding: utf-8 -*-
r"""
修 .iss：清掉指向"已删除文件"的 Source 行 + 更新版本号
================================================================================
背景：installer-src\files 里的 GCU payload 被精简过（删了 oled_care.scr / ICCProfile /
License / GCUUtil.exe / ACPIDriverDll.dll 等），但 .iss 仍逐条引用 -> 打包会失败。
做法：
  1) 逐条检查 Source: "files\..." 是否存在（**大小写不敏感**，Windows 语义）
  2) 删掉不存在的行（同时删掉紧邻的说明注释行？—— 不删，注释留着无害）
  3) 更新版本行：18.3 内测版 -> 0.290.3 beta21.3
用法：
  py -3 fix_iss.py            # dry-run
  py -3 fix_iss.py --apply    # 写回（先备份 .bak-<时间戳>）
"""
import io
import os
import re
import sys
import shutil
import datetime

sys.stdout.reconfigure(encoding="utf-8", errors="replace")

ROOT = r'D:\openLM\installer-src'
FILES = os.path.join(ROOT, 'files')
ISS_LIST = ('install_script.iss', 'install_script_nogcu.iss')

NEW_VER = '0.290.3'
NEW_LABEL = 'beta21.3'

VER_KEYS = ('AppVerName', 'AppVersion', 'OutputBaseFilename', 'VersionInfoVersion',
            'VersionInfoProductVersion', 'UninstallDisplayName')


def rewrite_version_lines(lines):
    """按 key=value 行重写版本（比正则匹配整行稳），返回 (新行列表, 改了几行)"""
    out, changed = [], 0
    for l in lines:
        m = re.match(r'^(\s*)([A-Za-z]+)[ \t]*=[ \t]*(.*)$', l)
        if m and m.group(2) in VER_KEYS and '18.3' in m.group(3):
            val = m.group(3)
            # 文件名用连字符（与既有产物一致：LM-0.290.0-beta21.1-setup.exe）；
            # 显示文本用空格（0.290.3 beta21.3）。
            sep = '-' if m.group(2) == 'OutputBaseFilename' else ' '
            val = val.replace('18.3内测版', NEW_VER + sep + NEW_LABEL)
            val = val.replace('18.3 内测版', NEW_VER + sep + NEW_LABEL)
            val = val.replace('18.3', NEW_VER)
            out.append(m.group(1) + m.group(2) + '=' + val)
            changed += 1
            continue
        out.append(l)
    return out, changed

# 大小写不敏感的磁盘索引
disk = set()
for dp, _d, fs in os.walk(FILES):
    for f in fs:
        disk.add(os.path.relpath(os.path.join(dp, f), FILES).lower().replace('/', '\\'))


def main():
    apply_ = '--apply' in sys.argv
    for name in ISS_LIST:
        p = os.path.join(ROOT, name)
        if not os.path.isfile(p):
            print('（跳过，不存在）' + p)
            continue
        raw = io.open(p, 'rb').read()
        text = raw.decode('utf-8-sig')
        bom = raw[:3] == b'\xef\xbb\xbf'
        nl = '\r\n' if '\r\n' in text else '\n'
        lines = text.split(nl)

        removed, kept, missing_ver = [], [], []
        out = []
        for l in lines:
            m = re.match(r'^Source:\s*"files\\([^"]+)"\s*;', l)
            if m:
                rel = m.group(1).replace('/', '\\')
                if rel.lower() not in disk:
                    removed.append(rel)
                    continue
            out.append(l)

        out2, ver_changed = rewrite_version_lines(out)
        txt = nl.join(out2)

        print('=' * 68)
        print(name)
        print('  Source 行删除 %d 条（目标不存在）' % len(removed))
        for r in removed[:40]:
            print('     - ' + r)
        if len(removed) > 40:
            print('     ... 其余 %d 条' % (len(removed) - 40))
        print('  版本行替换 %d 条' % ver_changed)
        for l in out2:
            if re.match(r'^\s*(AppVerName|AppVersion|OutputBaseFilename|VersionInfoVersion|VersionInfoProductVersion|UninstallDisplayName)\s*=', l):
                print('     ' + l.strip())

        if apply_:
            shutil.copy2(p, p + '.bak-' + datetime.datetime.now().strftime('%Y%m%d_%H%M%S'))
            data = txt.encode('utf-8')
            if bom:
                data = b'\xef\xbb\xbf' + data
            io.open(p, 'wb').write(data)
            print('  -> 已写入（备份 %s.bak-...）' % name)
    if not apply_:
        print('\n（dry-run，未写盘。加 --apply 执行）')
    return 0


if __name__ == '__main__':
    sys.exit(main())
