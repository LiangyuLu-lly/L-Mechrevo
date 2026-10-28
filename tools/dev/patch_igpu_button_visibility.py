# -*- coding: utf-8 -*-
"""
补丁：核显模式按钮在不支持的机型上仍显示（极光Pro 2024 反馈）
================================================================================
现象：极光Pro Series GM6PQ0X（Intel i7-13650HX + UHD + RTX 4060）不支持纯核显模式，
      诊断包能力位也正确写着「核显模式: 否」，但主界面仍显示"核显模式"按钮。

根因：VisualiseGPUButtons(bool eco, bool ultimate, bool auto) 的 **eco 参数 = 核显模式按钮可见性**
      （buttonEco.Visible = eco；第 3 个参数 auto 在函数体内从未被使用）。
      两个调用点对 eco 的取值不一致：

        SettingsForm.cs:4802   VisualiseGPUButtons(flag18, flag19, flag18)
                               flag18 = Show(hw.CanOfferIgpuOnly)   → 正确 ✅（2026-10-03 修过）
        GPUModeControl.cs:41   VisualiseGPUButtons(flag | flag2, flag2, flag)
                               flag  = canOfferGpuModeSwitch && CanOfferIgpuOnly
                               flag2 = canOfferGpuModeSwitch && SupportsDgpuDirect
                               → eco = flag | flag2 ❌（漏修的同一类 bug）

      极光Pro：CanOfferIgpuOnly=false、SupportsDgpuDirect=true、CanOfferGpuModeSwitch=true
               → flag=false、flag2=true → eco = false|true = **true** → 按钮被画出。

      **错误可见性会残留**：InitGPUMode 先执行第 41 行置 eco=true，
      紧接着第 42 行 RefreshDeviceCapabilities() 若能力签名未变（SettingsForm.cs:4702）
      会直接 return，不执行第 4802 行 → 没人把按钮改回来。

      注意正确值本来就躺在第 3 个参数里（两个调用点都是），说明原意就是纯 flag / num4>=0。

修法（两处，都是同一种笔误）：
  GPUModeControl.cs:41  flag | flag2          → flag
  GPUModeControl.cs:89  num4 >= 0 || num5 >= 0 → num4 >= 0
      （num4 = ACPI DeviceGet(0) "Eco flag"，num5 = DeviceGet(589846) "Mux flag"；
        num4 >= 0 才是 eco 受支持；第 89 行是 GCU 不可用时的旧 ACPI 路径）
"""
import io
import os
import sys

F = r'D:\openLM\source\MechrevoLite.Gpu\GPUModeControl.cs'

OPS = [
    (F,
     '\t\t\tsettings.VisualiseGPUButtons(flag | flag2, flag2, flag);\n',
     '\t\t\t// 2026-10-04：eco（核显模式按钮）只能用 flag（CanOfferIgpuOnly）；\n'
     '\t\t\t// 原来写成 flag | flag2，导致"支持独显直连但不支持核显模式"的机型\n'
     '\t\t\t// （如极光Pro GM6PQ0X）也显示核显模式按钮。\n'
     '\t\t\tsettings.VisualiseGPUButtons(flag, flag2, flag);\n',
     'InitGPUMode eco: flag|flag2 -> flag'),
    (F,
     '\t\tsettings.VisualiseGPUButtons(num4 >= 0 || num5 >= 0, num5 >= 0, num4 >= 0);\n',
     '\t\t// 2026-10-04：同上一处 —— eco 只看 num4(Eco flag)>=0，不能用 || num5(Mux flag)\n'
     '\t\tsettings.VisualiseGPUButtons(num4 >= 0, num5 >= 0, num4 >= 0);\n',
     'legacy eco: num4>=0||num5>=0 -> num4>=0'),
]


def main():
    if '--check' in sys.argv:
        print('（仅校验模式）')
    raw = io.open(F, 'rb').read()
    bom = raw.startswith(b'\xef\xbb\xbf')
    text = raw.decode('utf-8-sig')
    crlf = '\r\n' in text
    if crlf:
        text = text.replace('\r\n', '\n')
    if '2026-10-04：eco（核显模式按钮）只能用 flag' in text:
        print('已经应用过了，无需重复。')
        return 0
    errors = []
    for path, old, new, label in OPS:
        n = text.count(old)
        if n != 1:
            errors.append('%-34s 命中 %d 次(期望 1)  [%s]' % (os.path.basename(path), n, label))
    if errors:
        print('=== 校验失败，未写盘 ===')
        for e in errors:
            print('  ! ' + e)
        return 1
    print('=== 锚点校验全部通过（%d 项）===' % len(OPS))
    if '--check' in sys.argv:
        return 0
    for path, old, new, label in OPS:
        text = text.replace(old, new, 1)
    out = text.replace('\n', '\r\n') if crlf else text
    data = out.encode('utf-8')
    if bom:
        data = b'\xef\xbb\xbf' + data
    io.open(F, 'wb').write(data)
    print('  写入 %s' % F)
    print('=== 完成 ===')
    return 0


if __name__ == '__main__':
    sys.exit(main())
