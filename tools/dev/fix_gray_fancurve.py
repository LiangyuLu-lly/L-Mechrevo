# -*- coding: utf-8 -*-
p = r'D:\openLM\source\MechrevoLite\CustomModeForm.cs'
t = open(p, encoding='utf-8').read()
old = '''			_fanBoostCombo, _refreshCombo,
			_gpuOcAdminRow, _fanBoostRow, _refreshRow'''
new = '''			_fanBoostCombo, _refreshCombo,
			_fanCurveButton, _gpuOcAdminRow, _fanBoostRow, _refreshRow'''
n = t.count(old)
print('match:', n)
if n != 1:
    raise SystemExit('ABORT')
open(p, 'w', encoding='utf-8', newline='').write(t.replace(old, new))
print('OK')
