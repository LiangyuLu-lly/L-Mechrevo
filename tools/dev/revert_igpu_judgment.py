# -*- coding: utf-8 -*-
r"""
回退本次「核显模式判据」的全部改动（外科式，只删我加的行；不整文件还原，
避免丢掉 10-04 之后的其它工作）
================================================================================
用户决定：暂时回退 ✅
  —— 副作用：切到独显直连后（text24 变成 TOGGLE_ON），锁存因 LM 重启而清零，
     而 GCU 此时不再提供任何核显证据 -> 按钮又消失。
  —— 用户明确：「30系没有支持核显模式的机器，30系可以全部屏蔽的」
     -> MechrevoHw.cs:435 的 Gen30 硬编码排除**保留不动**（正是该行为）
回退 5 处：
  MechrevoHw.cs             : SupportsIgpuOnly / _igpuRouteStatusSeen / IGPU-DIAG 诊断 / 置位块
  MechrevoDeviceCapabilities.cs : IsIgpuOnlyRouteStatus 方法 / igpuOnly 那行
"""
import io
import sys

HW = r'D:\openLM\source\MechrevoLite.Hardware\MechrevoHw.cs'
CAP = r'D:\openLM\source\MechrevoLite.Hardware\MechrevoDeviceCapabilities.cs'

OPS = []


def op(path, old, new, label, expect=1):
    if isinstance(old, str):
        old = old.encode('utf-8')
    if isinstance(new, str):
        new = new.encode('utf-8')
    OPS.append((path, old, new, expect, label))


# ---- 1) SupportsIgpuOnly + 注释 + sticky 字段 ----
op(HW,
   '\t// 2026-10-05：部分 40 系 GCU 变体（实测 GCUService 1.0.2.70）不在 Customize/Info 里\n'
   '\t// 报 iGPUModeOnlySupport / IGpuOnlyConnectionSwitch_Support，而是在 Setting/Status 的\n'
   '\t// DiscreteGpuDirectConnectionSwitch_Status 里用 ..._TOGGLE_IGPU 表达核显模式。\n'
   '\t// IgpuOnlyStatusSupport 会被后续报文的 NotSupport 重置，所以这里另用一个只置位不清除的\n'
   '\t// sticky 标志（作用域=本次 LM 运行）。\n'
   '\tpublic bool SupportsIgpuOnly => Capabilities.IgpuOnly || _igpuRouteStatusSeen || (IgpuOnlyStatusSupport ?? false);\n'
   '\n'
   '\tprivate bool _igpuRouteStatusSeen;\n',
   '\tpublic bool SupportsIgpuOnly => Capabilities.IgpuOnly;\n',
   'SupportsIgpuOnly 还原')

# ---- 2) IGPU-DIAG 诊断行 ----
op(HW,
   '\t\t// 2026-10-05 diagnostic: 核显判据三次修改无效 -> 打真值（字段是否读到、键是否同名）\n'
   '\t\tstring diag24 = text24 ?? "<null>";\n'
   '\t\tstring diag25 = text25 ?? "<null>";\n'
   '\t\tstring diagKeys = string.Join(",", o.Properties().Select((Newtonsoft.Json.Linq.JProperty p) => p.Name).Where((string k) => k.IndexOf("pu", StringComparison.OrdinalIgnoreCase) >= 0).Take(40));\n'
   '\t\tLogger.WriteLineIfChanged("igpu-diag", $"IGPU-DIAG: text24={diag24} text25={diag25} keys={o.Count} has24={o.ContainsKey("DiscreteGpuDirectConnectionSwitch_Status")} has25={o.ContainsKey("IGpuOnlyConnectionSwitch_Status")} gpuKeys=[{diagKeys}] seen={_igpuRouteStatusSeen}");\n',
   '',
   'IGPU-DIAG 诊断移除')

# ---- 3) 置位块 ----
op(HW,
   '\t\t// 2026-10-05：这条路才是该类 GCU 变体的真实信号（见 SupportsIgpuOnly 注释）。\n'
   '\t\t// 实测：处于核显模式时 text24 == "DGPU_DIRECT_CONNECT_TOGGLE_IGPU"。\n'
   '\t\t// 注意：真实 token 是 DGPU_DIRECT_CONNECT_TOGGLE_IGPU（中间有 _TOGGLE_），\n'
   '\t\t// 只 Contains("DIRECT_CONNECT_IGPU") 匹配不上 —— 与 :1480 的判法保持一致。\n'
   '\t\tif (!string.IsNullOrWhiteSpace(text24)\n'
   '\t\t\t&& (text24.Equals("DGPU_DIRECT_CONNECT_TOGGLE_IGPU", StringComparison.OrdinalIgnoreCase)\n'
   '\t\t\t\t|| text24.Contains("DIRECT_CONNECT_IGPU", StringComparison.OrdinalIgnoreCase)))\n'
   '\t\t{\n'
   '\t\t\tIgpuOnlyStatusSupport = true;\n'
   '\t\t\t_igpuRouteStatusSeen = true;\n'
   '\t\t}\n',
   '',
   '置位块移除')

# ---- 4) IsIgpuOnlyRouteStatus 方法 ----
op(CAP,
   '\t/// <summary>\n'
   '\t/// 路由状态是否表示「核显模式」：<c>DGPU_DIRECT_CONNECT_TOGGLE_IGPU</c>。\n'
   '\t/// 必须显式匹配 token —— 该字段不是布尔量，TOGGLE_ON / TOGGLE_OFF 都不代表支持核显。\n'
   '\t/// 实测：翼龙15Pro + GCU 1.0.2.70 处于核显模式时该值恰为 TOGGLE_IGPU。\n'
   '\t/// </summary>\n'
   '\tprivate static bool IsIgpuOnlyRouteStatus(string? status)\n'
   '\t{\n'
   '\t\treturn !string.IsNullOrWhiteSpace(status)\n'
   '\t\t\t&& (status.Equals("DGPU_DIRECT_CONNECT_TOGGLE_IGPU", StringComparison.OrdinalIgnoreCase)\n'
   '\t\t\t\t|| status.Contains("DIRECT_CONNECT_IGPU", StringComparison.OrdinalIgnoreCase));\n'
   '\t}\n'
   '\n',
   '',
   'IsIgpuOnlyRouteStatus 移除')

# ---- 5) igpuOnly 那行还原 ----
op(CAP,
   '\t\t// 2026-10-05：另有部分 40 系 GCU 变体（实测 GCUService 1.0.2.70）两者都不给，\n'
   '\t\t// 而是在 DiscreteGpuDirectConnectionSwitch_Status 里用 ..._TOGGLE_IGPU 表达\n'
   '\t\t// 「可以/当前就是核显模式」。不看这一位会导致机器已在核显模式、LM 却不给按钮。\n'
   '\t\tbool igpuOnly = Flag(new string[4] { "iGPUModeOnlySupport", "IGpuOnlyModeSupport", "IntegratedGpuOnlySupport", "IGpuOnlyConnectionSwitch_Support" })\n'
   '\t\t\t|| IsIgpuOnlyRouteStatus(Value(new string[3] { "DiscreteGpuDirectConnectionSwitch_Status", "DGpuDirectConnectionSwitch_Status", "DGPU_DIRECT_STATUS" })?.ToString());\n',
   '\t\tbool igpuOnly = Flag(new string[4] { "iGPUModeOnlySupport", "IGpuOnlyModeSupport", "IntegratedGpuOnlySupport", "IGpuOnlyConnectionSwitch_Support" });\n',
   'igpuOnly 行还原')


def main():
    resolved, errors = [], []
    for path, old, new, expect, label in OPS:
        data = io.open(path, 'rb').read()
        if data.count(old) == expect:
            resolved.append((path, old, new, expect, label))
            continue
        if b'\r\n' in old:
            ao, an = old.replace(b'\r\n', b'\n'), new.replace(b'\r\n', b'\n')
        else:
            ao, an = old.replace(b'\n', b'\r\n'), new.replace(b'\n', b'\r\n')
        if data.count(ao) == expect:
            print('  (行尾适配) ' + label)
            resolved.append((path, ao, an, expect, label))
            continue
        errors.append('命中 %d/%d 次(期望 %d)  [%s]  %s' % (data.count(old), data.count(ao), expect, label, path.split('\\')[-1]))
    if errors:
        print('=== 校验失败，未写盘 ===')
        for e in errors:
            print('  ! ' + e)
        return 1
    print('=== 锚点校验通过（%d 项）===' % len(resolved))
    if '--check' in sys.argv:
        return 0
    for path, old, new, expect, label in resolved:
        d = io.open(path, 'rb').read()
        io.open(path, 'wb').write(d.replace(old, new, expect))
        print('  写入 ' + path)
    return 0


if __name__ == '__main__':
    sys.exit(main())
