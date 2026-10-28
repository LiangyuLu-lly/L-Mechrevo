# -*- coding: utf-8 -*-
r"""
核显模式判据（第三版）：对 TOGGLE_IGPU 做【正向锁存】
================================================================================
v2 失败原因：Setting/Status 分多条返回，只有一条带
DiscreteGpuDirectConnectionSwitch_Status；
后续不带该字段的报文里 IGpuOnlyConnectionSwitch_Support=NotSupport
又把 IgpuOnlyStatusSupport 重置为 false -> 我的 true 被覆盖。
修法：单独的 sticky 标志，只能被置位、不因后续报文清除
（作用域=本次 LM 运行；重启 LM 会重置，可接受）。
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


# ---- 1) 新增 sticky 字段 + 更新 SupportsIgpuOnly ----
op('\t// 2026-10-05：部分 40 系 GCU 变体（实测 GCUService 1.0.2.70）不在 Customize/Info 里\n'
   '\t// 报 iGPUModeOnlySupport / IGpuOnlyConnectionSwitch_Support，而是在 Setting/Status 的\n'
   '\t// DiscreteGpuDirectConnectionSwitch_Status 里用 ..._TOGGLE_IGPU 表达核显模式。\n'
   '\t// IgpuOnlyStatusSupport 由该字段/状态回读驱动，因此一并纳入能力判定。\n'
   '\tpublic bool SupportsIgpuOnly => Capabilities.IgpuOnly || (IgpuOnlyStatusSupport ?? false);\n',
   '\t// 2026-10-05：部分 40 系 GCU 变体（实测 GCUService 1.0.2.70）不在 Customize/Info 里\n'
   '\t// 报 iGPUModeOnlySupport / IGpuOnlyConnectionSwitch_Support，而是在 Setting/Status 的\n'
   '\t// DiscreteGpuDirectConnectionSwitch_Status 里用 ..._TOGGLE_IGPU 表达核显模式。\n'
   '\t// IgpuOnlyStatusSupport 会被后续报文的 NotSupport 重置，所以这里另用一个只置位不清除的\n'
   '\t// sticky 标志（作用域=本次 LM 运行）。\n'
   '\tpublic bool SupportsIgpuOnly => Capabilities.IgpuOnly || _igpuRouteStatusSeen || (IgpuOnlyStatusSupport ?? false);\n'
   '\n'
   '\tprivate bool _igpuRouteStatusSeen;\n',
   'sticky 字段 + SupportsIgpuOnly')

# ---- 2) 置位 sticky ----
op('\t\tif (!string.IsNullOrWhiteSpace(text24) && text24.Contains("DIRECT_CONNECT_IGPU", StringComparison.OrdinalIgnoreCase))\n'
   '\t\t{\n'
   '\t\t\tIgpuOnlyStatusSupport = true;\n'
   '\t\t}\n',
   '\t\tif (!string.IsNullOrWhiteSpace(text24) && text24.Contains("DIRECT_CONNECT_IGPU", StringComparison.OrdinalIgnoreCase))\n'
   '\t\t{\n'
   '\t\t\tIgpuOnlyStatusSupport = true;\n'
   '\t\t\t_igpuRouteStatusSeen = true;\n'
   '\t\t}\n',
   '置位 sticky')


def main():
    data = io.open(F, 'rb').read()
    if b'_igpuRouteStatusSeen' in data:
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
