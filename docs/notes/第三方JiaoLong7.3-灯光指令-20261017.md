# 第三方 JiaoLong7.3 的**灯光指令**（2026-10-17）

> ⭐ 本文只做**静态分析取事实**：⛔ 不抄代码、⛔ 不分发其内容。
> ⭐ 用途：给宝龙达「键盘灯 / LOGO 灯（氛围灯）」的实现做**对照依据**，避免反复猜。
> ⚠️ 合规：`JiaoLong7.3` 是**基于官方控制台修改的第三方版本**（⚠️ 不是原厂）；
>    主程序集 `第三方蛟龙游戏控制中心.dll` 里的「第三方」二字就是这个意思。
>    ⇒ 凡引用本文结论，都要注明「依据是**第三方修改版**」，⛔ 不能直接说成「原厂就是这样」。
>    ⚠️ 但 **原厂 40 系包**（v0.3.15）里同一批 WMI 功能号与本文完全一致（见 §8）。

---

## 一、材料与来源

| 项 | 值 |
|---|---|
| 压缩包 | `D:\openLM\_upstream\JiaoLong7.3\JiaoLong7.3.rar`（88,324,330 B · 559 个文件） |
| 主程序集 | `第三方蛟龙游戏控制中心.dll`（23,299,584 B） |
| 反编译产物 | `第三方蛟龙游戏控制中心.decompiled.cs`（272,836 B · 单文件） |
| 反编译工具 | `ilspycmd`（7 秒） |
| 解压工具 | `C:\Program Files\7-Zip\7z.exe` |

> ⚠️ 复现步骤：`7z x JiaoLong7.3.rar -o<临时目录> -y` ⇒ 主程序集在 **子目录 `JiaoLong7.3\`** 下
> （⚠️ 不在解压根目录，第一次找会踩空）。

---

## 二、WMI 功能号表（`enum WMIMethodName` · 逐字）

```csharp
public enum WMIMethodName
{
    SystemPerMode        = 8,
    GPUMode              = 9,
    RGBKeyboardStatus    = 10,
    FnLock               = 11,
    TPLock               = 12,
    CPUGPUFanSpeed       = 13,
    GPUFanSpeed_NotUse   = 14,
    Ambientlight         = 15,   // ⭐ LOGO 灯（氛围灯）
    RGBKeyboardMode      = 16,   // ⭐ 键盘灯模式
    RGBKeyboardColor     = 17,   // ⭐ 键盘灯颜色（3 字节 RGB）
    RGBKeyboardBrightness= 18,   // ⭐ 键盘灯亮度
    SystemAcType         = 19,
    MaxFanSpeedSwitch    = 20,
    MaxFanSpeed          = 21,
    CPUThermometer       = 22,
    CPUPower             = 23,
    UnKnow               = 255
}
```

**帧布局**（`_MakeMethodPrams`）：
```csharp
byte[] array = new byte[32];   // 32 字节
array[1] = (byte)wMIMethodType;  // 250 = Get · 251 = Set
array[3] = (byte)wMIMethodName;  // 功能号
```
⇒ ⭐ 与我们 `BaolongdaWmi` 的 `InData[1]` / `InData[3]` **逐项一致** ✅

**读值口径**（`GetValue<T>`）：
- 单字节 ⇒ `tuple.Item2[4]`
- `Tuple<int,int>` ⇒ `(Item2[5]<<8)+Item2[4]` / `(Item2[7]<<8)+Item2[6]`
- `Tuple<int,int,int>` ⇒ `Item2[4]` / `Item2[5]` / `Item2[6]`
⇒ ⚠️ **没有"读 3 字节 RGB"的路径** ⇒ ⭐ **官方与第三方都不读键盘灯颜色**（见 §7）

---

## 三、⭐ 键盘灯模式（`enum WMIRGBKeyboardMode`）

```csharp
public enum WMIRGBKeyboardMode : byte
{
    Mode_Off          = 0,   // 关
    Mode_RGBAutoCyclic= 1,   // 自动循环（彩虹流转）
    Mode_RGBFixedMode = 2,   // 固定色（单色）
    Mode_CustomColors = 3,   // 自定义颜色（多区/轮播）
    Unknow            = 255
}
```

### ⭐ 与官方对照

| 值 | 官方 30 系（v0.2.35） | 第三方 7.3 | 官方 40 系（v0.3.15） |
|---|---|---|---|
| 0 Off | ✅ 有 | ✅ 有 | ✅ 有 |
| 1 AutoCyclic | ✅ 有 | ✅ 有 | ✅ 有 |
| 2 FixedMode | ✅ 有 | ✅ 有 | ✅ 有 |
| 3 CustomColors | ⚠️ **定义了但从不写入** | ✅ 有 | ✅ 有 |

> ⚠️ **定性**：`3`（CustomColors）在 30 系官方**只定义不使用**；
> 第三方 7.3 把它**暴露到界面**。⇒ ⭐ 它是否真被 30 系固件接受，**仍需真机确认**。

---

## 四、⭐ 键盘灯亮度（`enum WMIRGBKeyboardBrightnessLevel`）

```csharp
public enum WMIRGBKeyboardBrightnessLevel : byte
{
    Level_0 = 0,
    Level_1 = 1,
    Level_2 = 2,
    Level_3 = 3,
    Level_4 = 4,      // ⭐⭐⭐ 有 4！共 5 档（0~4）
    // （截断处可能还有，但已知到 4）
}
```

### ⭐⭐ 这条推翻我们一个实现假设

我们 `MechrevoHw.BaolongdaBrightnessFromUi(ui0to4)`：
```csharp
if (ui0to4 <= 0) return 0;
return (byte)Math.Clamp(ui0to4, 1, 3);   // ⚠️ 把 4 压成 3
```

⇒ ⭐ **第三方 7.3 明确定义了 `Level_4`** ⇒ **40 系（4070）硬件很可能是 0~4 五档**。
⇒ ⚠️ 我们把 4 压成 3 ⇒ **最高档丢失**（用户会觉得"亮度调不上去 / 调不了"）。
⇒ ⭐ **待真机确认 30 系是否也支持 4**（30 系官方包里没找到这个枚举的完整定义）。

**写入方法**（⚠️ 无 clamp、无延迟）：
```csharp
public void SetSP_KeyboardLightBrightness(byte keyboardLightBrightnessValue)
{
    Task.Run(() =>
    {
        WMIMethodServices.SetValue(WMIMethodName.RGBKeyboardBrightness, keyboardLightBrightnessValue);
    });
}
```

---

## 五、LOGO 灯 / 氛围灯（`Ambientlight` · 功能号 15）

### `enum WMIResultState`
```csharp
public enum WMIResultState : byte
{
    OFF = 0,
    ON  = 1,
    Unknow = 255
}
```

### 读写
```csharp
// 写（开/关）
public void SetSP_Ambientlight(WMIResultState m)
{
    Task.Run(() => WMIMethodServices.SetValue(WMIMethodName.Ambientlight, m));
}

// 读
WMIResultState Ambientlight = WMIMethodServices.GetValue<WMIResultState>(WMIMethodName.Ambientlight);
```

⇒ ⭐ **LOGO 灯只有一个字节、只有 0/1** ✅ —— 与我们的 `TrySetLogoLight(bool)` / `TryReadLogoLight(out bool)` **完全一致**。
⇒ ⭐ **没有颜色、没有效果、没有亮度、没有速度/帧率/角度** —— 本文与官方包双向印证 ✅

### ⭐⭐ 灯条（Lightbar）搜索：**零命中**

```powershell
Select-String -Pattern 'Lightbar|LightBar'   # ⇒ 0 命中
```

⇒ ⭐ **第三方 7.3 根本没有「灯条 / 氛围灯条」通道**。
⇒ 结合用户澄清「30 系屁股灯 = 尾部灯带**与 LOGO 同亮**」⇒ ⭐ **证实宝龙达只有 LOGO 灯一路**（功能号 15），⛔ 没有独立灯条。

---

## 六、⭐⭐⭐ 关键发现：`SetSP_KeyboardColor` 有 **500ms 延迟**

```csharp
public void SetSP_KeyboardColor(byte red, byte green, byte blue)
{
    Task.Run(async () =>
    {
        WMIMethodServices.SetValue(WMIMethodName.RGBKeyboardMode, WMIRGBKeyboardMode.Mode_RGBFixedMode);  // ① 先写模式 2
        await System.Threading.Tasks.Task.Delay(500);                                                    // ⭐⭐ 等 500ms！
        WMIMethodServices.SetValue(WMIMethodName.RGBKeyboardColor, new byte[3] { red, green, blue });    // ② 再写颜色
    });
}
```

### ⭐ 三份实现的延迟对照

| 来源 | 顺序 | 延迟 |
|---|---|---|
| 官方 **30 系**（v0.2.35） | 模式 2 → 颜色 | ⛔ **无** |
| 官方 **40 系**（v0.3.15） | 模式 2 → 颜色 | ✅ **500ms** |
| 第三方 **7.3**（40 系改） | 模式 2 → 颜色 | ✅ **500ms** |
| **我们**（`62c82ea`） | 模式 → 颜色 → 亮度 | ⛔ **无延迟** |

⇒ ⭐⭐ **40 系（4070）大概率需要 500ms 延迟**，否则颜色会被紧接着的写入覆盖/丢失。
⇒ ⚠️ 而 **30 系不需要**（官方 30 系就是立即写）⇒ ⭐ **不能一刀切**，要按 GPU 代数区分。

**另一个方法**（恢复默认）：
```csharp
public void SetSP_KeyboardColorDefault()
{
    Task.Run(() =>
    {
        WMIMethodServices.SetValue(WMIMethodName.RGBKeyboardMode, WMIRGBKeyboardMode.Mode_RGBAutoCyclic);  // 模式 1
    });
}
```
⇒ ⭐ 「恢复默认」= **只写模式 1**（不碰颜色）✅

---

## 七、⭐ 颜色**只写不读**（三方印证）

`GetValue<T>` 只支持三种形态（§2）：
- 单字节 `Item2[4]`
- `Tuple<int,int>`（16 位对）
- `Tuple<int,int,int>` ⇒ `[4]/[5]/[6]`（⚠️ 但**没有**灯光代码用它读颜色）

⇒ ⭐ **三方（官方 30 系 / 官方 40 系 / 第三方 7.3）都不读键盘灯颜色**。
⇒ ⭐ 我们真机回读 `功能号 17 颜色 = 0,0,0` **是固件不回读，不是写失败** ✅
⇒ ⭐ 所以 `confirmed` 判据**不该包含颜色**（我们现在的 `modeMatch && brightMatch` 已是正确口径 ✅）。

---

## 八、与官方 40 系（v0.3.15）的一致性

| 项 | 官方 40 系 | 第三方 7.3 | 结论 |
|---|---|---|---|
| `WMIMethodName` 8~23 | ✅ 一致 | ✅ 一致 | ⭐ 同一套协议 |
| 帧布局 `[1]=250/251` `[3]=功能号` | ✅ | ✅ | ⭐ 一致 |
| `Mode_Off/AutoCyclic/FixedMode/CustomColors` (0/1/2/3) | ✅ | ✅ | ⭐ 一致 |
| `SetSP_KeyboardColor` 顺序+500ms | ✅ | ✅ | ⭐ 一致 |
| LOGO 灯单字节 0/1 | ✅ | ✅ | ⭐ 一致 |
| 灯条通道 | ❌ 无 | ❌ 无 | ⭐ **都没有** |

⇒ ⭐ 第三方 7.3 在**灯光这块没有额外协议**，只是把官方的一些入口暴露出来。

---

## 九、WMI 事件表（`enum WMIEventName`）里的灯光项

```csharp
public enum WMIEventName : byte
{
    AirPlaneMode           = 4,
    RGBKeyboardBrightness  = 5,    // ⭐ 亮度变化上报
    TouchPadState          = 6,
    FnState                = 7,
    RGBKeyboardMode        = 8,    // ⭐ 模式变化上报
    CapsLkState            = 9,
    AmbientlightState      = 10,   // ⭐ LOGO 灯状态上报
    ...
    SystemPerMode          = 15,
    ...
    WinKeyLock             = 33,   // ⚠️ 只在**事件表**里，功能表(WMIMethodName)里**没有**
}
```

### ⭐ 两条重要推论

1. **`WinKeyLock = 33` 只在事件表** ⇒ ⭐ **宝龙达没有 Win 键锁的【写】功能** ⇒ 我们的「win 键锁不可用」是**正确隐藏**，⛔ 不是 bug ✅
2. **`AmbientlightState = 10`** ⇒ ⭐ LOGO 灯状态**有事件上报** ⇒ 如果我们订阅这个事件，就不用每 3 秒轮询功能号 15（属于「HID_EVENT/WMI 事件订阅」那条待办）。

⚠️ 注意：`WMIEventService` 用的是 `ManagementEventWatcher`（WMI 事件），⛔ 不是 HID 报告。

---

## 十、⚠️ 由本文得出的**待修正项**（放进待办）

| # | 项 | 依据 | 优先级 |
|---|---|---|---|
| 1 | **40 系（4070）颜色写入需 500ms 延迟** | 第三方 7.3 + 官方 40 系都有 `Task.Delay(500)` | ⭐ 高（可能解释「彩虹=红、轮播=白」） |
| 2 | **亮度支持 0~4 五档**（我们把 4 压成 3） | `WMIRGBKeyboardBrightnessLevel.Level_4` | ⭐ 高（40 系）· 30 系待确认 |
| 3 | **`Mode_CustomColors=3` 是否真被 30 系接受** | 30 系官方定义但从不写 | 中（待真机） |
| 4 | **订阅 `AmbientlightState`（10）等 WMI 事件** 代替 3s 轮询 | 事件表有定义 | 低 |

---

## 十一、复现命令（存档）

```powershell
# 1) 解压（⚠️ 主程序集在子目录 JiaoLong7.3\ 下）
& 'C:\Program Files\7-Zip\7z.exe' x 'D:\openLM\_upstream\JiaoLong7.3\JiaoLong7.3.rar' -o"$env:TEMP\_jl73" -y

# 2) 反编译
$dll = Get-ChildItem "$env:TEMP\_jl73" -Recurse -Filter '第三方蛟龙游戏控制中心.dll' | Select-Object -First 1
& 'C:\Users\xiaohan\.dotnet\tools\ilspycmd.exe' -o "$env:TEMP\_jl73_dec" $dll.FullName

# 3) 查灯光
$cs = Get-ChildItem "$env:TEMP\_jl73_dec" -Recurse -Filter '*.cs' | Select-Object -First 1
Select-String -Path $cs.FullName -Pattern 'enum WMIRGBKeyboardMode|enum WMIRGBKeyboardBrightnessLevel|SetSP_Keyboard|SetSP_Ambientlight|Ambientlight'
```
