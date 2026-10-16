# -*- coding: utf-8 -*-
r"""
核对 installer-src\files 与两个 .iss 的 [Files] 清单（修正版）
================================================================================
修正上一版两个错误：
  1) 大小写：Windows 不区分大小写 -> 比较时统一 lower（原先把 files\Fonts 误判为缺失）
  2) 冗余判定：应为"**两个 .iss 都没引用**"（NoGCU 只打包主程序，GCU 目录对它天然"未引用"）
输出：
  A) 任一 .iss 引用但磁盘不存在 -> 打包会失败 ⚠️
  B) 两个 .iss 都没引用          -> 真冗余 ✅
"""
import io
import os
import re
import sys

sys.stdout.reconfigure(encoding="utf-8", errors="replace")

ROOT = r'D:\openLM\installer-src'
FILES = os.path.join(ROOT, 'files')
ISS_LIST = ('install_script.iss', 'install_script_nogcu.iss')


def parse_refs(path):
    t = io.open(path, encoding='utf-8-sig', errors='replace').read()
    refs = set()
    for m in re.finditer(r'^Source:\s*"([^"]+)"\s*;', t, re.M):
        src = m.group(1).replace('/', '\\')
        if src.lower().startswith('files\\'):
            refs.add(src[len('files\\'):].lower())
    return refs


def walk(root):
    out = {}
    for dp, _d, fs in os.walk(root):
        for f in fs:
            full = os.path.join(dp, f)
            out[os.path.relpath(full, root)] = os.path.getsize(full)
    return out


def main():
    disk = walk(FILES)
    lower_map = {k.lower(): k for k in disk}
    union = set()
    per = {}
    for name in ISS_LIST:
        p = os.path.join(ROOT, name)
        if os.path.isfile(p):
            r = parse_refs(p)
            per[name] = r
            union |= r

    print("files\\ 下共 %d 个文件\n" % len(disk))
    for name, refs in per.items():
        print("  %-26s 引用 %d 项" % (name, len(refs)))

    missing = sorted(r for r in union if r not in lower_map)
    print("\n== A) 任一 .iss 引用但磁盘不存在: %d 项 ==" % len(missing))
    for r in missing:
        print("   " + r)

    unref = sorted(k for k in disk if k.lower() not in union)
    tot = sum(disk[k] for k in unref)
    print("\n== B) 两个 .iss 都没引用（真冗余）: %d 项, %.2f MB ==" % (len(unref), tot / 1048576.0))
    for k in sorted(unref, key=lambda x: -disk[x]):
        print("   %9.1f KB  %s" % (disk[k] / 1024.0, k))
    return 0


if __name__ == '__main__':
    sys.exit(main())
