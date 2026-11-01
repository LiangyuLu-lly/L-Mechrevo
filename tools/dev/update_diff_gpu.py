# -*- coding: utf-8 -*-
"""差异表更新：命名限长、显卡切换高亮 → 已加入（绿），整体重排三段"""
from openpyxl import load_workbook
from openpyxl.styles import PatternFill
import shutil

SRC = r'D:\openLM\差异对比_已更新.xlsx'
DST = r'C:\Users\xiaohan\Desktop\L-Mechrevo-作者beta21-vs-群友版0.289.6-差异对比.xlsx'

RED = PatternFill('solid', fgColor='FDE9E9')
YEL = PatternFill('solid', fgColor='FDF3DC')
GRN = PatternFill('solid', fgColor='E9F3EC')

wb = load_workbook(SRC)
ws = wb['功能对比']

rows = []
for r in range(3, ws.max_row + 1):
    vals = [ws.cell(r, c).value for c in range(1, 7)]
    if any(v is not None for v in vals[:5]):
        rows.append(vals)

def find_row(rows, key):
    for i, row in enumerate(rows):
        if key in str(row[1]):
            return i
    return -1

# 1) 命名限长行 → 已加入
i = find_row(rows, '命名写回确认')
print('命名限长 idx:', i, rows[i] if i >= 0 else None)
if i >= 0:
    rows[i] = ['性能模式', '自定义档命名写回确认（≤12 字符）', '有', '有（0.290.0 合并）', '已加入（作者版合并）', '已加入']

# 2) 显卡切换高亮行 → 已加入
i = find_row(rows, '显卡切换按钮高亮')
print('显卡高亮 idx:', i, rows[i] if i >= 0 else None)
if i >= 0:
    rows[i] = ['显卡模式', '显卡切换按钮高亮按硬件实际状态/未确认态', '有（按硬件状态）', '有（0.290.0 移植 GpuRowLayout 体系）',
               '已移植：GpuRowLayout 布局分流 + 路由回读解析 + GpuRestartVerifier 重启确认闭环 + 状态行', '已加入']

# 3) 三段重排：未加入(红) → 有差异(黄) → 已加入(绿)
def seg(rows, status):
    return [r for r in rows if r[5] == status]

red_rows = seg(rows, '未加入')
yel_rows = seg(rows, '有差异')
grn_rows = seg(rows, '已加入')
new_rows = red_rows + yel_rows + grn_rows
print('red:', len(red_rows), 'yel:', len(yel_rows), 'grn:', len(grn_rows), 'total:', len(new_rows))

# 4) 写入
# 清空旧数据区
for r in range(3, ws.max_row + 1):
    for c in range(1, 7):
        ws.cell(r, c).value = None
        ws.cell(r, c).fill = PatternFill(fill_type=None)

for j, row in enumerate(new_rows):
    r = 3 + j
    for c in range(1, 7):
        ws.cell(r, c).value = row[c - 1]
    st = row[5]
    fill = RED if st == '未加入' else (YEL if st == '有差异' else GRN)
    ws.cell(r, 6).fill = fill

# 5) 版本概况 sheet 更新说明（若第 5 行存在）
ws2 = wb['版本概况']
for r in range(1, ws2.max_row + 1):
    v = ws2.cell(r, 2).value
    if v and '0.290.0' in str(v):
        print('版本概况 row', r, ':', v)
        break

wb.save(SRC)
# 写回桌面
try:
    shutil.copy2(SRC, DST)
    print('copy2 OK ->', DST)
except PermissionError as e:
    print('PERMISSION ERROR:', e)
print('DONE')
