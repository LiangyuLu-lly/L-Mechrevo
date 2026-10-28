# -*- coding: utf-8 -*-
"""汇总所有存档日志里 Keyboard/Status 的 solution 上报值，并关联 GCU 代系线索。
   用法: py -3 scan_solution.py
"""
import io
import os
import re
import collections

ROOTS = [
    r'D:\openLM\patches',
    r'D:\openLM\diagnostics',
    r'D:\openLM\_diag_compare',
    r'D:\openLM\_diag_20261003',
    r'D:\openLM\_diag_20261003_30',
    r'D:\openLM\tmp_diag_102155',
    r'D:\openLM\_re_decomp_924',
    r'D:\openLM\backup',
]

RE_SOL = re.compile(r'"solution"\s*:\s*"([^"]*)"')
RE_TYPE = re.compile(r'"type"\s*:\s*"([^"]*)"')
RE_PROJ = re.compile(r'ProjectID["\s:=]+(\d+)')
RE_VAR = re.compile(r'GcuVariant["\s:=]+(\w+)')
RE_KB = re.compile(r'Keyboard/Status')


def scan(path):
    sols = collections.Counter()
    types = collections.Counter()
    projs = collections.Counter()
    variants = collections.Counter()
    kb_lines = 0
    try:
        with io.open(path, encoding='utf-8', errors='replace') as fh:
            for line in fh:
                for m in RE_SOL.finditer(line):
                    sols[m.group(1)] += 1
                if RE_KB.search(line):
                    kb_lines += 1
                    for m in RE_TYPE.finditer(line):
                        types[m.group(1)] += 1
                for m in RE_PROJ.finditer(line):
                    projs[m.group(1)] += 1
                for m in RE_VAR.finditer(line):
                    variants[m.group(1)] += 1
    except Exception as e:
        return None
    if not (sols or projs or variants):
        return None
    return dict(sols=sols, types=types, projs=projs, variants=variants, kb=kb_lines)


def main():
    rows = []
    for root in ROOTS:
        if not os.path.isdir(root):
            continue
        for dirpath, _dirs, files in os.walk(root):
            for name in files:
                if not name.lower().endswith(('.log', '.txt')):
                    continue
                p = os.path.join(dirpath, name)
                try:
                    if os.path.getsize(p) > 60 * 1024 * 1024:
                        continue
                except OSError:
                    continue
                r = scan(p)
                if r:
                    rows.append((p, r))
    for p, r in rows:
        rel = p.replace(r'D:\openLM\\', '')
        print('=' * 96)
        print(rel)
        print('  Keyboard/Status 行数: %d' % r['kb'])
        print('  solution : %s' % (dict(r['sols']) or '-'))
        print('  type     : %s' % (dict(r['types']) or '-'))
        print('  ProjectID: %s' % (dict(r['projs']) or '-'))
        print('  GcuVariant: %s' % (dict(r['variants']) or '-'))
    print('=' * 96)
    print('共扫描到 %d 个含相关字段的日志文件' % len(rows))


if __name__ == '__main__':
    main()
