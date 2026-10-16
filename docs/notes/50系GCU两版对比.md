# 50 系 GCU 两版对比（50old vs 50new）

- 分析日期：2026-10-10
- 分析对象：
  - **50old** = 官方 ControlCenter 5.56.51.58（`D:\innounp-2\tools\50old`，UI 为 ControlCenter3 / WebView2 时代）
  - **50new** = 官方 ControlCenter 5.56.60.34（`D:\innounp-2\tools\50new`，UI 为 WINUI.CCU 时代）
- 参照物：LM 当前 50 系载荷（`D:\openLM\installer-src\files\GCU\payload\GCU` base + `payload\50` 覆盖层）

## 一、总体结论

1. **底层硬件协议层零变化**：ACPIDriverDll / GPUInfoDLL / DisplayInfo 等 9 个核心 DLL + 1 个 exe 的 SHA256 逐字节一致，EC 读写、GPU/显示信息、注册表、飞行模式、磁盘健康等协议层两版完全相同。
2. **变的只有服务与桥的分工**：GCUService.exe 大版本换代（1.0.2.70 → 1.2.0.0，+1.7MB），GCUBridge.exe 由 2.1MB 完整桥缩为 682KB 精简网关（-67.9%，版本号同名 1.0.1.10 但内容完全不同）——桥的职责并入了服务。
3. **新增一批功能组件**：SQLite 游戏模式数据库、SystemHelper、OTA 0.4.13（替代 BIOS_OTA 0.3.10）、Realtek 网卡助手、OLED 屏保、电源管理预配包。
4. **UI 只是壳**：GamingCenter 启动器两版完全相同（23,736 B），真 UI 不在解包目录内；UI 换代不影响后端 MQTT 接口（13688），LM 只依赖后端服务。
5. **LM 当前 50 系后端 = 新版世代**，但 GCUService 与本次解包的 50new 官方包并非同一份（差 14,336 B，来源为另一 50 系官方包）。

## 二、核心后端对比

| 组件 | 50old (5.56.51.58) | 50new (5.56.60.34) | 变化 |
|---|---|---|---|
| GCUService.exe | 15,199,992 B / 1.0.2.70 | 17,124,912 B / **1.2.0.0** | 大版本跳变，+1,924,920 B (+12.7%) |
| GCUBridge.exe | 2,125,496 B / 1.0.1.10（Uniwill 完整桥） | 682,208 B / 1.0.1.10（精简网关） | 同名不同物，-1,443,288 B (-67.9%) |
| UEFI_Firmware.dll | 191,200 B / 1.0.0.6 | 193,760 B / 1.0.0.7 | 小版本升级 |
| OSDTpDetect.exe | 522,480 B / 0.0.0.0 | 590,048 B / 2.0.0.1 | 版本升级 (+12.9%) |
| GCUUtil.exe | 30,888 B | 101,600 B | +228.9% |
| DefaultTool.exe | 1,166,584 B / 5.2.6.0 | 1,252,912 B / 6.0.0.4 | 版本升级 (+7.4%) |
| 7z.dll / 7za.exe | 19.00 | 26.02 | 压缩工具升级 |
| BIOS_OTA.exe | 11,753,184 B / 0.3.10 | 删除 | 被 OTA.exe 替代 |
| OTA.exe | 无 | 15,163,104 B / 0.4.13 | 新版 BIOS 更新 |
| UWACPIDriver.zip | 34,756 B | 33,303 B | 有微调（哈希不同） |

桥变小 + 服务变大 ⇒ 桥的逻辑/职责并入了新版 GCUService，桥退化为最小网关。MQTT 通道（13688）两版均保留。

## 三、底层硬件层：SHA256 逐字节一致清单

以下组件在两版官方包中逐字节相同（哈希完全一致）：

| 文件 | 版本 | 用途 |
|---|---|---|
| ACPIDriverDll.dll | 1.0.0.1（4,116,648 B） | EC 读写（0x740 / 0x766 走它） |
| GPUInfoDLL.dll | 1.0.0.11（2,587,632 B） | GPU 信息 |
| DisplayInfo.dll | 1.0.0.1（2,548,920 B） | 显示信息 |
| RegistryUtils.dll | 1.0.6467.28800 | 注册表工具 |
| UWAirplane.dll | — | 飞行模式 |
| UWDiskInfo.dll | 1.0.0.1 | 磁盘健康 |
| NVControlSetting.dll | — | NVIDIA 控制 |
| IntelOverclockingSDK.dll | 6.4.1.24 | Intel 超频 SDK |
| GCUServicePlugin.dll | 0.0.0.1（58,536 B） | 服务插件 |
| sysConUtil.exe | — | 系统控制工具 |

结论：**两版之间底层协议层一个字节都没变**，EC 行为、硬件访问接口完全兼容。

## 四、文件树差异

### 仅 50old 有（旧独有，8 项）
- ControlCenter_5.56.51.58_Mechrevo_GX.exe（安装器）
- AiStoneService\MicrosoftEdgeWebView2RuntimeInstallerX64.exe（旧 UI 基于 WebView2，需运行时）
- MyControlCenter\BIOS_OTA.exe（被 OTA.exe 替代）
- CreateShortcut\CreateShortcut.exe / _AIstone / _n / _new / _old（旧快捷方式系列）

### 仅 50new 有（新独有，21 项）
- ControlCenter_5.56.60.34_Mechrevo_GX.exe（安装器）
- AistonePowerManagement\AistonePowerManagement,1.ppkg / ,2.ppkg / ReleaseNote.txt（电源管理预配包）
- MyControlCenter\oled_care.scr（OLED 保护屏保）
- MyControlCenter\OTA.exe（新版 BIOS 更新，替代 BIOS_OTA.exe）
- MyControlCenter\rtknrai.exe + snynrai.dll（Realtek 网卡助手）
- MyControlCenter\SQLite.Interop.dll + System.Data.SQLite.dll（SQLite 支持）
- MyControlCenter\SystemHelper.exe（系统辅助进程）
- MyControlCenter\License\protocol_zh.html / protocol_zh.rtf / protocol_en.html / protocol_en.rtf（协议文件）
- MyControlCenter\SQLiteDB\GamdeMode.db（游戏模式数据库）
- CreateShortcut\CreateShortcut_WINUI.exe + System.Runtime.WindowsRuntime.dll + Windows.Foundation.*.winmd ×2 + windows.winmd（WINUI 快捷方式，WinRT API）
- RefreshShortcut\RefreshShortcut.exe

## 五、UI 层

- GamingCenter 目录两版**完全相同**：ControlCenterU.exe + GamingCenterU.exe（均 23,736 B / 1.0.0.1，只是启动器/跳板）。
- 50old UI = ControlCenter3（WebView2 壳，包内带 WebView2 运行时安装器）。
- 50new UI = WINUI.CCU（UWP/WinUI 应用，安装到 WindowsApps；快捷方式经 CreateShortcut_WINUI.exe + WinRT 创建）。
- **UI 换代对后端无影响**：LM 只依赖 GCU 后端服务（MQTT 13688），官方 UI 换成什么壳都不影响。

## 六、LM 当前 50 系载荷定位

- LM base（payload\GCU）：GCUBridge.exe **682,208 B（= 50new 新版桥）**、UEFI_Firmware 1.0.0.7、OSDTpDetect 2.0.0.1、SystemHelper、SQLite 全家（GamdeMode.db / System.Data.SQLite / SQLite.Interop）、rtknrai、snynrai —— 即 **50new 世代**。
- LM payload\50 覆盖层（4 文件）：GCUService.exe **1.2.0.0**（17,110,576 B）+ NVControlSetting.dll + UEFI_Firmware.dll 1.0.0.7 + GamingMonitor.json。
- ⚠️ 细节：LM 的 GCUService.exe（17,110,576 B / SHA256 4F0F74E6…）与本次解包的官方 50new（17,124,912 B / SHA256 734EF334…）**不是同一份**（差 14,336 B，版本号同为 1.2.0.0）。来源是另一个 50 系官方包或经调整的版本。

## 七、对维护的启示

1. 若官方 50new 修复了 50 系后端问题，只换 GCUService.exe + GCUBridge.exe 两件即可（硬件层无需动）。
2. 桥版本号 1.0.1.10 在两版中相同但内容不同 —— 判断桥新旧不能只看版本号，必须比对哈希/大小。
3. 40B 变体使用的 GCUBridge（2,122,248 B，Uniwill）与 50old 的桥（2,125,496 B）为同族完整桥；LM base 用的是 50new 精简网关（682,208 B）。

## 八、分析依据

- 两版官方包解包目录：`D:\innounp-2\tools\50old` / `D:\innounp-2\tools\50new`（innounp 解包，路径带 {app} 前缀）
- 对比方法：文件树对比（相对路径集合差）、同名文件大小对比、SHA256 哈希对比、PE/版本信息读取（Get-Item.VersionInfo）
- 本文件由 MainAgent 于 2026-10-10 生成
