# -*- coding: utf-8 -*-
r"""
30 系 payload 精简工具（三层，可复跑）
================================================================================
目标：D:\openLM\installer-src\files\GCU\payload\30\AiStoneService
依据：底座 payload\GCU **总是先铺**（Select-GcuPayload.ps1 明载），
      故 30 补丁里"与底座逐字节相同"的文件纯属重复。

三层：
  T1  与底座逐字节相同            -> 底座已提供，零风险删        （~11.5 MB）
  T2  文档/调试类 (.xml/.pdb)     -> 仅 IntelliSense/符号，零功能影响（~6.5 MB，与 T3 有重叠）
  T3  官方控制台专属 (GamingMonitor 等) -> GCU 无头服务不需要，**建议先验证**（~8 MB）

用法：
  py -3 trim_30_payload.py                 # dry-run，只列清单
  py -3 trim_30_payload.py --apply         # 真正执行（移到 backup，可还原）
  py -3 trim_30_payload.py --tiers T1,T2   # 只做某几层
"""
import io
import os
import sys
import shutil
import hashlib
import datetime

sys.stdout.reconfigure(encoding="utf-8", errors="replace")

N = r'D:\openLM\installer-src\files\GCU\payload\30\AiStoneService'
G = r'D:\openLM\installer-src\files\GCU\payload\GCU\AiStoneService'
BACKUP = r'D:\openLM\backup\gcu30_trim_' + datetime.datetime.now().strftime('%Y%m%d_%H%M%S')

# T3：官方控制台专属（游戏监控 / 覆盖层），GCU 无头服务不需要
T3_PREFIXES = (
    r'MyControlCenter\GamingMonitor',
    r'MyControlCenter\ICCProfile',
    r'MyControlCenter\DisplayProfile',
    r'MyControlCenter\AppSettings',
    r'MyControlCenter\KeyboardManager',
)
T3_EXTRA = (
    r'MyControlCenter\SharpDX.dll',
    r'MyControlCenter\SharpDX.RawInput.dll',
    r'MyControlCenter\SharpDX.RawInput.xml',
    r'MyControlCenter\NAudio.dll',
    r'MyControlCenter\NAudio.xml',
)


def walk(root):
    out = {}
    for dirpath, _d, filenames in os.walk(root):
        for name in filenames:
            full = os.path.join(dirpath, name)
            out[os.path.relpath(full, root)] = os.path.getsize(full)
    return out


def sha(path):
    h = hashlib.sha256()
    with io.open(path, 'rb') as f:
        for chunk in iter(lambda: f.read(1 << 20), b''):
            h.update(chunk)
    return h.hexdigest()


def main():
    apply_ = '--apply' in sys.argv
    tiers = set()
    for a in sys.argv:
        if a.startswith('--tiers'):
            tiers = set(a.split('=', 1)[1].split(',')) if '=' in a else {'T1', 'T2', 'T3'}
    if not tiers:
        tiers = {'T1', 'T2', 'T3'}

    n = walk(N)
    g = walk(G)
    picked = {}

    if 'T1' in tiers:
        for rel, size in n.items():
            if rel in g and size == g[rel] and sha(os.path.join(N, rel)) == sha(os.path.join(G, rel)):
                picked[rel] = ('T1', size)
    if 'T2' in tiers:
        for rel, size in n.items():
            if rel.lower().endswith(('.xml', '.pdb')):
                picked.setdefault(rel, ('T2', size))
    if 'T3' in tiers:
        for rel, size in n.items():
            if any(rel.startswith(p) for p in T3_PREFIXES) or rel in T3_EXTRA:
                picked.setdefault(rel, ('T3', size))
    if 'T4' in tiers:
        # 与底座"相似"（大小差 <=4 字节但非逐字节相同）——主要是一堆【别的机型】的风扇表，
        # 体积可忽略（~0.19 MB），删掉纯为"文件数清爽"；底座会提供近乎相同的版本。
        # ⚠️ 必须排除 UserFanTables 的【直接子文件】（那是本机的表：
        #    DefaultFanTable_* / M1T1..M3T5，正是"风扇曲线"功能用的，不能动）。
        fan_root = r'MyControlCenter\UserFanTables'
        for rel, size in n.items():
            if rel in picked or rel not in g:
                continue
            if rel.startswith(fan_root + '\\') and '\\' not in rel[len(fan_root) + 1:]:
                continue  # 本机的表，保留
            if abs(size - g[rel]) <= 4 and sha(os.path.join(N, rel)) != sha(os.path.join(G, rel)):
                picked.setdefault(rel, ('T4', size))

    by_tier = {}
    for rel, (t, size) in picked.items():
        by_tier.setdefault(t, []).append((rel, size))

    print("目标: %s" % N)
    print("现有: %d 个文件, %.2f MB\n" % (len(n), sum(n.values()) / 1048576.0))
    total = 0
    for t in ('T1', 'T2', 'T3', 'T4'):
        items = by_tier.get(t, [])
        s = sum(x[1] for x in items)
        total += s
        print("===== %s : %d 个, %.2f MB =====" % (t, len(items), s / 1048576.0))
        for rel, size in sorted(items, key=lambda x: -x[1])[:15]:
            print("   %9.2f KB  %s" % (size / 1024.0, rel))
        if len(items) > 15:
            print("   ... 其余 %d 个" % (len(items) - 15))
        print()
    print("合计可删: %d 个, %.2f MB   -> 剩余约 %.2f MB"
          % (len(picked), total / 1048576.0, (sum(n.values()) - total) / 1048576.0))

    if not apply_:
        print("\n（dry-run，未改动任何文件。加 --apply 执行，会移到 %s）" % BACKUP)
        return 0

    moved = 0
    for rel in picked:
        src = os.path.join(N, rel)
        dst = os.path.join(BACKUP, rel)
        os.makedirs(os.path.dirname(dst), exist_ok=True)
        shutil.move(src, dst)
        moved += 1
    print("\n已移动 %d 个文件到 %s" % (moved, BACKUP))
    left = walk(N)
    print("精简后: %d 个文件, %.2f MB" % (len(left), sum(left.values()) / 1048576.0))
    # 清理空目录
    for dirpath, dirnames, filenames in os.walk(N, topdown=False):
        if not dirnames and not filenames:
            try:
                os.rmdir(dirpath)
                print("  删除空目录 " + os.path.relpath(dirpath, N))
            except OSError:
                pass
    return 0


if __name__ == '__main__':
    sys.exit(main())
