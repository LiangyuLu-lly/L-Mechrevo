# -*- coding: utf-8 -*-
r"""r25（0.291.7.6 同名重打）把新构建产物并入 installer-src\files：逐文件 SHA-256 比对，只覆盖真不同的。

纪律（沿用 _r23_merge_files.py / tools\merge_and_clean.py）：
  - ⛔ 不新增文件、⛔ 不删除文件、⛔ 不碰 GCU / fonts / usbmmidd_v2 / Resources / 更新日志.txt / 启动壳；
  - 只覆盖 files\ 里【已存在】的同名文件；
  - 写 .tmp-copy -> 校验 SHA -> 原子替换（⛔ 防半截文件）；
  - 收尾断言：文件数不变、无 .tmp-copy 残留。

用法: py -3 tools\dev\_r25_merge_files.py [--apply]
"""
import os, sys, shutil, hashlib

sys.stdout.reconfigure(encoding='utf-8', errors='replace')

FILES = r'D:\openLM\installer-src\files'
BUILD = r'D:\openLM\source\bin\Release\net10.0-windows10.0.19041.0'

# ⛔ 显式排除（与 r23 一致 + 目录）：这些由别的步骤/别的来源负责，绝不从 build 覆盖。
NEVER_FILES = {'L-Mechrevo-Launcher.exe', '更新日志.txt', '更新日志.txt.bak-20261013-013711',
               '更新日志.txt.bak-20261013_r18', '用前必看.txt'}
NEVER_DIRS = {'gcu', 'fonts', 'usbmmidd_v2', 'resources'}


def sha(p):
    h = hashlib.sha256()
    with open(p, 'rb') as f:
        for blk in iter(lambda: f.read(1 << 20), b''):
            h.update(blk)
    return h.hexdigest().upper()


def main():
    apply_ = '--apply' in sys.argv
    n_before = sum(len(f) for _, _, f in os.walk(FILES))
    print('files\\ 改前文件数 = %d' % n_before)
    updated, same, skipped, nomatch = [], [], [], []

    for dp, dns, fns in os.walk(FILES):
        rel_dir = os.path.relpath(dp, FILES)
        rel_dir = '' if rel_dir == '.' else rel_dir
        parts = [p.lower() for p in rel_dir.split(os.sep) if p]
        if any(p in NEVER_DIRS for p in parts):
            for fn in fns:
                skipped.append(os.path.join(rel_dir, fn))
            continue
        for fn in fns:
            rel = os.path.join(rel_dir, fn) if rel_dir else fn
            if fn in NEVER_FILES or fn.lower() == 'l-mechrevo-launcher.exe':
                skipped.append(rel)
                continue
            src = os.path.join(BUILD, rel)
            if not os.path.isfile(src):
                nomatch.append(rel)
                continue
            dst = os.path.join(FILES, rel)
            a, b = os.path.getsize(src), os.path.getsize(dst)
            if a == b and sha(src) == sha(dst):
                same.append(rel)
                print('   = SAME   %-44s %9d B  %s' % (rel, a, sha(src)[:16]))
                continue
            print('   -> UPDATE %-42s %9d B %s -> %9d B %s' % (rel, b, sha(dst)[:16], a, sha(src)[:16]))
            updated.append(rel)
            if apply_:
                tmp = dst + '.tmp-copy'
                shutil.copyfile(src, tmp)
                assert sha(tmp) == sha(src), 'TEMP copy verify failed: ' + rel
                os.replace(tmp, dst)
                assert sha(dst) == sha(src), 'POST replace verify failed: ' + rel

    print('\n--- build 里无同名对应（保持原样）: %d ---' % len(nomatch))
    for rel in sorted(nomatch):
        print('      no-src  %s' % rel)
    print('--- 主动跳过（由别的步骤负责）: %d ---' % len(skipped))
    for rel in sorted(skipped):
        print('      skip    %s' % rel)

    n_after = sum(len(f) for _, _, f in os.walk(FILES))
    print('\nfiles\\ 改后文件数 = %d（%s）' % (n_after, '未变' if n_after == n_before else '★变了'))
    assert n_after == n_before, '文件数变了 —— 必须为 0 新增 / 0 删除'
    leftovers = [os.path.join(dp, f) for dp, _, fs in os.walk(FILES) for f in fs if f.endswith('.tmp-copy')]
    assert not leftovers, leftovers
    print('更新 %d 个 / 未变 %d 个 / 跳过 %d 个 / 无源 %d 个%s'
          % (len(updated), len(same), len(skipped), len(nomatch),
             '' if apply_ else '   （dry-run，未改动；加 --apply）'))
    return 0


if __name__ == '__main__':
    sys.exit(main())
