# -*- coding: utf-8 -*-
r"""
恢复「更早的逻辑」：CanOfferIgpuOnly 增加回退分支（支持切换 && 支持独显直连）
================================================================================
背景（用户要求）：「暂时1 …或者先回退到更早的逻辑吧 但是保留30系的屏蔽」
  更新日志 0.289.1.0 原文：
    修复托盘核显（集显模式）不显示问题：官方 GCU 在 40 系三模机型不上报 IgpuOnly
    支持字段，改为按「支持切换 && 支持独显直连」显示集显模式
  现状代码（:419-429）只认 SupportsIgpuOnly（本机为 False）-> 核显模式不显示 ❌

改法：**只加回退分支**（原路径保留，风险最小）
    支持核显 = SupportsIgpuOnly 时按原逻辑（含代际动作校验）
             否则退化为 CanOfferGpuModeSwitch && SupportsDgpuDirect

✅ 30 系屏蔽自动保留：CanOfferGpuModeSwitch 内部已有
   `if (DgpuGeneration != DgpuGenerationKind.Gen30 && ...)`（:435）
   -> Gen30 时 CanOfferGpuModeSwitch=False -> 回退分支也为 False ✅

⚠️ 不动 ThreeModeCapability（:415）：它被 CanOfferGpuModeSwitch 经 :1378 间接使用，
   若反过来依赖 CanOfferGpuModeSwitch 会**无限递归** ❌
⚠️ 切换路径无需改：CreateGpuSwitchPayload(mode=0, supDgpu=true, supIgpu=false)
   条件为假 -> 正好返回 DGPU_DIRECT_CONNECT_TOGGLE_IGPU（实测有效那条）✅
"""
import io
import sys

F = r'D:\openLM\source\MechrevoLite.Hardware\MechrevoHw.cs'

OLD = ('\tpublic bool CanOfferIgpuOnly\n'
       '\t{\n'
       '\t\tget\n'
       '\t\t{\n'
       '\t\t\tif (SupportsIgpuOnly)\n'
       '\t\t\t{\n'
       '\t\t\t\treturn IsGpuActionAllowedByGeneration("IGPU_ONLY_CONNECT_RB_ON");\n'
       '\t\t\t}\n'
       '\t\t\treturn false;\n'
       '\t\t}\n'
       '\t}\n')

NEW = ('\tpublic bool CanOfferIgpuOnly\n'
       '\t{\n'
       '\t\tget\n'
       '\t\t{\n'
       '\t\t\tif (SupportsIgpuOnly)\n'
       '\t\t\t{\n'
       '\t\t\t\treturn IsGpuActionAllowedByGeneration("IGPU_ONLY_CONNECT_RB_ON");\n'
       '\t\t\t}\n'
       '\t\t\t// 2026-10-05（恢复更早的逻辑）：部分 40 系三模 GCU 变体不上报 IgpuOnly 支持字段\n'
       '\t\t\t// （实测 GCUService 1.0.2.70：IGpuOnlyConnectionSwitch_Support=NotSupport、\n'
       '\t\t\t// iGPUModeOnlySupport 缺失），但确实支持三模 —— 发 IGPU_ONLY_CONNECT_RB_ON 后\n'
       '\t\t\t// NVRAM @0x62=2 且 NVIDIA 被屏蔽。\n'
       '\t\t\t// 因此按「支持切换 && 支持独显直连」判定（与 0.289.1.0 的修法一致）。\n'
       '\t\t\t// 注意：Gen30 的屏蔽由 CanOfferGpuModeSwitch 内部的代际判定保留（见 :435）。\n'
       '\t\t\treturn CanOfferGpuModeSwitch && SupportsDgpuDirect;\n'
       '\t\t}\n'
       '\t}\n')


def main():
    data = io.open(F, 'rb').read()
    ob, nb = OLD.encode('utf-8'), NEW.encode('utf-8')
    if data.count(ob) == 1:
        pass
    else:
        alt = ob.replace(b'\r\n', b'\n')
        if data.count(alt) == 1:
            print('  (行尾适配)')
            ob, nb = alt, nb.replace(b'\r\n', b'\n')
        else:
            if b'CanOfferGpuModeSwitch && SupportsDgpuDirect' in data:
                print('已经应用过了。')
                return 0
            print('=== 锚点命中 %d 次，未写盘 ===' % data.count(ob))
            return 1
    print('=== 锚点校验通过 ===')
    if '--check' in sys.argv:
        return 0
    io.open(F, 'wb').write(data.replace(ob, nb, 1))
    print('  写入 ' + F)
    return 0


if __name__ == '__main__':
    sys.exit(main())
