# -*- coding: utf-8 -*-
r"""
修核显模式判据（第二版，走 Setting/Status 这一路）
================================================================================
上一版改 MechrevoDeviceCapabilities.FromValues 无效：那条路吃的是 Customize/Info
（ProjectID/KeyboardType/SupportColorCalibration 都在那里），
而 DiscreteGpuDirectConnectionSwitch_Status 在 Setting/Status —— 两条路不通。

本版改 MechrevoHw（它已经在 :2792 解析了那个字段）：
  1) 当 text24 表示 TOGGLE_IGPU 时，把 IgpuOnlyStatusSupport 置 true
  2) SupportsIgpuOnly 改为 Capabilities.IgpuOnly || (IgpuOnlyStatusSupport ?? false)
     （IgpuOnlyStatusSupport 本来就被 :2796/:2808 用作状态回读，这里只是让它参与能力判定）
"""
import io
import sys

F = r'D:\openLM\source\MechrevoLite.Hardware\MechrevoHw.cs'

OPS = []


def op(old, new, label, expect=1):
    if isinstance(old, str):
        old = old.encode('utf-8')
    if isinstance(new, str):
        new = new.encode('utf-8')
    OPS.append((old, new, expect, label))


# ---- 1) SupportsIgpuOnly 承认状态回读 ----
op('\tpublic bool SupportsIgpuOnly => Capabilities.IgpuOnly;\n',
   '\t// 2026-10-05：部分 40 系 GCU 变体（实测 GCUService 1.0.2.70）不在 Customize/Info 里\n'
   '\t// 报 iGPUModeOnlySupport / IGpuOnlyConnectionSwitch_Support，而是在 Setting/Status 的\n'
   '\t// DiscreteGpuDirectConnectionSwitch_Status 里用 ..._TOGGLE_IGPU 表达核显模式。\n'
   '\t// IgpuOnlyStatusSupport 由该字段/状态回读驱动，因此一并纳入能力判定。\n'
   '\tpublic bool SupportsIgpuOnly => Capabilities.IgpuOnly || (IgpuOnlyStatusSupport ?? false);\n',
   'SupportsIgpuOnly 承认状态回读')

# ---- 2) 解析 text24 时，TOGGLE_IGPU 直接置 IgpuOnlyStatusSupport ----
op('\t\tstring text26 = o.GetValue("CheckDGpuStatusforIGpuOnlyOnSuccess", StringComparison.OrdinalIgnoreCase)?.ToString();\n',
   '\t\t// 2026-10-05：这条路才是该类 GCU 变体的真实信号（见 SupportsIgpuOnly 注释）。\n'
   '\t\t// 实测：处于核显模式时 text24 == "DGPU_DIRECT_CONNECT_TOGGLE_IGPU"。\n'
   '\t\tif (!string.IsNullOrWhiteSpace(text24) && text24.Contains("DIRECT_CONNECT_IGPU", StringComparison.OrdinalIgnoreCase))\n'
   '\t\t{\n'
   '\t\t\tIgpuOnlyStatusSupport = true;\n'
   '\t\t}\n'
   '\t\tstring text26 = o.GetValue("CheckDGpuStatusforIGpuOnlyOnSuccess", StringComparison.OrdinalIgnoreCase)?.ToString();\n',
   '解析 text24 置 IgpuOnlyStatusSupport')


def main():
    data = io.open(F, 'rb').read()
    if b'IgpuOnlyStatusSupport ?? false' in data:
        print('已经应用过了。')
        return 0
    resolved, errors = [], []
    for old, new, expect, label in OPS:
        if data.count(old) == expect:
            resolved.append((old, new, expect, label))
            continue
        if b'\r\n' in old:
            ao, an = old.replace(b'\r\n', b'\n'), new.replace(b'\r\n', b'\n')
        else:
            ao, an = old.replace(b'\n', b'\r\n'), new.replace(b'\n', b'\r\n')
        if data.count(ao) == expect:
            print('  (行尾适配) ' + label)
            resolved.append((ao, an, expect, label))
            continue
        errors.append('命中 %d/%d 次(期望 %d)  [%s]' % (data.count(old), data.count(ao), expect, label))
    if errors:
        print('=== 校验失败，未写盘 ===')
        for e in errors:
            print('  ! ' + e)
        return 1
    print('=== 锚点校验通过（%d 项）===' % len(resolved))
    if '--check' in sys.argv:
        return 0
    for old, new, expect, label in resolved:
        data = io.open(F, 'rb').read()
        io.open(F, 'wb').write(data.replace(old, new, expect))
    print('  写入 ' + F)
    return 0


if __name__ == '__main__':
    sys.exit(main())
