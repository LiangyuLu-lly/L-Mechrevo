# -*- coding: utf-8 -*-
"""查群友版 DisplayNative 结构与 ToastForm 方法"""
import re

# 群友版 DisplayNative 相关类型
s = open(r'D:\openLM\source\MechrevoLite.Display\DisplayNative.cs', encoding='utf-8', errors='replace').read()
print('=== 群友版 DisplayNative 类型 ===')
for m in re.finditer(r'(?:struct|class)\s+(\w+)|DISPLAYCONFIG_\w+', s):
    print(m.group(0) if m.group(0) else m.group(1))
print('len', len(s))

# beta21 DisplayNative 有没有 DISPLAYCONFIG_ADAPTER_NAME
b = open(r'D:\openLM\beta21\decomp\MechrevoLite.Display\DisplayNative.cs', encoding='utf-8', errors='replace').read()
print('=== beta21 DisplayNative ADAPTER_NAME / DisplayConfigGetDeviceInfo ===')
i = b.find('DISPLAYCONFIG_ADAPTER_NAME')
print(b[i-200:i+600] if i >= 0 else 'not found in beta21 DisplayNative')
for m in re.finditer(r'DisplayConfigGetDeviceInfo', b):
    print('call:', b[max(0,m.start()-100):m.end()+200].replace('\n',' '))
    break

# 群友版 ToastForm 方法
t = open(r'D:\openLM\source\MechrevoLite.Helpers\ToastForm.cs', encoding='utf-8', errors='replace').read()
print('=== 群友版 ToastForm 方法 ===')
for m in re.finditer(r'(?:public|private|internal|protected)\s+(?:static\s+)?(?:void|Task|bool|string)\s+(\w+)\s*\(', t):
    print(m.group(1))
