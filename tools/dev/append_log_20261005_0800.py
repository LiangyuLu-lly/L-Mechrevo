# -*- coding: utf-8 -*-
import io

ENTRY = """
[2026-10-05 08:00] ✅ 核显模式判据【修好了】—— 根因是一个子串写错
================================================================================
【真值（修诊断行后拿到）】
```
03:11:53  IGPU-DIAG: text24=DGPU_DIRECT_CONNECT_TOGGLE_IGPU  text25=IGPU_ONLY_CONNECT_RB_OFF
                     keys=38  has24=True  has25=True
                     gpuKeys=[DGpu, DiscreteGpuDirectConnectionSwitch_Status,
                              DiscreteGpuDirectConnectionSwitch_Support,
                              IGpuOnlyConnectionSwitch_Status,
                              IGpuOnlyConnectionSwitch_Support, IGpuAutoSupportSwitch]
                     seen=False        <- 置位块没跑
```
- ✅ **`text24` 一直有值**（LM 读得到），字段名/时机都没问题
- ⚠️ **`o` 是 `JObject`（Newtonsoft）不是 `Dictionary`** —— 顺带踩了个编译错（`Keys` -> `Properties()`）

【🎯 真正的根因】
真实 token = `DGPU_DIRECT_CONNECT` + `_TOGGLE_` + `IGPU`
而我写的判断是 `text24.Contains("DIRECT_CONNECT_IGPU")` -> **中间有 `_TOGGLE_`，永远不匹配** ❌
（`MechrevoHw.cs:1480` 原本是
  `text == "DGPU_DIRECT_CONNECT_TOGGLE_IGPU" || text.Contains("DIRECT_CONNECT_IGPU")`
 —— 我**只抄了后半句** ✅，精确相等那一半才是关键 ✅）

【修法（tools\\patch_igpu_token_fix.py，2 项锚点）】
两处都改成"精确相等 或 含 DIRECT_CONNECT_IGPU"：
  A) `MechrevoHw.cs` 置位块（v2/v3 引入的那个 if）
  B) `MechrevoDeviceCapabilities.cs` 的 `IsIgpuOnlyRouteStatus`
    ---- 顺带说明 v1/v2/v3 之前"无效"的真正原因就是这个子串 ❌，
         不是"两条路不通"、也不是"报文分多条重置"（那两条推断都作废 ✅）

【验证】
```
03:12:29  IGPU-DIAG: ... seen=True
03:12:29  TrayGPU: IgpuOnly=True SupIgpu=True SupDgpu=True Switch=True IgpuStatus=True   ✅
```
- 产物 1936896 字节 @03:12:21；替换完成 03:12:21；LM 已加载新 DLL
- 预期：**核显模式按钮出现**，且当前处于核显模式时高亮在"核显模式"（待用户目视确认）

【最终生效改动清单（本次任务，共 4 处）】
1. `MechrevoDeviceCapabilities.cs`
   - `FromValues` 的 `igpuOnly` 并上 `IsIgpuOnlyRouteStatus(...)`（保留 ✅）
   - 新增 `IsIgpuOnlyRouteStatus`（token 精确匹配 + 兼容 Contains）
2. `MechrevoHw.cs`
   - `SupportsIgpuOnly => Capabilities.IgpuOnly || _igpuRouteStatusSeen || (IgpuOnlyStatusSupport ?? false)`
   - 新增 `private bool _igpuRouteStatusSeen`（**正向锁存**：只置位不清除）
   - Setting/Status 解析处：`text24` 命中即置 `IgpuOnlyStatusSupport = true` + `_igpuRouteStatusSeen = true`
   - **保留 `IGPU-DIAG` 日志行**（`WriteLineIfChanged`，只在变化时输出）
     -> 正好充当设计文档里说的"溯源日志"，下次排查直接用 ✅
3. 备份：backup\\MechrevoHw_20261005_pre_igpucap2.cs、
        backup\\MechrevoDeviceCapabilities_20261005_pre_igpucap.cs

【⚠️ 遗留（未做，供后续）】
1. `MechrevoHw.cs:435` 的 **Gen30 硬编码排除**（`CanOfferGpuModeSwitch`）仍未动
   —— 对真 30 系有据（官方 30 系载荷无 IGPU_ONLY_*），但仍是"按代际猜"；
   本次未改是因为 30 系那条路径**没有实测样本**，按 §9.2 教训不猜 ✅
2. `_igpuRouteStatusSeen` 的作用域 = **本次 LM 运行**（重启 LM 会清零 ✅ 可接受）
   —— 若换 GCU 变体（如切 30 系）应重置；目前靠重启 LM 达成 ✅
3. 《核显模式判据设计与三方对照.md》里"能力位不可靠"的旧结论**仍需更正**为：
   **不是位不可靠，而是 LM 没看对字段 + 子串匹配写错** ✅
4. 蛟龙16S / 极光Pro 的诊断包仍是日志 Off，无法三方对照运行时信号 ✅
- 未打包（用户要求）
"""

with io.open(r'D:\openLM\修改日志.txt', 'a', encoding='utf-8', newline='') as f:
    f.write(ENTRY)
print('appended, bytes =', len(ENTRY.encode('utf-8')))
