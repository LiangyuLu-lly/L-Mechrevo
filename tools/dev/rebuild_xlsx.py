# -*- coding: utf-8 -*-
"""重建功能对比 sheet：未加入→有差异→已加入(绿色) 三段排序，去掉空白合并行"""
import openpyxl
from openpyxl.styles import Font, PatternFill, Alignment
from copy import copy

XLSX = r'D:\openLM\差异对比_已更新.xlsx'
wb = openpyxl.load_workbook(r'C:\Users\xiaohan\Desktop\L-Mechrevo-作者beta21-vs-群友版0.289.6-差异对比.xlsx')

old = wb['功能对比']

# 读取数据行（跳过空白行）
rows = []
for r in range(3, old.max_row + 1):
    vals = [old.cell(r, c).value for c in range(1, 7)]
    if all(v in (None, '') for v in vals):
        continue
    status = str(vals[5] or '')
    rows.append((status, vals))

# 三段排序（保持各自原有相对顺序）
groups = {'未加入': [], '有差异': [], '已加入': []}
for status, vals in rows:
    key = status if status in groups else '已加入'
    groups[key].append(vals)

order = groups['未加入'] + groups['有差异'] + groups['已加入']

# 重建 sheet
del wb['功能对比']
ws = wb.create_sheet('功能对比', 1)

# 标题行
ws.cell(1, 1, '功能对比：作者 beta21  vs  群友版 0.290.0 —— 未加入在上 · 有差异居中 · 已加入在底（绿）')
ws.merge_cells('A1:F1')
ws.cell(1, 1).font = Font(bold=True, size=12)
ws.cell(1, 1).alignment = Alignment(horizontal='center', vertical='center')
ws.row_dimensions[1].height = 24

# 列头
headers = ['模块', '功能项', '作者 beta21', '群友版', '说明 / 备注', '已加入']
for c, h in enumerate(headers, start=1):
    cell = ws.cell(2, c, h)
    cell.font = Font(bold=True)
    cell.alignment = Alignment(horizontal='center', vertical='center')
    cell.fill = PatternFill('solid', fgColor='D9E2F3')
ws.row_dimensions[2].height = 22

# 数据行（仅"已加入"状态列 F 列格子按状态标色，行其余列保持普通；配色参考备份版）
STATUS_FILL = {
    '未加入': PatternFill('solid', fgColor='FDE9E9'),
    '有差异': PatternFill('solid', fgColor='FDF3DC'),
    '已加入': PatternFill('solid', fgColor='E9F3EC'),
}
for i, vals in enumerate(order):
    r = 3 + i
    st = str(vals[5] or '')
    for c, v in enumerate(vals, start=1):
        cell = ws.cell(r, c, v)
        if c == 6 and st in STATUS_FILL:
            cell.fill = STATUS_FILL[st]
        cell.alignment = Alignment(vertical='center', wrap_text=(c == 2))

# 列宽
widths = [12, 46, 20, 30, 34, 10]
for c, w in enumerate(widths, start=1):
    ws.column_dimensions[openpyxl.utils.get_column_letter(c)].width = w

print('重建完成: 未加入=%d 有差异=%d 已加入=%d' % (
    len(groups['未加入']), len(groups['有差异']), len(groups['已加入'])))
print('总数据行:', len(order))

wb.save(XLSX)
print('SAVED')
