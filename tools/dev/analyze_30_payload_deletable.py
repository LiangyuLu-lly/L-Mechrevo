# -*- coding: utf-8 -*-
r"""
新的 30 系 payload 分析：找可删文件
================================================================================
底座 payload\GCU 总是先铺 -> 30 补丁里 **与底座逐字节相同的文件可删**（底座已提供）
另外标记：文档类(.xml/.pdb/.txt/.md)、官方控制台专属(GamingMonitor 等)
对比对象：
  N = D:\openLM\installer-src\files\GCU\payload\30\AiStoneService
  G = D:\openLM\installer-src\files\GCU\payload\GCU\AiStoneService   （底座）
  O = C:\Program Files\OEM\机械革命电竞控制台\UniwillService          （官方 30 系）
"""
import io
import os
import sys
import hashlib

sys.stdout.reconfigure(encoding="utf-8", errors="replace")

N = r'D:\openLM\installer-src\files\GCU\payload\30\AiStoneService'
G = r'D:\openLM\installer-src\files\GCU\payload\GCU\AiStoneService'
O = r'C:\Program Files\OEM\机械革命电竞控制台\UniwillService'


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
    n = walk(N)
    g = walk(G)
    o = walk(O)
    print("文件数: 30补丁=%d   底座=%d   官方30系=%d" % (len(n), len(g), len(o)))
    print("体积  : 30补丁=%.2f MB  底座=%.2f MB  官方30系=%.2f MB"
          % (sum(n.values()) / 1048576.0, sum(g.values()) / 1048576.0, sum(o.values()) / 1048576.0))

    same_as_base, diff_from_base, only_n = [], [], []
    for rel, size in n.items():
        if rel not in g:
            only_n.append(rel)
            continue
        if size != g[rel]:
            diff_from_base.append(rel)
            continue
        if sha(os.path.join(N, rel)) == sha(os.path.join(G, rel)):
            same_as_base.append(rel)
        else:
            diff_from_base.append(rel)

    print("\n===== 相对底座 =====")
    print("  与底座逐字节【相同】-> 底座已提供，30 补丁里【可删】: %d 个  %.2f MB"
          % (len(same_as_base), sum(n[r] for r in same_as_base) / 1048576.0))
    print("  与底座【不同】-> 必须保留                            : %d 个  %.2f MB"
          % (len(diff_from_base), sum(n[r] for r in diff_from_base) / 1048576.0))
    print("  底座【没有】-> 必须保留                              : %d 个  %.2f MB"
          % (len(only_n), sum(n[r] for r in only_n) / 1048576.0))

    # 可删清单（按体积降序，前 40）
    if same_as_base:
        print("\n----- 可删（与底座相同）体积最大的 40 个 -----")
        for rel in sorted(same_as_base, key=lambda r: -n[r])[:40]:
            print("  %9.2f KB  %s" % (n[rel] / 1024.0, rel))

    # 文档/调试类
    doc = [r for r in n if r.lower().endswith(('.xml', '.pdb', '.txt', '.md', '.log', '.config'))]
    print("\n===== 文档/调试类（通常可删）: %d 个  %.2f MB =====" % (len(doc), sum(n[r] for r in doc) / 1048576.0))
    for rel in sorted(doc, key=lambda r: -n[r])[:25]:
        print("  %9.2f KB  %s" % (n[rel] / 1024.0, rel))

    # 官方控制台专属（LM 的 GCU 服务不需要的）
    prefixes = ('GamingMonitor', 'ICCProfile', 'DisplayProfile', 'AppSettings',
                'KeyboardManager', 'PreinstallKit', 'Command')
    for pre in prefixes:
        hits = [r for r in n if r.startswith(pre + os.sep) or r.startswith(pre)]
        if hits:
            print("  [%s] %d 个  %.2f MB" % (pre, len(hits), sum(n[r] for r in hits) / 1048576.0))

    # 与官方对比：30 补丁是否等于官方
    same_as_off = [r for r in n if r in o and n[r] == o[r] and sha(os.path.join(N, r)) == sha(os.path.join(O, r))]
    print("\n===== 相对官方 30 系 =====")
    print("  与官方逐字节相同: %d 个  %.2f MB" % (len(same_as_off), sum(n[r] for r in same_as_off) / 1048576.0))
    print("  官方没有的      : %d 个" % len([r for r in n if r not in o]))
    print("  官方有但补丁没带: %d 个" % len([r for r in o if r not in n]))
    return 0


if __name__ == '__main__':
    sys.exit(main())
