# -*- coding: utf-8 -*-
import io

ENTRY = """
[2026-10-05 01:25] 修「显卡模式切换弹两次重启提示」+ 实现用户要求的"废弃"语义
- 用户设计决策原话：「我的想法是如果在弹窗前点击其他模式了直接废弃这次的弹窗」
- 实现（tools\\patch_gpu_restart_prompt_guard.py，5 项锚点）—— 双守卫 + 真值日志：
  1. 新增 `private static int _gpuSwitchRequestSeq;`（每次 UI 切换请求 +1）
     与 `private static bool _gpuRestartPromptOpen;`
  2. `SwitchGpuModeFromUi` 入口 `int requestSeq = ++_gpuSwitchRequestSeq;`
  3. `ShowGpuRestartPromptAsync` 增加 `int requestSeq` 参数；**弹窗前**判断：
     - `requestSeq != _gpuSwitchRequestSeq` -> 已被更新的请求取代 -> **废弃**（用户要求）✅
     - `_gpuRestartPromptOpen` -> 已有提示在显示 -> **丢弃**（去重）✅
     - 只在 `MessageBox.Show` 前后置/清标志（try/finally，**不包裹整个方法体，改动最小**）
  4. 两个调用点补传 `requestSeq`
  5. `GpuSwitchPolicy.Resolve` 之后加真值日志：
     `GpuSwitch UI: target=.. route=.. current=.. gen=.. allowsRestart=.. pendingRestart=.. seq=..`
     （沿用 §9.2 教训：先打真值日志，别猜）
- 编译 0 错误；提权替换完成（L-Mechrevo.dll **1934848B** @01:24:36；
  LM PID 9208，`Responding=True` ✅）
- 备份：backup\\SettingsForm_20261005_0145_pre_promptguard.cs
- 待实测：复现"混合→独显→混合"，日志应出现 `GpuSwitch UI:` 与
  `GpuRestartPrompt: seq=.. latest=.. open=..`，
  且重复/过期的那次应记录 `superseded by a newer mode request -> discarded`
  或 `another prompt is already open -> discarded`，界面上**只弹一个**
- 未打包
"""

with io.open(r'D:\openLM\修改日志.txt', 'a', encoding='utf-8', newline='') as f:
    f.write(ENTRY)
print('appended, bytes =', len(ENTRY.encode('utf-8')))
