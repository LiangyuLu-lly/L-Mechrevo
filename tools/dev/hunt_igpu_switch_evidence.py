# -*- coding: utf-8 -*-
r"""
全盘搜「切核显」证据：哪些诊断包里记录了切到 iGPU 模式（mode 0）的动作
================================================================================
寻找证据行：
  SwitchGpuMode(0) -> <ACTION>            <- LM 实际发出的命令（最有价值）
  GpuMode -> 0 (dgpu=<...>, igpu=<...>)   <- 回读到核显模式
  route=IgpuOnly / NVRAM ... =2           <- 路由与待生效
"""
import io
import os
import re
import sys
import glob

sys.stdout.reconfigure(encoding="utf-8", errors="replace")

ROOTS = [r'D:\openLM\diagnostics', r'D:\openLM\_diag_compare',
         r'D:\openLM\archive\diag_unpacked', r'D:\openLM\tmp_diag_102155',
         r'D:\openLM\_diag_20261003', r'D:\openLM\_diag_20261003_30', r'D:\openLM\archive\存档_20260924']

PAT = re.compile(r'SwitchGpuMode\(0\)|GpuMode -> 0 |route=IgpuOnly|IGPU_ONLY_CONNECT_RB_ON\b|TOGGLE_IGPU|'
                 r'GPU route verify: target=0|IgpuSaving|SetIGpuOnlySaveMode', re.I)


def read(p):
    try:
        return io.open(p, encoding='utf-8-sig', errors='replace').read()
    except OSError:
        return ''


def main():
    seen = set()
    for root in ROOTS:
        cands = [root] + [d for d in glob.glob(os.path.join(root, '*')) if os.path.isdir(d)]
        for c in cands:
            si = os.path.join(c, '系统信息.txt')
            if not os.path.isfile(si):
                continue
            key = os.path.normcase(os.path.abspath(c))
            if key in seen:
                continue
            seen.add(key)
            t = read(si)
            m = re.search(r'^型号:\s*(.+)$', t, re.M)
            model = m.group(1).strip() if m else '?'
            hits = []
            for dp, _d, fs in os.walk(c):
                for f in fs:
                    if not (f.lower().endswith('.log') or f.lower() == 'log.txt'):
                        continue
                    for line in read(os.path.join(dp, f)).split('\n'):
                        if PAT.search(line):
                            hits.append(line.strip())
            if hits:
                print('=' * 80)
                print('机型: %s   (%s)' % (model, c))
                print('命中 %d 行：' % len(hits))
                for h in hits[:25]:
                    print('   ' + h[:200])
                if len(hits) > 25:
                    print('   ... 其余 %d 行' % (len(hits) - 25))
    print('\n=== 扫描完毕 ===')


if __name__ == '__main__':
    main()
