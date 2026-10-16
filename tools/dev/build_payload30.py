# -*- coding: utf-8 -*-
# 落地 payload\30：从 30 系解包复制最小文件集（带 AiStoneService 前缀，剔 xml/pdb）
import os, io, shutil

d30 = r'D:\openLM\archive\unpacks\cc_30\app\UniwillService'
dst = r'D:\openLM\installer-src\files\GCU\payload\30'

# 复用 gcu_30_patch_plan.txt 清单（含"新增/覆盖"标记）
plan = io.open(r'D:\openLM\archive\refs\gcu_30_patch_plan.txt', encoding='utf-8').read().splitlines()
skip_ext = ('.xml', '.pdb')  # 纯文档/调试符号，运行时不需要
skip_dir = '/GamingMonitor/'  # 游戏监控组件，用户确认用不到，全部剔除
skip_name = ('SharpDX.dll', 'SharpDX.RawInput.dll', 'NAudio.dll')  # 游戏监控/OSD 相关，用户确认剔除

copied = 0
total = 0
for line in plan[1:]:
    if not line.strip():
        continue
    # 行格式: [类型] 路径  大小
    if not line.startswith('['):
        continue
    rel = line.split(']', 1)[1].split(None, 1)[0].strip()
    if rel.lower().endswith(skip_ext):
        continue
    if skip_dir in ('/' + rel + '/'):
        continue
    if os.path.basename(rel) in skip_name:
        continue
    src = os.path.join(d30, rel.replace('/', os.sep))
    tgt = os.path.join(dst, 'AiStoneService', rel.replace('/', os.sep))
    if not os.path.exists(src):
        print('MISSING SRC:', src)
        continue
    os.makedirs(os.path.dirname(tgt), exist_ok=True)
    shutil.copy2(src, tgt)
    copied += 1
    total += os.path.getsize(src)

print('copied:', copied, 'files,', round(total/1048576, 2), 'MB ->', dst)
