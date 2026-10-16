# -*- coding: utf-8 -*-
import io

ENTRY = """
[2026-10-05 08:20] 核显模式判据改动【全部回退】+ 保留 Gen30 全屏蔽
- 用户反馈：「切独显模式核显模式又不显示了 暂时回退吧 30系没有支持核显模式的机器 30系可以全部屏蔽的」
- **为什么切独显后又消失**：切到独显直连后 `text24` 变成 `DGPU_DIRECT_CONNECT_TOGGLE_ON`，
  而我的 `_igpuRouteStatusSeen` 锁存作用域是**本次 LM 运行** -> 切模式伴随重启 -> 锁存清零
  -> 此时 GCU 不再提供任何核显证据（`IGpuOnlyConnectionSwitch_Support=NotSupport`、
     `iGPUModeOnlySupport` 缺失）-> 按钮又消失 ✅
  （即：我的方案只能在"当前处于核显模式"时把按钮显示出来，无法在切走之后继续提供入口）
- **回退方式：外科式**（只删本次加的行，**不整文件还原**）
  ⚠️ 原因：两个备份是 10-03 / 10-04 的（`MechrevoDeviceCapabilities_20261005_pre_igpucap.cs`
     15684B@10-03、`MechrevoHw_20261005_pre_igpucap2.cs` 136425B@10-04 22:48），
     整文件还原可能丢掉 10-04 之后的其它工作 -> 放弃整文件还原 ✅
  工具：`tools\\revert_igpu_judgment.py`（5 项锚点，含行尾自动适配）
  回退清单：
  1. `MechrevoHw.cs`：`SupportsIgpuOnly` 还原为 `=> Capabilities.IgpuOnly;`
  2. `MechrevoHw.cs`：删除 sticky 字段 `_igpuRouteStatusSeen`
  3. `MechrevoHw.cs`：删除 `IGPU-DIAG` 诊断 4 行
  4. `MechrevoHw.cs`：删除 `text24` 置位块
  5. `MechrevoDeviceCapabilities.cs`：`igpuOnly` 那行还原为单独的 `Flag(...)`
  6. `MechrevoDeviceCapabilities.cs`：删除 `IsIgpuOnlyRouteStatus` 方法
- **验证**：
  ```
  03:16:35  TrayGPU: IgpuOnly=False SupIgpu=False SupDgpu=True Switch=True IgpuStatus=False  ✅ 回到原始
  （日志中已无 IGPU-DIAG 行 ✅）
  ```
  产物 1935872 字节 @03:16:27；替换完成 03:16:27；编译 0 错误
- **✅ 保留不动**：`MechrevoHw.cs:435` 的 **Gen30 硬编码排除**
  （`if (DgpuGeneration != Gen30 && (SupportsDgpuDirect || SupportsIgpuOnly))`）
  用户明确：「30系没有支持核显模式的机器 30系可以全部屏蔽的」
  -> 该排除正是"30 系全部屏蔽核显模式"的实现，**符合预期，不需要改** ✅
  -> 我此前设计文档里"应把 Gen30 从否决条件降级"的建议**作废** ✅
- 备份：backup\\MechrevoHw_20261005_pre_revert.cs、
        backup\\MechrevoDeviceCapabilities_20261005_pre_revert.cs
  （回退前的状态，含本次改动，便于日后重新评估）
- **经验（留给日后）**：要想"切走之后仍有核显入口"，光靠 GCU 上报是不够的
  （该变体只在**当前就是核显模式**时才报 TOGGLE_IGPU）——需要跨重启持久化
  （例如把"曾见过 TOGGLE_IGPU"写进 LM 配置，或按机型画像/BIOS 能力判断）✅
- 未打包（用户要求）
"""

with io.open(r'D:\openLM\修改日志.txt', 'a', encoding='utf-8', newline='') as f:
    f.write(ENTRY)
print('appended, bytes =', len(ENTRY.encode('utf-8')))
