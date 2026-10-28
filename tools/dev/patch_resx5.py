# -*- coding: utf-8 -*-
import re

def patch_resx(path, replaces, inserts_after):
    t = open(path, encoding='utf-8', errors='replace').read()
    for key, old, new in replaces:
        pat = re.compile(r'(<data name="' + key + r'" xml:space="preserve">)(.*?)(</data>)', re.S)
        m = pat.search(t)
        assert m, 'key not found: ' + key
        block = m.group(2)
        assert old in block, f'value not found in {key}: {old!r}'
        block = block.replace(old, new)
        t = t[:m.start()] + m.group(1) + block + m.group(3) + t[m.end():]
    for key, value in inserts_after:
        pat = re.compile(r'(<data name="' + key + r'" xml:space="preserve">.*?</data>)', re.S)
        m = pat.search(t)
        assert m, 'anchor not found: ' + key
        new_block = '\n<data name="' + key + '_TIP_DUMMY" xml:space="preserve">\n    <value>' + value + '</value>\n  </data>'
        # 用目标键名替换占位
        anchor_key = inserts_after_anchor(key)
        new_block = new_block.replace('_TIP_DUMMY', anchor_key)
        t = t[:m.end()] + new_block + t[m.end():]
    open(path, 'w', encoding='utf-8', newline='').write(t)
    print(path.split('\\')[-1], 'patched')

def inserts_after_anchor(key):
    # key 为锚点（QuickCpuAdvPerf），新键 = QuickCpuAdvPerfTip
    return key + 'Tip'

# ---- 英文 resx ----
patch_resx(r'D:\openLM\source\MechrevoLite.Properties.Strings.resx', [
    ('DisplayEffect', 'Vivid', 'Vibrancy'),
    ('QuickCpuAdvPerf', 'CPU advanced performance', 'Unlock BIOS advanced menu'),
], [
    ('QuickCpuAdvPerf', 'Unlocks the BIOS overclocking advanced menu on supported models'),
])

# ---- 中文 resx ----
patch_resx(r'D:\openLM\source\MechrevoLite.Properties.Strings.zh-CN.resx', [
    ('QuickUsb', 'USB充电', '关机USB供电'),
    ('QuickCpuAdvPerf', 'CPU高级性能', '解锁BIOS高级菜单'),
], [
    ('QuickCpuAdvPerf', '为支持的机型解锁BIOS超频高级菜单'),
])
print('ALL RESX OK')
