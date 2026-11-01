# -*- coding: utf-8 -*-
# 30系完整GCU vs 50系完整GCU（基础树 + 50 trio 覆盖）
import os, io

d30 = r'D:\openLM\archive\cc_30\app\UniwillService'
gcu_base = r'D:\openLM\installer-src\files\GCU\payload\GCU'
gcu_50 = r'D:\openLM\installer-src\files\GCU\payload\50'

def walk(d):
    out = {}
    for r, ds, fs in os.walk(d):
        for f in fs:
            p = os.path.join(r, f)
            rel = os.path.relpath(p, d)
            out[rel] = os.path.getsize(p)
    return out

# 50系完整 = 基础树 + 50 trio 覆盖（trio 以 AiStoneService\MyControlCenter 为前缀）
base = walk(gcu_base)
trio = walk(gcu_50)
b = dict(base)
for k, v in trio.items():
    b[k] = v

a = walk(d30)

only30 = sorted(k for k in a if k not in b)
only50 = sorted(k for k in b if k not in a)
same = sorted(k for k in a if k in b and a[k] == b[k])
diff = sorted(k for k in a if k in b and a[k] != b[k])

out = io.open(r'D:\openLM\archive\gcu_30_vs_50_full_diff.txt', 'w', encoding='utf-8')
def dump(title, keys, which):
    out.write('\n===== ' + title + ' (' + str(len(keys)) + ') =====\n')
    for k in keys:
        s = a.get(k) if which == 'a' else b.get(k)
        out.write(('%-80s %s\n' % (k, s)))

dump('仅30系有（50系缺）', only30, 'a')
dump('仅50系有（30系缺）', only50, 'b')
dump('同名同大小（完全相同）', same, 'a')
dump('同名不同大小（版本差异）', diff, 'a')
out.close()
print('only30', len(only30), 'only50', len(only50), 'same', len(same), 'diff', len(diff))
