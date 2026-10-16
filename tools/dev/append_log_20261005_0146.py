# -*- coding: utf-8 -*-
import io

ENTRY = """
[2026-10-05 01:46] 研究：30 系 GCU 与 LM 自带 GCU 的文件树差异（只读调研，无代码改动）
- 产出：**D:\\openLM\\30系GCU与LM自带GCU文件树差异.md**（新建，含复跑脚本）
- 对比脚本：tools\\diff_gcu_30_tree.py（含关键文件 SHA256 逐字节校验）
- 对比对象：
  A = D:\\openLM\\installer-src\\files\\GCU\\payload\\30\\AiStoneService\\
  B = C:\\Program Files\\OEM\\机械革命电竞控制台\\UniwillService\\
- **总览**：A=118 文件，B=279 文件；相同=117，大小不同=1，只在A=**0**，只在B=161
  -> **A 是 B 的严格子集** ✅
- **核心结论**：
  1. **`GCUBridge.exe` SHA256 逐字节相同**（A=B=2aa9b296…，同为 77320B，时间戳差 8h=时区）
     -> LM 自带的 30 系 GCU **就是官方那一份，没被改过** ✅
  2. ⚠️ **唯一大小不同**：`MyControlCenter\\GPUPowerSavingSettings\\GPUPowerSavingSettings.json`
     A=**0 字节** / B=**329 字节**；而 `Install-Gcu.ps1:66 Copy-Tree` 用
     `Copy-Item … -Recurse -Force` **会覆盖** -> 用 LM 包覆盖装会**清空这份配置**（记一笔）
  3. 官方独有 161 个 = ①安装/运行期生成约 6 个（InstallLog/InstallState/service.ini/install.bat/
     uninstall.bat）②**`MyControlCenter` 的其余树**（LM payload 不含）：
     `UserPofiles\\` 16（含我们功能用的 `Mode{N}_Profile{M}.json`）、`UserFanTables\\` 84（风扇曲线）、
     `GamingMonitor\\` 27、根目录 27、`Command\\` 7、`AppSettings\\` 4、`ICCProfile\\` 2、
     `DisplayProfile\\` 1、`KeyboardManager\\` 1
     -> **LM 安装脚本不碰它们**（payload 没有就不会复制）-> **不会丢数据** ✅
     -> 且 `UserPofiles`/`UserFanTables` 时间戳（18:49:40）**晚于**安装（18:47:38）
        = **GCUService 首次运行时自建** ✅（风扇曲线/模式×配置 不依赖安装包）
  4. 30 系 payload 的 `MyControlCenter` 17 个文件里**含"三件套"**：
     `GCUService.exe` / `NVControlSetting.dll` / `UEFI_Firmware.dll`
     （README 说 40/50 三件套不含在包内、需从官方控制台提取 —— 与 30 系自带不矛盾）
  5. **语言包**：LM `logo\\Language\\{zh-cn,en-us}.json` = 28232/30271B，
     官方同路径仅 1693/226B -> LM 带的是**完整翻译**（官方那份是极小占位）✅
  6. ⚠️ **`DefaultCurve_{Gaming,Office,Turbo}.json` 是未被使用的遗留文件，且格式与官方不同**：
     含 `PL1/PL2/PL1_dc/PL2_dc/TCC/CTGP/DB/WM`（CPU/GPU 参数）+ 曲线数组，`Name` 为 `M1T1`
     -> 是"参数+曲线"**混合格式**（疑旧版 GCU 格式），与官方 `DefaultFanTable_*` 不同种；
     且 **`Install-Gcu.ps1` 里 grep `DefaultCurve` = 0 命中** -> 不参与安装，**无影响** ✅
- 未打包
"""

with io.open(r'D:\openLM\修改日志.txt', 'a', encoding='utf-8', newline='') as f:
    f.write(ENTRY)
print('appended, bytes =', len(ENTRY.encode('utf-8')))
