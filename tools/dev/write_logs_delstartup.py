# -*- coding: utf-8 -*-
def append(path, text):
    with open(path, 'a', encoding='utf-8') as f:
        f.write(text)

mod = '''【2026-10-03 删除主窗"更多开关-电源与系统"里的开机自启动项】
- 背景：开机自启动已移至设置对话框"系统"标题右侧（12:17:16 版），主窗 QuickActions 里的 startup 项冗余，且该项存在与其他开关一样的首次显示白底矩形问题
- 删除范围（SettingsForm.cs 6 处）：
  1. QuickSwitchGroups 电源组 "startup" 键（数组 12→11）
  2. QuickActions 定义 ("startup", Strings.QuickStartup)（数组 28→27）
  3. BuildQuickRow 的 startup 分支（读 ScheduledState + ApplyScheduledState 切换逻辑）
  4. RefreshQuickSwitch 的 startup 刷新分支
  5. GetQuickSwitchState 的 startup 分支
  6. 能力开关 switch 的 case "startup"
- 保留：设置对话框 checkStartup（全新宿主）、Startup 类、RefreshStartupStatusAsync/CheckStartup_Click（checkStartup 专属逻辑）
- 白底矩形调查结论：RCheckBox 单套自绘（OnPaint 一次 FillRectangle+轨道+滑块，无两套绘制）；更多开关 FlowLayoutPanel 背景 = 外层面板色（随主题）；各快捷开关无重复 Add。startup 项删除后如其他项不再出现白底则根源即该项；若仍有项出现，再针对性处理
- 编译 0 错误；提权替换完成（主 DLL 1895936B + zh-CN 资源 @12:25:03，LM 已重启）

'''

append(r'D:\openLM\修改日志.txt', mod)
print('log ok')
