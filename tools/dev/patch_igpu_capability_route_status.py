# -*- coding: utf-8 -*-
r"""
修核显模式能力判据：补上 DiscreteGpuDirectConnectionSwitch_Status == TOGGLE_IGPU
================================================================================
实测（翼龙15Pro + GCU 1.0.2.70）：
  处于核显模式时
    IGpuOnlyConnectionSwitch_Support = 'NotSupport'        （该变体不用此字段）
    IGpuOnlyConnectionSwitch_Status  = 'IGPU_ONLY_CONNECT_RB_OFF'
    iGPUModeOnlySupport             = 缺失
    DiscreteGpuDirectConnectionSwitch_Status = 'DGPU_DIRECT_CONNECT_TOGGLE_IGPU'  ★
  -> MechrevoDeviceCapabilities.FromValues 的 igpuOnly 因此为 false
  -> SupportsIgpuOnly=false -> CanOfferIgpuOnly=false -> 核显按钮消失（但机器就在核显模式）
修法：igpuOnly 再并上一个"路由状态"信号（用显式 token 匹配，**不能**用 Flag——
      否则 TOGGLE_OFF 这类非空字符串也会被当成 true）
"""
import io
import sys

F = r'D:\openLM\source\MechrevoLite.Hardware\MechrevoDeviceCapabilities.cs'

OPS = []


def op(old, new, label, expect=1):
    if isinstance(old, str):
        old = old.encode('utf-8')
    if isinstance(new, str):
        new = new.encode('utf-8')
    OPS.append((old, new, expect, label))


# ---- 1) igpuOnly 并上路由状态信号 ----
op('\t\t// 50 系 GCU（ConnectionSwitch 系列）用 IGpuOnlyConnectionSwitch_Support=Enable 表达核显能力；40 系 GCU 用 iGPUModeOnlySupport。两者均认。\n'
   '\t\tbool igpuOnly = Flag(new string[4] { "iGPUModeOnlySupport", "IGpuOnlyModeSupport", "IntegratedGpuOnlySupport", "IGpuOnlyConnectionSwitch_Support" });\n',
   '\t\t// 50 系 GCU（ConnectionSwitch 系列）用 IGpuOnlyConnectionSwitch_Support=Enable 表达核显能力；40 系 GCU 用 iGPUModeOnlySupport。两者均认。\n'
   '\t\t// 2026-10-05：另有部分 40 系 GCU 变体（实测 GCUService 1.0.2.70）两者都不给，\n'
   '\t\t// 而是在 DiscreteGpuDirectConnectionSwitch_Status 里用 ..._TOGGLE_IGPU 表达\n'
   '\t\t// 「可以/当前就是核显模式」。不看这一位会导致机器已在核显模式、LM 却不给按钮。\n'
   '\t\tbool igpuOnly = Flag(new string[4] { "iGPUModeOnlySupport", "IGpuOnlyModeSupport", "IntegratedGpuOnlySupport", "IGpuOnlyConnectionSwitch_Support" })\n'
   '\t\t\t|| IsIgpuOnlyRouteStatus(Value(new string[3] { "DiscreteGpuDirectConnectionSwitch_Status", "DGpuDirectConnectionSwitch_Status", "DGPU_DIRECT_STATUS" })?.ToString());\n',
   'igpuOnly 并上路由状态')

# ---- 2) 新增判定辅助方法（放在 FromValues 之前）----
op('\tinternal static MechrevoDeviceCapabilities FromValues(IReadOnlyDictionary<string, object?> values, IReadOnlyDictionary<string, object?>? identity = null, bool logoRegistryDetected = false)\n',
   '\t/// <summary>\n'
   '\t/// 路由状态是否表示「核显模式」：<c>DGPU_DIRECT_CONNECT_TOGGLE_IGPU</c>。\n'
   '\t/// 必须显式匹配 token —— 该字段不是布尔量，TOGGLE_ON / TOGGLE_OFF 都不代表支持核显。\n'
   '\t/// 实测：翼龙15Pro + GCU 1.0.2.70 处于核显模式时该值恰为 TOGGLE_IGPU。\n'
   '\t/// </summary>\n'
   '\tprivate static bool IsIgpuOnlyRouteStatus(string? status)\n'
   '\t{\n'
   '\t\treturn !string.IsNullOrWhiteSpace(status)\n'
   '\t\t\t&& status.Contains("DIRECT_CONNECT_IGPU", StringComparison.OrdinalIgnoreCase);\n'
   '\t}\n'
   '\n'
   '\tinternal static MechrevoDeviceCapabilities FromValues(IReadOnlyDictionary<string, object?> values, IReadOnlyDictionary<string, object?>? identity = null, bool logoRegistryDetected = false)\n',
   '新增 IsIgpuOnlyRouteStatus')


def main():
    data = io.open(F, 'rb').read()
    if b'IsIgpuOnlyRouteStatus' in data:
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
