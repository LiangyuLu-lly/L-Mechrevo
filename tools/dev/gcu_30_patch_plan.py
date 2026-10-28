# -*- coding: utf-8 -*-
# 生成"30系最小复制方案"：从 30 系 GCU 复制到 50 系基础树的文件清单
# 原则：只复制 30系独有 + 必须替换版本的文件（install/uninstall.bat 保留50系版）
import os, io

d30 = r'D:\openLM\archive\cc_30\app\UniwillService'
gcu_base = r'D:\openLM\installer-src\files\GCU\payload\GCU'

def walk(d):
    out = {}
    for r, ds, fs in os.walk(d):
        for f in fs:
            p = os.path.join(r, f)
            rel = os.path.relpath(p, d).replace('\\', '/')
            out[rel] = os.path.getsize(p)
    return out

def norm(d):
    out = {}
    for k, v in d.items():
        if k.startswith('AiStoneService/'):
            out[k[len('AiStoneService/'):]] = v
        else:
            out[k] = v
    return out

a = norm(walk(d30))      # 30系（归一化）
b = norm(walk(gcu_base)) # 50系基础树（归一化）

# 复制清单 = 30系独有 + 同名不同版本(排除 install/uninstall.bat)
skip_keep_50 = {'install.bat', 'uninstall.bat'}
need_copy = []
for k in sorted(a):
    if k not in b:
        need_copy.append((k, a[k], '新增'))
    elif a[k] != b[k] and k not in skip_keep_50:
        need_copy.append((k, a[k], '覆盖'))

total = sum(v for _, v, _ in need_copy)
out = io.open(r'D:\openLM\archive\gcu_30_patch_plan.txt', 'w', encoding='utf-8')
out.write('30系最小复制方案（复制到 50系基础树）共 %d 个文件，%.2f MB\n\n' % (len(need_copy), total/1048576))
for k, v, t in need_copy:
    out.write('[%s] %-78s %s\n' % (t, k, v))
out.close()
print('files:', len(need_copy), 'total:', round(total/1048576, 2), 'MB')
