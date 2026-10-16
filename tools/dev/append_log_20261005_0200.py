# -*- coding: utf-8 -*-
import io

ENTRY = """
[2026-10-05 02:00] 补充研究：LM 自带 GCU 的 40/50 包与 30 系对比（只读，无代码改动）
- 背景（用户说明）：「我是为了方便替换和减小体积才把 50 40 替换文件分离的，
  因为大部分文件是一样的只有个别不一样」
- 产出：追加到 **D:\\openLM\\30系GCU与LM自带GCU文件树差异.md** 的「附录 A」；
  对比脚本 tools\\diff_gcu_payloads.py
- **packaging 结构（实测）**：
  ```
  payload\\GCU\\AiStoneService\\   261 个  ← 【公共底座】= 完整 50 系 GCU（README：改名自 payload\\50）
  payload\\30\\AiStoneService\\    118 个  ← 30 系补丁（30 系二进制 + 完整 .NET 运行时）
  payload\\40\\AiStoneService\\      3 个  ← 40 系【三件套】覆盖文件
  payload\\50\\AiStoneService\\      3 个  ← 50 系【三件套】覆盖文件
  ```
  `Select-GcuPayload.ps1` 头部原文确认：**底座 payload\\GCU 始终先铺**，再按机型覆盖
  （50→payload\\50 / 30→payload\\30 / 40或未知→payload\\40）；
  `Install-Gcu.ps1:930` = `'[3/8] copying base payload\\GCU + {0} trio'`
- **40/50 差异文件 = "三件套"（各 3 个）**：
  | 路径 | 40 系 | 50 系 | 30 系补丁里 | 底座 |
  |---|---|---|---|---|
  | MyControlCenter\\GCUService.exe | 14,447,864 | 17,110,576 | 1,769,216 | 无 |
  | MyControlCenter\\NVControlSetting.dll | 2,775,208 | 2,775,224 | 2,766,736 | 无 |
  | MyControlCenter\\UEFI_Firmware.dll | 134,312 | 193,760 | 131,080 | 无 |
  （payload\\40\\README 背景：40 系机型用 50 系 GCU 时**狂暴 TGP 写不进去**（115W vs 官方 140W），
   换这三件后可达 140W，OSD 仍 50 系样式）
- **底座 vs 30 系：12 个同名不同版本**（GCUBridge.exe 682208→77320、UEFI_Firmware.dll 193760→131080、
  GPUInfoDLL/ACPIDriverDll/OSDTpDetect/Newtonsoft.Json/RegistryUtils/UWAirplane/UWDiskInfo/
  disableWebcam.ps1/enableWebcam.ps1、以及 **GPUPowerSavingSettings.json 301→0** ⚠️）
- **关键数字吻合（证实打包模型）**：
  ```
  底座 System.*.dll = 2  +  30 系补丁 = 94  =  96  =  官方已装（30 系）的 96  ✅
  ```
- **根因（运行时模型不同）**：
  50 系 `GCUBridge.exe`(682KB) 与 40/50 `GCUService.exe`(14~17MB) 是**自包含**的（单文件含运行时）；
  30 系 `GCUBridge.exe`(77KB) 与 `GCUService.exe`(1.7MB) 是**框架依赖**的
  -> 必须随包带 .NET Core 运行时 -> 这就是"30 系补丁 118 个文件中 94 个是运行时"的原因 ✅
- ⚠️ 唯一隐患：`GPUPowerSavingSettings.json` 底座 301B、30 系补丁 **0B**
  -> 30 系机器安装后该文件被清空（`Copy-Tree` 用 `-Force` 覆盖）
- 未打包
"""

with io.open(r'D:\openLM\修改日志.txt', 'a', encoding='utf-8', newline='') as f:
    f.write(ENTRY)
print('appended, bytes =', len(ENTRY.encode('utf-8')))
