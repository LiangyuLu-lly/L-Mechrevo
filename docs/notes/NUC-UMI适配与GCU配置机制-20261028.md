# NUC/UMI 适配与 GCU 配置机制整理

日期：2026-10-28  
范围：openLM WPF（L-Mechrevo）NUC/UMI 30 系直连适配；50 系 GCU 静音狂暴实现；GCU JSON / EC / BIOS 配置分层。

---

## 1. 结论摘要

### 1.1 NUC/UMI 适配路线

```text
NUC/UMI
  → 不连接 GCU/MQTT
  → 直接走 \\.\ACPIDriver
  → IOCTL_ECREAD  = 0x9C40A488
  → IOCTL_ECWRITE = 0x9C40A48C
  → BackendProbe 本地就绪
  → 顶部显示“● 已就绪”
```

### 1.2 50 系“静音狂暴”

不是独立第四档，而是狂暴档内的 BIOS 子模式：

```text
OperatingMode 仍为狂暴档
EC 1832 bit0 选择 Silent / Extreme
WMI 写 BIOS RAM 子模式
Profile.Silent / Profile.Extreme 区分配置
```

### 1.3 GCU 配置分层

```text
JSON / 注册表：偏好与模板
EC / BIOS / WMI：机器真实能力
GCU：合并、校验、裁剪后写 EC
```

同一套 GCU 安装包里的 JSON 不能代表不同机器的实际行为。

---

## 2. NUC/UMI 直连架构

### 2.1 后端状态

`MechrevoHw.ConnectAsync` 在识别为 Intel NUC / UMI 后跳过 GCU/MQTT：

```csharp
if (IsIntelNucMachine || IsUmiMachine)
{
    Logger.WriteLine("[backend] NUC/UMI 本地 ACPI 模式：跳过 GCU/MQTT");
    MarkNucLocalReady();
    return true;
}
```

`IsConnected` 包含本地就绪：

```csharp
UsesBuiltInModes || IsGcuConnected || _nucLocalReady
```

### 2.2 性能键

ACPI 事件：

```text
root\WMI · AcpiTest_EventULong
ULong = 176
```

本机连续三次捕获一致。

切档顺序：

```text
狂暴 → 静音 → 平衡 → 狂暴
```

### 2.3 性能档 EC 写入

```text
PL1  1923 (0x783)
PL2  1924 (0x784)
PL4  1925 (0x785)
TCC  1926 (0x786)
CustomMode 1798 (0x706)
  64 = 普通档
  65 = 自定义档
Trigger 1895 (0x767) bit5 = 0x20
```

默认档位值来源寄存器：

```text
狂暴：PL1 1959 / PL2 1960 / PL4 1961
平衡：PL1 1840 / PL2 1841 / PL4 1842
静音：PL1 1844 / PL2 1845 / PL4 1846
```

### 2.4 性能指示灯

原厂 Power LED 状态在：

```text
EC 1957 (0x7A5) 低 2 位
0 = 平衡
1 = 狂暴
2 = 静音
```

写前保留高 6 位：

```text
value = (current & 0xFC) | bits
```

注意：不是 1963。1963 是风扇/模式索引，曾被误用于 LED，导致灯态异常。

### 2.5 风扇

自定义风扇表：

```text
CPU UpT    3840
CPU DownT  3856
CPU Duty   3872

GPU UpT    3888
GPU DownT  3904
GPU Duty   3920

Max1 3933
Max2 3934
Ctrl 3935
Latch 1990 bit2 = 0x04
```

附加控制：

```text
Fan Ramp      1927
  0x80 | (ms / 100)
  或 0x81

Fan Isolated  1989 bit7
```

### 2.6 电池档位

```text
EC 1958 (0x7A6) 低 2 位
0 = 性能
1 = 平衡
2 = 健康
```

保留 bit3 触摸板位：

```text
value = (current & ~0x03) | mode
```

### 2.7 快捷开关

```text
Fn 锁      1870 bit4
摄像头     1831 bit3（bit3=1 表示物理断电）
Win 键锁   1896 bit0 / 触发 1895 bit0
触摸板     1958 bit3 + PTP 注册表
USB 触发   1895 bit4
来电自启   1830 bit3
强冷       1873 bit6
```

NUC/UMI 每次界面刷新从 EC 回读状态；摄像头优先使用 Windows PnP 设备状态。

---

## 3. NUC/UMI 灯带

### 3.1 寄存器

```text
AC：
  1864 控制
  1865 R
  1866 G
  1867 B

DC：
  2018 控制
  2019 R
  2020 G
  2021 B
```

### 3.2 原厂控制值

```text
1864 = 0     单色常亮
1864 = 8     关闭
1864 = 128   彩色/循环
```

RGB 写入：

```text
寄存器值 = 颜色等级 * 2
```

UI 亮度通过缩放 RGB 实现；Intel 默认灯带没有独立速度寄存器。

### 3.3 当前限制

```text
NUC 已接 UmiLightbar
UMI 仍需确认是否统一走本地灯带
原版 Intel 灯带只确认单色/彩色/关闭
其他效果枚举仍需实机抓取
```

---

## 4. 色彩增强与 ICC

### 4.1 软件色彩增强

参考 open-revo：

```text
GDI SetDeviceGammaRamp
  → gamma
  → 色温
  → RGB gain

Magnification.dll
  MagSetFullscreenColorEffect
  → 5x5 矩阵
  → saturation
  → contrast
```

六档：

```text
VibrantMode
InternetMode
VideoMode
LowBlueMode
CinemaMode
PhotoMode
```

关闭时恢复 identity gamma + identity matrix。

限制：

```text
桌面级效果，不是单屏
和 ICC 校色可能叠加
```

### 4.2 云端 ICC

入口：

```text
设置 → 屏幕校色 → 自定义校色 → 云端获取
```

仅 NUC 显示按钮。

自动获取：

```text
面板 ID（WmiMonitorID / DISPLAY\BOE0CBE\...）
BIOS 序列号
有线/无线 MAC
```

候选：

```text
<面板ID>_<序列号>
<面板ID>_<MAC>
<面板ID>
```

服务器：

```text
http://iccprofile.uniwill.com.tw/api/iccprofile/
```

校验 `acsp` 后导入 `%APPDATA%\MechrevoLite\customICC`。

本机实测成功：

```text
BOE0CBE_<MAC>.icm
20,340 字节
ICC 签名 acsp
```

另外支持程序目录或 `程序目录\ICC` 下的 `.icm/.icc` 启动时自动导入。

---

## 5. GPU cTGP / Dynamic Boost / OC

### 5.1 EC 寄存器

```text
1859  控制位
  bit2 cTGP 功能使能
  bit1 Dynamic Boost 使能
  bit0 Dynamic Boost 功能使能

1860  cTGP 目标偏移
  写入值 = 目标 TGP - 默认 TGP

1861  Dynamic Boost 总处理功耗目标

1862  Dynamic Boost 最大 TGP / DB 瓦数
```

本机初始读值：

```text
1859 = 0x07
1860 = 0x00
1861 = 0xFF
1862 = 0x19（25W）
1988 = 0x38
2004 = 0xFF
2005 = 0x19（25W）
```

### 5.2 写入逻辑

```text
默认 TGP：NVML 当前功耗上限
目标范围：默认 TGP .. 默认 TGP + 20W

1860 = 目标 TGP - 默认 TGP
1862 = DB 瓦数
1861 = 目标 TGP + DB 瓦数
1859 置 cTGP / DB 控制位
```

关闭 DB 时：

```text
1859 清 bit0/bit1
1861 = 0
1862 = 0
```

### 5.3 GPU 超频

不走 EC，走 NVAPI：

```text
NvidiaGpuControl.TryCreateOverclockControl()
  → SetCoreOffset
  → SetMemoryOffset
```

EC 没有 GPU 核心/显存偏移寄存器。

### 5.4 状态

```text
cTGP / DB EC 写入：已实现
GPU 驱动超频：已实现
实机稳定性：需继续小步测试
```

---

## 6. 50 系 GCU 静音狂暴实现

### 6.1 能力判定

```text
WMIEC.WMIIsTurboSubModeSupport() == 0x55
  → 支持
```

写入注册表：

```text
HKLM\SOFTWARE\OEM\GamingCenter2\ItemSupport
  IsTurboSubModeSupport = 1
```

### 6.2 用户选择

```text
HKLM\...\ItemSupport
  SilentPerformanceModeSwitch
0 = 静音狂暴
1 = 极限狂暴
```

### 6.3 静音路径

```text
UserSet_Cpu_Core_Offset_Silent
  → Profile.Silent = 1
  → Profile.Extreme = 0
  → EC 1832（0x728）bit0 = 1
  → WMIWriteSilentSubModeForBiosRAM()
  → GetSetULong Data = 5497558138889
```

### 6.4 极限路径

```text
UserSet_Cpu_Core_Offset_Extreme
  → Profile.Extreme = 1
  → Profile.Silent = 0
  → EC 1832（0x728）bit0 = 0
  → WMIWriteExtremeSubModeForBiosRAM()
  → GetSetULong Data = 5497558138890
```

### 6.5 关键结论

```text
静音狂暴仍是狂暴档（OperatingMode=2）
不是新增 OperatingMode
不是只改风扇
不是独立 EC 配置块
```

功耗墙、风扇曲线仍使用狂暴档 Profile。

### 6.6 GPU OC 子模式

```text
静音狂暴 → TurboSilentGPUOCOffset
极限狂暴 → TurboGPUOCOffset
```

---

## 7. GCU 配置块在哪里

### 7.1 JSON 目录

本机：

```text
C:\Program Files\L-Mechrevo\GCU\AiStoneService\MyControlCenter\
```

主要文件：

```text
UserPofiles\MainOption.json
UserPofiles\ModeX_ProfileY.json
UserFanTables\DefaultFanTable_*.json
```

### 7.2 Profile 内容

`ModeX_ProfileY.json` 包含：

```text
Silent / Extreme / SmartBalance
CPU：PL1 / PL2 / PL4 / TccOffset
GPU：cTGP / DynamicBoost / DynamicBoostTotalProcessingPowerTarget
     CoreClockOffsetOC / MemoryClockOffsetOC
FAN：TableName / FanSwitchSpeed
```

### 7.3 MainOption

```text
TurboProfileIndex
OperatingMode
TurboGPUOCOffset
TurboSilentGPUOCOffset
```

### 7.4 风扇表

```text
DefaultFanTable_Gaming.json
DefaultFanTable_Office.json
DefaultFanTable_Turbo.json
```

每个包含 CPU/GPU 12 段：

```text
UpT / DownT / Duty
```

### 7.5 EC 与 JSON 的关系

```text
JSON：模板 / 用户偏好
EC / BIOS：真实能力、范围、默认表
GCU：读 JSON + EC/BIOS 能力
     → 校验 / 裁剪
     → 写 EC
```

因此同一套 GCU JSON 在不同机器上实际行为不同，原因包括：

```text
EC ProjectID / BIOS ProjectID
SmartApcTable（PL/TGP/DB/TCC 范围）
WMI 能力位（TurboModeSupport / IsTurboSubModeSupport）
风扇表长度与地址
NVRAM 变量
```

---

## 8. 当前状态

| 功能 | 状态 | 备注 |
|---|---|---|
| GCU 断开本地就绪 | 完成 | 顶部显示已就绪 |
| 性能键 | 完成 | ULong=176 |
| 性能模式 PL | 完成 | 直写 EC |
| 性能指示灯 | 已修 | 1957 低 2 位 |
| 自定义 PL/TCC | 完成 | 直写 EC |
| 风扇曲线 | 完成 | 表 + Ramp + Isolated |
| 电池档位 | 已实现 | 1958 低 2 位，待复测 |
| 快捷开关回读 | 已修 | 周期 EC 同步 |
| 摄像头 | 已修 | 优先 PnP |
| 灯带基础 | 已实现 | NUC 1864 控制值 |
| 灯带完整效果 | 未完成 | 需要效果枚举 |
| 软件色彩增强 | 完成 | gamma + 5x5 |
| 云端 ICC | 完成 | NUC 按钮 |
| GPU cTGP/DB | 已实现 | EC 1859–1862 |
| GPU 驱动超频 | 已实现 | NVAPI |
| 息屏 | 已修 | 增加输入稳定期 |
| UMI 灯带分支 | 待确认 | 是否统一走 UmiLightbar |
| 睡眠省电 | 未测试 | — |

---

## 9. 待办

```text
1. 复测性能 LED 映射（1957）
2. 复测电池三档（1958）
3. 复测灯带单色/彩色/关闭
4. 确认 UMI 灯带是否走本地 UmiLightbar
5. 自定义 GPU 参数实机小步验证
6. 快捷开关：触摸板/摄像头/强冷实际效果核对
7. 高性能电源 / 任务栏自动隐藏 / 禁用 Win 更新改为本地状态源
8. 睡眠省电测试
```

---

## 10. 相关文件

```text
source/MechrevoLite.Hardware/NucPerformanceMode.cs
source/MechrevoLite.Hardware/NucFanTable.cs
source/MechrevoLite.Hardware/NucQuickSwitches.cs
source/MechrevoLite.Hardware/NucBatteryMode.cs
source/MechrevoLite.Hardware/NucGpuPower.cs
source/MechrevoLite.Hardware/UmiLightbar.cs
source/MechrevoLite.Hardware/SoftwareColorEnhancement.cs
source/MechrevoLite.Display/CloudIccFetcher.cs
source/MechrevoLite.Display/VirtualDisplayBlank.cs
tools/dev/wpf-p0/BackendProbe.cs
tools/dev/wpf-p0/OriginalStyleWindow.xaml.cs
tools/dev/wpf-p0/CustomModeWindow.xaml.cs
tools/icc-fetch/Get-CloudIcc.ps1
```

---

## 11. 参考

- open-revo：`src-tauri/src/system/display_tuning.rs`
- GCU 50 反编译：`ColorProfileInfo.cs`、`ColorProfileControl.cs`、`GpuFeatures.cs`、`MyFanManager_RamFan1p5.cs`、`WMIEC.cs`
- 本机诊断包：`L-Mechrevo-诊断包-0.293.6-beta23.6-20261028-194155-LAPKC71F.zip`
