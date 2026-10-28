# 平台声明式 · 第 1 步：`BaolongdaProfile`（事实表）（2026-10-17）

> 起因（用户原话）：「所谓声明式架构是什么 · 我们也可以对不同平台分离吗」→「第一步来」。
> 本文只记录**第 1 步做了什么、边界在哪、为什么这样做**。第 2/3 步尚未开始。

---

## 一、为什么先做"事实表"而不是拆插件

现状（2026-10-17 统计）：

```
UsesBaolongda    77 处
IsGcuConnected   74 处
UsesLegacyMifs   17 处
UsesBuiltInModes 12 处
```

**同一个事实在几十处各判一遍** ⇒ 改一处漏一处。宝龙达适配已经踩过三次同类事故：

| 事故 | 本质 |
|---|---|
| 30 系"整机退回 legacy" | 功能号 22 读不到 ⇒ 被当成"整机是否接管"的判据 |
| 百分比 40% vs 官方 33% | 功能号 21 的**读值**被当成"最大转速" |
| 亮度 3/4 档 | 按代数的分叉散在换算函数里 |

⇒ 所以第 1 步**不拆程序集、不引入解释器**，只做一件事：
**把"这台机器是什么 / 数值范围多少 / 通道怎么语义 / 温度按什么顺序取"收拢成一张声明表**。

## 二、做了什么

新增 `source\MechrevoLite.Hardware\BaolongdaProfile.cs`（纯常量 + 纯函数，**零依赖、零行为**）：

| 分组 | 内容 |
|---|---|
| 身份 | `MatchesBoard()` —— **只委托** `BoardWhitelist.Matches()`（判据本体不复制） |
| 风扇 | `FanPercentDenominatorRpm = 6000` · `FanManualMaxUnits100Rpm = 60` · `FanTypeLarge = 0` / `FanTypeSmall = 1` |
| 功耗墙 | `PlMinWatts = 5` · `Pl1MaxWatts = 120` · `Pl2MaxWatts = 150` · `TccMinCelsius = 60` · `TccMaxCelsius = 105` · `Unset = 0` |
| 键盘灯 | `BrightnessMax(gen)` —— **唯一分叉点**：Gen40/50 ⇒ 4，其余 ⇒ 3 |
| 温度来源 | `CpuTempSources = [厂商, LHM, ACPI热区]` · `GpuTempSources = [厂商, LHM, NVML]` + `DescribeTemperatureSources()` |

然后 `MechrevoHw` 里那 9 个宝龙达常量 + 亮度换算 + 一行启动日志改成**引用**这张表（11 处）：

```csharp
internal const int BaolongdaFanRpmAtFullPercent = BaolongdaProfile.FanPercentDenominatorRpm;
private  const int BaolongdaPlMinWatts          = BaolongdaProfile.PlMinWatts;
// …
return (byte)Math.Clamp(ui0to4, 1, BaolongdaProfile.BrightnessMax(gen));
```

### ⚠️ 这一步**没有**改什么（重要）

- ⛔ **写入路径的判据/顺序/夹取/回读校验一行未动**（功耗墙、风扇手动、键盘灯写入都原样）
- ⛔ 没有动 GCU / 老 MIFS 的任何分支
- ⛔ 没有改 UI（`SettingsForm` 一行未动）
- ⛔ 没有拆程序集、没有引入 JSON/DSL 解释器

唯一"新增的行为"是启动时打一行**只读日志**：

```
宝龙达平台事实表（声明）：转速百分比分母=6000 RPM · 手动转速上限=60（x100RPM） ·
功耗墙范围 PL1=[5,120]W PL2=[5,150]W 温度墙=[60,105]℃ · 温度来源顺序 CPU=[厂商→LHM→ACPI热区] · GPU=[厂商→LHM→NVML]
```

⇒ 以后诊断包里能**直接看到这台机器按什么口径算**（这次排查百分比/温度时最想要的一行）。

## 三、验证

- `tools\dev\bdprofile-selftest`（新增，link 生产源文件）：**35 PASS / 0 FAIL**
  - 常量自洽（60×100 = 6000、下限 < 上限）
  - 亮度上限按代数（Gen30=3 / Gen40=4 / Gen50=4 / Unknown=3）
  - 身份委托（`MRID6` ✅ / `MRID6-23` ✅ / `MRID6-99` ✅ / `MRID60` ❌ / `GM5HG0A` ❌ / 空 ❌ / null ❌）
  - 温度来源顺序（CPU 末档 = ACPI、GPU 末档 = NVML、人读描述）
- 主工程 Release：**0 错误 / 1457 警告**（与改前同基线）
- `fancurve-selftest`：**114 PASS / 0 FAIL**（未受影响）

## 四、下一步（未做，按需）

| 步 | 内容 | 风险 |
|---|---|---|
| 2 | 抽 `IPlatformBackend`，把 `MechrevoHw` 里 3 条分支收成 3 个实现（`MechrevoHw` 保留门面） | 中（动"写"） |
| 3 | `SettingsForm` 里 151 处 `IsGcuConnected`/`UsesBaolongda` 改成问能力位（"这行该不该显示/可不可点"） | 中高（动 UI 布局） |

⚠️ 第 2/3 步都必须**每平台单独真机回归**；第 1 步之所以先做，是因为它**零行为变化**。

## 五、纪律（写进下一步的手册）

1. ⛔ **不重复事实**：功能号在 `BaolongdaWmi.Function`、板号在 `BoardWhitelist`、数值在这里
2. ⛔ **不放行为**：profile 里不读注册表/WMI、不打日志、不写硬件
3. ⭐ 新增"按代数不同"的值 ⇒ 写成**纯函数**放这里，⛔ 不要散成调用点的 `if`
