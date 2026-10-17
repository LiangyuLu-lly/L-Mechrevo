# 第三方 JiaoLong7.3 的四个开关实现（2026-10-17）

查对象：`_upstream\JiaoLong7.3\JiaoLong7.3.rar`（88.3 MB）
- 解包 241,953,384 B / 559 文件
- 主程序集：`第三方蛟龙游戏控制中心.dll`（23,299,584 B）
- 反编译：`ilspycmd` → 单文件 272,836 B

**目的**：查清「触摸板 / 大小写 / 数字小键盘 / Fn 键锁定」这四个开关在这套控制台里怎么实现，
判断我们能否在宝龙达机型上接入。

---

## 一、结论：四个开关走**两种完全不同**的通道

| 开关 | 通道 | 对我们 |
|---|---|---|
| **触摸板** | **WMI 功能号 12** `TPLock` | ✅ 已接（`TrySetTouchpadViaBackend`，极性已修 `ca035fb`） |
| **Fn 键锁定** | **WMI 功能号 11** `FnLock` | ✅ 已接（`TrySetFnLockViaBackend`），极性未定论 |
| **大小写** | ⭐ **模拟按键** `keybd_event(VK_CAPITAL)` | ❌ 没接 —— **但零成本可实现** |
| **数字小键盘** | ⭐ **模拟按键** `keybd_event(VK_NUMLOCK)` | ❌ 没接 —— **但零成本可实现** |

⭐ **核心发现**：大小写 / 数字小键盘**不走 WMI、不走 EC、不需要驱动**，纯粹是
`user32.dll` 的模拟按键 + 状态查询。**这是我们能直接照做的部分。**

---

## 二、模拟按键的确切实现（`第三方蛟龙游戏控制中心.decompiled.cs`）

### 常量（`:3818`、`:3820`）
```csharp
private const int VK_CAPITAL = 20;    // 0x14
private const int VK_NUMLOCK = 144;   // 0x90
```

### 写（切一下）
```csharp
[DllImport("user32.dll")]
private static extern void keybd_event(byte bVk, byte bScan, uint dwFlags, nuint dwExtraInfo);

public static bool SetCapsLock()
{
    keybd_event(20,  69, 1u, 0u);   // flags=1 = KEYEVENTF_EXTENDEDKEY（按下）
    keybd_event(20,  69, 3u, 0u);   // flags=3 = KEYEVENTF_EXTENDEDKEY | KEYEVENTF_KEYUP（抬起）
    return true;
}

public static bool SetNumLock()
{
    keybd_event(144, 69, 1u, 0u);
    keybd_event(144, 69, 3u, 0u);
    return true;
}
```
⇒ 一次调用 = **切换一次**（CapsLock / NumLock 是 toggle 键，没有"设为开"的语义，只有"翻转"）

### 读（状态）
```csharp
[DllImport("user32.dll", CharSet = CharSet.Auto, ExactSpelling = true)]
public static extern short GetKeyState(int keyCode);

public static bool CapsLockStatus() => (GetKeyState(20)  & 0xFFFF) != 0;
public static bool NumLockStatus()  => (GetKeyState(144) & 0xFFFF) != 0;
```

### ⚠️ 它这里有个 bug，我们实现时要修正

`GetKeyState` 返回值语义：
- **bit 0（低位）= 锁定状态**（toggle）← 我们要的是这个
- **bit 15（高位）= 当前是否被按下**

它用 `& 0xFFFF != 0` ⇒ **任一位非零都算真** ⇒ **键正被按住时会误判为"已锁定"**。

⇒ ⭐ 正确写法：**`(GetKeyState(VK_CAPITAL) & 0x0001) != 0`**（只看锁定位）。

### 状态同步：它用**低级键盘钩子**
```csharp
SetWindowsHookEx(HookType.WH_KEYBOARD_LL, callbackDelegate, …)

public int LowLevelKeyboardProc(int nCode, nint wParam, nint lParam)
{
    if (nCode >= 0 && wParam == WM_KEYDOWN /*256*/)
    {
        int vk = Marshal.ReadInt32(lParam);
        if (vk == 20)  { /* 按 CapsLockStatus() 同步 CapsLockToggleButton */ }
        if (vk == 144) { /* 按 NumLockStatus()  同步 NumLockToggleButton  */ }
    }
    return CallNextHookEx(hHook, nCode, wParam, lParam);
}
```
⇒ 用户按物理键时界面实时跟随。

⚠️ 但**全局键盘钩子**有性能与安全顾虑。**我们不必用它** —— 用**定时轮询 `GetKeyState`** 更简单安全
（我们已有 3 秒周期的宝龙达状态刷新定时器，把这两项加进去即可，代价是一次两个 API 调用）。

---

## 三、触摸板 / Fn 锁：走 WMI，与我们一致

```csharp
// 读（:4753、:4770）
WMIResultState TPLock = WMIMethodServices.GetValue<WMIResultState>(WMIMethodName.TPLock);
WMIResultState _FnLock = WMIMethodServices.GetValue<WMIResultState>(WMIMethodName.FnLock);

// 写（:5275、:5287）
SetValue(WMIMethodName.TPLock, m);
SetValue(WMIMethodName.FnLock, m);
```

`WMIResultState`：
```csharp
public enum WMIResultState : byte { OFF = 0, ON = 1, Unknow = 255 }
```
⇒ **`1` = 锁定 / 使能**（依 UI 语义）。

⭐ 这与我们对 `TPLock` 的极性判断方向**一致**（`TPLock = 1` 表示"锁定"= 触摸板禁用，所以界面的
"触摸板可用"要取反）—— 印证 `ca035fb` 那个修复是对的。

它的触摸板状态还用 WMI 事件同步：
```csharp
case WMIEventName.TouchPadState:  // =6
    → 翻转 TPLockButton.IsChecked
```

---

## 四、`WMIMethodName` 枚举 —— **只到 23，没有超出**

```csharp
SystemPerMode=8, GPUMode=9, RGBKeyboardStatus=10, FnLock=11, TPLock=12,
CPUGPUFanSpeed=13, GPUFanSpeed_NotUse=14, Ambientlight=15, RGBKeyboardMode=16,
RGBKeyboardColor=17, RGBKeyboardBrightness=18, SystemAcType=19, MaxFanSpeedSwitch=20,
MaxFanSpeed=21, CPUThermometer=22, CPUPower=23, UnKnow=255
```

⇒ 与 40 系官方**完全一致**、**没有新增功能号**。

⇒ ⭐ 这条**反证**了"官方若有 Cap/Num 功能一定不走 WMI"——它们确实走模拟按键。

---

## 五、`WMIEventName` 完整表（协议层，供将来参考）

```csharp
Reserved_1..3 = 1..3
AirPlaneMode = 4, RGBKeyboardBrightness = 5, TouchPadState = 6, FnState = 7,
RGBKeyboardMode = 8, CapsLkState = 9, AmbientlightState = 10,
CalculatorStart = 11, DefaultBrowserStart = 12, NumLockState = 13, ScrlockState = 14,
SystemPerMode = 15, FN_J = 16, FN_F = 17, FN_0..FN_5 = 18..24,
PanelRefreshRate = 25, CPUFanSpeed = 26, GPUFanSpeed = 32, WinKeyLock = 33,
Reserved_22..29 = 34..41
```

⚠️ 注意：虽然表里有 `CapsLkState=9` / `NumLockState=13` / `ScrlockState=14` / `WinKeyLock=33`，
但 **Caps/Num 的实际状态它用 `GetKeyState` 读、用键盘钩子同步**，没走这个事件
（该表是完整协议能力表，不代表都用到）。

---

## 六、⚠️ 两处纠正（此前的说法要撤回）

### 1. 它带的驱动是 `WinRing0x64.sys`，**不是** `JiaoLongDriver64`

目录里唯一的 `.sys`：
```
WinRing0x64.sys   14,544 B    (+ WinRing0x64.dll)
```
551 个 dll 里只有 `LibreHardwareMonitorLib.dll` 与 `WinRing0x64.dll` 含 `WinRing` 字符串。

主 dll 的 **P/Invoke 全清单**：
```
user32.dll  16 次 · Lossless.dll 11 · kernel32.dll 7 · powrprof.dll 7
kernel32     6 次 · KaronOC.dll 2 · user32 2
```
⇒ ⛔ **没有** `WinRing0` / `JiaoLongDriver` / `GetBLDPortVal` 的直接 P/Invoke。

⇒ 此前"第三方 7.3 用 `JiaoLongDriver64` 走 `0x4E`/`0x4F` 控风扇"**是错的**
   —— 那是**开源 `JiaolongControl`** 的做法。

⇒ 它主要靠 `WinRing0`（经 LHM 库）读传感器 + `KaronOC.dll`（native，GPU 超频）。

### 2. **四个开关跟 EC 一点关系都没有**

触摸板/Fn 锁 → WMI；大小写/数字小键盘 → 模拟按键。
⇒ 不需要 PawnIO、不需要端口 I/O、不需要 `EC XRAM` 那条暂缓的线。

---

## 七、我们能不能零成本接入 —— **能，而且很干净**

### 大小写 / 数字小键盘
- **写**：`keybd_event(VK, bScan, KEYEVENTF_EXTENDEDKEY, 0)` + `keybd_event(VK, bScan, EXTENDEDKEY|KEYUP, 0)`
- **读**：`(GetKeyState(VK) & 0x0001) != 0` ← ⚠️ **修正它的 `& 0xFFFF`**
- **同步**：加进现有 3 秒定时器轮询（**不用**全局键盘钩子）
- **依赖**：仅 `user32.dll`，**无驱动、无 EC、无 GCU**

### 触摸板 / Fn 锁
- 我们**已经接了**（WMI 11/12）
- ⚠️ 唯一未完成：**Fn 锁极性未定论**（需机主一句话："那个开关显示的状态对不对"）

### 建议的实现形态
| 开关 | 建议 |
|---|---|
| 大小写 | 新增独立开关（我们目前**界面上根本没有**这一项） |
| 数字小键盘 | 把现有 `numpad` 从"仅 MQTT"改成"宝龙达下走模拟按键"（或新增一条分支） |
| 触摸板 / Fn 锁 | 保持现状；Fn 锁等机主确认极性 |

---

## 八、未查项（如实说明）

1. **官方 30/40 系有没有 CapsLock/NumLock 开关** —— 需解包 90/148 MB 安装器才能扫，**耗时较大，未做**。
   ⚠️ 但**不影响结论**：模拟按键是纯 `user32.dll`，无论官方有没有，我们都能做。
2. **`KaronOC.dll` 的导出**（GPU 超频，native）—— 与本次四个开关无关，未查。
3. **`Lossless.dll`**（11 次 P/Invoke）—— 未查用途（疑似无损缩放/画质相关）。

---

## 九、许可与合规

- `JiaoLong7.3` 是**第三方修改的厂商软件** ⇒ **只做静态分析取事实**，⛔ 不抄代码、不分发其内容
- `WinRing0x64` 在微软易受攻击驱动列表里、签名早已过期 ⇒ **⛔ 我们绝不引入**
  （好在四个开关**不需要它**）
