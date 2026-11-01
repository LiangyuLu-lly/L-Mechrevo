# -*- coding: utf-8 -*-
"""提取各抓包能力字段值（JSON 引号格式）"""
import re, glob, os

FILES = glob.glob(r'D:\openLM\archive\refs\patches\*.log')
FILES = sorted(set(f for f in FILES if os.path.exists(f)))

FIELDS = ['IGpuOnlyConnectionSwitch_Support', 'IGpuOnlyConnectionSwitch_Status',
          'IGpuAutoSupportSwitch', 'DiscreteGpuDirectConnectionSwitch_Support',
          'AcRecoverySwitch_Support', 'DGpuDirectConnectionSupport',
          'GpuHotSwapSwitchSupport', 'lgpuHotSwapSwitchStatus',
          'iGPUModeOnlySupport', 'IGpuOnlyModeSupport', 'MuxSwitchSupport',
          'ColorCalibrationSupport', 'SupportColorCalibration']

def vals_of(blob, fld):
    vals = []
    # JSON 格式 "字段":"值"
    for m in re.finditer(re.escape(fld) + r'"\s*:\s*"([^"]+)"', blob):
        if m.group(1) not in vals:
            vals.append(m.group(1))
    # 冒号/等号直接跟值
    for m in re.finditer(re.escape(fld) + r'\s*[:=]\s*([^\s",\]]+)', blob):
        if m.group(1) not in vals:
            vals.append(m.group(1))
    return vals

for f in FILES:
    blob = open(f, encoding='utf-8', errors='replace').read()
    found = []
    for fld in FIELDS:
        vals = vals_of(blob, fld)
        if vals:
            found.append(f'  {fld} = {vals}')
    if found:
        print('=' * 60)
        print('文件:', os.path.basename(f))
        print('\n'.join(found))
