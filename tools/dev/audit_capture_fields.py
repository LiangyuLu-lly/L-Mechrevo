# -*- coding: utf-8 -*-
"""核对抓包 log 中 GCU 能力字段"""
import re, os, glob

FILES = glob.glob(r'D:\openLM\archive\refs\patches\*.log')
FILES += [r'D:\Downloads\gcu_display_capture3.log',
          r'D:\Downloads\gcu_gpu_saving_capture_20260930_002614.log']
FILES = sorted(set(f for f in FILES if os.path.exists(f)))

FIELDS = [
    'IGPuOnlyConnectSwitch_Support',
    'GpuHotSwapSwitchSupport',
    'lgpuHotSwapSwitchStatus',
    'iGPUModeOnlySupport',
    'IGpuOnlyModeSupport',
    'IntegratedGpuOnlySupport',
    'DGpuDirectConnectionSupport',
    'MuxSwitchSupport',
    'IGpuOnlyModeOnlySupport',
    'IgpuOnlyStatusSupport',
]

for f in FILES:
    print('=' * 60)
    print('文件:', os.path.basename(f), f'({os.path.getsize(f)} B)')
    try:
        blob = open(f, encoding='utf-8', errors='replace').read()
    except Exception as e:
        print('  读取失败:', e)
        continue
    hits = {}
    for fld in FIELDS:
        ms = list(re.finditer(re.escape(fld), blob))
        if ms:
            vals = set()
            for m in ms[:5]:
                seg = blob[m.start():m.start()+120].split('\n')[0]
                vm = re.search(re.escape(fld) + r'\s*[:=]\s*([^\s",\]]+)', seg)
                if vm:
                    vals.add(vm.group(1))
                else:
                    vals.add('<有字段名>')
            hits[fld] = vals
    if hits:
        for fld, vals in hits.items():
            print(f'  {fld} = {vals}')
    else:
        # 可能字段名有大小写差异，宽松搜
        low = blob.lower()
        for fld in FIELDS:
            if fld.lower() in low:
                print(f'  {fld} = <大小写差异出现>')
