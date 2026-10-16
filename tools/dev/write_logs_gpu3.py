# -*- coding: utf-8 -*-
def append(path, text):
    with open(path, 'a', encoding='utf-8') as f:
        f.write(text)

mod = '''【2026-10-03 显卡模式切换提示框改三选项 + 待重启橙色高亮】
- 背景：原切换提示为 MessageBox 是/否两选项（是=重启切换；否=取消）
- 新方案（三选项）：
  1. 是：发送切换指令并立即重启（原逻辑保留：MarkGpuRestartPending + RequestGpuModeRestartOutcomeAsync + VisualiseGPUMode）
  2. 否：先发送切换指令（Program.service.SwitchGpuMode，不重启）→ 成功后写 gpu_mode/标记待重启（MarkGpuRestartPending）→ 界面以橙色高亮待切换目标模式；失败回滚配置并 Toast 提示
  3. 取消：不切换也不重启，仅刷新 UI 状态
- 待重启高亮实现：
  - RButton 新增 ActivatedColor 属性（默认 Color.Empty = 沿用 UiVisualStyle.Accent 蓝）；激活态填充色改为 activatedColor.IsEmpty ? Accent : activatedColor
  - VisualiseGPUMode 检测 HasPendingGpuRestart() 时：高亮按钮 = GpuRestartVerifier.PendingTarget（而非当前 route 推断值），三枚模式按钮 ActivatedColor = UiVisualStyle.Warn（橙色）；非待重启时恢复 Accent
- 文案：GpuRestartPrompt 更新为三选项说明（zh/en resx）
- beta21 对照：21 版仍是两选项（YesNo），但具备 GpuRestartVerifier 待重启状态体系与本版一致；21 的"点当前 pending 模式取消待重启"（CancelPendingGpuRestartAsync）本版未移植（当前版 service 无该接口，暂不做）
- 编译 0 错误；提权替换完成，LM 已重启

'''

append(r'D:\openLM\修改日志.txt', mod)

upd = '''【2026-10-03】〇、显卡模式切换交互
- 切换需重启的提示改为三选项：是=立即重启切换 / 否=先发送切换指令（重启后生效，界面以橙色高亮待切换状态，并显示"重启以应用"标记）/ 取消=不做更改
- 新增 RButton.ActivatedColor（待重启高亮用橙色，正常激活仍为蓝色）

'''

append(r'D:\openLM\更新日志.txt', upd)
print('logs ok')
