# -*- coding: utf-8 -*-
"""
Task B proof: line-number diff between the pre-edit backup and the edited file.
Classifies every changed line as comment / blank / CODE, and asserts that the
multiset of executable code lines is unchanged (i.e. only comments were touched).
"""
import difflib
import io

OLD = r'D:\openLM\backup\pkgs\Select-GcuPayload.ps1_pre_v2907finish'
NEW = r'D:\openLM\installer-src\files\GCU\Select-GcuPayload.ps1'


def read(p):
    return io.open(p, encoding='ascii', newline='').read().split('\r\n')


old = read(OLD)
new = read(NEW)


def classify(lines):
    """-> list of ('comment'|'blank'|'code', line) using block-comment state."""
    out = []
    in_block = False
    for ln in lines:
        s = ln.strip()
        if in_block:
            out.append(('comment', ln))
            if '#>' in s:
                in_block = False
            continue
        if '<#' in s:
            in_block = True
            out.append(('comment', ln))
            continue
        if s.startswith('#'):
            out.append(('comment', ln))
        elif not s:
            out.append(('blank', ln))
        else:
            out.append(('code', ln))
    return out


co, cn = classify(old), classify(new)

sm = difflib.SequenceMatcher(a=[l for _, l in co], b=[l for _, l in cn], autojunk=False)
blocks = [b for b in sm.get_opcodes() if b[0] != 'equal']

print('=' * 78)
print('LINE DIFF  %s' % OLD.split('\\')[-1])
print('=' * 78)
print('old: %d lines   new: %d lines' % (len(old), len(new)))
print('changed hunks: %d' % len(blocks))
print('')

n_comment = n_blank = n_code = 0
code_del, code_ins = [], []

for tag, i1, i2, j1, j2 in blocks:
    print('--- %s  old %d-%d -> new %d-%d ---' % (tag, i1 + 1, i2, j1 + 1, j2))
    for k in range(i1, i2):
        kind, ln = co[k]
        if kind == 'comment':
            n_comment += 1
        elif kind == 'blank':
            n_blank += 1
        else:
            n_code += 1
            code_del.append(ln)
        print('  -[%s] %s' % (kind, ln))
    for k in range(j1, j2):
        kind, ln = cn[k]
        if kind == 'comment':
            n_comment += 1
        elif kind == 'blank':
            n_blank += 1
        else:
            n_code += 1
            code_ins.append(ln)
        print('  +[%s] %s' % (kind, ln))
    print('')

print('=' * 78)
print('SUMMARY OF CHANGED LINES')
print('=' * 78)
print('  comment lines changed : %d' % n_comment)
print('  blank   lines changed : %d' % n_blank)
print('  CODE    lines changed : %d' % n_code)

if code_del or code_ins:
    print('')
    print('  changed CODE lines - removed:')
    for l in code_del:
        print('    - %s' % l)
    print('  changed CODE lines - added:')
    for l in code_ins:
        print('    + %s' % l)

# independent fingerprint: sorted multiset of code lines
oc = sorted(l for k, l in co if k == 'code')
nc = sorted(l for k, l in cn if k == 'code')
print('')
print('code-line multiset old=%d new=%d  identical=%s' % (len(oc), len(nc), oc == nc))
print('')
if n_code == 0 and oc == nc:
    print('RESULT: PASS - not one executable line changed; only comment/blank lines differ.')
else:
    print('RESULT: FAIL - code lines changed, investigate.')
