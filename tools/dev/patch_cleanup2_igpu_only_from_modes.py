# -*- coding: utf-8 -*-
r"""
第二类清理：把【核显省电】命令(IGPU_ONLY_*)从「三档显卡模式按钮」路径彻底清除
================================================================================
用户要求：「把热切切换（核显省电模式）完全从三档按钮清理掉 因为已经有独立按钮了」

清理依据（都在本次会话里查实）：
- 核显省电是**独立功能**：UI `_igpuSavingRow` -> `SwitchIgpuSavingAsync`
  -> `MechrevoService.SetIGpuOnlySaveMode()` -> `IGPU_ONLY_CONNECT_RB_{ON,OFF,AUTO}`
- `DisplayRouteMatrix:48`（Gen50 官方控制台反编译）显示官方**同时具备两族命令**
  -> 二者本是不同功能（§9.6）
- 极光 50 系实测（RTX 5070 Ti / Gen50）：切核显(target=0) 发的是
  `GPU restart route applied for target=0: 1 payload(s) [DGPU_DIRECT_CONNECT_TOGGLE_IGPU]`
  -> 三档路径本就该用 DGPU_DIRECT_CONNECT_* ✅
- 本机同一 mode 1 的另一分支（CreateGpuRestartTargetPayloads:1736）**已经在用**
  `DGPU_DIRECT_CONNECT_TOGGLE_OFF` -> 说明原表里的 ...RB_OFF 是**内部不一致的遗留**

改动（4 处）：
  1. CreateGpuModePayload(1)：IGPU_ONLY_CONNECT_RB_OFF(+SetToWMIEC) -> DGPU_DIRECT_CONNECT_TOGGLE_OFF
  2. CreateGpuRestartTargetPayloads(2)：3 条 [ON, CreateGpuModePayload(1), ON] -> 1 条 [DGPU_DIRECT_CONNECT_TOGGLE_ON]
     （中间那条唯一目的是"清核显省电"，属要清掉的对象；极光 50 系实测同类切换就是 1 条载荷）
  3. MechrevoService:1888-1896  删除切 mode 2 前先发 IGPU_ONLY_CONNECT_RB_OFF 的整块
  4. MechrevoHw:3436-3444       同上（重复实现）
保留不动：
  - CreateGpuModePayload(3) = IGPU_ONLY_CONNECT_RB_AUTO（mode 3 = 核显省电 AUTO，属独立按钮功能）
  - SetIGpuOnlySaveMode / IgpuOnlySaveMode / 状态词表 / 核显省电 UI（独立功能，完整保留）
"""
import io
import sys

SVC = r'D:\openLM\source\MechrevoLite.Hardware\MechrevoService.cs'
HW = r'D:\openLM\source\MechrevoLite.Hardware\MechrevoHw.cs'

OPS = []


def op(path, old, new, label, expect=1):
    if isinstance(old, str):
        old = old.encode('utf-8')
    if isinstance(new, str):
        new = new.encode('utf-8')
    OPS.append((path, old, new, expect, label))


# ---- 1) CreateGpuModePayload(1) ----
op(SVC,
   '\t\t\t1 => new Dictionary<string, object>\n'
   '\t\t\t{\n'
   '\t\t\t\t["Action"] = "IGPU_ONLY_CONNECT_RB_OFF",\n'
   '\t\t\t\t["SetToWMIEC"] = "OK"\n'
   '\t\t\t}, \n',
   '\t\t\t// 2026-10-05：混合模式用 DGPU_DIRECT_CONNECT_TOGGLE_OFF ——\n'
   '\t\t\t// 与同文件 CreateGpuRestartTargetPayloads 的 supportsDgpuDirect 分支一致；\n'
   '\t\t\t// IGPU_ONLY_CONNECT_RB_* 归【核显省电】独立功能，三档模式不再借用（§9.6）。\n'
   '\t\t\t1 => new Dictionary<string, object> { ["Action"] = "DGPU_DIRECT_CONNECT_TOGGLE_OFF" }, \n',
   'CreateGpuModePayload(1) -> TOGGLE_OFF')

# ---- 2) CreateGpuRestartTargetPayloads(2)：3 条 -> 1 条 ----
op(SVC,
   '\t\tcase 2:\n'
   '\t\t\treadOnlyList3 = new _003C_003Ez__ReadOnlyArray<Dictionary<string, object>>(new Dictionary<string, object>[3]\n'
   '\t\t\t{\n'
   '\t\t\t\tnew Dictionary<string, object> { ["Action"] = "DGPU_DIRECT_CONNECT_TOGGLE_ON" },\n'
   '\t\t\t\tCreateGpuModePayload(1),\n'
   '\t\t\t\tnew Dictionary<string, object> { ["Action"] = "DGPU_DIRECT_CONNECT_TOGGLE_ON" }\n'
   '\t\t\t});\n'
   '\t\t\tbreak;\n',
   '\t\tcase 2:\n'
   '\t\t\t// 2026-10-05：原来是 3 条 [TOGGLE_ON -> CreateGpuModePayload(1) -> TOGGLE_ON]，\n'
   '\t\t\t// 中间那条唯一目的是"清核显省电"（属独立功能，已从三档移除）。\n'
   '\t\t\t// 极光 50 系(Gen50)实测：同类冷切换只有 1 条载荷 —— 收敛为 1 条。\n'
   '\t\t\treadOnlyList3 = new _003C_003Ez__ReadOnlySingleElementList<Dictionary<string, object>>(new Dictionary<string, object> { ["Action"] = "DGPU_DIRECT_CONNECT_TOGGLE_ON" });\n'
   '\t\t\tbreak;\n',
   'CreateGpuRestartTargetPayloads(2) 收敛为 1 条')

# ---- 3) MechrevoService 删除切 mode 2 前的 ...RB_OFF ----
op(SVC,
   '\t\t\tif (mode == 2)\n'
   '\t\t\t{\n'
   '\t\t\t\tawait _hw.Publish("Setting/Control", new Dictionary<string, object>\n'
   '\t\t\t\t{\n'
   '\t\t\t\t\t["Action"] = "IGPU_ONLY_CONNECT_RB_OFF",\n'
   '\t\t\t\t\t["SetToWMIEC"] = "OK"\n'
   '\t\t\t\t}).ConfigureAwait(continueOnCapturedContext: false);\n'
   '\t\t\t\tawait Task.Delay(300, requestCts.Token).ConfigureAwait(continueOnCapturedContext: false);\n'
   '\t\t\t}\n',
   '\t\t\t// 2026-10-05：删除"切独显直连前先发 IGPU_ONLY_CONNECT_RB_OFF"——\n'
   '\t\t\t// 那是【核显省电】命令，已由独立按钮负责，三档模式不再发送（§9.6）。\n',
   'MechrevoService 删除 mode2 前的 RB_OFF')

# ---- 4) MechrevoHw 同样 ----
op(HW,
   '\t\t\tif (mode == 2)\n'
   '\t\t\t{\n'
   '\t\t\t\tawait Publish("Setting/Control", new Dictionary<string, object>\n'
   '\t\t\t\t{\n'
   '\t\t\t\t\t["Action"] = "IGPU_ONLY_CONNECT_RB_OFF",\n'
   '\t\t\t\t\t["SetToWMIEC"] = "OK"\n'
   '\t\t\t\t});\n'
   '\t\t\t\tawait Task.Delay(300);\n'
   '\t\t\t}\n',
   '\t\t\t// 2026-10-05：同上 —— 核显省电命令不再出现在三档模式切换里（§9.6）。\n',
   'MechrevoHw 删除 mode2 前的 RB_OFF')


def main():
    probe = io.open(SVC, 'rb').read()
    if '中间那条唯一目的是'.encode('utf-8') in probe:
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
        print('  写入 ' + path.split('\\')[-1] + '  <- ' + label)
    return 0


if __name__ == '__main__':
    sys.exit(main())
