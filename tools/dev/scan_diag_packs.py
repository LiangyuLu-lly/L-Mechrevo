# -*- coding: utf-8 -*-
r"""
扫描所有诊断包：机型 / 日志级别 / 是否含日志 / 有没有【切核显动作】的记录
================================================================================
关注：极光（Jiguang）系列
"""
import io
import os
import re
import sys
import glob

sys.stdout.reconfigure(encoding="utf-8", errors="replace")

ROOTS = [
    r'D:\openLM\diagnostics',
    r'D:\openLM\_diag_compare',
    r'D:\openLM\archive\diag_unpacked',
    r'D:\openLM\tmp_diag_102155',
    r'D:\openLM\_diag_20261003',
    r'D:\openLM\_diag_20261003_30',
]

GPU_PAT = re.compile(
    r'SwitchGpuMode|GpuMode\s*->|IGPU_ONLY|DGPU_DIRECT|GPU route|IgpuOnly|GpuSwitch|'
    r'route=IgpuOnly|TOGGLE_IGPU|核显省电|IgpuSaving',
    re.I)


def read(p):
    try:
        return io.open(p, encoding='utf-8-sig', errors='replace').read()
    except OSError:
        return ''


def main():
    seen = set()
    for root in ROOTS:
        # 找根目录本身 + 一级子目录（诊断包解压根）
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
            model = ''
            m = re.search(r'^型号:\s*(.+)$', t, re.M)
            if m:
                model = m.group(1).strip()
            lvl = ''
            m = re.search(r'日志级别[:：]\s*([^\s（(]+)', t)
            if m:
                lvl = m.group(1)
            nv = 'NVIDIA' in t
            igpu_line = ''
            m = re.search(r'^.*(集显模式|核显模式):.*$', t, re.M)
            if m:
                igpu_line = m.group(0).strip()

            logfiles = []
            for dp, _d, fs in os.walk(c):
                for f in fs:
                    if f.lower().endswith('.log') or f.lower() == 'log.txt':
                        logfiles.append(os.path.join(dp, f))

            hits = []
            for lf in logfiles:
                txt = read(lf)
                for line in txt.split('\n'):
                    if GPU_PAT.search(line):
                        hits.append(line.strip())

            print('=' * 78)
            print('目录   : %s' % c)
            print('机型   : %s' % (model or '(未解析)'))
            print('日志级别: %s' % (lvl or '(未解析)'))
            print('能力位 : %s' % (igpu_line or '(无集显/核显模式行)'))
            print('日志文件: %d 个 %s' % (len(logfiles), [os.path.basename(x) for x in logfiles]))
            print('切核显/切模式相关行: %d 行' % len(hits))
            for h in hits[:12]:
                print('    ' + h[:190])
            if len(hits) > 12:
                print('    ... 其余 %d 行' % (len(hits) - 12))


if __name__ == '__main__':
    main()
