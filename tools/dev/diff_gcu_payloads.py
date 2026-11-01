# -*- coding: utf-8 -*-
r"""
GCU payload 交叉对比：底座(GCU) vs 30系 vs 40/50 差异文件
================================================================================
根 = D:\openLM\installer-src\files\GCU\payload
  GCU\  公共底座   30\  30系完整   40\  40系差异   50\  50系差异
找出：
  1) GCU 与 30 的文件集差异（只在GCU / 只在30 / 大小不同）
  2) 40/50 差异文件的清单，以及它们相对 GCU/30 中同路径文件的大小对比
"""
import io
import os
import sys

sys.stdout.reconfigure(encoding="utf-8", errors="replace")

ROOT = r'D:\openLM\installer-src\files\GCU\payload'


def walk(rel):
    base = os.path.join(ROOT, rel)
    out = {}
    if not os.path.isdir(base):
        return out
    for dirpath, _dirnames, filenames in os.walk(base):
        for name in filenames:
            full = os.path.join(dirpath, name)
            out[os.path.relpath(full, base)] = os.path.getsize(full)
    return out


def main():
    gcu = walk(r'GCU\AiStoneService')
    p30 = walk(r'30\AiStoneService')
    p40 = walk(r'40\AiStoneService')
    p50 = walk(r'50\AiStoneService')
    print("文件数: GCU=%d  30=%d  40=%d  50=%d\n" % (len(gcu), len(p30), len(p40), len(p50)))

    only_g = sorted(set(gcu) - set(p30))
    only_3 = sorted(set(p30) - set(gcu))
    both = set(gcu) & set(p30)
    diff = sorted(k for k in both if gcu[k] != p30[k])

    print("===== GCU(底座) vs 30 系 =====")
    print("  共有=%d  只在GCU=%d  只在30=%d  大小不同=%d" % (len(both), len(only_g), len(only_3), len(diff)))
    if diff:
        print("  -- 大小不同 --")
        for k in diff:
            print("     %-56s GCU=%-9d 30=%-9d" % (k, gcu[k], p30[k]))
    if only_3:
        print("  -- 只在 30 系（30 特有，共 %d）--" % len(only_3))
        for k in only_3[:40]:
            print("     %-56s %d" % (k, p30[k]))
    if only_g:
        print("  -- 只在底座 GCU（共 %d，前 40）--" % len(only_g))
        for k in only_g[:40]:
            print("     %-56s %d" % (k, gcu[k]))

    for label, d in (("40 系差异文件", p40), ("50 系差异文件", p50)):
        print("\n===== %s（共 %d）=====" % (label, len(d)))
        for k in sorted(d):
            a = gcu.get(k)
            b = p30.get(k)
            print("  %-52s 差异=%-9d 底座=%-9s 30系=%s" % (
                k, d[k],
                ("%d" % a) if a is not None else "无",
                ("%d" % b) if b is not None else "无"))
    return 0


if __name__ == '__main__':
    sys.exit(main())
