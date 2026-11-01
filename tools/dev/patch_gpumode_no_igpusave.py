# -*- coding: utf-8 -*-
r"""
修：显示路由解析不再借用【核显省电】字段（去掉 mode 3 的来源）
================================================================================
现状（MechrevoHw.cs:1480 ResolveGpuModeStatus）：
  text  = DiscreteGpuDirectConnectionSwitch_Status   （显示路由字段）
  text2 = IGpuOnlyConnectionSwitch_Status            （**核显省电**字段）
  :1492 用 text2 反推 num = RB_ON?0 : RB_AUTO?3 : RB_OFF?1 : -1，然后 return num
        -> mode 3(AUTO) 就是这么来的（GCU 的显示路由字段**根本没有 AUTO 取值**：
           DisplayRouteMatrix 的 Gen50 官方词汇只有 DGPU_DIRECT_CONNECT_TOGGLE_ON/OFF/IGPU）

问题（§9.6 功能混淆）：
  · 核显省电是独立功能（IgpuOnlySaveMode / SetIGpuOnlySaveMode / 独立 UI 行）
  · 删除「自动切换显卡模式」后 mode 3 已无归宿
  · 50 系把核显省电设为 AUTO 时，IGpuOnlyConnectionSwitch_Status = ...RB_AUTO
    -> 会让 GpuMode 变成 3 -> 三档高亮/切换策略错乱

改法（2 处）：
  1. ResolveGpuModeStatus 去掉 igpuStatus 参数与该路反推，未知时 return currentMode
  2. 调用点 :2848 去掉第三个实参
"""
import io
import sys

F = r'D:\openLM\source\MechrevoLite.Hardware\MechrevoHw.cs'

OLD_FN = '''\tinternal static int ResolveGpuModeStatus(int currentMode, string? directStatus, string? igpuStatus)
\t{
\t\tstring text = directStatus?.ToUpperInvariant() ?? "";
\t\tstring text2 = igpuStatus?.ToUpperInvariant() ?? "";
\t\tif (text == "DGPU_DIRECT_CONNECT_TOGGLE_ON" || text.Contains("DIRECT_CONNECT_ON") || text.Contains("DIRECT_CONNECTION_ON"))
\t\t{
\t\t\treturn 2;
\t\t}
\t\tif (text == "DGPU_DIRECT_CONNECT_TOGGLE_IGPU" || text.Contains("DIRECT_CONNECT_IGPU"))
\t\t{
\t\t\treturn 0;
\t\t}
\t\tint num = ((!text2.Contains("IGPU_ONLY_CONNECT_RB_ON") && !text2.Contains("IGPU_ONLY_ON")) ? ((text2.Contains("IGPU_ONLY_CONNECT_RB_AUTO") || text2.Contains("IGPU_ONLY_AUTO")) ? 3 : ((text2.Contains("IGPU_ONLY_CONNECT_RB_OFF") || text2.Contains("IGPU_ONLY_OFF")) ? 1 : (-1))) : 0);
\t\tif (text == "DGPU_DIRECT_CONNECT_TOGGLE_OFF" || text.Contains("DIRECT_CONNECT_OFF") || text.Contains("DIRECT_CONNECTION_OFF"))
\t\t{
\t\t\treturn 1;
\t\t}
\t\tif (num < 0)
\t\t{
\t\t\treturn currentMode;
\t\t}
\t\treturn num;
\t}
'''

NEW_FN = '''\t// 2026-10-05：显示路由**只**由 DiscreteGpuDirectConnectionSwitch_Status 决定。
\t// 原来这里还会用【核显省电】字段（IGpuOnlyConnectionSwitch_Status = IGPU_ONLY_CONNECT_RB_*）
\t// 反推出 mode 3(AUTO)/0/1 —— 那是 §9.6 的功能混淆：
\t//   · GCU 的显示路由字段本就没有 AUTO 取值（只有 TOGGLE_ON / TOGGLE_OFF / TOGGLE_IGPU）
\t//   · 核显省电有独立字段、独立功能与独立按钮（IgpuOnlySaveMode / SetIGpuOnlySaveMode），
\t//     不该影响 GpuMode
\t//   · 删除「自动切换显卡模式」后 mode 3 已无归宿；50 系把核显省电设为 AUTO 时，
\t//     ...RB_AUTO 会让 GpuMode 变成 3，导致三档高亮与切换策略错乱
\t// 因此：认不出就沿用上一次的已知模式。
\tinternal static int ResolveGpuModeStatus(int currentMode, string? directStatus)
\t{
\t\tstring text = directStatus?.ToUpperInvariant() ?? "";
\t\tif (text == "DGPU_DIRECT_CONNECT_TOGGLE_ON" || text.Contains("DIRECT_CONNECT_ON") || text.Contains("DIRECT_CONNECTION_ON"))
\t\t{
\t\t\treturn 2;
\t\t}
\t\tif (text == "DGPU_DIRECT_CONNECT_TOGGLE_IGPU" || text.Contains("DIRECT_CONNECT_IGPU"))
\t\t{
\t\t\treturn 0;
\t\t}
\t\tif (text == "DGPU_DIRECT_CONNECT_TOGGLE_OFF" || text.Contains("DIRECT_CONNECT_OFF") || text.Contains("DIRECT_CONNECTION_OFF"))
\t\t{
\t\t\treturn 1;
\t\t}
\t\treturn currentMode;
\t}
'''

OLD_CALL = '\t\tint num5 = ResolveGpuModeStatus(GpuMode, text24, text25);\n'
NEW_CALL = '\t\t// 2026-10-05：不再传 text25（核显省电字段）—— 显示路由与核显省电无关（§9.6）\n' \
           '\t\tint num5 = ResolveGpuModeStatus(GpuMode, text24);\n'

OPS = [(OLD_FN, NEW_FN, 'ResolveGpuModeStatus 去核显省电路'), (OLD_CALL, NEW_CALL, '调用点去掉第三实参')]


def main():
    raw = io.open(F, 'rb').read()
    if 'ResolveGpuModeStatus(int currentMode, string? directStatus)'.encode('utf-8') in raw:
        print('已经应用过了。')
        return 0
    crlf = b'\r\n' in raw
    text = raw.decode('utf-8-sig')
    if text.startswith('\ufeff'):
        text = text[1:]
    norm = text.replace('\r\n', '\n')
    print('行尾: %s' % ('CRLF' if crlf else 'LF'))
    for old, new, label in OPS:
        c = norm.count(old)
        print('  [%s] 命中 %d 次' % (label, c))
        if c != 1:
            print('=== 校验失败，未写盘 ===')
            return 1
    if '--check' in sys.argv:
        return 0
    for old, new, _l in OPS:
        norm = norm.replace(old, new, 1)
    out = norm.replace('\n', '\r\n') if crlf else norm
    io.open(F, 'w', encoding='utf-8-sig', newline='').write(out)
    print('  写入 ' + F)
    return 0


if __name__ == '__main__':
    sys.exit(main())
