# -*- coding: utf-8 -*-
"""
修「显卡模式切换弹两次重启提示」+ 用户要求的废弃语义
================================================================================
用户决策：「如果在弹窗前点击其他模式了直接废弃这次的弹窗」
根因（两个触发点，见修改日志 01:26）：
  :6663 if (plan.Route == GpuSwitchRoute.NoChange) { if (HasPendingGpuRestart()) 弹 }
  :6673 case GpuSwitchRoute.Restart: if (AllowsRestart(gen)) 弹
  `MessageBox` 模态但带消息循环 -> 提示开着时后续点击仍被处理 -> 会再弹一个
实现（双守卫 + 真值日志）：
  1. 新增全局请求序号 `_gpuSwitchRequestSeq`：每次 UI 切换请求 +1
     `SwitchGpuModeFromUi` 入口记下本次序号 requestSeq
  2. `ShowGpuRestartPromptAsync` 增加 requestSeq 参数；**弹窗前**：
     - `requestSeq != _gpuSwitchRequestSeq` -> 已被更新的请求取代 -> **废弃** ✅（用户要求）
     - `_gpuRestartPromptOpen` -> 已有提示在显示 -> **丢弃** ✅（去重）
     - 只在 `MessageBox.Show` 前后置/清该标志（不包裹整个方法体，改动最小）
  3. 在 `GpuSwitchPolicy.Resolve` 之后打一行真值日志：
     route / current / gen / allowsRestart / pendingRestart / seq
行尾：SettingsForm.cs 是 CRLF。
"""
import io
import os
import sys

F = r'D:\openLM\source\MechrevoLite\SettingsForm.cs'

OPS = []


def op(old, new, label, expect=1):
    OPS.append((old, new, expect, label))


# ---- 1) 字段 ----
op(b'\tprivate bool _gpuSwitchInProgress;\r\n',
   b'\tprivate bool _gpuSwitchInProgress;\r\n'
   b'\r\n'
   b'\t// 2026-10-05: monotonic id of the latest GPU-mode UI request. A pending restart\r\n'
   b'\t// prompt whose id is stale (the user picked another mode meanwhile) is discarded,\r\n'
   b'\t// and only one prompt may be on screen at a time.\r\n'
   b'\tprivate static int _gpuSwitchRequestSeq;\r\n'
   b'\r\n'
   b'\tprivate static bool _gpuRestartPromptOpen;\r\n',
   '新增序号与提示标志字段')

# ---- 2) 入口记序号 ----
op(b'\tprivate async Task SwitchGpuModeFromUi(int targetMode, string modeName)\r\n'
   b'\t{\r\n'
   b'\t\tif (Program.service != null)\r\n',
   b'\tprivate async Task SwitchGpuModeFromUi(int targetMode, string modeName)\r\n'
   b'\t{\r\n'
   b'\t\tint requestSeq = ++_gpuSwitchRequestSeq;\r\n'
   b'\t\tif (Program.service != null)\r\n',
   '入口记请求序号')

# ---- 3) 真值日志 ----
op(b'\t\t\t\tGpuSwitchPlan gpuSwitchPlan = GpuSwitchPolicy.Resolve(Program.service.CurrentGpuMode, Program.hw.GpuSwitchResult, targetMode, gpuModeStatusReadback == GpuModeStatusReadback.Fresh);\r\n',
   b'\t\t\t\tGpuSwitchPlan gpuSwitchPlan = GpuSwitchPolicy.Resolve(Program.service.CurrentGpuMode, Program.hw.GpuSwitchResult, targetMode, gpuModeStatusReadback == GpuModeStatusReadback.Fresh);\r\n'
   b'\t\t\t\tLogger.WriteLine($"GpuSwitch UI: target={targetMode} route={gpuSwitchPlan.Route} current={Program.service.CurrentGpuMode} gen={Program.hw.DgpuGeneration} allowsRestart={DisplayRouteMatrix.AllowsRestart(Program.hw.DgpuGeneration)} pendingRestart={HasPendingGpuRestart()} seq={requestSeq}");\r\n',
   '加真值日志')

# ---- 4) 两个调用点传序号 ----
op(b'\t\t\t\t\t\tawait ShowGpuRestartPromptAsync(targetMode, modeName);\r\n',
   b'\t\t\t\t\t\tawait ShowGpuRestartPromptAsync(targetMode, modeName, requestSeq);\r\n',
   '调用点传序号', expect=2)

# ---- 5) 提示函数：签名 + 双守卫（只替换前三行，不包裹方法体）----
op(b'\tprivate async Task<bool> ShowGpuRestartPromptAsync(int targetMode, string modeName)\r\n'
   b'\t{\r\n'
   b'\t\tDialogResult dialogResult = MessageBox.Show(string.Format(Strings.GpuRestartPrompt, modeName), "L-Mechrevo", MessageBoxButtons.YesNoCancel, MessageBoxIcon.Exclamation);\r\n',
   b'\tprivate async Task<bool> ShowGpuRestartPromptAsync(int targetMode, string modeName, int requestSeq)\r\n'
   b'\t{\r\n'
   b'\t\tLogger.WriteLine($"GpuRestartPrompt: seq={requestSeq} latest={_gpuSwitchRequestSeq} open={_gpuRestartPromptOpen}");\r\n'
   b'\t\tif (requestSeq != _gpuSwitchRequestSeq)\r\n'
   b'\t\t{\r\n'
   b'\t\t\tLogger.WriteLine("GpuRestartPrompt: superseded by a newer mode request -> discarded");\r\n'
   b'\t\t\treturn false;\r\n'
   b'\t\t}\r\n'
   b'\t\tif (_gpuRestartPromptOpen)\r\n'
   b'\t\t{\r\n'
   b'\t\t\tLogger.WriteLine("GpuRestartPrompt: another prompt is already open -> discarded");\r\n'
   b'\t\t\treturn false;\r\n'
   b'\t\t}\r\n'
   b'\t\tDialogResult dialogResult;\r\n'
   b'\t\t_gpuRestartPromptOpen = true;\r\n'
   b'\t\ttry\r\n'
   b'\t\t{\r\n'
   b'\t\t\tdialogResult = MessageBox.Show(string.Format(Strings.GpuRestartPrompt, modeName), "L-Mechrevo", MessageBoxButtons.YesNoCancel, MessageBoxIcon.Exclamation);\r\n'
   b'\t\t}\r\n'
   b'\t\tfinally\r\n'
   b'\t\t{\r\n'
   b'\t\t\t_gpuRestartPromptOpen = false;\r\n'
   b'\t\t}\r\n',
   '提示函数加双守卫')


def main():
    data = io.open(F, 'rb').read()
    if b'_gpuSwitchRequestSeq' in data:
        print('已经应用过了，无需重复。')
        return 0
    resolved = []
    errors = []
    for old, new, expect, label in OPS:
        if data.count(old) == expect:
            resolved.append((old, new, expect, label))
            continue
        alt_old = old.replace(b'\r\n', b'\n') if b'\r\n' in old else old.replace(b'\n', b'\r\n')
        alt_new = new.replace(b'\r\n', b'\n') if b'\r\n' in new else new.replace(b'\n', b'\r\n')
        if data.count(alt_old) == expect:
            print('  (行尾适配) ' + label)
            resolved.append((alt_old, alt_new, expect, label))
            continue
        errors.append('命中 %d/%d 次(期望 %d)  [%s]' % (data.count(old), data.count(alt_old), expect, label))
    if errors:
        print('=== 校验失败，未写盘 ===')
        for e in errors:
            print('  ! ' + e)
        return 1
    print('=== 锚点校验全部通过（%d 项）===' % len(resolved))
    if '--check' in sys.argv:
        return 0
    for old, new, expect, label in resolved:
        data = data.replace(old, new, expect)
    io.open(F, 'wb').write(data)
    print('  写入 ' + F)
    return 0


if __name__ == '__main__':
    sys.exit(main())
