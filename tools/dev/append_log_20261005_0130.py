# -*- coding: utf-8 -*-
import io

ENTRY = """
[2026-10-05 01:30] 清理死代码：ShowGpuRestartPromptAsync 里的"取代/去重"守卫（用户指出）
- 用户原话：「上轮那个守卫是不是容易代码 因为三个键的弹窗它不关掉是不能点击LM主UI的
  它是点击切换模式就弹窗的」→ **推理正确**
- 论证：`MessageBox.Show(this, ...)` 是 **owner 模态** -> 弹窗未关时 LM 主 UI 收不到点击
  -> 不可能有第二次切换请求进来 -> 因此：
    * `requestSeq != _gpuSwitchRequestSeq` **永远不成立**（序号就是本次刚加的）
    * `_gpuRestartPromptOpen` **永远为 false**（没有别的提示能同时开着）
  且该弹窗**点击切换瞬间同步弹出**，中间没有延迟窗口 -> 两处判断均为**死代码** ✅
- 改动（tools\\patch_remove_dead_prompt_guard.py）：删掉那两处判断与包裹的 try/finally，
  恢复为直接 `DialogResult dialogResult = MessageBox.Show(...)`；
  **保留** `requestSeq` 参数（仅供诊断日志）与一行日志
- **保留的字段**（仍被看门狗使用，不能删）：
  `_gpuSwitchRequestSeq`（`WatchGcuRestartFallbackAsync` 比对用）
  `_gpuRestartPromptOpen`（看门狗 lambda 内去重用）
- 编译 0 错误；提权替换完成（L-Mechrevo.dll 1934848B @01:29:10；
  LM PID 7264，`Responding=True` ✅）
- 备份：backup\\SettingsForm_20261005_0215_pre_deadguard.cs
- 结论：重启提示的正确守卫点是**15s 看门狗**（上一条已修），不是同步三键弹窗
- 未打包
"""

with io.open(r'D:\openLM\修改日志.txt', 'a', encoding='utf-8', newline='') as f:
    f.write(ENTRY)
print('appended, bytes =', len(ENTRY.encode('utf-8')))
