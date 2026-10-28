# 30 系 GCU 与 LM 自带 GCU 的文件树差异

生成时间：2026-10-05 01:45
对比脚本：`tools\diff_gcu_30_tree.py`（可重跑）

## 一、对比对象

| | 路径 | 文件数 |
|---|---|---|
| **A = LM 自带（30 系 payload）** | `D:\openLM\installer-src\files\GCU\payload\30\AiStoneService\` | **118** |
| **B = 官方已安装（本机在跑）** | `C:\Program Files\OEM\机械革命电竞控制台\UniwillService\` | **279** |

## 二、总览结果

```
文件数: A=118   B=279
相同=117   大小不同=1   只在A=0   只在B=161
```

**A 是 B 的严格子集**（`只在A=0`）✅

## 三、核心结论

### 3.1 ✅ `GCUBridge.exe` 与官方**逐字节相同**

```
SHA256  A=2aa9b29606b292b1…   B=2aa9b29606b292b1…
```
（大小同为 77320；时间戳差 8 小时 = 时区）
→ **LM 自带的 30 系 GCU 就是官方那一份**，没有二次修改 ✅

### 3.2 ⚠️ 唯一"大小不同"的文件 —— 有覆盖风险

| 文件 | A (LM) | B (官方) |
|---|---|---|
| `MyControlCenter\GPUPowerSavingSettings\GPUPowerSavingSettings.json` | **0 字节** | **329 字节** |

而 `Install-Gcu.ps1` 的 `Copy-Tree`（:66）用的是
`Copy-Item -LiteralPath (Join-Path $Source '*') -Destination $Destination -Recurse -Force`
—— **`-Force` 会覆盖** ✅
→ **用 LM 的 GCU 安装包覆盖安装时，会把这份配置清空。** 是否要紧待观察，但值得记一笔。

### 3.3 官方独有的 161 个文件 = 两类

**(a) 安装/运行期生成（约 6 个）**
```
GCUBridge.InstallLog  GCUBridge.InstallState  InstallUtil.InstallLog
service.ini  install.bat  uninstall.bat
```

**(b) `MyControlCenter\` 的其余树（LM payload 不含）** —— 按子目录：

| 子目录 | 文件数 | 说明 |
|---|---|---|
| `UserPofiles\` | **16** | `MainOption.json` + 15 个 `Mode{N}_Profile{M}.json` ← **「模式×配置」功能用的配置** |
| `UserFanTables\` | **84** | 风扇曲线表（`M{N}T{M}.json` 等）← **风扇曲线功能用** |
| `GamingMonitor\` | 27 | SharpDX / EasyHook / GPUInfoDLL 等（游戏监控，与 LM 无关） |
| `(根目录)` | 27 | `EntityFramework*`、`NAudio`、`IntelOverclockingSDK`、`SharpDX` 等 |
| `Command\` | 7 | 官方 `.ps1`（含 `disableDGpu.ps1` / `enableDGpu.ps1`） |
| `AppSettings\` | 4 | 官方 App 绑定/监控配置 |
| `ICCProfile\` | 2 | 屏幕校色 ICC |
| `DisplayProfile\` | 1 | `DisplayTable.json` |
| `KeyboardManager\` | 1 | `settings.json` |

**两个确认**：
1. **LM 的安装脚本不会碰它们**（payload 里没有 → `Copy-Tree` 不会复制 → **不会丢数据**）✅
2. `UserPofiles` / `UserFanTables` 的时间戳（`2026/10/4 18:49:40`）**晚于**安装时刻
   （`2026/10/4 18:47:38`）→ 由 **GCUService 首次运行时生成** ✅
   → 即：即使 LM 的 GCU 从零装上，这两个目录也会被服务自动建出来 ✅

### 3.4 LM payload 里含 40/50 的"三件套"

`payload\30\AiStoneService\MyControlCenter\` 的 17 个文件中有：
```
GCUService.exe          1769216     ← GCU 服务本体
NVControlSetting.dll    2766736     ← README 所称"三件套"之一
UEFI_Firmware.dll        131080     ← 三件套之二
ACPIDriverDll.dll / Colourful.dll / GPUInfoDLL.dll / M2Mqtt.Net.dll /
Newtonsoft.Json.dll / OSDTpDetect.exe / OutputDiskInfo.exe /
RegistryUtils.dll / UWAirplane.dll / UWDiskInfo.dll / Gma.UserActivityMonitor.dll
Command\disableWebcam.ps1 / enableWebcam.ps1
GPUPowerSavingSettings\GPUPowerSavingSettings.json (0 字节)
```
README 说"40/50 三件套为官方控制台提取的闭源二进制，未包含在此包内"，
但 **30 系 payload 自带这些** —— 两者不矛盾（30 系随包；40/50 需从已装的官方控制台提取）✅

### 3.5 语言包：LM 的是**补充翻译**

| 文件 | LM（`archive\refs\GCU语言包与安装脚本\logo\Language\`） | 官方（`...\logo\Language\`） |
|---|---|---|
| `zh-cn.json` | **28232** 字节 | **1693** 字节 |
| `en-us.json` | **30271** 字节 | **226** 字节 |

→ 官方安装目录里那份是**极小的占位**；LM 带的是**完整翻译** ✅
（这也解释了这个目录为何叫"GCU 语言包与安装脚本"）

### 3.6 ⚠️ `DefaultCurve_*.json` 是**未被使用的遗留文件**，且格式与官方不同

`archive\refs\GCU语言包与安装脚本\DefaultCurve_{Gaming,Office,Turbo}.json`（3101/3025/3024 字节）：

| 字段 | LM `DefaultCurve_Gaming.json` | 官方 `UserFanTables\DefaultFanTable_Gaming.json` |
|---|---|---|
| `Name` | **`M1T1`**（这是**配置表**名，不是默认表名） | `DefaultFanTable_Gaming` |
| `PL1/PL2/PL1_dc/PL2_dc/TCC/CTGP/DB/WM` | **有**（CPU/GPU 参数！） | 无 |
| `FanControlRespective` | 无 | 有 |
| `CPU` / `GPU` 曲线数组 | 有 | 有 |

→ LM 那三份是**"参数 + 风扇曲线"混合格式**（看起来是**旧版 GCU** 的格式），
   与现行官方 `DefaultFanTable_*` **不是同一种文件** ✅

**而且：`Install-Gcu.ps1` 里完全没有引用 `DefaultCurve`** （grep 0 命中）✅
→ 它们**不参与安装**，属遗留/手工用途，**对 30 系功能没有影响** ✅

## 四、对本项目功能的实际影响

| 关注点 | 结论 |
|---|---|
| LM 的 GCU 是否被改过 | ❌ 没有，`GCUBridge.exe` 与官方逐字节相同 |
| 装 LM 的 GCU 会不会丢 `UserPofiles`/`UserFanTables` | ✅ 不会（payload 不含，脚本不碰；且服务会自建） |
| 会不会丢别的官方数据 | ⚠️ 仅 `GPUPowerSavingSettings.json` 会被 0 字节覆盖（3.2） |
| 风扇曲线 / 模式×配置 功能依赖的文件从哪来 | GCUService 首次运行时生成 ✅（不依赖安装包） |
| LM 带的三件套是否用于 30 系 | 是（随 30 系 payload 一起装）✅ |

## 五、复跑方式

```powershell
py -3 D:\openLM\tools\diff_gcu_30_tree.py
```
（含关键文件的 SHA256 逐字节校验）

---

# 附录 A：payload 打包结构（底座 + 机型覆盖）

对比脚本：`tools\diff_gcu_payloads.py`

## A.1 三个 payload 的角色

```
D:\openLM\installer-src\files\GCU\payload\
     GCU\AiStoneService\   261 个  ← 【公共底座】= 完整 50 系 GCU（README：改名自 payload\50）
     30\AiStoneService\    118 个  ← 30 系补丁（30 系 GCU 二进制 + 完整 .NET 运行时）
     40\AiStoneService\      3 个  ← 40 系【三件套】覆盖文件
     50\AiStoneService\      3 个  ← 50 系【三件套】覆盖文件
```

`Select-GcuPayload.ps1` 头部原文：
```
payload\50  - the 50-series service trio (to be overlaid on payload\GCU for 50-series).
payload\40  - the 40-series service trio (mixed-GCU fix; overlaid for 40/unknown).
payload\30  - the 30-series service patch (30-series GCUService + .NET Core runtime ...)
Selection: 50-series -> payload\50 ; 30-series -> payload\30 ; anything else (40/unknown) -> payload\40
           **The base tree payload\GCU is always** [laid down first]
```
`Install-Gcu.ps1:930` = `'[3/8] copying base payload\GCU + {0} trio'`
→ **安装顺序：先铺底座 `payload\GCU`，再把机型补丁覆盖上去** ✅

## A.2 40/50 的差异文件 = "三件套"（各 3 个）

| 相对路径 | 40 系 | 50 系 | 30 系（补丁里） | 底座 |
|---|---|---|---|---|
| `MyControlCenter\GCUService.exe` | **14,447,864** | **17,110,576** | 1,769,216 | 无 |
| `MyControlCenter\NVControlSetting.dll` | 2,775,208 | 2,775,224 | 2,766,736 | 无 |
| `MyControlCenter\UEFI_Firmware.dll` | 134,312 | 193,760 | 131,080 | 无 |

（README\40 说明背景：40 系双显三模机型用 50 系 GCU 时**狂暴模式 TGP 写不进去**
（满载 115W，官方 40 系可达 140W）；换这三件后可达 140W，OSD 仍为 50 系样式）

## A.3 底座 vs 30 系：12 个同名但**版本不同**的文件

| 文件 | 底座(50系) | 30 系补丁 |
|---|---|---|
| `GCUBridge.exe` | **682,208** | **77,320** |
| `UEFI_Firmware.dll` | 193,760 | 131,080 |
| `MyControlCenter\GPUInfoDLL.dll` | 2,587,632 | 2,540,264 |
| `MyControlCenter\ACPIDriverDll.dll` | 4,116,648 | 4,113,416 |
| `MyControlCenter\OSDTpDetect.exe` | 586,464 | 90,632 |
| `MyControlCenter\Newtonsoft.Json.dll` | 711,952 | 700,336 |
| `MyControlCenter\RegistryUtils.dll` | 35,000 | 31,752 |
| `MyControlCenter\UWAirplane.dll` | 133,800 | 130,568 |
| `MyControlCenter\UWDiskInfo.dll` | 2,754,728 | 2,751,496 |
| `MyControlCenter\Command\disableWebcam.ps1` | 242 | 120 |
| `MyControlCenter\Command\enableWebcam.ps1` | 246 | 122 |
| `MyControlCenter\GPUPowerSavingSettings\GPUPowerSavingSettings.json` | **301** | **0** ⚠️ |

## A.4 为什么 30 系补丁要带 94 个 `System.*.dll`，而底座只有 2 个

| | 总 | `System.*.dll` | netstandard | MQTTnet | NLog | GCUBridge |
|---|---|---|---|---|---|---|
| 底座 GCU（50 系） | 261 | **2** | ❌ | ❌ | ❌ | 682,208 |
| 30 系补丁 | 118 | **94** | ✅ | ✅ | ✅ | 77,320 |
| 40 系补丁 | 3 | 0 | ❌ | ❌ | ❌ | 无 |
| 50 系补丁 | 3 | 0 | ❌ | ❌ | ❌ | 无 |
| **官方已装（本机 30 系）** | **279** | **96** | ✅ | ✅ | ✅ | 77,320 |

**关键吻合**：`底座 2 + 30 系补丁 94 = 96` = **官方已装的 96** ✅
→ 证实"底座先铺 + 30 系补丁覆盖"的模型 ✅

**根因**：50 系的 `GCUBridge.exe`(682KB) 与 40/50 的 `GCUService.exe`(14~17MB)
是**自包含**的（单文件已含运行时）；而 30 系的 `GCUBridge.exe`(77KB) 与
`GCUService.exe`(1.7MB) 是**框架依赖**的 → 必须随包带 .NET Core 运行时 DLL ✅
这就是"30 系补丁 118 个文件、其中 94 个是运行时"的原因 ✅

## A.5 结论

- **打包设计合理**：底座承担"大部分文件"，机型补丁只带**真正不同的那部分** ✅
  （30 系特殊：因运行时模型不同，补丁体积最大）
- ⚠️ **唯一隐患**：`GPUPowerSavingSettings.json` 底座 301 字节、30 系补丁 **0 字节**
  → 30 系机器安装后该文件被清空（对应正文 §3.2）
  → **已修复（2026-10-05 02:15）**：把 30 系补丁里那个 0 字节文件移出到
  `backup\gcu_payload_20261005_0215\`，安装时将使用底座的 301 字节版本 ✅
  （30 系补丁文件数 118 → 117）

---

# 附录 B：体积分析（用户关切："3 套 GCU 会不会体积爆炸"）

## B.1 实测占用

| payload | 文件 | 占用 |
|---|---|---|
| 底座 `GCU`（完整 50 系） | 271 | **48.14 MB** |
| `30` 补丁 | 117（修复后） | **18.73 MB** |
| `40` 三件套 | 3 | **16.55 MB** |
| `50` 三件套 | 3 | **19.15 MB** |
| **合计** | — | **102.57 MB** |
| 官方已装（本机 30 系，对照） | 279 | 44.17 MB |

## B.2 ✅ 结论一：30 与 50 **大量共用，没有爆炸**

- **底座 48.14 MB / 261 文件被所有机型共用** ✅
- 30 系补丁只**覆盖 12 个**、**新增 106 个**（其中 94 个是 .NET 运行时 DLL）✅
- → **不存在"三套完整 GCU"** ✅ 当前设计已经做到了共用 ✅

## B.3 ⭐ 结论二：真正的大头是 **40/50 三件套（35.7 MB），而每台机器只用一套**

```
40 三件套 = 16.55 MB   ← 只有 40 系机器需要
50 三件套 = 19.15 MB   ← 只有 50 系机器需要
```
安装器虽然按机型选择，但 **Inno 会把两者都压进同一个 EXE** → 每个用户都白下 ~17 MB ⚠️

**优化方案（按性价比）**：

| # | 做法 | 省 | 风险 |
|---|---|---|---|
| **1** | **三件套拆成独立附带包**（或安装时按机型下载），主包只带底座 + 30 补丁 | **~17 MB** | 低（需改打包/分发流程） |
| **2** | 删 `EntityFramework.xml`(3.51 MB) + `EntityFramework.SqlServer.xml`(0.15 MB) | **3.66 MB** | **零**（XML 仅为 IntelliSense 文档；官方包也带，但 GCU 运行不需要） |
| **3** | 按机型拆 `oled_care.scr`(12.35 MB，OLED 屏保) / `rtknrai.exe`(2.48 MB) / `DisplayInfo.dll`(2.43 MB) / `SQLite.Interop.dll`(1.91 MB) | **~19 MB** | 中（需确认 GCU 是否引用；这些**官方 30 系包里没有**，属 50 系包特有） |
| 4 | 底座里被 30 补丁覆盖的 3 个大文件（`ACPIDriverDll.dll` 3.93 / `UWDiskInfo.dll` 2.63 / `GPUInfoDLL.dll` 2.47 ≈ **9 MB**） | ~9 MB（**仅对 30 系机器**） | 高（底座要服务 40/50，需按机型拆包） |

## B.4 粗略目标

- 当前：**102.57 MB**
- 只做 ①②（低风险）：**≈ 82 MB**
- 再做 ③（需验证依赖）：**≈ 63 MB**
- 若按机型彻底拆包（每台机器只带自己那套）：主包 ≈ **48（底座）+ 18.7（30）+ 3.7（xml 不删）= 70 MB**，
  40/50 用户再取 16.5 / 19 MB ⇒ **单机型安装包 ≈ 50~70 MB**

## B.5 复跑

```powershell
py -3 D:\openLM\tools\diff_gcu_payloads.py     # 文件集差异
# 体积：见本附录 B.1 的量法（Get-ChildItem -Recurse | Measure-Object Length -Sum）
```

---

# 附录 C：30 系 payload 精简（2026-10-05，30 补丁换成完整官方 279 个之后）

用户把**完整官方 30 系 GCU（279 个 / 44.17 MB）**复制进了 `payload\30\AiStoneService\`
（实测：与官方已装 **279 个全部逐字节相同** ✅）。分析可删项。

工具：`tools\trim_30_payload.py`（**默认 dry-run**；`--apply` 才执行，**移到备份**不是删除；
`--tiers=T1,T2` 可选层）

## C.1 三层可删清单

| 层 | 判据 | 个数 | 可省 | 风险评估 |
|---|---|---|---|---|
| **T1** | **与底座 `payload\GCU` 逐字节相同** —— 底座**总是先铺**，已提供这些文件 | 20 | **11.49 MB** | ✅ **零风险** |
| **T2** | `.xml`（IntelliSense 文档）+ `.pdb`（调试符号）—— **运行时永不加载** | 7 | **6.32 MB** | ✅ **零风险** |
| **T3** | 官方控制台专属：`GamingMonitor\`、`ICCProfile\`、`DisplayProfile\`、`AppSettings\`、`KeyboardManager\`、`SharpDX*`、`NAudio*` | 34 | 7.37 MB | ❌ **不可删（已验证）** |

**T1 明细（按体积）**
```
5102.70 KB  MyControlCenter\EntityFramework.dll
3593.83 KB  MyControlCenter\EntityFramework.xml
1571.98 KB  MyControlCenter\System.Management.Automation.dll
 717.80 KB  MyControlCenter\IntelOverclockingSDK.dll
 607.70 KB  MyControlCenter\EntityFramework.SqlServer.dll
 154.68 KB  MyControlCenter\EntityFramework.SqlServer.xml
   + 14 个小文件（风扇表 json / ps1 / 键盘与显示配置 / service.ini）
```

**T2 明细**
```
2432.60 KB  MyControlCenter\SharpDX.xml
1475.31 KB  NLog.xml
1030.33 KB  MyControlCenter\NAudio.xml
 691.13 KB  Newtonsoft.Json.xml
 691.13 KB  MyControlCenter\Newtonsoft.Json.xml
 114.97 KB  MyControlCenter\SharpDX.RawInput.xml
  35.50 KB  MyControlCenter\GamingMonitor\GameTesing.pdb
```
（T1 与 T2 **无重叠**）

## C.2 ⚠️ T3 为什么不能删 —— 二进制引用实测

扫 `GCUService.exe`（1728 KB）的字符串：
```
命中 GamingMonitor    ascii=True  unicode=True      ← 路径字符串
命中 SharpDX          ascii=True                    ← 程序集引用，出现 3 次
命中 NAudio           ascii=True                    ← 程序集引用，出现 3 次
命中 ICCProfile / DisplayProfile / KeyboardManager / AppSettings   ← 路径字符串
另有 SQLite 引用 ×3
```
`GCUBridge.exe`(76 KB) 与 `GCUServicePlugin.dll`(57 KB) 干净 ✅
→ 删 T3 可能让 `GCUService.exe` 在某个代码路径上抛 `FileNotFoundException` ✅ **放弃**

## C.3 安装顺序验证（决定 T1 是否成立）

用户反馈：**「MyControlCenter 实测不能删」** ✅ 正确。但需区分两种删除：

| 操作 | 会不会坏 | 原因 |
|---|---|---|
| ❌ 删**整个** `MyControlCenter\` | **会坏** | `GCUService.exe` / `GCUServicePlugin.dll` / `EntityFramework*` 等**只在这里**，根目录没有 |
| ✅ 删 **T1 那 20 个**（与底座逐字节相同的） | **不会坏** | **底座也有一份且逐字节相同** |

**依据（`Install-Gcu.ps1` :930-962 实测）**：
```powershell
Write-Log ('[3/8] copying base payload\GCU + {0} trio' -f $selection.StagedDir)
# 先备份 UserPofiles / UserFanTables（跨升级保留用户数据）
Copy-Tree -Source $baseServiceSource -Destination $serviceTarget   # ← 底座先铺
Copy-Tree -Source $trioServiceSource -Destination $serviceTarget   # ← 机型补丁后覆盖
Copy-Tree -Source $driverSource      -Destination $driverTarget
... 恢复 UserPofiles / UserFanTables
```
- `Copy-Tree` = `Copy-Item -LiteralPath (Join-Path $Source '*') -Destination $Dest -Recurse -Force`
  → **合并覆盖，不是替换整个目录** ✅
- **没有任何 `Remove-Item` 清空目标目录** ✅（:479 那次删的是 allow-list 里的**官方控制台目录**，:960 删的是临时备份）
→ **补丁里缺失的文件 = 保留底座版本** ⇒ **T1 成立** ✅
→ 附带确认：`UserPofiles` / `UserFanTables` 被备份+恢复，**升级不丢用户数据** ✅

## C.4 结论与目标

- 安全精简：**T1 + T2 = 27 个 / 17.81 MB**
- **`payload\30` ：44.17 MB → 26.36 MB** ✅
- 有趣对照：精简后 **26.36 MB** 仍略大于原 117 文件版（18.73 MB）——
  因为原版**还额外缺了** `GamingMonitor`(6.61 MB) 等 T3 内容 ⚠️
  → 即：**原 117 文件版是"激进精简"（含 T3），风险未评估**；
    建议改用 **T1+T2 精简版（279-27=252 个）**，兼顾体积与安全 ✅

## C.4 执行方式

```powershell
py -3 D:\openLM\tools\trim_30_payload.py                  # 先看清单（dry-run）
py -3 D:\openLM\tools\trim_30_payload.py --tiers=T1,T2 --apply   # 执行（移到 backup，可还原）
```

## B.6 底座 `payload\GCU`（50 系） vs 本机官方 30 系 GCU —— 重复度

对比脚本：`tools\diff_gcu_base_vs_official30.py`（含 SHA256 逐字节校验）

```
文件数              : 底座(A)=261   官方(B)=279
同名文件            : 122
  ├ 逐字节【完全相同】: 20     （重复体积 11.49 MB）
  └ 同名但【内容不同】: 102    （11.50 MB）
只在底座 A 独有      : 139    （25.03 MB）
只在官方 B 独有      : 157    （22.36 MB）
```

**逐字节相同的 20 个里，10.5 MB 是跨机型通用组件**：
```
EntityFramework.dll                4.98 MB   ← 完全相同
EntityFramework.xml                3.51 MB   ← 完全相同
System.Management.Automation.dll   1.54 MB   ← 完全相同
IntelOverclockingSDK.dll           0.70 MB   ← 完全相同
EntityFramework.SqlServer.dll      0.59 MB   ← 完全相同
其余 15 个（小 json / ps1）        ~0.17 MB
```

**"同名内容不同"的 102 个里，93 个是机型风扇表**（`UserFanTables\PH4A*` / `PH6A*`），
差异仅 **1–2 字节**（例：`PH4ARxx\M1T1.json` A=3114 / B=3116）。

**重要结论（推翻一个猜想）**：
- ❌ **不能靠"复用官方已装的文件"来瘦身** —— 底座与官方 30 系只有 **20 个**文件逐字节相同
  （底座是 50 系版本，绝大多数二进制都不一样）
- ✅ **风扇表不是体积问题**：底座 `UserFanTables` 共 **167 个文件但仅 0.49 MB**
  （官方才 0.24 MB）→ 无需为体积优化（只影响文件数/整洁度）
- ⭐ 真正的省体积点仍是 B.3 的三条（三件套二选一 / 删 xml / 按机型拆 50 系特有的大文件）

### 底座 `UserFanTables` 明细（25 个机型前缀 × 6 个文件 + 根 23 个）

```
(根) 23   PH4AQE3 6   PH4AQxx 6   PH4ARxx 6   PH4AUxf 6   PH4AUxx 6   PH4AXxx 6
PH4PGx1 6  PH4PGx2 6  PH4PRxx 6   PH4PUxx 6   PH4TQx1 6   PH4TRX1 6   PH4TUX1 6
PH6AQxx 6  PH6ARxx 6  PH6PG0x 6   PH6PG0x150W 6  PH6PG3x 6  PH6PG3x150W 6
PH6PG7x 6  PH6PG7x150W 6  PH6PGEx 6  PH6PRxx 6  PH6TRX1 6
合计 167 个 / 0.49 MB
```
（每个前缀 6 个 = `M1T*`/`M2T*` 之类；这些是**其它机型的**表，本机只用到自己那 5 张）
