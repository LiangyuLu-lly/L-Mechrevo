# -*- coding: utf-8 -*-
"""提取各抓包中 ConnectionSwitch 能力字段的值"""
import re, glob, os

FILES = glob.glob(r'D:\openLM\archive\refs\patches\*.log')
FILES = sorted(set(f for f in FILES if os.path.exists(f)))

FIELDS = ['IGpuOnlyConnectionSwitch_Support', 'IGpuAutoSupportSwitch',
          'DiscreteGpuDirectConnectionSwitch_Support', 'AcRecoverySwitch_Support',
          'DGpuDirectConnectionSupport', 'GpuHotSwapSwitchSupport',
          'lgpuHotSwapSwitchStatus', 'iGPUModeOnlySupport', 'IGpuOnlyModeSupport']

def val_of(blob, fld):
    """取字段后的值（同段内冒号/等号后到行尾或逗号）"""
    vals = []
    for m in re.finditer(re.escape(fld), blob):
        seg = blob[m.start(): m.start() + 200]
        seg = seg.split('\n')[0]
        vm = re.search(re.escape(fld) + r'\s*[:=]\s*([^\s",\]]+)', seg)
        if vm:
            v = vm.group(1)
            if v not in vals:
                vals.append(v)
    return vals

for f in FILES:
    blob = open(f, encoding='utf-8', errors='replace').read()
    found = []
    for fld in FIELDS:
        vals = val_of(blob, fld)
        if vals:
            found.append(f'  {fld} = {vals}')
    if found:
        print('=' * 60)
        print('文件:', os.path.basename(f), f'({len(blob)} B)')
        print('\n'.join(found))
