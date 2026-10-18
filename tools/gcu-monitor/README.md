# gcu_monitor_gui.py（作者自有工具，仅作口径参考）

**来源**：`C:\Users\xiaohan\Doubao\chats\2026-09-22\new-chat\gcu_monitor_gui.py`（保存到本仓库以便追溯口径；原始文件仍保留在原处）。

## 用途
GCU 后端机型的独立监控小工具（PyInstaller 打包成 `D:\GCUmonitor\gcu_monitor.exe`，单文件、内置 LibreHardwareMonitor DLL）。

## 读取口径（LM 对齐参考）
```python
comp.IsCpuEnabled = True
comp.IsGpuEnabled = True
comp.Open()
hw.Update()            # 每 2 秒（递归 hw.SubHardware）

cpu_w   =  "8745H" + "Package"      的 Power
igpu_w  =  "Radeon 780M" 且 ("GPU Core" 或 "GPU SoC") 的 Power **相加**
igpu_temp = "Radeon 780M" + "VR SoC" 的 Temperature
igpu_load = "Radeon 780M" + "GPU Core" 的 Load
```

## 依赖
- Python 3 + pythonnet（`clr`）
- `LibreHardwareMonitorLib.dll` + `HidSharp.dll`（与 LM 自带的同版本，PyInstaller `--add-binary` 内嵌）
- 需要管理员（CPU 功耗）

## 注意
- 本工具是**只读**监控，不写硬件；
- LM 侧对应实现：`source/MechrevoLite.Hardware/LhmMonitor.cs`（APU 核显功耗选源、冻结判据）。
