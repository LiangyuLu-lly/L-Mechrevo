# -*- coding: utf-8 -*-
import io

ENTRY = """
[2026-10-05 01:28] 🔴 真凶：重启"看门狗"被重复派发（上轮改错了函数）
- 用户复现反馈：「还是弹了两个」+ 截图
- **截图给出的决定性线索**：两个弹窗内容与按钮**完全相同**，都是
  「GCU 未自动重启系统…点击「确定」5 秒后重启 Windows 完成切换。」+ **确定/取消(OKCancel)**
  -> 而上轮我改的 `ShowGpuRestartPromptAsync` 用的是 `Strings.GpuRestartPrompt` + `YesNoCancel`
  -> **改错了地方**（那是流程里的第一个弹窗；那个守卫本身仍正确，保留）
- 定位真正的弹窗：
  ```
  GpuRestartManualPrompt = 「GCU 未自动重启系统。显卡模式指令已写入，重启后才会生效。
                             点击「确定」5 秒后重启 Windows 完成切换。」
  使用点：SettingsForm.cs:6855（在 WatchGcuRestartFallbackAsync 内）
  ```
- **真因**：
  ```
  :6840  WatchGcuRestartFallbackAsync();      // fire-and-forget，【每次切换请求派发一个】
  方法内：await Task.Delay(15000);            // 等 15s 看 GCU 有没有自动重启
          BeginInvoke(() => MessageBox.Show(... GpuRestartManualPrompt ... OKCancel ...));
  ```
  用户做了 **2 次**切换请求 -> **2 个独立看门狗** -> 15s 后**各弹一个** ✅
  与"分别弹了两个"、且两个堆叠同时出现 —— 完全吻合
- 修法（tools\\patch_gpu_restart_watchdog_supersede.py，3 项锚点）—— 实现用户要求的废弃语义：
  1. `WatchGcuRestartFallbackAsync(int requestSeq)`，调用点传序号
  2. 15s 后先比对：`requestSeq != _gpuSwitchRequestSeq` ->
     **已被更新的请求取代 -> 废弃** ✅（用户原话：「如果在弹窗前点击其他模式了直接废弃这次的弹窗」）
  3. `BeginInvoke` 的 lambda 内用 `_gpuRestartPromptOpen` 去重
     （在 **UI 线程内**检查/置位，天然原子；try/finally 复位）
- 编译 0 错误；提权替换完成（L-Mechrevo.dll **1935360B** @01:27:01；
  LM PID 9440，`Responding=True` ✅）
- 备份：backup\\SettingsForm_20261005_0200_pre_watchdog.cs
- 教训（再次印证 §9.2）：**先看"弹窗长什么样/什么按钮"再定位函数** ——
  上轮我按"提示重复"猜代码，没核对弹窗文本与按钮，白改一处。
- 待实测：快速连点两次显卡模式 -> 15s 后应**只弹一个**；
  日志应出现 `GCU restart fallback: superseded (seq=.. latest=..) -> discarded`
- 未打包
"""

with io.open(r'D:\openLM\修改日志.txt', 'a', encoding='utf-8', newline='') as f:
    f.write(ENTRY)
print('appended, bytes =', len(ENTRY.encode('utf-8')))
