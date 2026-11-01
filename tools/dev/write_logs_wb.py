# -*- coding: utf-8 -*-
def append(path, text):
    with open(path, 'a', encoding='utf-8') as f:
        f.write(text)

mod = '''【2026-10-03 修复设置对话框开机自启动白底矩形 + 官方控制台折行回归】
1. 开机自启动白底矩形：
   - 现象：设置对话框"系统"标题右侧的开机自启动开关在浅色主题下呈纯白矩形，与对话框浅灰背景对比明显
   - 根因：RCheckBox 构造默认 BackColor = UiVisualStyle.Surface，浅色主题下 Surface 为纯白 (255,255,255)；设置对话框原生开关（跟随系统/匿名统计）创建时显式 BackColor = UiVisualStyle.Window（浅灰 242,245,249）——checkStartup 从 SettingsForm 移植进设置对话框时未同步设置，保持纯白
   - 修复：SettingsDialog 加入 systemHeader 前显式 startupCheck.BackColor = UiVisualStyle.Window（与原生开关一致）
2. 官方控制台"未检测到官方控制台"折行回归：
   - 根因：12:17:16 版把状态 Label 改为 AutoSize=true + Anchor=Right，TableLayout 单元格内 AutoSize 控件宽度被 Percent 列 clamp 导致换行
   - 修复：回退为 12:08:47 版方案（Dock=Fill + AutoEllipsis + 固定列 110/140）——该方案在实机已验证单行完整
- 编译 0 错误；提权替换完成（主 DLL 1895936B + zh-CN 资源 @12:36:12，LM 已重启）

'''

append(r'D:\openLM\修改日志.txt', mod)
print('log ok')
