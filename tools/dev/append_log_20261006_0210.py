# -*- coding: utf-8 -*-
import io

ENTRY = """
[2026-10-06 02:10] 立规矩：验证力度与功能体量配比（用户明确要求）+ 第⑧步客户端遥测管线完成
================================================================================
【用户原话与规矩（长期有效）】
- 「还有就是不要重度自证浪费过多时间和token」
- 「基本的验证还是要的 你来把握 就是不要在一些小功能废太多时间了」
=> 立为**长期尺度**：
   1) 基本验证必做：编译 0 错误 → 部署 → 程序能起 → 日志无异常；
   2) 再加 **1 个**针对本次核心逻辑的**轻量**检查（能用临时配置/桩宿主几分钟搞定的就用，别造工具链）；
   3) **严禁**：新建取证/截图/渲染工具（如再版 uvshot/d1shot，动辄 31 MB）、场景矩阵、逐字节比对、
      DPI/宽高自检、目录清点与"抢救归档"；
   4) **时间盒**：单个验证若需 >5 分钟搭工具/环境，**跳过并在报告里写明原因**；
   5) 报告精简（≤25 行），日志一段话。
   > 触发这次立规的具体案例：D-1 只加了一句提示文案，却配了 31.79 MB 的取证工具 + 7 场景矩阵，
   > 耗时约 15 分钟 —— 明显不成比例（该工具事后已归档到 archive\\tools-20261005\\d1shot）。

【本轮成果：上游 0.290.6 第⑧步「遥测管线」客户端部分 + 方案 C（按上述尺度完成）】
- 改动：`MechrevoLite.Usage\\UsageTelemetry.cs`（重写日志管线，保留我们自己的心跳/Capture/脱敏）、
  新增 `TelemetryLogOutbox.cs` / `TelemetryLogBatch.cs`、`Program.cs` 3 处异常处理器接入；
  `Logger.cs` **未改** —— 核实"独立错误环(Errors=16384) / ErrorRecorded 事件 / SnapshotErrorBuffer"
  今天已随 ①~⑥ 合并进来，跳过不重做 ✅
- 新增配置键：`telemetry_ack_fallback` = **1（默认开）**＝无回执时把 HTTP 200 视为已确认，
  置 0 退回严格模式；`telemetry_outbox_max_batches` = 32（钳制 8~128）
- 待传队列策略：默认 32 批，按文件名（UtcNow.Ticks）**超限删最旧**；损坏条目隔离为 `*.invalid`（最多 16）；
  写入失败/淘汰经节流日志可观测且不自我放大
- 方案 C 两条分支（代码走查）：`UsageTelemetry.cs:815-818` 回执不匹配 → **不删** + 报错日志；
  `:819-825` 回执缺失 → `AckFallbackEnabled` 为真则确认，否则不确认
- 验收（轻量，符合新尺度）：编译 0 错误（1251 既有 warning 无新增类别）；
  部署 DLL 2,080,768 B @00:52:13 → `C:\\Program Files\\L-Mechrevo\\`；LM 重启 PID 23856，启动日志 87 行无异常；
  **队列上限/淘汰**用真实 `TelemetryLogOutbox.cs` + 3 个最小桩挂进 `%TEMP%` 临时宿主，9 项断言全 PASS
  （40→32、淘汰恰为最旧 8 批、上限 8 生效、999 钳制到 128、损坏隔离、Acknowledge 等）
- ⚠️ 隐私：本机 `usage_telemetry = 0`（用户关闭上报）→ **全程零上传** ✅，且**未生成** telemetry-outbox 目录 ✅
  （本次没有打开该开关、没有联网测试）
- 未做/差异：上游第⑧步里非遥测客户端的 `PowerWallVerifier`、`HardwareControl`/`CustomModeForm` 的
  `DateTime.UtcNow`（属 A-38）按时间盒跳过；崩溃路径**故意**不做上游的 1.5s 网络排空
  （`FlushFatalLog` 只保证证据落盘）—— 与上游有行为差异，已记录
- ⚠️ 发现：0:47 已存在同任务的前序产物（`backup\\20261006_0200_pre_telemetry\\` + outbox 两文件），
  即**被取消的那次运行留下了部分产物**；本次备份内容与其一致，判断为同一任务重跑，无冲突
- 未打包、未打 NoGCU、未动 installer-src ✅
"""

with io.open(r'D:\openLM\修改日志.txt', 'a', encoding='utf-8', newline='') as f:
    f.write(ENTRY)
print('appended, bytes =', len(ENTRY.encode('utf-8')))
