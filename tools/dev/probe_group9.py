# -*- coding: utf-8 -*-
"""修复：ToastForm 加 ShowNotice + DisplayNative 加 DISPLAYCONFIG_ADAPTER_NAME"""
import re

# ---------- ToastForm ----------
t = open(r'D:\openLM\source\MechrevoLite.Helpers\ToastForm.cs', encoding='utf-8', errors='replace').read()
print('=== ToastForm 字段区 ===')
for m in re.finditer(r'(FailureCallback|NoticeCallback|Callback)\s*\{', t):
    print('...', t[max(0, m.start()-120):m.end()+120].replace('\n', ' '))
    print('---')

# ---------- DisplayNative 位置 ----------
d = open(r'D:\openLM\source\MechrevoLite.Display\DisplayNative.cs', encoding='utf-8', errors='replace').read()
# DISPLAYCONFIG_SOURCE_DEVICE_NAME 结构结束位置（找下一个 struct 或 DllImport 区）
i = d.find('struct DISPLAYCONFIG_SOURCE_DEVICE_NAME')
print('=== SOURCE_DEVICE_NAME 结构 ===')
print(d[i:i+700])
