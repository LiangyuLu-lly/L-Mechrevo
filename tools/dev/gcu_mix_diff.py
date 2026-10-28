# -*- coding: utf-8 -*-
# diff 30系GCU vs 群友版50系GCU 文件清单
import os, io

d30 = r'D:\openLM\archive\cc_30\app\UniwillService'
d50 = r'D:\openLM\installer-src\files\GCU\payload\GCU\AiStoneService'

def walk(d):
    out = {}
    for r, ds, fs in os.walk(d):
        for f in fs:
            p = os.path.join(r, f)
            rel = os.path.relpath(p, d)
            out[rel] = os.path.getsize(p)
    return out

a = walk(d30)
b = walk(d50)

only30 = sorted(k for k in a if k not in b)
only50 = sorted(k for k in b if k not in a)
same = sorted(k for k in a if k in b and a[k] == b[k])
diff = sorted(k for k in a if k in b and a[k] != b[k])

out = io.open(r'D:\openLM\archive\gcu_mix_diff.txt', 'w', encoding='utf-8')
def dump(title, keys, base=None):
    out.write('\n===== ' + title + ' (' + str(len(keys)) + ') =====\n')
    for k in keys:
        sz = (a if base == 'a' else b).get(k, '')
        out.write(('%-80s %s\n' % (k, sz)))

dump('仅30系有（50系缺）', only30, 'a')
dump('仅50系有（30系缺）', only50, 'b')
dump('同名同大小（可共用）', same)
dump('同名不同大小（版本冲突）', diff)
out.close()
print('only30', len(only30), 'only50', len(only50), 'same', len(same), 'diff', len(diff))
