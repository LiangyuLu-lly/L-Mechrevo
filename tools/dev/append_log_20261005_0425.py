# -*- coding: utf-8 -*-
import io

ENTRY = """
[2026-10-05 04:25] 修 30 系 GCU 无风扇转速导致的两个显示问题
- 用户报：「30系GCU没有风扇转速读取 主界面cpu和风扇转速百分比中间隔一大块 托盘浮窗则是转速-1」
- **根因定位（两处）**：
  1) 主界面：`SettingsForm.cs` BuildTelemetryRow 对 `_telemetryCpuRest`/`_telemetryGpuRest`
     设了**固定**预留 `obj.MinimumSize = new Size(D(84), 0)`（按 "1234rpm 100%" 宽度）；
     而 `LayoutTelemetryPieces()` 的 `Advance(piece) = Max(PreferredWidth, MinimumSize.Width)`
     -> 30 系无转速时那 84 宽仍被占用（且标签右对齐）-> **空档 + 整体居中偏移** ✅
     （字符串本身是对的：`TelemetryRest` 在 `rpm >= 0/1` 才加 rpm）
  2) 托盘浮窗：`SettingsForm.cs` 组装 trayTip 时**无条件**拼
     `array[3]="% ("; array[4]=cpuFanRPM; array[5]=" rpm)"`
     -> 转速为 -1 时显示 `风扇: 30% (-1 rpm)` ✅
- **修法**（tools\\patch_fan_rpm_missing.py，5 项锚点；随后
  tools\\patch_fix_rpm_reserve.py 修一处编译错）：
  1) 托盘 CPU/GPU：`if (cpuFanRPM/gpuFanRPM >= 0)` 才拼 " (Nrpm)"，否则只显示 "N%"
  2) 预留宽度改为**能力感知**：新增 `private bool _telemetryRpmSeen;` 与
     `private int TelemetryRestReserveWidth()` —— 见过有效转速按 "1234rpm 100%" 预留，
     从未见到（30 系 GCU）只按 "100%" 预留；运行时在 `LayoutTelemetryPieces` 里
     一旦见到 rpm>=0 就**一次性**升级预留（有 latch，不会来回抖动）
     -> `Advance` 取 Max，所以**缩小预留永远不会裁字** ✅
- ⚠️ **踩坑**：`D()` 是 `SettingsForm.cs:9286` 处**方法内局部函数**，我新加的
  `TelemetryRestReserveWidth()` 看不到它 -> CS0103。
  改为 `TextRenderer.MeasureText(sample, font, ...)` **按当前字体实测宽度** ✅
  （不用 D()，且 DPI/字体变化天然正确 ✅）
- ⚠️ **另一个老坑复现**：Python 的 `b''` 字面量不能含中文 -> 首轮补丁 SyntaxError 未写盘
  （"编译 0 错误"其实是**未打补丁**的旧代码，险些误判 ✅）。
  已在补丁工具里加 `op()` 自动把 `str` 编码为 UTF-8 ✅
- 编译 0 错误；提权替换完成（L-Mechrevo.dll 1935360B @02:24:57；LM PID 19960，
  `Responding=True` ✅）
- 备份：backup\\SettingsForm_20261005_0420_pre_rpm.cs
- 待实测：主界面 CPU/GPU 温标与百分比应紧邻（无空档）；托盘浮窗应显示
  `风扇: 30%`（不再有 `-1 rpm`）
- 未打包
"""

with io.open(r'D:\openLM\修改日志.txt', 'a', encoding='utf-8', newline='') as f:
    f.write(ENTRY)
print('appended, bytes =', len(ENTRY.encode('utf-8')))
