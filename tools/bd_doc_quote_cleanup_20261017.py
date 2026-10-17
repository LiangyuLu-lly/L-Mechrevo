# -*- coding: utf-8 -*-
r"""把 0.291.9.8 说明里的两段**原厂反编译代码**换成"事实表"（合规：不贴原文）。"""
import sys

sys.stdout.reconfigure(encoding="utf-8", errors="replace")
P = r"D:\openLM\docs\notes\0.291.9.8-风扇曲线修复与开关-20261017.md"

with open(P, "rb") as f:
    raw = f.read()
assert not raw.startswith(b"\xef\xbb\xbf")
t = raw.decode("utf-8")
assert "\r\n" not in t
must = lambda c, m: (_ for _ in ()).throw(SystemExit("FAIL: " + m)) if not c else None


def cut(text, start_marker, end_marker, new):
    s = text.find(start_marker)
    must(s >= 0, "找不到起点: " + start_marker[:30])
    e = text.find(end_marker, s)
    must(e > s, "找不到终点: " + end_marker[:30])
    return text[:s] + new + text[e:]


B1 = """```csharp
Tuple<int, int> _CPUGPUFanSpeed = WMIMethodServices.GetValue<Tuple<int, int>>(WMIMethodName.CPUGPUFanSpeed); // 功能号 13
FanSpeed.Text = _CPUGPUFanSpeed.Item1 + " RPM";        // [4..5] 小端 = RPM
float num = (float)_CPUGPUFanSpeed.Item1 / 60f;        // ⭐⭐ 分母 = 60 ⇒ 100% = 6000 RPM
FANProgressBar.Value = (int)Math.Round(num);
FANTextBlock.Text = num.ToString("F0") + "%";          // ⭐ 整数百分比
```
"""
N1 = """| 事实 | 值 |
|---|---|
| 采样功能号 | **13**（`CPUGPUFanSpeed`）——我们本来就在读 |
| 百分比折算 | **RPM ÷ 60** ⇒ ⭐ **100% = 6000 RPM** |
| 显示格式 | 四舍五入成**整数**百分比（如 `33%`） |
| 手动转速范围（功能号 21 的写值） | 22 ~ 58（⇒ 2200 ~ 5800 RPM），与百分比分母**不是同一个数** |
| 取证位置 | 40 系 `蛟龙游戏控制中心.dll`（v0.3.15）· ilspycmd 反编译产物在临时目录（不入库） |

> ⚠️ **合规（用户 2026-10-17 强调）**：「和官方对齐就行，不要照抄官方代码，有法律风险」。
> ⇒ 本文只记**数值事实**（功能号 / 常量 / 顺序），**不贴反编译原文**；源码里同样不贴。

"""
t = cut(t, "```csharp\nTuple<int, int> _CPUGPUFanSpeed", "真机对照（`MRID6-23`", N1 + "真机对照（`MRID6-23`")

B2 = """```csharp
private void Set_FanSpeed(byte speedValue) {
    MaxFanSpeed.Text = speedValue.ToString();          // 显示"58"这类数字
    MaxFanSpeedSlider.Value = Set_MaxFanSpeedSlider(); // 22~58 映射到 0~100
    Task.Run(() => SetValue(WMIMethodName.MaxFanSpeed, speedValue));
}
WMISystemPerMode value = GetValue<WMISystemPerMode>(WMIMethodName.MaxFanSpeed);  // 读回来就是上次写下去的值
```
"""
N2 = """> **事实**：原厂对功能号 21 的用法 = 把读回来的值**回显/定位手动转速滑块**（22~58 映射到 0~100），
> 写回去的值就是"手动设定转速" ⇒ **读值 = 上次写下去的值**，⛔ 不是硬件最大转速。
> （⛔ 不贴其代码。）
"""
must("```csharp\nprivate void Set_FanSpeed" in t, "block2 不在预期位置")
t = cut(t, "```csharp\nprivate void Set_FanSpeed", "⇒ ⛔ **它不是 max**", N2)

must("```csharp" not in t, "仍有 csharp 代码块")
with open(P, "wb") as f:
    f.write(t.encode("utf-8"))
print("[OK] 说明文档里的两段原厂代码已换成事实表")
