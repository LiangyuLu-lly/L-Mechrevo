# -*- coding: utf-8 -*-
r"""
第一类清理：模式切换路径不再引用核显省电命令（IGPU_ONLY_*）
================================================================================
依据：
- 核显省电（SettingsForm._igpuSavingRow / SetIGpuOnlySaveMode）是**完全独立**的一套 ✅
- DisplayRouteMatrix:48（Gen50 官方控制台反编译）显示官方同时具备两族命令：
  DGPU_DIRECT_CONNECT_TOGGLE_ON/OFF/IGPU  与  IGPU_ONLY_CONNECT_RB_ON/OFF/AUTO
  -> 二者是**不同功能**（§9.6），模式切换不该用 IGPU_ONLY_*
- 实测本机 mode 0 实际发的是 {"Action":"DGPU_DIRECT_CONNECT_TOGGLE_IGPU"}（无 SetToWMIEC）
改两处（50 系同样安全 —— Gen50 词汇表里 TOGGLE_IGPU 也在）：
  ① MechrevoHw.cs:425  CanOfferIgpuOnly 里 "IGPU_ONLY_CONNECT_RB_ON" -> "DGPU_DIRECT_CONNECT_TOGGLE_IGPU"
  ② MechrevoService.cs:1596  CreateGpuModePayload(0)：
       IGPU_ONLY_CONNECT_RB_ON + SetToWMIEC  ->  DGPU_DIRECT_CONNECT_TOGGLE_IGPU（与 LM 实测一致）
不改（保留）：
  - 切 mode 2 时先发 IGPU_ONLY_CONNECT_RB_OFF（离开混合模式时清核显省电，属合理联动）
  - CreateGpuModePayload(1) 的 IGPU_ONLY_CONNECT_RB_OFF（牵动 50 系，无样本不盲改）
"""
import io
import sys

HW = r'D:\openLM\source\MechrevoLite.Hardware\MechrevoHw.cs'
SVC = r'D:\openLM\source\MechrevoLite.Hardware\MechrevoService.cs'

OPS = []


def op(path, old, new, label, expect=1):
    if isinstance(old, str):
        old = old.encode('utf-8')
    if isinstance(new, str):
        new = new.encode('utf-8')
    OPS.append((path, old, new, expect, label))


# ---- 1) MechrevoHw:425 许可判据 ----
op(HW,
   '\t\t\tif (SupportsIgpuOnly)\n'
   '\t\t\t{\n'
   '\t\t\t\treturn IsGpuActionAllowedByGeneration("IGPU_ONLY_CONNECT_RB_ON");\n'
   '\t\t\t}\n',
   '\t\t\tif (SupportsIgpuOnly)\n'
   '\t\t\t{\n'
   '\t\t\t\t// 2026-10-05：核显【模式】对应的是 DGPU_DIRECT_CONNECT_TOGGLE_IGPU；\n'
   '\t\t\t\t// IGPU_ONLY_CONNECT_RB_* 属【核显省电】（另一功能，见 §9.6 / SettingsForm._igpuSavingRow）。\n'
   '\t\t\t\treturn IsGpuActionAllowedByGeneration("DGPU_DIRECT_CONNECT_TOGGLE_IGPU");\n'
   '\t\t\t}\n',
   'MechrevoHw 许可判据改用 TOGGLE_IGPU')

# ---- 2) CreateGpuModePayload(0) ----
op(SVC,
   '\t\t\t0 => new Dictionary<string, object>\n'
   '\t\t\t{\n'
   '\t\t\t\t["Action"] = "IGPU_ONLY_CONNECT_RB_ON",\n'
   '\t\t\t\t["SetToWMIEC"] = "OK"\n'
   '\t\t\t}, \n',
   '\t\t\t// 2026-10-05：mode 0（核显模式）用 DGPU_DIRECT_CONNECT_TOGGLE_IGPU ——\n'
   '\t\t\t// 与 LM 实测发送一致，也不再借用【核显省电】的 IGPU_ONLY_CONNECT_RB_ON（§9.6）。\n'
   '\t\t\t0 => new Dictionary<string, object> { ["Action"] = "DGPU_DIRECT_CONNECT_TOGGLE_IGPU" }, \n',
   'CreateGpuModePayload(0) 改用 TOGGLE_IGPU')


def main():
    d0 = io.open(HW, 'rb').read()
    if '核显【模式】对应的是'.encode('utf-8') in d0:
        print('已经应用过了。')
        return 0
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
        errors.append('命中 %d/%d 次(期望 %d)  [%s]' % (data.count(old), data.count(ao), expect, label))
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
