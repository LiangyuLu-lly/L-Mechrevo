# -*- coding: utf-8 -*-
r"""
核对：30 补丁相对"原始完整官方版"缺失了哪些文件，以及底座提供的替代版本是否【不同】
================================================================================
原始完整版 = C:\Program Files\OEM\...\UniwillService     （279 个，与最初的 30 补丁逐字节相同）
当前 30 补丁  = payload\30\AiStoneService                  （用户手动瘦身后）
底座         = payload\GCU\AiStoneService

对每个"缺失的文件"分类：
  A) 底座没有            -> 安装后该文件彻底不存在 ⚠️⚠️ 高风险
  B) 底座有但【内容不同】-> 安装后用底座（50系）版本 ⚠️ 中风险（版本混用）
  C) 底座有且【逐字节相同】-> 无影响 ✅
"""
import io
import os
import sys
import hashlib

sys.stdout.reconfigure(encoding="utf-8", errors="replace")

ORIG = r'C:\Program Files\OEM\机械革命电竞控制台\UniwillService'
CUR = r'D:\openLM\installer-src\files\GCU\payload\30\AiStoneService'
BASE = r'D:\openLM\installer-src\files\GCU\payload\GCU\AiStoneService'


def walk(root):
    out = {}
    for dp, _d, fs in os.walk(root):
        for f in fs:
            full = os.path.join(dp, f)
            out[os.path.relpath(full, root)] = os.path.getsize(full)
    return out


_c = {}


def sha(p):
    if p not in _c:
        h = hashlib.sha256()
        try:
            with io.open(p, 'rb') as f:
                for chunk in iter(lambda: f.read(1 << 20), b''):
                    h.update(chunk)
        except OSError:
            h = None
        _c[p] = h.hexdigest() if h else '?'
    return _c[p]


def main():
    o, c, b = walk(ORIG), walk(CUR), walk(BASE)
    print("原始完整=%d   当前30补丁=%d   底座=%d\n" % (len(o), len(c), len(b)))
    missing = sorted(set(o) - set(c))
    print("相对原始完整版【缺失】%d 个：\n" % len(missing))

    group = {'A': [], 'B': [], 'C': []}
    for rel in missing:
        if rel not in b:
            group['A'].append(rel)
        elif b[rel] == o[rel] and sha(os.path.join(BASE, rel)) == sha(os.path.join(ORIG, rel)):
            group['C'].append(rel)
        else:
            group['B'].append(rel)

    def show(tag, title, note):
        items = group[tag]
        tot = sum(o[r] for r in items)
        print("===== %s : %d 个, %.2f MB —— %s =====" % (title, len(items), tot / 1048576.0, note))
        for rel in sorted(items, key=lambda r: -o[r])[:30]:
            extra = ''
            if tag == 'B':
                extra = '  (底座版 %d vs 原 %d)' % (b[rel], o[rel])
            print("   %9.2f KB  %s%s" % (o[rel] / 1024.0, rel, extra))
        if len(items) > 30:
            print("   ... 其余 %d 个" % (len(items) - 30))
        print()

    show('A', 'A 底座也没有 —— 安装后彻底缺失', '⚠️⚠️ 高风险')
    show('B', 'B 底座有但内容不同 —— 会用 50 系版本', '⚠️ 版本混用风险')
    show('C', 'C 底座有且逐字节相同 —— 无影响', '✅ 安全（等同我做的 T1）')
    return 0


if __name__ == '__main__':
    sys.exit(main())
