# -*- coding: utf-8 -*-
"""核对各诊断包 GCU 能力字段上报情况"""
import zipfile, re, os, glob

packs = []
for pat in (r'D:\openLM\diagnostics\**\L-Mechrevo-诊断包*.zip',
            r'C:\Users\xiaohan\Desktop\L-Mechrevo-诊断包*.zip'):
    packs += glob.glob(pat, recursive=True)
packs = sorted(set(packs))

FIELDS = [
    'IGPuOnlyConnectSwitch_Support',      # 核显省电能力
    'GpuHotSwapSwitchSupport',            # 热切换能力1
    'lgpuHotSwapSwitchStatus',            # 热切换能力2
    'iGPUModeOnlySupport',                # 核显模式支持（候选1）
    'IGpuOnlyModeSupport',                # 核显模式支持（候选2）
    'IntegratedGpuOnlySupport',           # 核显模式支持（候选3）
    'DGpuDirectConnectionSupport',        # 独显直连
    'MuxSwitchSupport',                   # MUX
    'IsNvGpu',                            # N卡
    'IsAmdGpu',                           # A卡
]

def find_model(text):
    m = re.search(r'(翼龙|蛟龙|耀世|苍龙|极光|旷世|泰坦|曙光|猎影|擎天|曜影|隐星|天启|神舟|联想|机械革命|Mechrevo|L-Mechrevo)[^\n]{0,30}(15|16|17|18|14|13|Ultra|Pro)?[^\n]{0,20}', text)
    return m.group(0).strip() if m else '?'

for p in packs:
    print('=' * 60)
    print('包:', os.path.basename(p))
    try:
        z = zipfile.ZipFile(p)
    except Exception as e:
        print('  打开失败:', e)
        continue
    # 提取系统信息.txt + 日志/log.txt + config.json
    texts = []
    for name in z.namelist():
        bn = name.replace('\\', '/').split('/')[-1]
        if bn in ('系统信息.txt', 'log.txt', 'config.json', '内存日志缓冲.txt'):
            try:
                raw = z.read(name)
                for enc in ('utf-8', 'gbk', 'utf-16'):
                    try:
                        texts.append(raw.decode(enc))
                        break
                    except UnicodeDecodeError:
                        continue
            except Exception:
                pass
    blob = '\n'.join(texts)
    if not blob:
        print('  (无文本内容)')
        continue
    model = find_model(blob)
    print('  机型线索:', model)
    found = {}
    for f in FIELDS:
        for m in re.finditer(re.escape(f) + r'\s*[:=]\s*([^\s",\]]+)', blob):
            found[f] = m.group(1)
            break
        if f not in found:
            # 宽松：字段名出现即可（可能值在下一行）
            if f in blob:
                found[f] = '<出现但未匹配值>'
    for f in FIELDS:
        if f in found:
            print(f'  {f} = {found[f]}')
