# 交接：WiFi 与「蓝牙」两颗快捷磁贴的图标

> ⚠️ **本文档的用途**：L-Mechrevo（仓库根 `D:\openLM`）的「快捷开关」磁贴行里，`wifi` 与 `bt` 两颗磁贴此前
> **没有自己的图标**（落到通用「开关」字形）。你要做的事只有一件：**给这两颗磁贴各自一枚独立、可辨识的图标**。
>
> ✅ **状态：本仓库已按本文档 §4 的规范自行完成**（2026-10-12，见 §3 末「已完成」一栏）。
> 因此本文档现在是**「已完成 / 仅供备查」**：
> * 如果你只是要**换风格 / 重画**这两枚图标 ⇒ 按 §4 的形状规范改 `UiGlyph.cs` 里那两段几何即可；
> * 如果你是要给**其它**磁贴补图标（例如 `touchpad` / `sleepnetoff` 等，见 §3 对照表里标 ❌ 的行）⇒ 照 §2 + §4 同一套做法加即可。
>
> ⛔ 只做图标。⛔ 不要碰磁贴的行为、布局、排序、持久化（见 §6 边界）。

---

## 1. 目标（一句话）

磁贴行里 `wifi`（文案「WiFi」）与 `bt`（文案「蓝牙」）两颗磁贴，各自画出**一眼能认出的**
WiFi 信号扇 / 蓝牙符文，**风格、字重、颜色、开-关状态表现与同排其它磁贴完全一致**，
在 **各 DPI（100/125/150/200%）与深/浅两套主题**下都正确。

---

## 2. 本仓库的图标机制（⚠️ 读这一节就够动手了）

### 2.1 是什么

* 图标**全部是代码画的矢量**，没有位图、没有图标字体、没有 SVG、没有资源文件。
* 唯一实现：`source\MechrevoLite.UI\UiGlyph.cs` —— `internal static class UiGlyph`（第 7 行）。
* 入口：`internal static Bitmap Render(UiGlyph.Kind kind, int size, Color color, int gap = 0)`（**`UiGlyph.cs:43`**）。
  返回一张 `size × size`（+`gap`）的 32 位位图，调用方拿到后自己设置前景色。
* 字形表：`internal enum Kind`（**`UiGlyph.cs:9-41`**）—— 一个成员 = 一枚图标。
  * 通用字形（标题行/底栏用）：`Gauge, VideoCard, Battery, Display, Toggles, Droplet, Refresh, Contrast, Console, Info, Overlay, Heart, Close, Gear, Chevrons, Package`
  * 磁贴专用字形（2026-10-11 加）**`UiGlyph.cs:27-40`**：`Fan, Camera, Microphone, MicrophoneMute, Keyboard, Moon, Plus` + 本轮新增 **`Wifi`（:39）、`Bluetooth`（:40）**
* 两种画法（都在同一个 `Render` 里分派）：
  * **大图标**：`Render` 自己的 `switch (kind)`（`UiGlyph.cs:63` 起）—— 给标题行/底栏那种 16–22 逻辑像素的图标用。
  * **磁贴图标**：`private static void DrawTileGlyph(...)`（**`UiGlyph.cs:285`**）—— 磁贴专用。
    `Render` 在 **`UiGlyph.cs:236-245`** 把 `Fan/Camera/Microphone/MicrophoneMute/Keyboard/Moon/Plus/Wifi/Bluetooth`
    转给 `DrawTileGlyph`。
    ⚠️ **加磁贴字形时必须同时改两处**：①`DrawTileGlyph` 里加 `case`；②`UiGlyph.cs:236-245` 的分派列表里加同名 `case`。
    漏了第 ② 步 ⇒ 落到 `Render` 的 `default`（画成一个"info/圆点人形"图标），磁贴上看起来就是"没生效"。

### 2.2 共同口径（`UiGlyph.cs:43-62`，⛔ 不要另造一套）

| 量 | 含义 | 代码 |
| --- | --- | --- |
| `num` | 画布边长（设备像素）= `max(8, size)` | `:45` |
| `num2` | **笔宽** = `max(1, size/12)` | `:49` |
| `pen` | 笔：`StartCap/EndCap = Round`、`LineJoin = Round`（**圆头圆角**） | `:50-55` |
| `brush` | 同色实心刷（画点/填充用） | `:56` |
| `num3` | **内边距** = `num2 * 1.6` | `:57` |
| `num4` | **内容框边长** = `num - num3*2` | `:58` |
| `num5/num6` | 内容框左上角 `(x, y)` | `:59-60` |
| `num7/num8` | 内容框中心 `(x, y)` | `:61-62` |

⇒ 写新几何时**一律用 `num4/num5/num6/num7/num8` 的相对比例**表达，⛔ 不要写死像素，
这样各 DPI 自动等比（`size = ResponsiveLayout.LogicalToDevice(this, 16)`）。

### 2.3 颜色与状态（谁给的颜色）

* `Render` 的 `color` 由**调用方**给 ⇒ 图标本身是**单色**的，深浅主题/开-关全靠调用方换色。
* 磁贴的调用点：`source\MechrevoLite\SettingsForm.cs:4735`
  `tile.Image = UiGlyph.Render(QuickTileGlyph(key, switchOn), num, on ? UiVisualStyle.Text : UiVisualStyle.Muted);`
  ⇒ **亮 = 开（`UiVisualStyle.Text` + 强调色描边）、暗 = 关（`UiVisualStyle.Muted`）**（同方法 `:4698-4716`）。
* 键 ⇒ 字形的映射：`private static UiGlyph.Kind QuickTileGlyph(string key, bool on)`（**`SettingsForm.cs:4135`**）。
  ⚠️ **同一个键只能选一枚字形**；"开/关"默认**不改字形**（只有 `micmute` 例外：静音时画带斜杠的麦克风）。
* 标题行/底栏走 `UiVisualStyle.ApplyGlyph(...)`（`source\MechrevoLite.UI\UiVisualStyle.cs:1097 / 1104 / 1115`，
  内部 `RenderGlyph` `:1152`）—— 换主题时会**重画**（`UiVisualStyle.cs:1314-1351` 按 `Tag` 里的 `Kind` 重渲染）。

### 2.4 磁贴的实际尺寸（画之前必须知道）

| 项 | 值 | 出处 |
| --- | --- | --- |
| 磁贴图标边长 | **16 逻辑像素** ⇒ 100/125/150/200% = **16 / 20 / 24 / 32 设备像素** | `SettingsForm.cs:825`（`QuickTileLogicalIcon = 16`）、`:4705` |
| 磁贴行高 | **44 逻辑像素** | `SettingsForm.cs:819`（`QuickTileLogicalHeight = 44`） |
| 磁贴按钮圆角 | **8 逻辑像素** | `SettingsForm.cs:4549` |
| 图标与文字布局 | 图标在上、文字在下（由 `RButton` 画 `Image`+`Text`） | `SettingsForm.cs:4537-4559` |
| 列宽 | 按**实测文字宽**加权切（不是等宽） | `SettingsForm.cs:4801`（`DistributeQuickTileColumns`） |
| 最窄窗 | 客户区最小宽 **420 逻辑像素** | `SettingsForm.cs:116` |
| 动作格上限 | **5** 格 + 末位「＋」 | `SettingsForm.cs:772` |
| 磁贴行控件名（出图定位用） | `Name = "perfQuickTiles"` | `SettingsForm.cs:4383` |

⚠️ 16 逻辑像素是本项目**最小的图标尺寸** ⇒ 形状要"只看轮廓就能认"，线条别太密（3 条弧已经接近上限）。

---

## 3. 现状：这两颗磁贴此前画的是什么（含完整对照表）

**改前**：`QuickTileGlyph`（`SettingsForm.cs:4135`）的 `switch` **没有 `"wifi"` / `"bt"` 两个 `case`**
⇒ 落进 `default:`（旧 `:4171`）⇒ 返回通用 `UiGlyph.Kind.Toggles` —— 就是「快捷开关」**标题行那枚"两个开关"的图标**。
所以改前 WiFi 与 蓝牙**两颗磁贴的图标完全一样**，而且和标题行图标一样（引擎/其它开关也共用它）。
**不是空白、也不是各自的图标** —— 是**复用了 `Toggles`**。

**磁贴可用 key ⇒ 当前字形**（来源：`SettingsForm.cs:132-143` 的「更多开关」分组 − 睡眠省组；
睡眠组不进「＋」面板，但老配置里若手改塞进 `perf_quick_tiles` 仍会走 `default`）：

| key | 磁贴文案 | 改前字形 | 现在字形 | 状态 |
| --- | --- | --- | --- | --- |
| `wifi` | WiFi | `Toggles`（复用） | **`Wifi`（新）** | ✅ 本轮新增 |
| `bt` | 蓝牙 | `Toggles`（复用） | **`Bluetooth`（2026-10-12 r6/r6b 定稿：标准蓝牙符文 —— 竖杆+上下两个三角环+两条探到环外的斜线；外伸量 r6b 缩半）** | ✅ |
| `touchpad` | 触摸板 | `Toggles`（复用） | **`Touchpad`（新，2026-10-12 定稿：圆角矩形触控板面 + 下部一条横分隔带 + 该带内竖分成两格＝左右键）** | ✅ 本轮新增 |
| `touchpadtoggle` | 触摸板切换键 | `Toggles`（复用） | **`Touchpad`——已接同一枚 Touchpad**（2026-10-12，用户原话「触摸板切换键是干啥用的 也用触摸板图标吧」⇒ 加 `case "touchpadtoggle": return UiGlyph.Kind.Touchpad;`，与 `touchpad` 共用 pad 字形） | ✅ 已接同一枚 `Touchpad` |
| `webcam` | 摄像头 | `Camera` | `Camera` | ✅ |
| `micmute` | 麦克风 | 关=`Microphone` / 静音=`MicrophoneMute` | 同 | ✅（唯一"按状态换字形"的键） |
| `numpad` | 小键盘 | `Keyboard` | `Keyboard` | ✅ |
| `winkey` / `fnkey` / `copilot` | Win键锁 / Fn键锁 / Copilot | `Keyboard` | `Keyboard` | ✅（三个键共用一枚，属实） |
| `osd` | OSD | `Console` | `Console` | ✅ |
| `taskbarautohide` | 任务栏自动隐藏 | `Console` | `Console` | ✅（与 OSD 共用） |
| `usb` / `acrecovery` | USB 供电 / 一键恢复 | `Battery` | `Battery` | ⚠️ 语义不贴（USB 用电池图标），沿用未改 |
| `highperf` / `cpuadvperf` | 高性能 / CPU 高级性能 | `Gauge` | `Gauge` | ✅ |
| `fanboost` | 风扇强冷 | `Fan` | `Fan` | ✅ |
| `deepsleep` | 深度睡眠 | `Moon` | `Moon` | ✅ |
| `gamewhitelist` | 游戏白名单 | `Package` | `Package` | ✅ |
| `transparency` / `darktheme` | 透明效果 / 深色主题 | `Contrast` | `Contrast` | ✅ |
| `monitoroff` | 息屏 | `Display` | `Display` | ✅ |
| `winupdate` | Windows 更新 | `Refresh` | **`Refresh`（2026-10-12 r6 定稿：缺口仍右下，箭头尖＝弧末端、**轴线＝该点切线**（顺着圈）；`Refresh` 的**唯一使用点**就是这一行）** | ✅（底栏「更新」按钮走 `Kind.Update`，⛔ 两枚不混用） |
| `sleepnetoff` / `sleepaway` / `sleepsysreq` / `sleeprtcwake` / `sleepdeep` / `sleephibernate` | 睡眠组 | `Toggles` | `Toggles` | ❌ 不在磁贴来源里（⛔ 不建议补） |
| 末位「＋」 | 自定义 | `Plus`（强调色） | `Plus` | ✅ |

---

## 4. 要交付什么（形状 / 线条规范 + 怎么注册）

### 4.1 形状与线条规范（与既有字形同一套"观感"）

* **线宽** = `max(1, size/12)`（= 变量 `num2`，⛔ 不要传自定义笔宽）。
* **端点/拐角** = 圆头圆角（用同一个 `pen`）。
* **占位**：主体落在 `num4` 内容框内（`num5..num5+num4` / `num6..num6+num4`）；
  允许个别笔画略微超出内容框（位图本身还有 `num3*2` 边距，不会裁），但**不要超出位图**。
* **单色**：只用 `pen`（描边）与 `brush`（实心点/填充），⛔ 不要自己算颜色。
* **16 px 可辨识**：先用轮廓读一遍再定稿。

#### 已实现的 `Kind.Wifi`（`UiGlyph.cs:348-363`）

> 信号扇：**3 层上凸弧 + 底部圆点**。
> 弧心 `(num7, num6 + 0.82*num4)`；半径 `num4 * (0.24, 0.40, 0.56)`；
> 每条弧扫 `210° → +120°`（GDI+ 角度约定：0°=右、90°=下 ⇒ 这段是"彩虹"那截，270°=正上方）；
> 圆点直径 `max(num2*1.2, num4*0.11)`，画在弧心处（实心 `FillEllipse`）。

```csharp
float wifiX = num7;
float wifiY = num6 + num4 * 0.82f;
for (int ring = 0; ring < 3; ring++)
{
    float radius = num4 * (0.24f + 0.16f * (float)ring);
    graphics.DrawArc(pen, wifiX - radius, wifiY - radius, radius * 2f, radius * 2f, 210f, 120f);
}
float wifiDot = Math.Max(num2 * 1.2f, num4 * 0.11f);
graphics.FillEllipse(brush, wifiX - wifiDot / 2f, wifiY - wifiDot / 2f, wifiDot, wifiDot);
```

#### 已实现的 `Kind.Bluetooth`（`UiGlyph.cs:374` 起，2026-10-12 **r6b 定稿**）

> **标准蓝牙符文**（⛔ 不是字母 B）＝ ①一根**竖杆** + ②右侧上下两个**三角环** + ③**两条探到环外的斜线**。
> 在 `DrawTileGlyph` 里是 **5 笔**（⚠️ 千万不要写成一笔闭合折线 —— 那样就退化成"字母 B"，
> 就是 2026-10-12 上一版被用户否掉的原因：**「蓝牙应当有两条线突出来的 而不是单纯的B形」**）。
>
> 1. **竖杆**：`x = num5 + 0.50*num4`，`y: 0.02 → 0.98` —— 符文的轴；
> 2. **两个三角环**（上下对称）：竖杆上端 →（右上折点 `0.96 / 0.30`）→ 中线右侧 `0.66 / 0.50`；
>    竖杆下端 →（右下折点 `0.96 / 0.70`）→ 同一个 `0.66 / 0.50` 点；竖杆把这两段折线封成两个三角环；
> 3. **两条探出的斜线**：从中线左侧 `0.44 / 0.50` 斜到左上 `0.26 / 0.32` 与左下 `0.26 / 0.68` ——
>    它们**越过竖杆、穿过环臂、探到环外**，于是左右两侧都露出线头（经典交错形）。
>    ⚠️ 2026-10-12 **r6b**（用户原话**「凸出来的可以短一点吗 感觉太夸张了」**）：外伸量由 `0.46·num4`
>    缩到 **`0.24·num4`（≈原来的 1/2）**；16px（内容框≈11.7px、笔宽 1.33px）下仍留 **≈2px 线头**
>    —— 看得见、⛔ 但不抢主体、不糊。

```csharp
float btStemX = num5 + num4 * 0.5f;
float btApexX = num5 + num4 * 0.96f;
float btJoinX = num5 + num4 * 0.66f;
float btTop = num6 + num4 * 0.02f;
float btMid = num6 + num4 * 0.5f;
graphics.DrawLine(pen, btStemX, btTop, btStemX, num6 + num4 * 0.98f);          // ① 竖杆
graphics.DrawLines(pen, new PointF[3] { new PointF(btStemX, btTop), new PointF(btApexX, num6 + num4 * 0.3f), new PointF(btJoinX, btMid) });
graphics.DrawLines(pen, new PointF[3] { new PointF(btStemX, num6 + num4 * 0.98f), new PointF(btApexX, num6 + num4 * 0.7f), new PointF(btJoinX, btMid) });
graphics.DrawLine(pen, num5 + num4 * 0.44f, btMid, num5 + num4 * 0.26f, num6 + num4 * 0.32f);   // ③ 探出的斜线
graphics.DrawLine(pen, num5 + num4 * 0.44f, btMid, num5 + num4 * 0.26f, num6 + num4 * 0.68f);
```

> 三态对照（原字母B → 上一版长外伸 → 本次短外伸）：
> `docs\screenshots\字形-蓝牙符文-外伸改短-三态对照-放大.png`

#### 已实现的 `Kind.Refresh`（`UiGlyph.cs:138` 起，2026-10-12 **r6 定稿**）

> 2026-10-12 r6（用户原话**「一个是更新的箭头应该顺着圈的方向」**）：缺口仍在**右下**（弧 `75°` 起、
> 顺时针扫 `300°` ⇒ 终点 `15°`），但箭头**不再是"斜着岔出去"的 V**：
> **箭头尖 ＝ 圆弧末端的那个点**，**箭头轴线 ＝ 该点的切线**（GDI+ 弧角顺时针 ⇒ 行进方向在屏幕坐标里是
> `(sin a, cos a)`），两翼从尖沿"切线反向 ±38°"张开、长 `0.46·半径` ⇒ 观感是箭头**顺着圈跑**。
> ⚠️ **唯一使用点**：`SettingsForm.cs:4169`（`QuickTileGlyph` 的 `case "winupdate"`，「禁用 Windows 更新」磁贴）。
> 底栏「更新」按钮**不用**它 —— 那枚是 `Kind.Update`（圆环+下箭头，`SettingsForm.cs:12092 / 15372`，2026-10-12 由另一会话换过）。

```csharp
float refreshR = num4 * 0.5f;
graphics.DrawArc(pen, num5, num6, num4, num4, 75f, 300f);
double refreshEnd = 15.0 * Math.PI / 180.0;
double refreshTangent = Math.Atan2(Math.Cos(refreshEnd), Math.Sin(refreshEnd));   // 弧终点的切线（行进方向）
float refreshTipX = num7 + refreshR * (float)Math.Cos(refreshEnd);
float refreshTipY = num8 + refreshR * (float)Math.Sin(refreshEnd);
for (int i = -1; i <= 1; i += 2)   // 两翼：从尖沿"切线反向 ±38°"
{
	double refreshBarb = refreshTangent + (double)i * (38.0 * Math.PI / 180.0) + Math.PI;
	graphics.DrawLine(pen, refreshTipX, refreshTipY, refreshTipX + refreshR * 0.46f * (float)Math.Cos(refreshBarb), refreshTipY + refreshR * 0.46f * (float)Math.Sin(refreshBarb));
}
```

* ⛔ **没有**做"关的时候加斜杠"的变体：本仓库的既定口径是**开=亮色 / 关=暗淡**（`SettingsForm.cs:4698-4716`），
  只有 `micmute` 因为语义反过来才用斜杠变体。要加变体就得连 `QuickTileGlyph(key, on)` 一起改，
  并且会与"高亮=开"的既有观感冲突 —— **不建议**。

#### 已实现的 `Kind.Touchpad`（`UiGlyph.cs:439` 起，2026-10-12 定稿）

> 2026-10-12（用户原话**「触摸板能不能有专属图标」**）：触摸板磁贴改用**专属**字形 ——
> **圆角矩形（触控板面）+ 下部一条横分隔 + 该带内竖分成两格（左右键）**
> （改前它没有自己的 `case`，与 WiFi/蓝牙改前一样**复用了通用 `Toggles`**）。
>
> 几何（0..1 归一化到内容框 `num4`，原点 `num5`/`num6`，⛔ 不另造笔宽/坐标口径）：
>
> 1. **板面**：`x = num5`、宽 `num4`、高 `0.84·num4`、**垂直居中**（宽 > 高 ⇒ 一眼是"触控板"，不是"方框"），
>    圆角 `num2 * 1.2`（与 `Battery` / `Camera` 同一档）；
> 2. **横分隔**：`y = 板顶 + 0.64·板高` ⇒ 下带（按键带）高 `0.36·板高`；
> 3. **竖分隔**：`x = num7`（中线），从横分隔竖到**板底** —— 与②的横线、①的底边是**同笔宽同心线**，
>    圆头端点恰好落在那两条边的中心线上（既不外溢、也不留缝）。
>
> ⚠️ **16 px 判据**（`num4 ≈ 11.7`、笔宽 1.33）：板面 ≈ 11.7×9.85、下带高 ≈ 3.55 ⇒ 每格内空 ≈ **4.5×2.2 px**，
> 圆角矩形 / 横分隔 / 两格按键**三者都还在**（⛔ 不糊成一团、⛔ 也不是只剩一个空框）。
> 100 / 125 / 150 / 200%（16/20/24/32 逻辑像素）× 深/浅复核图：
> `docs\screenshots\触摸板磁贴-16px实像素放大-深浅对照.png`（最近邻实像素放大，不做平滑）。

```csharp
float tpW = num4;
float tpH = num4 * 0.84f;
float tpTop = num8 - tpH / 2f;
float tpBottom = tpTop + tpH;
float tpSplit = tpTop + tpH * 0.64f;
using (GraphicsPath pathTouchpad = RoundedRect(new RectangleF(num5, tpTop, tpW, tpH), num2 * 1.2f))
{
	graphics.DrawPath(pen, pathTouchpad);                          // ① 触控板面
}
graphics.DrawLine(pen, num5, tpSplit, num5 + tpW, tpSplit);        // ② 下部横分隔
graphics.DrawLine(pen, num7, tpSplit, num7, tpBottom);             // ③ 竖分隔（左右两格按键）
```

* ⛔ **没有**做"关的时候加斜杠/变体"：本仓库既定口径是**开=亮色 / 关=暗淡**（见 §4.1 末尾同样的说明）。
* ✅ `touchpadtoggle`（触摸板切换键）：2026-10-12 用户明确要求（原话「触摸板切换键是干啥用的 也用触摸板图标吧」）
  ⇒ 已接**同一枚** `Touchpad` 字形（`QuickTileGlyph` 加 `case "touchpadtoggle": return UiGlyph.Kind.Touchpad;`）。

### 4.2 怎么注册（3 步，缺一不可）

1. **加枚举成员**：`UiGlyph.cs:9-41` 的 `enum Kind` **末尾追加**（⛔ 不要插在中间 —— 虽然本仓库不持久化 `Kind` 的数值，
   但追加最安全）。例：`Wifi,` / `Bluetooth`。
2. **加磁贴分派**：把新成员名加进 `UiGlyph.cs:236-245` 那个 `case Kind.Xxx:` 列表（和 `Fan/Camera/...` 并列）。
3. **写几何**：在 `DrawTileGlyph`（`UiGlyph.cs:260`）里加 `case`（放在 `default:` 之前）。
4. **接上 key**：`SettingsForm.cs:4135` `QuickTileGlyph` 里加
   `case "wifi": return UiGlyph.Kind.Wifi;` / `case "bt": return UiGlyph.Kind.Bluetooth;`
   （⛔ 放在 `default: return UiGlyph.Kind.Toggles;` 之前）。
   ⚠️ 键名必须与「更多开关」的键完全一致（`SettingsForm.cs:153-187` 的 `QuickSwitchLabels`）。

> 就这 4 处，**没有任何注册表/资源/配置文件**要改。

### 4.3 窗体标题栏左上角图标 = 对应按钮的**同一枚**字形（2026-10-12 r6b 新增）

**用户原话**：**「还有检查更新 设置等界面左上角图标 能不能同步按钮的图标」**。

* 机制：`UiVisualStyle.ApplyWindowGlyph(Form form, UiGlyph.Kind kind)`（`UiVisualStyle.cs`，紧挨 `ApplyFooterGlyph`）
  —— 把窗体登记进 `_windowGlyphs`，再按当前 DPI 画 **16 逻辑像素**的同款字形挂到 `Form.Icon`；
  颜色固定用**强调色**（`Accent`）：深浅两套标题栏都看得见（窗体只建一次，不随主题失效）。
  `ApplyTree` 开头有一次重画（换主题/换 DPI 时）。
* ⚠️ **图标必须用手写 ICO**（同文件的 `CreateAlphaIcon`）：`Bitmap.GetHicon()` → `Icon.FromHandle()` →
  `Icon.Save()` → `new Icon()` 这条链**会丢 alpha**（实测透明角落变 A=255 不透明黑）⇒ 标题栏上是一块黑方块。
  所以直接手写 `ICONDIR + ICONDIRENTRY + BITMAPINFOHEADER + XOR(BGRA) + AND 掩码`。
* **映射表**（改前一律是 exe 自带的 `app.ico`：深色圆角方块 + 白 "L"）：

  | 窗口/对话框 | 对应按钮（同一枚 Kind 的来源） | 字形 | 改前 → 改后 |
  | --- | --- | --- | --- |
  | `UpdateForm`（检查更新） | 底栏「更新」`buttonUpdates`（`SettingsForm.cs:12092 / 15372`） | `UiGlyph.Kind.Update` | `app.ico` → **Update** |
  | `SettingsDialog`（设置） | 底栏「设置」（`SettingsForm.cs:15384`） | `UiGlyph.Kind.Gear` | `app.ico` → **Gear** |
  | `DonateForm`（赞助） | 底栏「赞助」`buttonDonate`（`SettingsForm.cs:12058 / 15371`） | `UiGlyph.Kind.Heart` | `app.ico` → **Heart** |

* ⛔ **保持原样**（这些窗体**没有**"带字形的对应按钮"⇒ 按用户口径不动，⛔ 不要自己发明一枚）：
  `SettingsForm`（`ShowIcon=false`，标题栏本来就没有图标）、`FirstRunGuideForm`、`AboutLicensesForm`、
  `CustomModeForm`、`FanCurveForm`、`LightForm`、`RgbForm`、`CpuTuningForm`、`QuickTilePickerForm`、
  `FeedbackForm`、`RColorPicker`、以及 `Program.cs` 的托盘图标（那是托盘，不是标题栏）。
* ⛔ 只改 `Form.Icon`（标题栏左上角那枚），⛔ 不碰布局/尺寸/文本/行为。
* 出图：`docs\screenshots\窗体标题栏图标-设置-Gear同步-放大.png`

---

## 5. 怎么验证（`tools\dev\egroupshot`，只读渲染，⛔ 不碰硬件/配置）

### 5.1 准备（顺序不能反）

```powershell
# ① 先构建产品（工具引用的是 source\bin\Release\...\L-Mechrevo.dll）
& 'D:\openLM\dotnet-sdk\dotnet.exe' build source\L-Mechrevo.csproj -c Release
# ② 再构建出图工具（会把上面的 DLL 拷进自己的 bin）
& 'D:\openLM\dotnet-sdk\dotnet.exe' build tools\dev\egroupshot\egroupshot.csproj -c Release
```

* 工具的引用关系：`tools\dev\egroupshot\egroupshot.csproj:14-17`
  → `..\..\..\source\bin\Release\net10.0-windows10.0.19041.0\L-Mechrevo.dll`
  ⇒ ⚠️ **必须先 build 产品再 build 工具**，否则出的是旧图。
* ⚠️ **宿主需要字体**：`{EGroupShot.exe 所在目录}\Fonts\HarmonyOS_Sans_SC_Regular.ttf` 与 `..._Bold.ttf`
  （产品从 `{app}\Fonts` 加载；缺了会静默回退系统字体 ⇒ **文字宽度/版式判据全都不准**，但图标本身仍能看到）。
  仓库里 `tools\dev\egroupshot\bin\Release\...\Fonts\` 已带这两份 TTF。

### 5.1.1 自包含发布（2026-10-12 起，✅ 推荐直接从发布产物跑）

```powershell
& 'D:\openLM\dotnet-sdk\dotnet.exe' publish tools\dev\egroupshot\egroupshot.csproj `
    -c Release -r win-x64 --self-contained true -o tools\dev\egroupshot\publish
```

* 产物路径：`D:\openLM\tools\dev\egroupshot\publish\`（含 `EGroupShot.exe`、`coreclr.dll`、`hostfxr.dll`）。
  ✅ **直接从该目录运行**，⛔ **不要再把 exe 复制到 `tmp_*\` 里裸跑**（复制会漏掉运行时/`Fonts\`）。
* 自包含怎么认：`publish\EGroupShot.runtimeconfig.json` 里是 **`includedFrameworks`**（不是 `frameworks`），
  且 `publish\coreclr.dll` / `hostfxr.dll` 存在 ⇒ **不依赖系统 .NET**，不会再弹
  「You must install or update .NET to run this application」。
* ⚠️ **取证前先重新 publish**：自包含产物里会带一份 `L-Mechrevo.dll` 的**副本**，它可能落后于
  `source\bin\Release\net10.0-windows10.0.19041.0\L-Mechrevo.dll` 的最新构建
  ⇒ ⭐ **先跑一次上面那条 publish 命令**，或先核对两份哈希是否一致：

  ```powershell
  # 一致（同一份 DLL）才能拿这份 publish 产物去取证；不一致 ⇒ 重新 publish
  Get-FileHash tools\dev\egroupshot\publish\L-Mechrevo.dll, `
      source\bin\Release\net10.0-windows10.0.19041.0\L-Mechrevo.dll -Algorithm SHA256 |
    Select-Object Hash, Path
  ```

  ⚠️ 2026-10-12 实测就踩过：`publish\L-Mechrevo.dll` = `E90F52F7…` 比 2 秒后重建的
  `source\bin\…\L-Mechrevo.dll` = `4EF72A22…` **旧一版** ⇒ 那样出的图/几何是**旧一版产品**的行为。
* `Fonts\HarmonyOS_Sans_SC_Regular.ttf` / `..._Bold.ttf` 需在 exe 同级 `Fonts\` 下（见 5.1）；发布后确认它在不在。
* ⛔ **不是单文件**（`PublishSingleFile` 未开）：Windows Forms + 反射加载整目录更稳；请整目录一起用。
* 「隐形」行为：失败**不弹任何模态框**，只写 `stderr`（本工具是 WinExe，无控制台时该行看不到）+ 退出码 +
  `<LMECHREVO_LOG_DIR>\EGROUPSHOT-FAILED.txt`；成功/失败都 `Environment.Exit` **用完即退**，⛔ 不留后台。
* 所有输出仍只落在 `<outDir>`（`<outDir>\shots-<suffix>\`、`egroupshot-<suffix>.txt/.csv`、`tool-logs-<suffix>\`），
  ⛔ 不写用户 AppData。

### 5.2 让磁贴行出现 `wifi` 和 `bt`

工具把自己的配置**隔离**在 `<outDir>\tmp-cfg-<suffix>.json`（`Program.cs:109-114`，`LMECHREVO_CONFIG_FILE`），
文件**不存在时**才会写默认内容 ⇒ 想指定磁贴就先手写这个文件：

```json
{
  "performance_mode": 0,
  "igpu_only_asked": 1,
  "igpu_only_ui": "show",
  "igpu_attempt_pending": -1,
  "dgpu_generation": "Gen40",
  "dgpu_generation_source": "MarketingName",
  "dgpu_has_dgpu": "1",
  "perf_quick_tiles": "wifi,bt,fanboost,webcam,micmute"
}
```

* `perf_quick_tiles` = **逗号分隔、顺序即磁贴顺序**，最多 5 个动作格（`SettingsForm.cs:723-745`）。
* 磁贴还要过**能力位**：夹具 `Settings-Tiles`（`Program.cs:263-268`）会喂
  `Settings/DeviceSwitchItemStatus` 里的 `WIFIEnable:true` / `BTEnable:true`（`Program.cs:388`）
  ⇒ 这两颗开关才"存在"。⛔ 用别的夹具（如 `Settings-Full`）它们会被判成不支持而不上屏。

### 5.3 出图命令（深 / 浅各一张磁贴行）

```powershell
$exe = 'D:\openLM\tools\dev\egroupshot\publish\EGroupShot.exe'   # 自包含产物，直接从发布目录跑（见 5.1.1）

# 深色（⛔ 不设 LMECHREVO_SHOT_THEME = 保持夜间主题）
$env:CARD_CROP_PATH = 'perfQuickTiles'   # = 磁贴行控件名（SettingsForm.cs:4383）
$env:CARD_CROP_NAME = 'tiles'
$env:CARD_CROP_Y    = '24'
Remove-Item Env:\LMECHREVO_SHOT_THEME -ErrorAction SilentlyContinue
& $exe 'D:\openLM\tmp_icon_r1\icon-dark' 'icon-dark' 'Settings-Tiles' '1920x1080-100pct,1920x1080-200pct'

# 浅色
$env:LMECHREVO_SHOT_THEME = 'day'
& $exe 'D:\openLM\tmp_icon_r1\icon-day'  'icon-day'  'Settings-Tiles' '1920x1080-100pct,1920x1080-200pct'
```

* 用法：`EGroupShot.exe <outDir> <suffix> [夹具子串白名单] [视口白名单] [noshots]`（`Program.cs:44-45`）。
* 结果：`<outDir>\shots-<suffix>\card-<CARD_CROP_NAME>-<夹具>-<视口>.png`；
  整窗图另存 `shot-<夹具>-<视口>.png`。
* 可用的相关环境变量（全部只影响渲染，⛔ 不写硬件/配置）：
  | 变量 | 作用 | 出处 |
  | --- | --- | --- |
  | `CARD_CROP_PATH` | 按控件路径子串裁剪（本任务用 `perfQuickTiles`） | `Program.cs:989` |
  | `CARD_CROP_NAME` | 输出文件名里的名字 | `Program.cs:994` |
  | `CARD_CROP_Y` / `CARD_CROP_H` | 卡片落到客户区的 Y / 只取前 H 像素 | `Program.cs:1011 / 1053` |
  | `LMECHREVO_SHOT_THEME` | `day`\|`light` = 切浅色后再出图；不设 = 夜间 | `Program.cs:967-979` |
  | `LMECHREVO_SHOT_FORMWIDTH` | 额外取证"用户把窗口拉宽"的形态 | `Program.cs:887` |
  | `LMECHREVO_FORCE_30UI` | 30 系布局（多一行配置下拉，磁贴行位置不同） | 见 `run_matrix2.ps1` |
  | `LMECHREVO_SHOT_BRIGHT` | `0` = 夹具亮度上报 0%（与图标无关） | `Program.cs:387` |
  | `LMECHREVO_CONFIG_FILE` | 工具自己设的隔离配置路径 | `Program.cs:114` |

### 5.4 要看什么（验收清单）

1. **形状**：WiFi 是"3 弧 + 点"、蓝牙是"竖杆 + 两个右向折角"，两者**互不相同**，且**都不再是标题行那枚开关图标**。
2. **状态色**：开 = 亮色 + 强调色描边；关 = 暗淡 —— 与同排其它磁贴同一套（`SettingsForm.cs:4698-4716`）。
3. **主题**：深/浅各出一张，图标颜色跟着主题走（亮/暗两套都别"看不见"）。
4. **DPI**：至少 100%（16 px）与 200%（32 px）；125%/150% 建议一并扫（`1920x1080-125pct` 等视口 id 见 `Program.cs:177-190`）。
   ⚠️ 判据是"16 px 下仍能认出"，别只看放大图。
5. **版式未被图标带坏**：图标仍是 16 逻辑像素、磁贴行高度 44 逻辑、文字不裁、没有 `parent-overflow`
   （工具会跑产品自己的 `UiAuditRunner` 判据并把结论写进 `<outDir>\egroupshot-<suffix>.txt` / `.csv`）。
6. **⛔ 不要**为了看清图标去改磁贴尺寸/行高/字号 —— 那属于版式，不在本任务范围。

---

## 6. 边界（⛔ 硬约束）

* ⛔ **只动这两枚图标**（或你要补的那几枚）：⛔ 不改其它 `Kind` 的既有几何（改一处会波及标题行/底栏/其它磁贴）。
* ⛔ **不动磁贴行为**：点击、下发路径（`SettingsForm.ToggleQuickTileAsync`）、能力位过滤（`QuickTileSupported` / `StaticQuick`）、
  高亮极性（`QuickTileHighlightOn`）一律不碰。
* ⛔ **不动布局**：图标边长 16、行高 44、圆角 8、列宽加权分配、字号自适配，全部保持。
* ⛔ **不动排序/持久化**：`perf_quick_tiles` 的语义是"**只有已勾选磁贴的顺序有意义**，
  未勾选行的位置只是临时摆放、不跨'确定→重开'保留"（见 `QuickTilePickerForm.cs:34-41` 类头）。
  ⇒ ⛔ 不要改重开顺序规则、⛔ 不要加第二个键。
* ⛔ **不加素材**：不引入位图/图标字体/SVG/资源文件（本仓库图标机制就是纯 C# 矢量；
  引入字体还涉及**分发授权**问题 ⇒ 本任务不需要，也不应做）。
* ⚠️ **保持文件原有行尾与 BOM**：`UiGlyph.cs` = **CRLF、无 BOM**；`SettingsForm.cs` = **LF、无 BOM**。
  改含中文的源文件请用字节级读写（例如 Python `open(...,'rb')` / .NET `File.ReadAllBytes`+`WriteAllBytes`），
  ⛔ 不要用会改写编码/换行的工具。
* ⛔ **不出包**：不跑 ISCC、不动 `installer-src\`、不动版本文件。

---

## 7. 给用户的一小段「怎么跟那个 AI 说」

> **把本文档整份发过去**，然后说：
>
> 「这是 L-Mechrevo 仓库（`D:\openLM`）的图标交接文档。**只做「快捷开关」磁贴行里 WiFi 与 蓝牙这两颗磁贴的图标**，
> 让它们各自有自己的、一眼能认出的图标（WiFi 信号扇 / 蓝牙符文），不再和别的磁贴共用同一个字形。
>
> **最小要求（缺一不可）**：
> 1. 只改 `source\MechrevoLite.UI\UiGlyph.cs`（加字形几何 + 分派）和
>    `source\MechrevoLite\SettingsForm.cs` 的 `QuickTileGlyph`（把 `wifi` / `bt` 两个 key 接到新字形）；
> 2. 线宽/圆头/内边距/尺寸口径**照抄** `UiGlyph` 既有的（见文档 §2.2），⛔ 不要自己定一套；
> 3. 图标 16 逻辑像素下要能认出来；深色/浅色主题、100%~200% DPI 都要对（文档 §5 有验证命令）；
> 4. ⛔ **不要动其它字形、不要动磁贴的点击/布局/排序/持久化、不要加位图或图标字体、不要出安装包**；
> 5. 改完按文档 §5 出图（深/浅各一张磁贴行）+ 给出 `dotnet build source\L-Mechrevo.csproj -c Release` 的
>    错误数与警告数（本仓库基线 **1446 个警告**，⛔ 不许新增）。」
>
> ⚠️ 若你只想**换这两枚图标的风格**，就把上面第 1 条改成"按文档 §4.1 的两段几何重画，形状规范见 §4.1"。
> ⚠️ 若你要让它**给别的磁贴**补图标（文档 §3 表里标 ❌ 的行，如 `touchpad` / `sleepnetoff`），
> 就明说"只补哪些 key"，⛔ 别让它顺手改别的。

---

## 8. 本仓库已经做了什么（备查 / 便于对照）

| 文件 | 改动 | 行 |
| --- | --- | --- |
| `source\MechrevoLite.UI\UiGlyph.cs` | `enum Kind` 末尾加 `Wifi` / `Bluetooth` | `:39-40` |
| 同上 | `Render` 的磁贴分派列表加这两枚 | `:243-244` |
| 同上 | `DrawTileGlyph` 加两段几何 | `:348-363`（Wifi）、`:364-381`（Bluetooth） |
| `source\MechrevoLite\SettingsForm.cs` | `QuickTileGlyph` 加 4 行 `case "wifi"` / `case "bt"` | `:4172-4175` |
| `source\MechrevoLite.UI\UiGlyph.cs` | 2026-10-12：`Bluetooth` 重画为标准交叉菱形符文（一笔闭合折线 9 点）；`enum Kind` 末尾加 `Update`，磁贴分派 + `DrawTileGlyph` 各加 case（圆环+向下箭头） | `:364-381`、`Update` 在 `Bluetooth` 之后 |
| `source\MechrevoLite\SettingsForm.cs` | `QuickTileGlyph`：`case "winupdate"` 从 `Kind.Refresh` 改为 `Kind.Update` | `:4168-4169` |
| `source\MechrevoLite.UI\UiGlyph.cs` | 2026-10-12 11:39（r3b）：`Refresh` 缺口改到右上 315°、V 形箭头贴合环缺口两端、尖沿顺时针朝右上（用户要求「箭头在环上指向顺时针」）；`Bluetooth` 保持回退竖杆版、`Update` 保留 | `:138-142` |
| `source\MechrevoLite\SettingsForm.cs` | 2026-10-12 11:34（r3）：`case "winupdate"` 改回 `Kind.Refresh`；底栏 `buttonUpdates`（`ResetLegacyFooterButton`）改用 `Kind.Update` | `:4168-4169`、`:15372` |
| `source\MechrevoLite.UI\UiGlyph.cs` | 2026-10-12 **r6**：`Refresh` 箭头改为**贴合圆弧末端切线**（顺着圈）；`Bluetooth` 改为标准符文（竖杆 + 上下两三角环 + 两条探出斜线）。**r6b**：蓝牙外伸量 `0.46·num4 → 0.24·num4` | `:138-158`、`:374` 起 |
| `source\MechrevoLite.UI\UiVisualStyle.cs` | 2026-10-12 **r6b**：新增 `ApplyWindowGlyph` / `RenderWindowGlyph` / `CreateAlphaIcon`（手写带 alpha 的 ICO）、`_windowGlyphs`、`ApplyTree` 重画 | 紧挨 `ApplyFooterGlyph` |
| `source\MechrevoLite.Update\UpdateForm.cs`、`source\MechrevoLite\SettingsDialog.cs`、`source\MechrevoLite\DonateForm.cs` | 2026-10-12 **r6b**：标题栏图标 = 对应按钮同字形（Update / Gear / Heart） | 各自构造函数 `Text = ...;` 之后 |
| `source\MechrevoLite.UI\UiGlyph.cs` | 2026-10-12（用户原话**「触摸板能不能有专属图标」**）：`enum Kind` 末尾加 `Touchpad`；`Render` 的磁贴分派列表加 `case Kind.Touchpad`；`DrawTileGlyph` 加触摸板几何（圆角矩形触控板面 + 下部横分隔 + 带内竖分成两格＝左右键） | `:43-48`、`:269`、`:439-461` |
| `source\MechrevoLite\SettingsForm.cs` | 同上：`QuickTileGlyph` 加 `case "touchpad": return UiGlyph.Kind.Touchpad;`；2026-10-12 用户原话「触摸板切换键是干啥用的 也用触摸板图标吧」⇒ 再加 `case "touchpadtoggle": return UiGlyph.Kind.Touchpad;`（与 `touchpad` 共用同一枚 pad 字形） | `:4200-4205` |

* 出图（改后）：
  * `D:\openLM\docs\screenshots\磁贴行-WiFi蓝牙图标-深色.png`（1920x1080 100%）
  * `D:\openLM\docs\screenshots\磁贴行-WiFi蓝牙图标-浅色.png`（1920x1080 100%）
  * `D:\openLM\docs\screenshots\磁贴行-WiFi蓝牙图标-200pct-深色.png` / `-200pct-浅色.png`（放大看形状）
* 出图（2026-10-12 **r6b** 最新一轮，全部在 `docs\screenshots\`）：
  * 磁贴行：`磁贴行-WiFi蓝牙更新图标-深色-100pct / -125pct / -150pct / -200pct.png`、
    `磁贴行-WiFi蓝牙更新图标-浅色-100pct / -150pct / -200pct.png`
  * 放大对照：`字形-蓝牙符文-外伸改短-三态对照-放大.png`（原字母B → r6 长外伸 → r6b 短外伸）、
    `字形-更新箭头-改前改后对照-放大.png`
  * 标题栏图标：`窗体标题栏图标-设置-Gear同步-放大.png`
* 构建：`dotnet build source\L-Mechrevo.csproj -c Release` ⇒ **0 错误**、警告 **1445**（= 基线，差额 0）。
* 2026-10-12（用户原话**「触摸板能不能有专属图标」**）：`touchpad` 磁贴补上**专属** `Kind.Touchpad` 字形
  （圆角矩形触控板面 + 下部横分隔带 + 带内竖分成两格＝左右键）；`touchpadtoggle` **当时未改**。
  → 2026-10-12 后续（用户原话**「触摸板切换键是干啥用的 也用触摸板图标吧」**）：`touchpadtoggle` **已接同一枚 `Touchpad`**。
  * 出图：`docs\screenshots\磁贴行-触摸板专属图标-深色-100pct.png`、`-浅色-100pct.png`、
    `-深色-200pct.png`、`-浅色-200pct.png`；放大复核（100/125/150/200% × 深/浅）：
    `docs\screenshots\触摸板磁贴-16px实像素放大-深浅对照.png`。
  * 部署：安装件 SHA-256 == 构建产物（`4EF72A22D9138897BC356111C8CCC1E01CB58AECE9FE10786B470428D22020BC`），
    版本 `0.291.7-beta22.7`，`start_count` 108 → 109，无新增 crash。
