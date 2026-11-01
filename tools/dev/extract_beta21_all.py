# -*- coding: utf-8 -*-
"""提取 beta21 全部移植素材到 D:\openLM\gpu_port_material.txt"""
import re, io

out = []

def dump(tag, text):
    out.append('===== ' + tag + ' =====')
    out.append(text)
    out.append('')

s = open(r'D:\openLM\beta21\decomp\MechrevoLite\SettingsForm.cs', encoding='utf-8', errors='replace').read()

def full_method(name, pattern):
    for m in re.finditer(pattern, s):
        start = m.start()
        depth = 0
        i = start
        while i < len(s):
            if s[i] == '{':
                depth += 1
            elif s[i] == '}':
                depth -= 1
                if depth == 0:
                    break
            i += 1
        dump(name, s[start:i + 1])
        return

full_method('SwitchGpuModeFromUi', r'(?:private|public|internal|protected)[^\n]*SwitchGpuModeFromUi\(')
full_method('ApplyGpuRowLayout', r'(?:private|public|internal|protected)[^\n]*ApplyGpuRowLayout\(')
full_method('SetNvPreferredGpuFromUi', r'(?:private|public|internal|protected)[^\n]*SetNvPreferredGpuFromUi\(')
full_method('RefreshGpuModeUiStateAsync', r'(?:private|public|internal|protected)[^\n]*RefreshGpuModeUiStateAsync\(')
full_method('RefreshDeviceCapabilities', r'(?:private|public|internal|protected)[^\n]*RefreshDeviceCapabilities\(')

# GpuRowLayouts.Resolve 调用处（初始化）
dump('GpuRowLayouts.Resolve 调用', '\n'.join(
    s[max(0, m.start() - 220):m.end() + 220].replace('\n', ' ')
    for m in re.finditer(r'GpuRowLayouts\.Resolve', s)))

# GPUModeControl GpuRowLayout
g = open(r'D:\openLM\beta21\decomp\MechrevoLite.Gpu\GPUModeControl.cs', encoding='utf-8', errors='replace').read()
dump('GPUModeControl.cs GpuRowLayout 上下文', '\n'.join(
    g[max(0, m.start() - 150):m.end() + 150].replace('\n', ' ')
    for m in re.finditer(r'GpuRowLayout', g)))
dump('GPUModeControl.cs 全文', g)

# MechrevoHw GpuRowLayout
h = open(r'D:\openLM\beta21\decomp\MechrevoLite.Hardware\MechrevoHw.cs', encoding='utf-8', errors='replace').read()
dump('MechrevoHw.cs GpuRowLayout 上下文', '\n'.join(
    h[max(0, m.start() - 150):m.end() + 150].replace('\n', ' ')
    for m in re.finditer(r'GpuRowLayout', h)))

# Strings 键（中英）
for f in ('Strings.resx', 'Strings.zh-CN.resx'):
    p = r'D:\openLM\beta21\decomp\MechrevoLite.Properties\%s' % f
    try:
        r = open(p, encoding='utf-8', errors='replace').read()
    except Exception as e:
        out.append('STRINGS %s ERR %s' % (f, e))
        continue
    keys = re.findall(r'<data name="([^"]+)"', r)
    want = [k for k in keys if any(x in k for x in ('Gpu', 'GPU', 'Route', 'Igpu', 'Pref'))]
    dump('Strings.%s GPU 相关键' % f, '\n'.join(want))

io.open(r'D:\openLM\gpu_port_material.txt', 'w', encoding='utf-8', newline='').write('\n'.join(out))
print('written', len('\n'.join(out)), 'chars')
