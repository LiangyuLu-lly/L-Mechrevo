# -*- coding: utf-8 -*-
"""
风扇曲线（固件配置模式）：跳过 OpenFanCurveAsync 里的 PerfModeService 分支
================================================================================
调研结论（2026-10-05）：
  * `FanCurveForm` **完全不引用** PerfModeService / PerfModeDefinition ——
    它只通过 `GET_FAN_SPEED_CURVE_SETTING` / `SetFanCurve(0|1,...)`
    （`SET_FAN_SPEED_CURVE_SETTING`）读写**当前生效配置**的曲线 ✅
    （见 FanCurveForm:472 OnCurveUpdated -> 572 SendBoth -> 578 FlushPendingAsync
      -> 602/606 SetFanCurve -> 608 GET 回读；629 CurveMatches 自校验）
  * 而编辑器**只编辑当前模式+配置**，且"换配置"已强制下发 ProfileIndex
    -> 打开风扇曲线时硬件正处于该配置 -> 曲线就是该配置的曲线 ✅
  * **唯一的问题**：`OpenFanCurveAsync` 在 `mode.Route == BuiltIn` 时会执行
    `EnsureFirmwareSlotAsync(mode.Id)` —— 把内建预设档"迁移"到固件槽位路由，
    改变 LM 的模式体系（占槽位），这不是编辑固件配置曲线该做的事 ❌
修法：`if (perf != null)` -> `if (perf != null && !_firmwareMode)`
      （固件模式下直接跳到 `new FanCurveForm()`）
行尾：CustomModeForm.cs 是 LF。
"""
import io
import os
import sys

F = r'D:\openLM\source\MechrevoLite\CustomModeForm.cs'

OLD = (b'\t\tPerfModeService perf = PerfModeService.Instance;\n'
       b'\t\tif (perf != null)\n'
       b'\t\t{\n'
       b'\t\t\tPerfModeDefinition mode = FindMode(_modeId);\n'
       b'\t\t\tif ((object)mode != null && mode.Route == PerfModeRoute.BuiltIn)\n')

NEW = (b'\t\tPerfModeService perf = PerfModeService.Instance;\n'
       b'\t\t// 2026-10-05: in firmware-profile mode go straight to the editor.\n'
       b'\t\t// FanCurveForm already reads and writes the ACTIVE profile\'s curve through\r\n'
       b'\t\t// GET/SET_FAN_SPEED_CURVE_SETTING, and the active profile IS the config being\r\n'
       b'\t\t// edited, so the curve shown belongs to that config. Running the block below\r\n'
       b'\t\t// would call EnsureFirmwareSlotAsync() and convert the built-in preset mode\r\n'
       b'\t\t// into a firmware-slot route -- a side effect on LM\'s mode model we do not want.\r\n'
       b'\t\tif (perf != null && !_firmwareMode)\n'
       b'\t\t{\n'
       b'\t\t\tPerfModeDefinition mode = FindMode(_modeId);\n'
       b'\t\t\tif ((object)mode != null && mode.Route == PerfModeRoute.BuiltIn)\n')


def main():
    data = io.open(F, 'rb').read()
    if b'go straight to the editor' in data:
        print('已经应用过了，无需重复。')
        return 0
    n = data.count(OLD)
    if n != 1:
        print('=== 校验失败：锚点命中 %d 次(期望 1)，未写盘 ===' % n)
        return 1
    print('=== 锚点校验通过 ===')
    if '--check' in sys.argv:
        return 0
    data = data.replace(OLD, NEW, 1).replace(b'\r\n', b'\n')
    io.open(F, 'wb').write(data)
    print('  写入 ' + F)
    return 0


if __name__ == '__main__':
    sys.exit(main())
