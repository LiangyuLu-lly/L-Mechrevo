# -*- coding: utf-8 -*-
import re

def get(path, key):
    t = open(path, encoding='utf-8', errors='replace').read()
    m = re.search(r'<data name="' + key + r'" xml:space="preserve">(.*?)</data>', t, re.S)
    if not m:
        return 'NOT FOUND'
    v = re.search(r'<value>(.*?)</value>', m.group(1), re.S)
    return v.group(1).strip() if v else 'NO VALUE'

en = r'D:\openLM\source\MechrevoLite.Properties.Strings.resx'
zh = r'D:\openLM\source\MechrevoLite.Properties.Strings.zh-CN.resx'
print('en DisplayEffect =', get(en, 'DisplayEffect'))
print('en DisplayEffectVibrantMode =', get(en, 'DisplayEffectVibrantMode'))
print('en QuickCpuAdvPerf =', get(en, 'QuickCpuAdvPerf'))
print('en QuickCpuAdvPerfTip =', get(en, 'QuickCpuAdvPerfTip'))
print('zh QuickUsb =', get(zh, 'QuickUsb'))
print('zh QuickCpuAdvPerf =', get(zh, 'QuickCpuAdvPerf'))
print('zh QuickCpuAdvPerfTip =', get(zh, 'QuickCpuAdvPerfTip'))
