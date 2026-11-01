# -*- coding: utf-8 -*-
r"""
底座 payload\GCU（50 系） vs 本机在跑的官方 30 系 GCU —— 重复文件统计
================================================================================
A = D:\openLM\installer-src\files\GCU\payload\GCU\AiStoneService   （底座，50 系）
B = C:\Program Files\OEM\机械革命电竞控制台\UniwillService          （本机官方，30 系）
统计：同名文件数、其中【逐字节相同】/【内容不同】、各自独有、以及重复体积
"""
import io
import os
import sys
import hashlib

sys.stdout.reconfigure(encoding="utf-8", errors="replace")

A = r'D:\openLM\installer-src\files\GCU\payload\GCU\AiStoneService'
B = r'C:\Program Files\OEM\机械革命电竞控制台\UniwillService'


def walk(root):
    out = {}
    for dirpath, _d, filenames in os.walk(root):
        for name in filenames:
            full = os.path.join(dirpath, name)
            try:
                out[os.path.relpath(full, root)] = os.path.getsize(full)
            except OSError:
                out[os.path.relpath(full, root)] = -1
    return out


def sha(path):
    h = hashlib.sha256()
    try:
        with io.open(path, 'rb') as f:
            for chunk in iter(lambda: f.read(1 << 20), b''):
                h.update(chunk)
    except OSError:
        return '?'
    return h.hexdigest()


def main():
    a = walk(A)
    b = walk(B)
    same_path = sorted(set(a) & set(b))
    only_a = sorted(set(a) - set(b))
    only_b = sorted(set(b) - set(a))

    identical, differing = [], []
    for rel in same_path:
        if a[rel] != b[rel]:
            differing.append(rel)
            continue
        if sha(os.path.join(A, rel)) == sha(os.path.join(B, rel)):
            identical.append(rel)
        else:
            differing.append(rel)

    dup_bytes = sum(a[r] for r in identical)

    print("A 底座(50系) = %s" % A)
    print("B 官方(30系) = %s\n" % B)
    print("文件数            : A=%d  B=%d" % (len(a), len(b)))
    print("同名文件          : %d" % len(same_path))
    print("  ├ 逐字节【完全相同】: %d   （重复体积 %.2f MB）" % (len(identical), dup_bytes / 1048576.0))
    print("  └ 同名但【内容不同】: %d   （体积 %.2f MB）" % (len(differing), sum(a[r] for r in differing) / 1048576.0))
    print("只在底座 A 独有   : %d   （%.2f MB）" % (len(only_a), sum(a[r] for r in only_a) / 1048576.0))
    print("只在官方 B 独有   : %d   （%.2f MB）" % (len(only_b), sum(b[r] for r in only_b) / 1048576.0))

    if differing:
        print("\n===== 同名但内容不同（按 A 的体积降序，前 30）=====")
        for rel in sorted(differing, key=lambda r: -a[r])[:30]:
            print("  %-56s A=%-10d B=%-10d" % (rel, a[rel], b[rel]))

    print("\n===== 完全相同的那批里最大的 15 个（重复体积的主要来源）=====")
    for rel in sorted(identical, key=lambda r: -a[r])[:15]:
        print("  %-56s %.2f MB" % (rel, a[rel] / 1048576.0))
    return 0


if __name__ == '__main__':
    sys.exit(main())
