# -*- coding: utf-8 -*-
"""1) 写修改日志（新建）+ 追加更新日志；2) 更新桌面差异表格（备份后改）"""
import io, shutil, openpyxl

now = '2026-10-03'
ver = '0.290.0'

# ============ 1) 修改日志 ============
MOD = r'D:\openLM\修改日志.txt'
mod_txt = """L-Mechrevo 修改日志
====================

（睡眠省电批次，__NOW__，__VER__）

一、新增 PowerSleepGuard.cs（MechrevoLite.Helpers）
- 注册表直写当前活动电源方案隐藏电源设置（HKLM\\...\\PowerSchemes\\<方案>\\<子组>\\<设置>）
- 6 项 GUID 映射：sleepnetoff（无子组）、sleepaway/sleepsysreq/sleeprtcwake/sleephibernate
  （睡眠子组 238c9fa8）、sleepdeep（深度睡眠子组 2e601130）
- ReadEnabled(key, ac) / SetEnabled(key, enabled, ac)：插电/离电分档读写
- 关闭值语义修正：sleepnetoff Off=2（由Windows管理）、sleeprtcwake Off=2（仅重要唤醒源）
  （原为 1=启用，经注册表枚举与官方语义确认后修正）

二、SettingsForm.cs 快捷开关扩展
- QuickSwitchGroups 3→4 组；array2 22→28 项；sleep 独立 handler；周期刷新与回读分支
- StaticQuick() 白名单加入 6 个 sleep 键：修复根因——GCU 能力过滤（visibleByKey）把
  注册表类开关判为"不支持"导致整组隐藏（曾误试 flowLayoutPanel AutoSize / PerformLayout，
  均为治标，已回退）
- 睡眠组标题行：label + 插电/离电分段按钮。排布经历 3 次调整：
  FlowLayoutPanel AutoSize（高度异常 60px、两按钮不等高）→ TableLayoutPanel 百分比列
  （19% 列过窄文本截断"插..."）→ 最终：三列 AutoSize + 按钮固定 Size(56,22) 等高同宽
- SetQuickSleepAc(bool)：切换档位后按 _syncingSwitches 防回环刷新 6 开关显示
- ToolTip：6 个睡眠开关悬停解释（SleepTipFor 按 key 返回文案）

三、资源
- Strings.resx / Strings.zh-CN.resx / Strings.cs 新增 8 键：
  SleepAc（插电时/AC）、SleepDc（离电时/DC）+ 6 个 QuickSleep*Tip（中英文）

四、部署脚本
- replace_sleepguard.ps1：主 DLL + zh-CN 卫星程序集双备份双复制
  （睡眠省电中文在 zh-CN\\L-Mechrevo.resources.dll，只复制主 DLL 会显示英文）
"""
mod_txt = mod_txt.replace('__NOW__', now).replace('__VER__', ver)
with io.open(MOD, 'w', encoding='utf-8', newline='') as f:
    f.write(mod_txt)
print('OK 修改日志')

# ============ 2) 更新日志（追加） ============
UPD = r'D:\openLM\更新日志.txt'
upd_entry = """

十三、睡眠省电（Sleep Guard）（__NOW__，__VER__）
====================================================================
- 新增"更多开关"第 4 组"睡眠省电"：6 项注册表直写开关
  （待机断开网络 / 封杀离开模式 / 忽略系统所需策略 / 禁用RTC唤醒定时器 /
   启用ACPI深度睡眠 / 30分钟转休眠），基于 OpenRevo"智能睡眠守护"抓包实现，
   直接读写当前电源方案的 ACSettingIndex/DCSettingIndex
- 组标题右侧新增插电时 / 离电时（AC/DC）分档切换：切换后 6 个开关
  按所选档位独立显示与读写，插电/离电两套设置互不影响
- 6 个开关支持悬停解释提示（ToolTip，中英文随界面语言）
- 关闭值对齐 Windows 语义：待机断开网络 关闭=由Windows管理(2)，
  禁用RTC唤醒定时器 关闭=仅重要唤醒源(2)（原为"启用(1)"）
"""
upd_entry = upd_entry.replace('__NOW__', now).replace('__VER__', ver)
with io.open(UPD, 'a', encoding='utf-8', newline='') as f:
    f.write(upd_entry)
print('OK 更新日志')

# ============ 3) 差异表格 ============
XLSX = r'C:\Users\xiaohan\Desktop\L-Mechrevo-作者beta21-vs-群友版0.289.6-差异对比.xlsx'
BAK = r'C:\Users\xiaohan\Desktop\L-Mechrevo-作者beta21-vs-群友版0.289.6-差异对比_备份%s.xlsx' % now.replace('-', '')
shutil.copy2(XLSX, BAK)
print('OK 表格备份:', BAK)

wb = openpyxl.load_workbook(XLSX)

# 版本概况：群友版最新
ws0 = wb['版本概况']
for r in range(1, ws0.max_row + 1):
    if ws0.cell(r, 1).value and '群友版最新' in str(ws0.cell(r, 1).value):
        ws0.cell(r, 2).value = '0.290.0（在 0.289.6 基础上累计，含 beta21 功能合并与睡眠省电等新功能）'
        print('OK 版本概况行', r)

# 功能对比：追加睡眠省电条目（沿用 6 列）
ws1 = wb['功能对比']
new_rows = [
    ('电源与系统', '睡眠省电 6 开关（注册表直写当前电源方案）', '无', '有（0.290.0）', '群友独有（OpenRevo 抓包实现）', '已加入'),
    ('电源与系统', '插电/离电（AC/DC）分档切换', '无', '有（0.290.0）', '群友独有', '已加入'),
    ('电源与系统', '睡眠开关悬停解释提示（ToolTip）', '无', '有（0.290.0）', '群友独有', '已加入'),
    ('电源与系统', '睡眠关闭值对齐 Windows 语义（由Windows管理/仅重要唤醒源）', '无', '有（0.290.0）', '群友独有', '已加入'),
]
start = None
for r in range(2, ws1.max_row + 1):
    if ws1.cell(r, 1).value in (None, ''):
        start = r
        break
if start is None:
    start = ws1.max_row + 1
for i, row in enumerate(new_rows):
    for c, v in enumerate(row, start=1):
        ws1.cell(start + i, c).value = v
print('OK 功能对比追加', start, '-', start + len(new_rows) - 1)

# 独有功能汇总：群友版独有列追加
ws2 = wb['独有功能汇总']
r2 = ws2.max_row
while r2 > 1 and (ws2.cell(r2, 1).value in (None, '') and ws2.cell(r2, 2).value in (None, '')):
    r2 -= 1
ws2.cell(r2 + 1, 2).value = '睡眠省电 6 开关（AC/DC 分档 + 悬停解释，0.290.0）'
print('OK 独有功能汇总追加')

wb.save(XLSX)
print('ALL DONE')
