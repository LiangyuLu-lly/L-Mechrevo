# -*- coding: utf-8 -*-
import io

ENTRY = """
[2026-10-05 06:30] ①三方能力位对照 ②核显模式判据重新设计（均已落盘为文档）
- 用户已切回 **40 系 GCU**（实测 `HKLM\\SOFTWARE\\L-Mechrevo\\GcuVariant = 40`，
  GcuInstalledUtc 2026-10-04T18:38:37Z）-> 核显模式应已恢复 ✅
- **产出文档：`D:\\openLM\\核显模式判据设计与三方对照.md`**（含全部对照表与设计）
- 工具：`tools\\compare_gcu_caps.py`（三方能力位对照，可复跑）

【① 三方对照结论】
| 能力位 | 蛟龙16S | 极光Pro2024 | 翼龙15Pro(本机) |
|---|---|---|---|
| 型号 | GM6XG0X | GM6PQ0X | GM5HG0A |
| CPU / iGPU | R7 7840HS / AMD 780M | i7-13650HX / Intel UHD | R7 8745H / AMD 780M |
| dGPU | RTX 4060 | RTX 4060 | RTX 4060 |
| 显卡直连 | 是 | 是 | 是 |
| **集显/核显模式** | **否** | **否** | **是** |
| 显卡热切换 | 否 | 否 | 否 |
| GCU 版本 | （未列出） | **1.0.2.70（40 系主服务）** | （未列出） |
- 关键：**三台都是 40 系 + RTX 4060，但核显模式支持不一致（只有本机是"是"）**；
  极光Pro 装的正是 **40 系 GCU（GCUService 1.0.2.70）** 却仍报"否"
  -> **代际不能决定核显模式是否可用** ✅
- 蛟龙16S / 极光Pro 的诊断包**日志级别 Off -> 无 log.txt / 无 TrayGPU 行**
  -> 三方"运行时可观测信号"对不齐 -> **必须先在 LM 里把判定依据打成日志** ✅
- ⚠️ 本机自身时间线证明能力位**不稳定**（同一台机器）：
  `09-30 23:09 SupIgpu=False` -> `10-02 18:04 SupIgpu=True` -> `10-02 18:46 SupIgpu=False,IgpuStatus=False`
  -> 一次读空就会把按钮藏掉 ✅

【② 判据重新设计（详见文档 §三）】
- 现状信号（MechrevoHw.cs）：
  `:411 SupportsIgpuOnly = Capabilities.IgpuOnly`（会变 ⚠️）
  `:2796 IgpuOnlyStatusSupport` 状态回读（候选名 IGpuOnlyConnectionSwitch_Support /
        IGPUConnectionSwitch_Support / IGPU_ONLY_SUPPORT；可为 null ⚠️）
  `:435 if (DgpuGeneration != Gen30 && (SupportsDgpuDirect || SupportsIgpuOnly))` <- **静态代际硬编码**
  `:419 CanOfferIgpuOnly` 组合；`DisplayRouteMatrix.AllowsIgpuOnly(gen)` 静态协议表
- **两个问题**：①`:435` 的 Gen30 一票否决是"按代际猜"（对真 30 系有据，但会误伤
  "30 系 GCU 装在 40 系机器"这种测试场景）②能力位与状态回读**未取并集**，任一为假即隐藏
- **设计**：
  S1 能力上报 / S2 状态回读 / S3 **NVRAM 显示字节 0x62**（硬件真值，日志里已有
  `NVRAM display byte @0x62=1 (len=180, intel-decoded=IgpuOnly; offset unverified)`）
  / S4 协议词汇是否存在（`IGPU_ONLY_CONNECT_RB_*`）
  -> **任一为真即支持**；只有 `DisplayRouteMatrix` 标了 `ProvenAbsent` **且 S1..S4 全无**
     才判"不支持"（把代际从**否决条件**降级为**证据之一**）
  -> **正向锁存**：一旦见过支持就锁存（照 `_telemetryRpmSeen` 的写法），修"按钮忽隐忽现"
  -> **溯源日志** `IgpuOnlyDecision: supported=.. by=S? gen=.. gcuVariant=..`，
     并把 S1..S4 写进诊断包 `[能力位]` 段（`DiagnosticSystemInfo.cs:442` 附近）
     这样下一份外部诊断包就能直接对照 ✅
- 30 系 GCU 因 `IGPU_ONLY_*` 词汇确实不存在 -> S1/S2/S4 自然全假 -> **结论不变**
  （仍不支持），但依据从"猜代际"变成"实际观测" ✅
- **待办**：实现 3.1/3.2/3.4；校准 0x62 语义；回归验证（本机 40 系应显示、
  切 30 系应隐藏且日志 `by=hardAbsent`）；让用户提供**开着 All 日志**的新外部诊断包
- 未打包；无源码改动（本轮为调研 + 设计 + 文档）
"""

with io.open(r'D:\openLM\修改日志.txt', 'a', encoding='utf-8', newline='') as f:
    f.write(ENTRY)
print('appended, bytes =', len(ENTRY.encode('utf-8')))
