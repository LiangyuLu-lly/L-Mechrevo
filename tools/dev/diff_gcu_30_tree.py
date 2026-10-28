# -*- coding: utf-8 -*-
r"""
30 系 GCU 文件树 diff：LM 自带 payload vs 官方已安装
================================================================================
对比：
  A = D:\openLM\installer-src\files\GCU\payload\30\AiStoneService        （LM 自带，30 系）
  B = C:\Program Files\OEM\机械革命电竞控制台\UniwillService              （官方，本机在跑）
输出：只在 A / 只在 B / 大小不同 / 完全相同的数量
"""
import io
import os
import sys

sys.stdout.reconfigure(encoding="utf-8", errors="replace")

A = r'D:\openLM\installer-src\files\GCU\payload\30\AiStoneService'
B = r'C:\Program Files\OEM\机械革命电竞控制台\UniwillService'


def walk(root):
    out = {}
    for dirpath, dirnames, filenames in os.walk(root):
        for name in filenames:
            full = os.path.join(dirpath, name)
            rel = os.path.relpath(full, root)
            try:
                out[rel] = os.path.getsize(full)
            except OSError:
                out[rel] = -1
    return out


def main():
    for label, root in (("A LM自带", A), ("B 官方", B)):
        if not os.path.isdir(root):
            print("!! 不存在: %s = %s" % (label, root))
            return 1
    a = walk(A)
    b = walk(B)
    only_a = sorted(set(a) - set(b))
    only_b = sorted(set(b) - set(a))
    both = sorted(set(a) & set(b))
    diff = [k for k in both if a[k] != b[k]]
    same = [k for k in both if a[k] == b[k]]

    print("A = %s\nB = %s\n" % (A, B))
    print("文件数: A=%d  B=%d   相同=%d  大小不同=%d  只在A=%d  只在B=%d"
          % (len(a), len(b), len(same), len(diff), len(only_a), len(only_b)))

    if diff:
        print("\n===== 大小不同（%d）=====" % len(diff))
        for k in diff:
            print("  %-58s A=%-9d B=%-9d" % (k, a[k], b[k]))

    if only_a:
        print("\n===== 只在 A（LM 自带独有，%d）=====" % len(only_a))
        for k in only_a[:60]:
            print("  %-58s %d" % (k, a[k]))
        if len(only_a) > 60:
            print("  ... 其余 %d 项省略" % (len(only_a) - 60))

    if only_b:
        print("\n===== 只在 B（官方独有，%d）=====" % len(only_b))
        for k in only_b[:60]:
            print("  %-58s %d" % (k, b[k]))
        if len(only_b) > 60:
            print("  ... 其余 %d 项省略" % (len(only_b) - 60))

    # 逐字节校验"大小相同"的那批里，GCUBridge.exe / service.ini 是否真的同一份
    import hashlib

    def sha(path):
        h = hashlib.sha256()
        with io.open(path, 'rb') as f:
            for chunk in iter(lambda: f.read(1 << 20), b''):
                h.update(chunk)
        return h.hexdigest()

    print("\n===== 关键文件逐字节校验 =====")
    for rel in ("GCUBridge.exe", "service.ini", "install.bat", "uninstall.bat"):
        pa, pb = os.path.join(A, rel), os.path.join(B, rel)
        if os.path.isfile(pa) and os.path.isfile(pb):
            ha, hb = sha(pa), sha(pb)
            print("  %-20s %s  A=%s B=%s" % (rel, "同一份 ✅" if ha == hb else "不同 ❌", ha[:16], hb[:16]))
        else:
            print("  %-20s A存在=%s B存在=%s" % (rel, os.path.isfile(pa), os.path.isfile(pb)))
    return 0


if __name__ == '__main__':
    sys.exit(main())
