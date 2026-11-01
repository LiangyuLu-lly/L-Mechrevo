# -*- coding: utf-8 -*-
r"""
① 三方对照：蛟龙16S / 极光Pro2024 / 翼龙15Pro(本机) 里 GCU 实际上报了哪些能力位
================================================================================
数据源（每台机器一份诊断包）：
  系统信息.txt   -> 机型 +「GCU 连通与能力位」段
  配置\config.json -> dgpu_generation / gcu_variant 等
  日志\log.txt   -> 运行时打印的能力日志（TrayGPU: / DescribeResolvedCapabilities 等）
输出：逐机型的字段对照，重点看 IGpuOnly / DgpuDirect / IGPU_ONLY_* 相关
"""
import io
import os
import re
import sys
import json

sys.stdout.reconfigure(encoding="utf-8", errors="replace")

MACHINES = [
    ('蛟龙16S',        r'D:\openLM\_diag_compare\jiaolong16s'),
    ('极光Pro2024',    r'D:\openLM\diagnostics\jiguang_pro2024_unpacked'),
    ('翼龙15Pro(本机)', r'D:\openLM\_diag_compare\yilong15pro'),
]

# 关注的能力字段（GcuCapabilities 里的名字 + GCU 状态回读字段名）
KEYS = [
    'IgpuOnly', 'DgpuDirect', 'GpuHotSwap', 'GpuModeSwitch', 'ThreeMode',
    'IGpuOnlyConnectionSwitch_Support', 'IGPUOnlyConnectionSwitch_Support', 'IGPU_ONLY_SUPPORT',
    'DgpuDirectStatusSupport', 'IgpuOnlyStatusSupport',
    'CanOfferIgpuOnly', 'SupportsIgpuOnly', 'SupportsDgpuDirect', 'SupportsGpuHotSwap',
    'CanOfferGpuModeSwitch', 'GpuMode',
]


def read(path):
    try:
        return io.open(path, encoding='utf-8-sig', errors='replace').read()
    except OSError:
        return ''


def main():
    for name, root in MACHINES:
        print('=' * 78)
        print('== %s   %s' % (name, root))
        print('=' * 78)
        if not os.path.isdir(root):
            print('  （目录不存在）\n')
            continue

        # 1) 系统信息.txt
        si = read(os.path.join(root, '系统信息.txt'))
        if si:
            for line in si.split('\n'):
                if re.search(r'型号|SKU|主板|应用版本|显示标签|管理员', line):
                    print('  [系统] ' + line.strip())

        # 2) config.json
        for cfgname in ('配置\\config.json', '配置\\config.fallback.json'):
            p = os.path.join(root, cfgname)
            if os.path.isfile(p):
                try:
                    j = json.loads(read(p))
                except Exception:
                    continue
                print('  [配置 %s]' % cfgname.split('\\')[-1])
                for k in sorted(j):
                    if re.search(r'gen|gcu|variant|gpu_mode|igpu|dgpu|mux', k, re.I):
                        print('      %-32s = %s' % (k, j[k]))
                break

        # 3) 日志里的能力行
        log = read(os.path.join(root, '日志\\log.txt'))
        if log:
            lines = log.split('\n')
            hits = []
            for i, l in enumerate(lines):
                if re.search(r'TrayGPU:|Capabilit|IGpuOnly|IgpuOnly|DgpuDirect|GpuHotSwap|gpuDev=', l):
                    hits.append(l.strip())
            # 去重取最后若干条
            seen, uniq = set(), []
            for h in reversed(hits):
                key = re.sub(r'\d+', '#', h)[:120]
                if key not in seen:
                    seen.add(key)
                    uniq.append(h)
                if len(uniq) >= 6:
                    break
            print('  [日志能力行] 命中 %d 行，最后 %d 条不同内容：' % (len(hits), len(uniq)))
            for h in reversed(uniq):
                print('      ' + h[:300])
        else:
            print('  [日志] 无 log.txt')
        print()

    # 4) 字段出现总览
    print('=' * 78)
    print('== 关键字段在各机型数据里的出现次数 ==')
    print('=' * 78)
    print('%-46s %10s %10s %10s' % ('字段', '蛟龙16S', '极光Pro', '翼龙15Pro'))
    blobs = []
    for _n, root in MACHINES:
        b = ''
        for rel in ('系统信息.txt', '日志\\log.txt', '配置\\config.json', '配置\\config.fallback.json'):
            b += read(os.path.join(root, rel))
        blobs.append(b)
    for k in KEYS:
        print('%-46s %10d %10d %10d' % (k, blobs[0].count(k), blobs[1].count(k), blobs[2].count(k)))
    return 0


if __name__ == '__main__':
    sys.exit(main())
