# -*- coding: utf-8 -*-
import io

ENTRY = """
[2026-10-05 06:00] 诊断包命名根因（已修）+ 核显模式消失的原因 + 待办（蛟龙16S/极光Pro 对比）
--------------------------------------------------------------------------------
【① 诊断包仍叫 L-Mechrevo-诊断包-30gcu-kbd.zip —— 已修】
- 根因：`DiagnosticPackCommand.ResolveDestination()`（DiagnosticPackCommand.cs:48）
  优先读**环境变量** `LMECHREVO_DIAGNOSTIC_PACK_PATH`，命中就直接返回该路径、
  **跳过保存对话框** -> 永远写到那个固定文件名 ✅
  实测 User 环境变量：`LMECHREVO_DIAGNOSTIC_PACK_PATH = D:\\openLM\\diagnostics\\L-Mechrevo-诊断包-30gcu-kbd.zip`
  （是之前做 30 系键盘抓包时设的调试钩子）
- 正规命名（DiagnosticPackExporter.BuildFileName）：
  `L-Mechrevo-诊断包-{version}-{yyyyMMdd-HHmmss}.zip` ✅
- **已修**：清除该 User 环境变量 ✅（下次导出自动用正规名 + 弹保存对话框）
  并把旧包改名为 `L-Mechrevo-诊断包-0.290.3-20261005-024208.zip` ✅
- ⚠️ 另有一个调试变量 `LMECHREVO_LOG_LEVEL = All`（User）**仍保留** ——
  它让 log.txt 暴涨（本次诊断包内 log.txt 达 **7.3 MB**）。要恢复默认日志级别就一并清掉。
- ⚠️ 注意：环境变量修改对**新启动**的进程生效，已运行的 LM 需重启。

【② 核显模式没有了 —— 原因已定位】
- 代码依据（`MechrevoHw.cs`）：
  * `:411 SupportsIgpuOnly => Capabilities.IgpuOnly;`（来自 GCU 上报的能力位）
  * `:419 CanOfferIgpuOnly` —— **:435 里有硬编码的 Gen30 排除**：
    `if (DgpuGeneration != DgpuGenerationKind.Gen30 && (SupportsDgpuDirect || SupportsIgpuOnly))`
  * `:2796 IgpuOnlyStatusSupport` 从 GCU 状态 JSON 读多个候选字段名：
    `IGpuOnlyConnectionSwitch_Support` / `IGPUOnlyConnectionSwitch_Support` / `IGPU_ONLY_SUPPORT`
  * `DisplayRouteMatrix.cs:31`（Gen30 行）载明：**30 系载荷 `IGPU_ONLY_*` / `IGPUonly/*` 全载荷 0 命中**
    -> 30 系 GCU **根本没有核显模式**（官方 30 系控制台也只有 TOGGLE_ON/_OFF）
- **结论**：本机为了测试 30 系功能**手动装了 30 系 GCU**（`GcuVariant=30`），
  而本机硬件其实是 **RTX 4060（40 系）** -> 30 系 GCU 路径下核显模式消失 **是预期行为** ✅
  想恢复核显模式 -> 装回 **40 系 GCU**（`Select-GcuPayload.ps1 -Variant 40`）
- **用户要求：寻找更可靠的判断方式**（待办）：
  现有 5 个独立信号（SettingsForm.cs:6036 已打日志 `TrayGPU:`）：
  `CanOfferIgpuOnly` / `SupportsIgpuOnly` / `SupportsDgpuDirect` /
  `CanOfferGpuModeSwitch` / `IgpuOnlyStatusSupport`
  可疑点：
  1) `:435` 的 **Gen30 硬编码排除** —— 若某 30 系机型其实支持，会被一刀切掉；
  2) 只靠 `Capabilities.IgpuOnly` 单一位；`IgpuOnlyStatusSupport` 又是**另一条路**（状态回读）
     -> 两者可能不一致（此前 0.289.1.0 就修过"官方 GCU 在 40 系三模机型不上报 IgpuOnly"）
  建议的更可靠方式：**运行时实测能力** —— 用 MQTT 发 `GETSTATUS`/读回
  `Setting/Status`，检查上述候选字段是否出现且为真；
  与代际判断**取并集**（任一为真即可显示），并在日志里记录**判定来源**。
  工具已备：`tools\\gcu_probe_gpu.py`（探测）、`gcu_readback_mode.py`（回读）

【③ 对比蛟龙16S 与 极光Pro（待办）】
- 素材已找到：
  * `D:\\openLM\\_diag_compare\\jiaolong16s`            （蛟龙 16S）
  * `D:\\openLM\\diagnostics\\jiguang_pro2024_unpacked` （极光 Pro 2024，7 个文件）
- 对比目标：这两台机器的「诊断包/系统信息」里 **GCU 实际上报了哪些能力字段**
  （尤其 `IGpuOnly*` / `DgpuDirect*` / `IGPU_ONLY_SUPPORT` 等），
  与 30 系 GCU 在本机的上报做三方对照 -> 得出"哪些位可信、哪些位缺失"的结论，
  再据此设计 ② 的更可靠判据。
- 注意：诊断包里的 `系统信息.txt` 含「GCU 连通与能力位」段
  （`DiagnosticSystemInfo.cs:442` 会输出 `IgpuOnly` 等），是最直接的对照数据源。
- ⚠️ 建议**新开会话**做 ②③（读两台机器的数据 + 探测 MQTT + 改判据 + 验证）。

【本轮改动】
- 清 User 环境变量 `LMECHREVO_DIAGNOSTIC_PACK_PATH`（**唯一代码外改动**，无源码改动）
- 旧诊断包改名归档
- 未打包
"""

with io.open(r'D:\openLM\修改日志.txt', 'a', encoding='utf-8', newline='') as f:
    f.write(ENTRY)
print('appended, bytes =', len(ENTRY.encode('utf-8')))
