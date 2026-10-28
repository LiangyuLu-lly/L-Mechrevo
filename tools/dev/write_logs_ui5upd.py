# -*- coding: utf-8 -*-
def append(path, text):
    with open(path, 'a', encoding='utf-8') as f:
        f.write(text)

upd = '''【2026-10-03】〇、设置页修复（补录）
- 设置对话框"系统"标题右侧的开机自启动开关修复浅色主题下纯白白底（BackColor 对齐 UiVisualStyle.Window，与跟随系统/匿名统计开关一致）
- 官方控制台状态文字修复折行回归（恢复 Dock=Fill+AutoEllipsis 方案，"未检测到官方控制台"单行完整显示）

'''

append(r'D:\openLM\更新日志.txt', upd)
print('update log ok')
