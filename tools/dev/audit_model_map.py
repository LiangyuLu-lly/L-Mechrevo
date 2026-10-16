# -*- coding: utf-8 -*-
"""确认抓包文件机型归属 + 关键字段值"""
import re, glob, os

FILES = sorted(glob.glob(r'D:\openLM\patches\*.log'))
FIELDS = ['IGpuOnlyConnectionSwitch_Support', 'DiscreteGpuDirectConnectionSwitch_Support',
          'DGpuDirectConnectionSupport', 'iGPUModeOnlySupport', 'IGpuAutoSupportSwitch',
          'GpuHotSwapSwitchSupport', 'lgpuHotSwapSwitchStatus', 'IsNvGpu', 'IsAMDPlatform']

def vals_of(blob, fld):
    vals = []
    for m in re.finditer(re.escape(fld) + r'"\s*:\s*"([^"]+)"', blob):
        if m.group(1) not in vals:
            vals.append(m.group(1))
    return vals

for f in FILES:
    blob = open(f, encoding='utf-8', errors='replace').read()
    model = ''
    m = re.search(r'(翼龙|蛟龙|耀世|苍龙|极光|旷世|泰坦|曙光|猎影|擎天|隐星|天启)[^\n]{0,25}', blob)
    if m:
        model = m.group(0).strip()
    pid = re.search(r'ProjectID"?\s*[:=]\s*"?([^\s",]+)', blob)
    line = [os.path.basename(f)]
    if model:
        line.append('机型:' + model[:30])
    if pid:
        line.append('ProjectID:' + pid.group(1))
    for fld in FIELDS:
        vals = vals_of(blob, fld)
        if vals:
            line.append(fld + '=' + '|'.join(vals))
    print(' | '.join(line))
