# -*- coding: utf-8 -*-
r"""
按目录分析 30 补丁 vs 底座的重复情况 + ACPI/OSD 类文件评估
================================================================================
N = payload\30\AiStoneService       （30 补丁）
G = payload\GCU\AiStoneService      （底座）
输出：
  1) 每个目录：30 补丁文件数 / 与底座【相同】数 / 【相似(大小差≤4字节)】数 / 【不同】数 / 我可省字节
  2) ACPI/OSD 相关文件清单及其在底座里的对应情况
"""
import io
import os
import sys
import hashlib

sys.stdout.reconfigure(encoding="utf-8", errors="replace")

N = r'D:\openLM\installer-src\files\GCU\payload\30\AiStoneService'
G = r'D:\openLM\installer-src\files\GCU\payload\GCU\AiStoneService'


def walk(root):
    out = {}
    for dirpath, _d, filenames in os.walk(root):
        for name in filenames:
            full = os.path.join(dirpath, name)
            out[os.path.relpath(full, root)] = os.path.getsize(full)
    return out


_sha_cache = {}


def sha(path):
    if path not in _sha_cache:
        h = hashlib.sha256()
        try:
            with io.open(path, 'rb') as f:
                for chunk in iter(lambda: f.read(1 << 20), b''):
                    h.update(chunk)
        except OSError:
            h = None
        _sha_cache[path] = h.hexdigest() if h else '?'
    return _sha_cache[path]


def main():
    n = walk(N)
    g = walk(G)
    stat = {}
    for rel, size in n.items():
        d = os.path.dirname(rel) or '(根)'
        s = stat.setdefault(d, {'n': 0, 'same': 0, 'near': 0, 'diff': 0, 'only': 0,
                                'same_b': 0, 'near_b': 0})
        s['n'] += 1
        if rel not in g:
            s['only'] += 1
            continue
        gs = g[rel]
        if size == gs and sha(os.path.join(N, rel)) == sha(os.path.join(G, rel)):
            s['same'] += 1
            s['same_b'] += size
        elif abs(size - gs) <= 4:
            s['near'] += 1
            s['near_b'] += size
        else:
            s['diff'] += 1

    print("按目录（30 补丁 vs 底座）：  相同 = 逐字节一致（可删）；相似 = 大小差≤4字节；只在 = 底座没有")
    print("%-42s %5s %5s %5s %5s %5s  %10s" % ("目录", "总数", "相同", "相似", "不同", "只在", "可省(相同)"))
    tot_same_b = 0
    for d in sorted(stat, key=lambda k: -stat[k]['same_b']):
        s = stat[d]
        tot_same_b += s['same_b']
        if s['same'] or s['near'] or s['only']:
            print("%-42s %5d %5d %5d %5d %5d  %9.2f KB" % (
                d, s['n'], s['same'], s['near'], s['diff'], s['only'], s['same_b'] / 1024.0))
    print("\n逐字节相同总量 = %.2f MB" % (tot_same_b / 1048576.0))

    print("\n===== ACPI / OSD 相关文件（30 补丁 vs 底座）=====")
    keys = ('acpi', 'osd', 'uwairplane', 'uwdisk', 'uw', 'displayinfo', 'outputdisk', 'registryutils')
    for rel in sorted(n):
        base = os.path.basename(rel).lower()
        if not any(k in base for k in keys):
            continue
        gs = g.get(rel)
        if gs is None:
            state = "底座没有"
        elif gs == n[rel] and sha(os.path.join(N, rel)) == sha(os.path.join(G, rel)):
            state = "与底座逐字节相同(可删)"
        elif gs == n[rel]:
            state = "同大小但内容不同"
        else:
            state = "大小不同 %d vs %d" % (n[rel], gs)
        print("  %9.2f KB  %-46s %s" % (n[rel] / 1024.0, rel, state))
    return 0


if __name__ == '__main__':
    sys.exit(main())
