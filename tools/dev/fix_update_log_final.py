# -*- coding: utf-8 -*-
"""更新日志修复：9/30 备份(完好 UTF-8) + 当前文件(GBK 区无损 + UTF-8 区) 组装为全 UTF-8 干净版"""
import shutil, io

BACKUP = r'D:\Downloads\更新日志.txt'
CUR = r'D:\openLM\更新日志.txt'
OUT = r'D:\openLM\更新日志.txt'

# 1. 备份文件（完好 UTF-8）按行读取
with open(BACKUP, encoding='utf-8') as f:
    bk = f.read().splitlines()

# 按备份 Read 行号提取（Read 行号 = 文本行号，1-based；splitlines 后 0-based 少 1）
def seg(a, b):  # Read 行号区间含端点
    return '\n'.join(bk[a-1:b]) + '\n'

p1_18_1 = seg(1, 129)        # 18.1 完整段
p2_18_2 = seg(130, 166)      # 18.2
p3_18_3 = seg(167, 208)      # 18.3
p4_31_add = seg(210, 234)    # 追加 0.289.3.1 后续维护
p5_33 = seg(236, 248)        # 追加 0.289.3.3 语言跟随
p6_18_4 = seg(382, 505)      # 18.4 + 六~十四（跳过 250-380 整合版重复）

# 2. 十五~十七（当前文件损坏区，按语义修复后的权威文本）
p7_15_17 = '''十五、校色档位修复 / 显示类记录 / 限充三档回退（2026-10-01，0.289.6 beta18.6）
────────────────────────────────────────────
1. 屏幕校色：
   - 修复广色域机型（耀世/苍龙等 4 档屏）校色配置下拉只显示 2 项的问题：
     配置档位现随 GCU 实时上报自动重建（1/2/4 档：默认 / sRGB / P3 / AdobeRGB）；
   - 新增「开机自动应用校色」开关：校色行新增设置按钮，弹窗内可开启；
     默认关闭，一般无需开启，仅当重启后校色无法保持时使用。
2. 色彩增强：
   - 记忆上次的开启状态与所选模式；
   - 开机自启时若色彩增强为开启，将自动应用并恢复所选模式。
3. 电池限充：
   - 回退为三档模式（长效 / 平衡 / 工作站）；
   - 无极调节滑块暂不可用，待后续修复。
4. 版本号 0.289.5.4 → 0.289.6（beta18.6）。

十六、性能模式切换稳定性修复（2026-10-01，0.289.6）
────────────────────────────────────────────
1. 修复快速连续点击切换性能模式时误报「性能模式切换失败」。
2. 切换后档位显示与硬件实际状态同步（失败/被取代时按硬件回读结果刷新）。

十七、色彩增强中英文翻译补全（2026-10-01，0.289.6）
────────────────────────────────────────────
1. 色彩增强配置下拉补全英文翻译：
   Vivid / Internet / Video / Low Blue / Cinema / Photo
   （中文：多彩 / 上网 / 视频 / 低蓝光 / 影院 / 照片）。
2. 「编辑」按钮补全英文翻译（Edit），中英文界面均可正常显示。

'''

# 3. 十八~三十二：当前文件 GBK 区（LF lines 247-342）
with open(CUR, 'rb') as f:
    cb = f.read()
cl = cb.split(b'\n')
gbk_lines = []
for i in range(247, 343):
    raw = cl[i].rstrip(b'\r')
    if raw == b'':
        gbk_lines.append('')
    else:
        gbk_lines.append(raw.decode('gb18030'))
p8_18_32 = '\n'.join(gbk_lines) + '\n'

# 4. 睡眠省电（编号改三十三）+ 〇系列及 10-03 追加：当前文件 UTF-8 区（LF lines 344-413）
utf8_lines = []
for i in range(344, min(414, len(cl))):
    raw = cl[i].rstrip(b'\r')
    utf8_lines.append(raw.decode('utf-8'))
p9_tail = '\n'.join(utf8_lines) + '\n'
# 睡眠省电标题编号修正：十三 → 三十三
p9_tail = p9_tail.replace('十三、睡眠省电（Sleep Guard）', '三十三、睡眠省电（Sleep Guard）')

# 组装
out = (p1_18_1 + '\n' + p2_18_2 + '\n' + p3_18_3 + '\n' + p4_31_add + '\n'
       + p5_33 + '\n' + p6_18_4 + '\n' + p7_15_17 + p8_18_32 + '\n' + p9_tail)

# 先备份旧文件
shutil.copyfile(CUR, r'D:\openLM\更新日志_旧版含乱码备份.txt')

# 写回（UTF-8 无 BOM）
with open(OUT, 'w', encoding='utf-8', newline='\n') as f:
    f.write(out)
print('written, chars:', len(out))

# 验证
t = open(OUT, encoding='utf-8').read()
print('replacement chars:', t.count('\ufffd'))
for k in ['十八、内存占用优化', '三十三、睡眠省电', '问题反馈与匿名统计', '色彩增强中英文翻译补全', '整合版']:
    print(k, '->', t.count(k))
