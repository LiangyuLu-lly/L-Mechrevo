# -*- coding: utf-8 -*-
import io

ENTRY = """
[2026-10-05 09:00] 核显模式判据【存档】+ 回答"为什么之前显示支持、现在反复"
- 用户要求：「先存档 为什么之前显示翼龙支持核显模式 现在又反复了」
- **产出存档文档：`D:\\openLM\\核显模式判据-存档与结论.md`**（含实测字段全貌、判据史、
  当前实现、已作废推断、可选增强、工具清单）
  并声明**取代**《核显模式判据设计与三方对照.md》的结论部分 ✅

【回答"为什么之前显示支持、现在又反复"】
- **真正原因 = 判据换代，不是硬件在变** ✅：
  | 时期 | 判据 | 翼龙15Pro 结果 | 出处 |
  |---|---|---|---|
  | 早期 | `CanOfferGpuModeSwitch && SupportsDgpuDirect` | ✅ **True** | 更新日志 **0.289.1.0** 原文：
       「官方 GCU 在 40 系三模机型不上报 IgpuOnly 支持字段，改为按『支持切换 && 支持独显直连』显示集显模式」 |
  | 后来 | `SupportsIgpuOnly`（GCU 能力位） | ❌ False | 被代际判据取代 |
  | 现在(10-05 恢复) | 二者并存（优先能力位，否则回退早期规则） | ✅ **True** | `MechrevoHw.cs:419` |
  -> 10-02 诊断包显示 `集显模式: 是` **正是早期规则给出的** ✅
- ⚠️ 日志里 `SupIgpu` 在 10-02 18:04 True / 18:46 False 的**能力位本身**波动：
  最可能是那晚反复换 30/40/50 系 GCU 包测试所致（日志里可见多次 GCU 安装），
  但**当时的日志没记"当前装的哪个 GCU"** -> **无确凿证据，不硬下结论** ✅

【本轮新增的实测数据（支撑存档结论）】
- 独显直连下 `Setting/Status` 全部 iGPU 相关字段只有 5 个，其中
  `IGpuOnlyConnectionSwitch_Support=NotSupport`、`IGpuOnlyConnectionSwitch_Status=IGPU_ONLY_CONNECT_RB_OFF`、
  `IGpuAutoSupportSwitch=..._OFF` **恒为否定值** -> "字段存在"**不能**当支持证据 ✅
- `Customize/Info` 只有 AppType/APPTarget/BatteryCalibration/PluginPath/DebugMode（与显卡无关）
- `GPUDevice/Status` 只有 currentHZ/currentHZList/AutoEnable（与显卡模式无关）
- 唯一常驻可用信号：`DiscreteGpuDirectConnectionSwitch_Support = Support` ✅
  -> 这正是"支持切换 && 支持独显直连"规则的依据 ✅

【当前生效状态（未变）】
- `MechrevoHw.cs:419 CanOfferIgpuOnly` 回退分支 ✅（独显直连下 `IgpuOnly=True` 已验证 ✅）
- `:441` Gen30 全屏蔽 保留 ✅
- NVRAM 平台解码修复 保留 ✅
- 之前的 `IsIgpuOnlyRouteStatus` / `_igpuRouteStatusSeen` / `IGPU-DIAG` 全部已回退无残留 ✅
- 未打包（用户要求）
"""

with io.open(r'D:\openLM\修改日志.txt', 'a', encoding='utf-8', newline='') as f:
    f.write(ENTRY)
print('appended, bytes =', len(ENTRY.encode('utf-8')))
