# -*- coding: utf-8 -*-
def append(path, text):
    with open(path, 'a', encoding='utf-8') as f:
        f.write(text)

mod = '''【2026-10-03 返工：开机自启动开关在设置对话框不显示（漏了可见性）】
- 现象：上版（12:08:47）已把 checkStartup 移入设置对话框"系统"标题右侧，但实机截图设置对话框内不显示
- 根因：SettingsForm.BuildDashboardLayout 残留 `checkStartup.Visible = false;`（旧版本栏隐藏逻辑，4176 行）——控件移入设置对话框后仍保持不可见
- 修复：① SettingsForm.cs 删除该隐藏行；② SettingsDialog 加入 systemHeader 前显式 startupCheck.Visible = true（双保险）
- 附带确认：官方控制台"未检测到官方控制台"折行修复在 12:08:47 版已生效（实机截图单行完整显示）
- 编译 0 错误；提权替换完成（主 DLL + zh-CN 资源 @12:17:16，LM 已重启）

'''

append(r'D:\openLM\修改日志.txt', mod)
print('log ok')
