# -*- coding: utf-8 -*-
import io

ENTRY = """
[2026-10-05 08:40] ✅ 核显模式【恢复更早的逻辑】—— 独显直连下也显示（用户要求）
================================================================================
用户指示：「暂时1 问题是在独显模式能知道有igpu档位吗 或者先回退到更早的逻辑吧 但是保留30系的屏蔽」
- 「暂时1」= 用方案 A（先恢复能显示）
- 「独显模式能知道有 igpu 档位吗」-> **直接看 GCU 字段不能**（该变体只在当前就是核显模式时
  才报 `DGPU_DIRECT_CONNECT_TOGGLE_IGPU`）；但**间接能** —— 它支持三模的全部动作
  （实测发 IGPU_ONLY_CONNECT_RB_ON 后 NVRAM @0x62=2、NVIDIA 被屏蔽），
  所以「支持切换 && 支持独显直连」是可靠代理 ✅
- 「回退到更早的逻辑」-> 找到了！**更新日志 0.289.1.0 原文**：
    「修复托盘核显（集显模式）不显示问题：官方 GCU 在 40 系三模机型不上报 IgpuOnly
      支持字段，改为按『支持切换 && 支持独显直连』显示集显模式」
  —— 这正是这台机器**当初能显示核显模式**的原因 ✅，后来被代际判据取代了 ❌

【改法（tools\\patch_igpu_restore_earlier_rule.py，1 项锚点）】
`MechrevoHw.cs` 的 `CanOfferIgpuOnly` **只加回退分支**（原路径保留，风险最小）：
```csharp
if (SupportsIgpuOnly)
{
    return IsGpuActionAllowedByGeneration("IGPU_ONLY_CONNECT_RB_ON");   // 原逻辑不动
}
// 恢复更早的逻辑：支持切换 && 支持独显直连
return CanOfferGpuModeSwitch && SupportsDgpuDirect;
```
- ✅ **30 系屏蔽自动保留**（用户明确要求）：`CanOfferGpuModeSwitch` 内部已有
  `:435 if (DgpuGeneration != DgpuGenerationKind.Gen30 && ...)` -> Gen30 时为 False
  -> 回退分支也为 False ✅ **无需额外改动** ✅
- ✅ **不再依赖当前模式** -> 切到独显/混合后按钮**依然在** ✅（上一版失效的根因就是依赖当前上报）
- ⚠️ **刻意不动 `ThreeModeCapability`（:415）**：它被 `CanOfferGpuModeSwitch` 经 `:1378`
  间接使用，若反过来依赖 `CanOfferGpuModeSwitch` 会**无限递归** ❌
- ⚠️ **切换路径无需改**：`CreateGpuSwitchPayload(mode=0, supDgpu=true, supIgpu=false)`
  条件为假 -> 正好返回 `DGPU_DIRECT_CONNECT_TOGGLE_IGPU`（实测有效那条）✅

【验证】
```
03:20:15  TrayGPU: IgpuOnly=True SupIgpu=False SupDgpu=True Switch=True IgpuStatus=False
当前路由: DiscreteGpuDirectConnectionSwitch_Status = DGPU_DIRECT_CONNECT_TOGGLE_ON（独显直连）
```
-> **独显直连下 `IgpuOnly=True`** ✅ = 按钮应出现且能一直用 ✅（待用户目视确认）
- 产物 1935872 字节 @03:20:02；替换完成 03:20:02；编译 0 错误
- 备份：backup\\MechrevoHw_20261005_pre_earlierrule.cs

【本次会话核显相关最终状态】
- 生效改动：**只有** `CanOfferIgpuOnly` 的回退分支 ✅（其余 iGPU 试验已全部回退 ✅）
- 保留：Gen30 全屏蔽（`:435`）✅、NVRAM 平台解码修复（AMD 不再按 Intel 解）✅
- 未生效/已回退：`MechrevoDeviceCapabilities` 的 `IsIgpuOnlyRouteStatus`、
  `_igpuRouteStatusSeen` 锁存、`IGPU-DIAG` 诊断 ✅ 全部清除 ✅
- 未打包（用户要求）
"""

with io.open(r'D:\openLM\修改日志.txt', 'a', encoding='utf-8', newline='') as f:
    f.write(ENTRY)
print('appended, bytes =', len(ENTRY.encode('utf-8')))
