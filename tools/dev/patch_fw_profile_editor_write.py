# -*- coding: utf-8 -*-
"""
二期（方案 B 旁路）第 2 半：**写入路径**
================================================================================
机制（读源码确认）：
  * MechrevoHw.SetMode(mode) :3321 —— 选中模式；内部用
    `ResolveModeProfileIndex(this, mode, expectedOpMode, custom)` 取配置号，
    而它读的正是 `ModeProfileConfig.Get(visualMode)` = 设置行下拉里选的那个 ✅
    调用后自身会等 `OperatingMode` 就绪
  * MechrevoService.SetCustomDetail(fields) :620 —— 整组写参数（带能力校验/切换锁/GPU OC 路由）
  * FlushPendingAsync 里本就有两条路：`perf != null` 走 PerfModeStore（自定义档）；
    else 走 `SetCustomDetail`（**直接写硬件**）
修法：
  1. 拆掉上一版"固件模式直接 return"的只读守卫，改为**先选中目标 (模式, 配置号)**
  2. 把 `if (perf != null)` 改成 `if (perf != null && !_firmwareMode)`
     → 固件模式自然落到 `SetCustomDetail` 那条 else 分支，**复用全部现有管线与状态提示** ✅
  3. 新增 `SelectFirmwareTargetAsync()`：调 SetMode + 打印 ProfileName 回读日志
  4. 状态栏去掉 read-only 字样
注意：CustomModeForm.cs 是 LF。
"""
import io
import os
import sys

F = r'D:\openLM\source\MechrevoLite\CustomModeForm.cs'

OPS = []


def op(old, new, label, expect=1):
    OPS.append((old, new, expect, label))


# ---- 1) 只读守卫 -> 选中目标 ----
op(b'\t\t// 2026-10-04: firmware-profile bypass is read-only for now. Without this guard\n'
   b'\t\t// an edit would be committed through PerfModeStore into an LM custom mode.\n'
   b'\t\tif (_firmwareMode)\n'
   b'\t\t{\n'
   b'\t\t\treturn;\n'
   b'\t\t}\n',
   b'\t\t// 2026-10-04: firmware-profile mode -- select the target (mode, profile) first.\n'
   b'\t\t// SetMode() resolves ProfileIndex through ModeProfileConfig, i.e. exactly the\n'
   b'\t\t// profile picked in the settings-row dropdown -- which is what we are editing.\n'
   b'\t\tif (_firmwareMode)\n'
   b'\t\t{\n'
   b'\t\t\tawait SelectFirmwareTargetAsync();\n'
   b'\t\t}\n',
   '只读守卫改为选中目标')

# ---- 2) 固件模式落到 SetCustomDetail 分支 ----
op(b'\t\t\t\tif (perf != null)\n\t\t\t\t{\n',
   b'\t\t\t\t// 2026-10-04: firmware-profile mode writes straight to the active profile\n'
   b'\t\t\t\t// via SetCustomDetail (the else branch below) instead of PerfModeStore.\n'
   b'\t\t\t\tif (perf != null && !_firmwareMode)\n'
   b'\t\t\t\t{\n',
   '固件模式改走 SetCustomDetail')

# ---- 3) 新增 SelectFirmwareTargetAsync ----
NEW_METHOD = (b'\t/// <summary>\n'
              b'\t/// 2026-10-04: make (mode, profile) the active operating profile before writing\r\n'
              b'\t/// its parameters. SetMode() itself waits for OperatingMode to settle.\r\n'
              b'\t/// </summary>\n'
              b'\tprivate async Task SelectFirmwareTargetAsync()\n'
              b'\t{\n'
              b'\t\tMechrevoHw hw = Program.hw;\n'
              b'\t\tif (hw == null || !hw.IsConnected)\n'
              b'\t\t{\n'
              b'\t\t\treturn;\n'
              b'\t\t}\n'
              b'\t\tint num = ((_firmwareVisualMode == 1) ? 3 : ((_firmwareVisualMode == 2) ? 2 : 1));\n'
              b'\t\tstring text = "Mode" + num + "_Profile" + (_firmwareProfileIndex + 1);\n'
              b'\t\tLogger.WriteLine($"FirmwareProfile: selecting {text} (visualMode={_firmwareVisualMode})");\n'
              b'\t\tawait hw.SetMode(_firmwareVisualMode);\n'
              b'\t\tawait Task.Delay(150);\n'
              b'\t\tLogger.WriteLine($"FirmwareProfile: readback ProfileName={hw.ProfileName} expected={text}" + ((hw.ProfileName == text) ? " OK" : " MISMATCH"));\n'
              b'\t}\n'
              b'\n'
              b'\tprivate async Task FlushPendingAsync()\n\t{\n')
op(b'\tprivate async Task FlushPendingAsync()\n\t{\n', NEW_METHOD, 'SelectFirmwareTargetAsync')

# ---- 4) 状态栏去掉 read-only ----
op(b'\t\t_status.Text = Strings.ModeConfigShort + " " + (_firmwareProfileIndex + 1) + "\\uff08read-only\\uff09";\n',
   b'\t\t_status.Text = Strings.ModeConfigShort + " " + (_firmwareProfileIndex + 1);\n',
   '状态栏去掉 read-only')


def main():
    data = io.open(F, 'rb').read()
    if b'SelectFirmwareTargetAsync' in data:
        print('已经应用过了，无需重复。')
        return 0
    errors = []
    for old, new, expect, label in OPS:
        n = data.count(old)
        if n != expect:
            errors.append('命中 %d 次(期望 %d)  [%s]' % (n, expect, label))
    if errors:
        print('=== 校验失败，未写盘 ===')
        for e in errors:
            print('  ! ' + e)
        return 1
    print('=== 锚点校验全部通过（%d 项）===' % len(OPS))
    if '--check' in sys.argv:
        return 0
    for old, new, expect, label in OPS:
        data = data.replace(old, new, expect)
    data = data.replace(b'\r\n', b'\n')
    io.open(F, 'wb').write(data)
    print('  写入 ' + F)
    return 0


if __name__ == '__main__':
    sys.exit(main())
