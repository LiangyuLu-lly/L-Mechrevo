# -*- coding: utf-8 -*-
"""加 NoticeCallback 字段"""
p = r'D:\openLM\source\MechrevoLite.Helpers\ToastForm.cs'
t = open(p, encoding='utf-8', errors='replace').read()
old = 'internal static Action<string>? FailureCallback;'
assert t.count(old) == 1
t = t.replace(old, old + '\n\n	internal static Action<string>? NoticeCallback;', 1)
open(p, 'w', encoding='utf-8', newline='').write(t)
print('OK')
