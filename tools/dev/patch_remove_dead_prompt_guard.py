# -*- coding: utf-8 -*-
"""
清理死代码：`ShowGpuRestartPromptAsync` 里的"取代/去重"守卫（用户指出）
================================================================================
用户推理（正确）：
  `MessageBox.Show(this, ...)` 是 **owner 模态** -> 弹窗未关时 LM 主 UI 收不到点击
  -> 不可能有第二次切换请求进来 -> 于是：
    * `requestSeq != _gpuSwitchRequestSeq` 永远不成立（序号就是本次刚加的）
    * `_gpuRestartPromptOpen` 永远是 false（没有别的提示能同时开着）
  且该弹窗是**点击切换瞬间同步弹出**，中间没有延迟窗口。
=> 两处判断均为死代码，删除；只保留诊断日志（requestSeq 参数仅供日志使用）。
   `_gpuSwitchRequestSeq` 字段与 `_gpuRestartPromptOpen` 字段**保留** ——
   它们仍被 `WatchGcuRestartFallbackAsync`（那个 15s 看门狗）使用 ✅
行尾：SettingsForm.cs 是 CRLF。
"""
import io
import os
import sys

F = r'D:\openLM\source\MechrevoLite\SettingsForm.cs'

OLD = (b'\t\tLogger.WriteLine($"GpuRestartPrompt: seq={requestSeq} latest={_gpuSwitchRequestSeq} open={_gpuRestartPromptOpen}");\r\n'
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
       b'\t\t}\r\n')

NEW = (b'\t\t// 2026-10-05: no supersede/dedupe guard here on purpose. This dialog is shown\r\n'
       b'\t\t// synchronously and is owner-modal, so no other request can arrive while it is\r\n'
       b'\t\t// open (the guard that used to be here was dead code). The 15 s fallback\r\n'
       b'\t\t// watchdog in WatchGcuRestartFallbackAsync is where supersede/dedupe is needed.\r\n'
       b'\t\tLogger.WriteLine($"GpuRestartPrompt: seq={requestSeq} latest={_gpuSwitchRequestSeq}");\r\n'
       b'\t\tDialogResult dialogResult = MessageBox.Show(string.Format(Strings.GpuRestartPrompt, modeName), "L-Mechrevo", MessageBoxButtons.YesNoCancel, MessageBoxIcon.Exclamation);\r\n')


def main():
    data = io.open(F, 'rb').read()
    if b'no supersede/dedupe guard here on purpose' in data:
        print('已经应用过了，无需重复。')
        return 0
    n = data.count(OLD)
    if n != 1:
        alt = OLD.replace(b'\r\n', b'\n')
        if data.count(alt) == 1:
            print('  (行尾适配)')
            io.open(F, 'wb').write(data.replace(alt, NEW.replace(b'\r\n', b'\n'), 1))
            print('  写入 ' + F)
            return 0
        print('=== 校验失败：锚点命中 %d 次(期望 1)，未写盘 ===' % n)
        return 1
    print('=== 锚点校验通过 ===')
    if '--check' in sys.argv:
        return 0
    io.open(F, 'wb').write(data.replace(OLD, NEW, 1))
    print('  写入 ' + F)
    return 0


if __name__ == '__main__':
    sys.exit(main())
