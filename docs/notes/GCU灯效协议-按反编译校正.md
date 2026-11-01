# GCU 灯效协议（按去混淆反编译校正）

> 出处：`archive\decomp\gcusvc_unpacked_20261016\GCUService\`（⭐ 去混淆版，1,415 个 `.cs`、0 桩）
> 起因：机主报「耀世16Super 2024：logo 灯/灯带 单色=白、呼吸=红、混合=直接关闭」
> 目的：把**厂商真实口径**写下来，⛔ 以后不要再靠抓包猜。
> ⚠️ 只记**协议事实**，⛔ 不抄厂商实现。

---

## 1. 三个关键字段

GCU 侧灯效帧的"颜色"由 `RGBKB_Color` 承载（`LightingModel\RGBKB_Color.cs`）：

| 字段 | 含义 |
|---|---|
| `ColorBuffer` | 颜色数组（`RGB_S[]`，每项 R/G/B） |
| `ColorBlocks` | ⭐ 颜色**块数**（`uint`） |
| `isCircular` | ⭐ 是否**循环/多段** |

## 2. ⭐⭐ 决定性规则：`b2`（下发给 HID 的颜色模式字节）

出处：`LightingModel\LM_ITE_RGB.cs` → `Set_ITE_Effect_Type_FwMode(effect, light, speed, direction, save, layout_color, control)`
（约 3116–3199 行；⭐ 同一分支在 3496 / 3720 / 4856 处重复出现，逻辑一致）

```
按 layout_color 分三种，算出 b2：
  ① ColorBlocks == 0
        ⇒ b2 = 0   ⇒ 不发颜色（用固件自带色）
  ② ColorBlocks > 0  且  isCircular == true
        ⇒ b2 = 8   ⇒ ⭐ 逐段下发：HID_Set_Color_14H(1..N, R, G, B, …)
  ③ 其余（ColorBlocks > 0 但 isCircular == false）
        ⇒ b2 = 1   ⇒ ⭐ 且**强制 direction = 0**，⭐ **只取 ColorBuffer[0] 的 RGB** 发一次
最后：HID_Set_Effect_Type_08H(control, effect, speed, light, b2, direction, save)
```

⇒ ⭐ **`b2 = 1` 就是"单色"，`b2 = 8` 就是"多段循环"** ✅
⇒ ⭐ 颜色帧（`14H`）**先发**，效果帧（`08H`）**后发** ✅

## 3. ⭐⭐ logo 通道（`HIDLightbar4_logo`）的**逐效果**规则

出处：`MyControlCenter\MyRgbKeyboard\HIDLightbar4_logo.cs` → `RunEffct()`（约 181–217 行）

```csharp
save_layout_color.isCircular = false;              // ← 默认「单色」
if (save_effect == 1)  isCircular = false;         // STATIC(Single)     ⇒ 单色
else if (save_effect == 2)  isCircular = true;     // ⭐ BREATHING       ⇒ 多段循环!
else if (save_effect == 4  || save_effect == 13)  save_effect = 4;    // REACTIVE / IMPACT（保持默认）
else if (save_effect == 5  || save_effect == 33)  save_effect = 5;    // RAINBOW / THINKING（保持默认）
else if (save_effect == 6  || save_effect == 10) { isCircular = false; save_effect = 6; }  // RIPPLE/RAINDROP
// 其余走 else：保持开头设的 false（单色）
```

⭐ 结论（**这就是我们之前搞错的地方**）：
- ⭐ **Single（1）** ⇒ `isCircular = false` ⇒ `b2 = 1` ⇒ ⭐ **单色，且只吃 `ColorBuffer[0]`** ✅
- ⭐ **Breathing（2）** ⇒ `isCircular = **true**` ⇒ `b2 = 8` ⇒ ⭐ **走"多段循环"路径**，
  颜色是**逐段下发**的；⭐ 若只给 1 段，硬件就只用那 1 段（我们实测"呼吸变红"就是因为当时给了七彩的 7 段、取了第 0 段红）
- ⭐ **Mix（19）** ⇒ **不在上面任何分支** ⇒ `isCircular` 保持默认 **false** ⇒ ⭐ `b2 = 1` ⇒ **单色路径** ✅
  （⭐ 我们实测"混合 = 直接关灯"：当时发的是七彩 7 段 + `isCircular=true` ⇒ 走了多段路径，而 Mix 固件不接受该组合）

## 3b. ⭐⭐ 其余灯条通道的逐效果规则（2026-10-16 补，三处都核过）

| 通道 | 文件 | 逐效果规则 |
|---|---|---|
| 灯条（老） | `HIDLightbar.cs` / `HIDLightbar2.cs` | `1(Single)` ⇒ ⭐ **把整条刷成 `ColorBuffer[0]` 一个色**（第 110–116 行：循环用 `ColorBuffer[0]` 铺满）＋ 调 `base.RunEffct(saved, **2**)`；<br>⭐ `5(Rainbow)` ⇒ ⭐ **厂商自己塞 7 色**（见下）＋ `isCircular=true`＋`ColorBlocks=7`；<br>⭐ `19(Mix)` / `2(Breathing)` / `51(UserMode)` ⇒ 直接 `base.RunEffct(saved, 2)`（**不动颜色**，用调用方给的）；<br>⭐ `25(StarHitting)` ⇒ 先当 `51` 跑一次再当 `25` 跑一次（两段） |
| 铰链 | `HIDLightbar4_hinge.cs` | 默认 `isCircular=false`；`1`⇒false；⭐ `2`⇒**true**；⭐ `4/13`⇒**true**（并把 effect 归一成 4）；`5/33`⇒保持默认 **false**（并归一成 5）；`6/10`⇒false（归一成 6） |
| 同步灯 | `HIDLightbar4_sync.cs` | 默认 `false`；⭐ `1/2/3` ⇒ **false**，且**按 `ControlBytes` 逐个 control 各跑一遍**（多段同步）；`7` ⇒ 按 `RegistryCtrl.GetCustomizeTarget()==26` 塞一组固定色 |
| Logo | `HIDLightbar4_logo.cs` | 见上一节 |

### ⭐ 厂商自带的七色板（我们代码里那份"硬编码七彩"就是它）

`HIDLightbar.cs` 第 121–128 行，Rainbow（effect=5）时厂商自己构造的 7 色：

```
(255,0,0) 红 · (255,165,0) 橙 · (255,255,0) 黄 · (0,255,0) 绿 · (0,255,255) 青 · (0,0,255) 蓝 · (139,0,255) 紫
                                                                  ↑ 注意第 7 色是 (139,0,255)，不是 (255,0,255)
```

⇒ ⭐ **这份七彩只该用在 `Rainbow(5)`**，⛔ 不该像旧代码那样"没颜色时一律发它" ❌（那正是"呼吸变红/混合关灯"的根因）。

### ⭐ 键盘 per-key 与"单色"的关系（同一次核对得到）

`Set_ITE_Effect_Type_StaticMode`（约 3057–3112 行）：⭐ **只取 `colorBuffer[0]`**，
再按键盘型号做色彩校正（`WKDColor.cheatRGB_2ndME` / `cheatRGB_2p1ndME` / `cheatRGB_4Zone` / `cheatRGB_HIDLightbar*`…），
然后**铺满**整条 65 字节缓冲并下发 ⇒ ⭐ **"单色效果 = 整条同一个颜色"**，与"键盘是不是单分区"无关 ✅

### ⭐ 三台真机的 `KeyboardType`（2026-10-16 从各自诊断包读出）

| 机型 | `KeyboardType` | 厂商含义 | 我们侧判定 | 灯带 |
|---|---|---|---|---|
| 耀世16Super 2024（GM6IX8B） | **7** | `MEZone_3p1nd_101`（在 `PerKeyTypes` 里）⇒ ⭐ **单键 RGB** | `PerKey` ⇒ 11 项灯效表 ✅ | 有（`MEZone_Lighbar4` 系） |
| 耀世16ultra 2025（X6AR55xY） | **7** | 同上 ⇒ ⭐ **单键 RGB** | `PerKey` ⇒ 11 项 ✅ | 有 |
| 翼龙15Pro（GM5HG0A） | **1** | ⭐ **`SingleZone`** ⇒ 真·单分区 | `SingleZone` ⇒ 4 项（单色/呼吸/彩虹/手动）✅ | ⭐ **无**（`LightbarSupport=0`） |

⚠️ 翼龙那台 `solution="EC"`（键盘走 EC 而非 ITE/GCU），⭐ 与它"没有灯带、问题出在快捷开关"一致 ✅

## 4. 效果码表（`LightingModel\ITE_SPEC.cs`）

`STATIC=1` `BREATHING=2` `WAVE=3` `REACTIVE=4` `RAINBOW=5` `RIPPLE=6` `NOMO=8` `MARQUEE=9`
`RAINDROP=10` `STACK=12` `IMPACT=13` `AURORA=14` `NEON=15` `SPARK=17` `FLASH=18` ⭐**`MIX=19`**
`GAMING=21` `RIPPLEO=22` `ALPHABET=23` `STARSPARK=24` `STARHITTING=25` `THINKING=33` `MUSIC=34`
`BATTERYPERCENT=35` `USERMODE=51` `COLORFULWAVE=52` `UNKNOWN=255`

另有 `EFFECT_TYPE_*`：`FW=0` `ROW=1` `PICTURE=2` `MUSIC=3` `AP=4` `NIGHTMODE=254`

## 5. MQTT 主题（`MyControlCenter\Topic.cs`）

`Keyboard/Ctrl` · `HidLightbar/Ctrl` · **`HidLightbar_Logo/Ctrl`** · **`HidLightbar_Hinge/Ctrl`** ·
**`HidLightbar_Sync/Ctrl`** · `System/BatteryProtection` ＋ `BatteryProtection/Control` ·
`Settings/DeviceSwitchItemStatus` · `System/Status` 类· `Fan/Control` `Fan/Status` `Fan/Table` ·
`Display/Control` · `EC/Control` `EC/Status` · `Service/Ctrl` `Service/Close` · `Customize/Control` ·
`Settings/KeyboardManager` · `IGPUonly/Support` · `OTA/MRControl`

## 6. 我们的实现该怎么改（结论）

| 我们的效果 | 厂商口径 | 我们该发 |
|---|---|---|
| `Single` | `isCircular=false`，`ColorBlocks≥1` | ⭐ **1 段 + 用户颜色** ✅（r45 已对） |
| `Breathing` | ⭐ `isCircular=**true**` | ⭐ **多段循环**（颜色按段的循环用用户色；⚠️ 至少保证第 0 段 = 用户色，⛔ 不要塞七彩） |
| `Mix` | ⭐ 默认 `false` ⇒ **单色** | ⭐ **1 段 + 用户颜色** ✅（r45 已按单色处理，⭐ 与厂商一致） |
| `Rainbow` / `Aurora` / `Gaming` | 默认 `false`（⭐ 靠固件自己变色） | ⭐ **可以不发颜色或发 1 段** ⇒ ⚠️ 我们现在发七彩，⭐ 待真机验证后再定 |

⚠️ **待办（下一轮）**：`Wave` / `Impact` / `Raindrop`（灯条那套）在厂商侧也各走不同分支
（`HIDLightbar4_hinge.cs` / `HIDLightbar4_sync.cs` 里也有按 effect 设 `isCircular` 的段落），
要用同一方法逐条核对后再改。

## 7. 变更记录

| 日期 | 变更 |
|---|---|
| 2026-10-16 | 建立本笔记；从去混淆反编译确认 §2 的 `b2` 规则与 §3 的 logo 逐效果规则 |
