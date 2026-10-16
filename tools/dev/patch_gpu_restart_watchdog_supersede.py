# -*- coding: utf-8 -*-
"""
🔴 真凶：`WatchGcuRestartFallbackAsync` 被重复派发 → 15 秒后各弹一个手动重启提示
================================================================================
截图证据：两个弹窗内容与按钮完全相同，都是
  `Strings.GpuRestartManualPrompt`（「GCU 未自动重启系统…点击「确定」5 秒后重启」）
  + `MessageBoxButtons.OKCancel`（确定/取消）—— 对应 SettingsForm:6855
  （上一轮我改的 `ShowGpuRestartPromptAsync` 用的是 `Strings.GpuRestartPrompt` + `YesNoCancel`，
    那是流程里的**第一个**弹窗，不是截图这个 —— 改错了地方，但那个守卫本身仍是对的，保留）
根因：
  :6840 `WatchGcuRestartFallbackAsync();`  —— **fire-and-forget，每次切换请求派发一个**
  方法内 `await Task.Delay(15000)` 后弹提示
  -> 用户做了 2 次切换请求 = 2 个独立看门狗 -> 15s 后**各弹一个** ✅ 与"分别弹了两个"完全吻合
修法（实现用户要求的"废弃"语义）：
  1. `WatchGcuRestartFallbackAsync(int requestSeq)` —— 派发时带上当时的请求序号
  2. 15s 后先比对：`requestSeq != _gpuSwitchRequestSeq` -> **已被更新的请求取代 -> 废弃** ✅
  3. `BeginInvoke` 的 lambda 内用 `_gpuRestartPromptOpen` 去重（UI 线程内检查/置位，原子）
行尾：SettingsForm.cs 是 CRLF。
"""
import io
import os
import sys

F = r'D:\openLM\source\MechrevoLite\SettingsForm.cs'

OPS = []


def op(old, new, label, expect=1):
    OPS.append((old, new, expect, label))


# ---- 1) 调用点传序号 ----
op(b'\t\tWatchGcuRestartFallbackAsync();\r\n',
   b'\t\tWatchGcuRestartFallbackAsync(requestSeq);\r\n',
   '调用点传序号')

# ---- 2) 签名 + 15s 后的取代判断 ----
op(b'\tprivate async Task WatchGcuRestartFallbackAsync()\r\n'
   b'\t{\r\n'
   b'\t\tawait Task.Delay(15000);\r\n'
   b'\t\tif (IsDisposed || !IsHandleCreated)\r\n'
   b'\t\t{\r\n'
   b'\t\t\treturn;\r\n'
   b'\t\t}\r\n',
   b'\tprivate async Task WatchGcuRestartFallbackAsync(int requestSeq)\r\n'
   b'\t{\r\n'
   b'\t\tawait Task.Delay(15000);\r\n'
   b'\t\tif (IsDisposed || !IsHandleCreated)\r\n'
   b'\t\t{\r\n'
   b'\t\t\treturn;\r\n'
   b'\t\t}\r\n'
   b'\t\t// 2026-10-05: one watchdog per switch request used to mean one dialog each --\r\n'
   b'\t\t// two quick requests produced two stacked prompts. A newer request supersedes\r\n'
   b'\t\t// this watchdog (user decision: discard the stale prompt).\r\n'
   b'\t\tif (requestSeq != _gpuSwitchRequestSeq)\r\n'
   b'\t\t{\r\n'
   b'\t\t\tLogger.WriteLine($"GCU restart fallback: superseded (seq={requestSeq} latest={_gpuSwitchRequestSeq}) -> discarded");\r\n'
   b'\t\t\treturn;\r\n'
   b'\t\t}\r\n',
   '看门狗加取代判断')

# ---- 3) BeginInvoke 内去重 ----
op(b'\t\t\tBeginInvoke(() =>\r\n'
   b'\t\t\t{\r\n'
   b'\t\t\t\tif (MessageBox.Show(this, Strings.GpuRestartManualPrompt, "L-Mechrevo", MessageBoxButtons.OKCancel, MessageBoxIcon.Exclamation) == DialogResult.OK)\r\n'
   b'\t\t\t\t{\r\n'
   b'\t\t\t\t\tSystemRestart.CaptureUserConfirmation();\r\n'
   b'\t\t\t\t\tLogger.WriteLine("GCU restart did not happen; falling back to Windows shutdown /r.");\r\n'
   b'\t\t\t\t\tSystemRestart.RequestRestart("GCU restart fallback");\r\n'
   b'\t\t\t\t}\r\n'
   b'\t\t\t});\r\n',
   b'\t\t\tBeginInvoke(() =>\r\n'
   b'\t\t\t{\r\n'
   b'\t\t\t\tif (_gpuRestartPromptOpen)\r\n'
   b'\t\t\t\t{\r\n'
   b'\t\t\t\t\tLogger.WriteLine("GpuRestartManualPrompt: another prompt is already open -> discarded");\r\n'
   b'\t\t\t\t\treturn;\r\n'
   b'\t\t\t\t}\r\n'
   b'\t\t\t\t_gpuRestartPromptOpen = true;\r\n'
   b'\t\t\t\ttry\r\n'
   b'\t\t\t\t{\r\n'
   b'\t\t\t\t\tif (MessageBox.Show(this, Strings.GpuRestartManualPrompt, "L-Mechrevo", MessageBoxButtons.OKCancel, MessageBoxIcon.Exclamation) == DialogResult.OK)\r\n'
   b'\t\t\t\t\t{\r\n'
   b'\t\t\t\t\t\tSystemRestart.CaptureUserConfirmation();\r\n'
   b'\t\t\t\t\t\tLogger.WriteLine("GCU restart did not happen; falling back to Windows shutdown /r.");\r\n'
   b'\t\t\t\t\t\tSystemRestart.RequestRestart("GCU restart fallback");\r\n'
   b'\t\t\t\t\t}\r\n'
   b'\t\t\t\t}\r\n'
   b'\t\t\t\tfinally\r\n'
   b'\t\t\t\t{\r\n'
   b'\t\t\t\t\t_gpuRestartPromptOpen = false;\r\n'
   b'\t\t\t\t}\r\n'
   b'\t\t\t});\r\n',
   'lambda 内去重')


def main():
    data = io.open(F, 'rb').read()
    if b'WatchGcuRestartFallbackAsync(int requestSeq)' in data:
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
