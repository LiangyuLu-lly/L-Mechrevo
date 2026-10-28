# -*- coding: utf-8 -*-
def append(path, text):
    with open(path, 'a', encoding='utf-8') as f:
        f.write(text)

mod = '''【2026-10-03 设置页 5 项 UI/文案调整】
1. 色彩增强英文名：Strings.resx DisplayEffect "Vivid" → "Vibrancy"（多彩模式 DisplayEffectVibrantMode 保持 "Vivid" 不动）
2. 开机自启动位置：从主窗版本栏（panelVersion）移入设置对话框"系统"标题右侧
   - SettingsDialog 构造新增可选参数 startupCheck（默认 null，UiAuditRunner 调用不受影响）；"系统"标题行改为 2 列 TableLayoutPanel（标题 + 开关右对齐）
   - SettingsForm BuildDashboardLayout 删除 checkStartup→panelVersion 的移入逻辑（labelVersion 布局保留）
   - OpenSettingsDialog 传入 checkStartup
3. 官方控制台"未检测到官方控制台"折行修复：
   - 根因：设置对话框窄（470 逻辑宽），三列布局 126+150 固定列吃掉空间，状态列 <9 字宽，Label 默认换行导致"台"落第二行
   - 修复：固定列收窄 126→110、150→140；状态 Label 改 AutoSize=true + Anchor=Right（禁折行，宽度按内容自适应各 DPI）
4. 文案：QuickUsb "USB充电" → "关机USB供电"（zh-CN）
5. 文案+提示：QuickCpuAdvPerf "CPU高级性能" → "解锁BIOS高级菜单"（zh-CN）/ "CPU advanced performance" → "Unlock BIOS advanced menu"（en）；新增 QuickCpuAdvPerfTip 键（zh"为支持的机型解锁BIOS超频高级菜单"/en"Unlocks the BIOS overclocking advanced menu on supported models"），绑定到更多开关 cpuadvperf 项的悬停提示（复用 _quickSleepToolTip 实例）
- 编译返工记录：① Label 无 WordWrap 属性（TextBox 才有）——折行修复改用 AutoSize+Anchor；② resx 新键需同步 Strings.cs 强类型访问器（首次编译 CS0117，已补 QuickCpuAdvPerfTip 访问器）
- 编译 0 错误；提权替换完成（主 DLL 1896960B + zh-CN 资源 @12:08:47，LM 已重启）

'''

upd = '''【2026-10-03】〇、设置页调整
- 开机自启动开关移至设置对话框"系统"标题右侧（不再占用主窗版本栏）
- 官方控制台状态文字修复换行（"未检测到官方控制台"完整单行显示）
- 文案变更详见修改日志（色彩增强英文 Vibrancy、关机USB供电、解锁BIOS高级菜单+悬停提示）

'''

append(r'D:\openLM\修改日志.txt', mod)
append(r'D:\openLM\更新日志.txt', upd)
print('logs ok')
