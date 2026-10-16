# UI 样式规范（L-Mechrevo / 我们自己的一版）

> **本文档的性质**：这是我们**自己的**样式规范，不是任何外部项目的复刻说明。它先把"我们要什么"讲清楚，
> 再逐条对照**当前代码里实际在跑的值**。用户点头之后才动代码。
>
> **阅读约定**（很重要，避免把提案当现状）：
> - 标 **【现状】** 的数值 = 已确认与当前源码一致，可直接照抄实现，不需要再确认。
> - 标 **【提案】** 的数值 = **还没实现**，是本文档新提出的建议值，需要用户在 §9 拍板。
> - 标 **【现状·不一致】** = 代码里现在就是这样，但它与本规范的原则冲突，属于后续实现时要修的清单。
> - 所有对比度都用与本项目 `UiVisualStyle.ContrastRatio()` **完全相同**的 WCAG 公式（sRGB 线性化后
>   `(L1+0.05)/(L2+0.05)`）由脚本重算过，不是凭记忆。
>
> **本轮范围**：整体配色与深浅 ＋ 按钮。**下拉框出范围（用户自理）**，只在 §7 留一份交接说明。
>
> **本轮不改任何代码、不部署、不打包。** 本文档是唯一产物。

---

## 1. 设计原则

我们的取向是六句话，冲突时按顺序让位（越靠前越硬）：

1. **层次靠"底色分级 + 留白"，不靠画框。**
   页面底 → 卡片 → 按钮面 → 悬停 → 选中，是一条连续的明度阶梯；相邻两级必须"一眼能分出来"，
   但**不允许**靠"多画一圈边""加一个阴影""铺一块异色矩形"来假装层次。
   一个区域的颜色深浅就应该是它的语义。

2. **边框只画"该有的那一圈"，一个方向只允许一层边。**
   任何控件在任何一个状态下，**最多一层可见的边**。宁可这层边加粗/提亮，也不许并排出现第二层。
   （"外框 + 内缩彩框 + 深底"那种三层观感，是我们要明确避免的失败形态。）

3. **选中态用"强调色"表达，不许大面积实心色块把界面撕开。**
   强调色优先落在**边**和**少量淡底**上，把"大面积饱和色"留给真正需要抢注意力的少数控件。
   模式色（静音绿/平衡蓝/狂暴红…）是**语义**，它必须能被读出来，但不该把半个面板染成绿色。

4. **语义与深浅分离：色相管"是什么"，明度管"在哪一层"。**
   同一个控件在夜间和日间，**明度阶梯的方向必须一致**（底最暗 → 卡片 → 按钮面 → 悬停 → 选中）；
   色相可以随主题调整，但"按钮比它所在的卡片更浅（浮起）"这条不许因为换主题而反过来。

5. **禁止"靠几个色值差装作有边 / 装作有底"。**
   凡是"铺一块和自己底色差 3~6 的矩形""把一条 1px 线画在整数与半像素交界上"导致发虚、
   "用二值 Region 把抗锯齿羽化边切掉"导致锯齿 —— 这些都算 bug，不算风格。

6. **日间是独立的一版，不是夜间的副产品。**
   不许"顺手把夜间值也用到日间"。日间任何改动都需要单独说明、单独目视、单独点头。

---

## 2. 色板（夜间 ＋ 日间）

**来源**：`source\MechrevoLite.UI\UiVisualStyle.cs`
- `NightPalette`：第 165 行
- `DayPalette`：第 173 行
- `NightModePalette` / `DayModePalette`（五个模式色）：第 185 / 187 行

### 2.1 中性层 ＋ 语义层令牌表

"相对亮度"＝WCAG 线性化相对亮度 L；"相邻层对比"＝与它语义上的上一层/下一层之比（夜 ｜ 日）。

| 令牌 | 夜间值 | 日间值 | 用途 | 相对亮度（夜｜日） | 相邻层 WCAG（夜｜日） | 现状 |
|---|---|---|---|---|---|---|
| `Window` | `28,28,28` `#1C1C1C` | `233,238,244` `#E9EEF4` | **页面底**（窗体、行的底） | 0.0116 ｜ 0.8500 | 对 Surface 1.156 ｜ 1.167 | 【现状】0.291 改日间（`242,245,249`→本值） |
| `Surface` | `40,40,40` `#282828` | `255,255,255` `#FFFFFF` | **卡片**（分组容器） | 0.0212 ｜ 1.0000 | 对 Window 1.156 ｜ 1.167 | 【现状】日间＝纯白，不动（日间最亮层） |
| `SurfaceRaised` | `54,54,54` `#363636` | `233,239,246` `#E9EFF6` | **再抬一档的容器**（表头/分隔带；日间＝按钮面同值） | 0.0369 ｜ 0.8571 | 对 Surface 1.220 ｜ 1.158 | 【现状】 |
| `Input` | `20,20,20` `#141414` | `224,232,241` `#E0E8F1` | **凹陷/可输入**（输入框、数字框、下拉闭合态） | 0.0070 ｜ 0.7991 | 对 Surface 1.250 ｜ **1.237** | 【现状】0.291 改日间（纯白→本值，凹陷落地） |
| `ButtonFace` | `62,62,62` `#3E3E3E` | `233,239,246` `#E9EFF6` | **未选中按钮面**（浮起） | 0.0482 ｜ 0.8571 | 对 Surface 1.378 ｜ 1.158 | 【现状】日间值不动；0.291 起**分段按钮也用它** |
| `ButtonFaceHover` | `72,72,72` `#484848` | `218,227,238` `#DAE3EE` | 按钮悬停 | 0.0648 ｜ 0.7602 | 对 ButtonFace 1.169 ｜ **1.120** | 【现状】0.291 改日间（原与 ButtonFace 同值） |
| `ButtonFaceSelected` | `86,86,86` `#565656` | `209,219,233` `#D1DBE9` | 选中底 / 下拉展开选中行 | 0.0931 ｜ 0.7010 | 对 ButtonFace 1.457 ｜ **1.208** | 【现状】0.291 改日间（原同值） |
| `Border` | `56,56,56` `#383838` | `201,213,227` `#C9D5E3` | 普通边（日间通用边） | 0.0395 ｜ 0.6555 | 对 Window 1.453 ｜ 1.276（对 Surface 1.488） | 【现状】 |
| `BorderStrong` | `74,74,74` `#4A4A4A` | `158,175,196` `#9EAFC4` | **输入族描边**（夜间 2px） | 0.0685 ｜ 0.4191 | 对 Input 2.079 ｜ **1.810** | 【现状】0.291 改日间（原＝Border） |
| `ButtonEdge` | `82,82,82` `#525252` | `176,191,208` `#B0BFD0` | **未选中按钮那一圈 1px 硬边** | 0.0844 ｜ 0.5105 | 对 ButtonFace 1.369 ｜ **1.619** | 【现状】0.291 改日间（原＝Border） |
| `Track` | `46,46,46` `#2E2E2E` | `215,224,235` `#D7E0EB` | 开关滑轨 / 滑槽 | 0.0273 ｜ 0.7376 | 对 Window 1.255 ｜ 1.143 | 【现状】 |
| `Text` | `240,240,240` `#F0F0F0` | `22,32,46` `#16202E` | 主文字 | 0.8714 ｜ 0.0140 | 对 Window **14.95 ｜ 14.06** | 【现状】 |
| `Muted` | `158,158,158` `#9E9E9E` | `78,95,117` `#4E5F75` | 次要文字/图标 | 0.3419 ｜ 0.1109 | 对 Surface **5.50 ｜ 6.53** | 【现状】0.291 改日间（修掉 Track 上 4.09 ❌） |
| `Accent` | `77,163,255` `#4DA3FF` | `31,111,224` `#1F6FE0` | 主强调（开关开、主操作、通用选中） | 0.3499 ｜ 0.1704 | `AccentText` 在其上 7.16 ｜ 4.76 | 【现状】 |
| `AccentText` | `8,18,31` `#08121F` | `255,255,255` `#FFFFFF` | **`Accent` 上的文字** | 0.0058 ｜ 1.0000 | 对 `Accent` 7.16 ｜ 4.76 | 【现状】 |
| `AccentHover` | `111,180,255` `#6FB4FF` | `26,95,196` `#1A5FC4` | 主强调悬停 | 0.4324 ｜ 0.1239 | — | 【现状】 |
| `AccentPressed` | `58,140,230` `#3A8CE6` | `20,80,159` `#14509F` | 主强调按下 | 0.2537 ｜ 0.0839 | — | 【现状】 |
| `AccentBlue` | `116,168,232` `#74A8E8` | `0,102,204` `#0066CC` | 信息性蓝（非主操作） | 0.3754 ｜ 0.1386 | — | 【现状】 |
| `Warn` | `232,179,57` `#E8B339` | `139,95,15` `#8B5F0F` | **待生效＝棕** | 0.4969 ｜ 0.1371 | 对 Window 8.88 ｜ 5.13 | 【现状】 |
| `Danger` | `240,88,78` `#F0584E` | `190,30,45` `#BE1E2D` | 危险/失败 | 0.2605 ｜ 0.1207 | 对 Window 5.04 ｜ 5.63 | 【现状】 |
| `Ok` | `53,196,107` `#35C46B` | `22,120,63` `#16783F` | 成功/已生效 | 0.4130 ｜ 0.1396 | 对 Window 7.51 ｜ 5.06 | 【现状】 |

### 2.2 五个模式色（`NightModePalette` / `DayModePalette`）

每个模式是**一对**（高亮底 `Background` ＋ 该底上的文字 `Foreground`），由 `ModeAccent` 承载。

| 模式 | 夜间底 | 夜间文字 | 夜·实心文字对比 | 日间底 | 日间文字 | 日·实心文字对比 |
|---|---|---|---|---|---|---|
| 静音（绿） | `52,168,106` | `7,22,15` | **6.15** | `24,132,76` | `255,255,255` | **4.73** |
| 平衡（蓝，＝`Accent`） | `77,163,255` | `8,18,31` | **7.16** | `31,111,224` | `255,255,255` | **4.76** |
| 静音狂暴（浅红） | `230,128,120` | `40,10,8` | **6.79** | `226,116,106` | `56,12,10` | **5.66** |
| 高能狂暴（大红） | `206,44,40` | `255,255,255` | **5.23** | `192,38,38` | `255,255,255` | **5.92** |
| 自定义（浅蓝） | `122,200,236` | `8,24,36` | **9.69** | `118,192,232` | `12,34,52` | **8.10** |

全部 ≥ 4.5（AA）✅ 【现状】。

### 2.3 派生色（由代码公式算出，不是手写常量）

| 派生令牌 | 公式 | 夜间结果 | 说明 |
|---|---|---|---|
| `ButtonFaceHighlight` | `Mix(ButtonFace, White, 0.16)` | `93,93,93` | 按钮面顶部 1px 高光；对 `ButtonFace` 1.624 |
| `ModeActivatedFill(accent)` | `Mix(ButtonFaceSelected, accent, 0.18)` | 见 §4.6 | 选中态淡底 |
| `SelectionOutlineColor(accent)` | `Mix(accent, White, 0.12)` | 见 §4.6 | 选中态描边（色相不变，只提亮） |

### 2.4 日间：**已按本规范与夜间同构（0.291 beta22 落地）** ✅

**拍板记录**：§9 第 3 题由用户以行动拍板 **A（同构）** —— 2026-10-09 用户原话
「下拉框好像没改过来 还是旧版的 而且**浅色的是不是还没升级** 版本号升级 0.291 beta22」。
本节因此从【提案】变为【现状】：`source\MechrevoLite.UI\UiVisualStyle.cs` 的 `DayPalette` 已按下面的表改完并部署。

**改前日间的五个硬伤（实测）**：

| 日间问题 | 改前实测 | 后果 |
|---|---|---|
| `Input` ＝ `Surface` ＝ 纯白 | 对比 **1.000** | 输入框/下拉与卡片**完全同色**，只剩一圈 1px 边区分 |
| `ButtonFace` ＝ `ButtonFaceHover` ＝ `ButtonFaceSelected` ＝ `233,239,246` | 三者两两 **1.000** | 按钮**没有悬停反馈、没有选中底**，一切全靠边与实心强调色 |
| `Window↔Surface` = **1.094** | 低于 §3 的下限 1.15 | 页面底与卡片几乎分不开 |
| `BorderStrong` ＝ `Border` ＝ `ButtonEdge` | 逐像素相同 | 输入族"强一档"的边与"按钮硬边"在日间都不存在 |
| 分段按钮未选中面取 `Input`（纯白），而普通按钮面取 `ButtonFace`（`233,239,246`） | 两套"按钮面" | 日间同一屏里两种按钮底不同 |

**改前 → 改后对照（日间；相对亮度 L 与 WCAG 均由本项目 `ContrastRatio()` 同公式脚本重算）**：

| 日间令牌 | 改前 | 改后 | 改后 L | 关键级差（改前 → 改后） |
|---|---|---|---|---|
| `Window` | `242,245,249` | **`233,238,244`** | 0.8500 | `Window↔Surface` 1.094 → **1.167** ✅ |
| `Surface` | `255,255,255` | `255,255,255`（不动） | 1.0000 | 卡片＝纯白，日间最亮层（身份不变） |
| `SurfaceRaised` | `233,239,246` | `233,239,246`（不动） | 0.8571 | `Surface↔SurfaceRaised` 1.158 ✅ |
| `Input` | `255,255,255` | **`224,232,241`** | 0.7991 | `Input↔Surface` **1.000 → 1.237** ✅（凹陷落地）；`Input` 比 `Window` 再暗 1.060（与夜间 1.081 同向） |
| `ButtonFace` | `233,239,246` | `233,239,246`（不动） | 0.8571 | 对卡片 1.158 ✅（浅底上不可能比纯白更亮 ⇒ 日间的"浮起"＝比卡片深一档的浅灰面 + 一圈硬边） |
| `ButtonFaceHover` | `233,239,246` | **`218,227,238`** | 0.7602 | 对 `ButtonFace` **1.000 → 1.120** ✅ |
| `ButtonFaceSelected` | `233,239,246` | **`209,219,233`** | 0.7010 | 对 `ButtonFace` **1.000 → 1.208** ✅ |
| `Border` | `201,213,227` | `201,213,227`（不动） | 0.6555 | 对 `Surface` 1.488 ✅；对 `Window` 1.276 |
| `BorderStrong` | `201,213,227` | **`158,175,196`** | 0.4191 | 对 `Input` **1.000 → 1.810** ✅（恢复"强一档"） |
| `ButtonEdge` | `201,213,227` | **`176,191,208`** | 0.5105 | 对 `ButtonFace` 1.286 → **1.619** ✅（与夜间 1.369 同向） |
| `Muted` | `90,107,128` | **`78,95,117`** | 0.1109 | 修掉 §3.3 里日间唯一 AA 不达标：`Muted` 在 `Track` 上 **4.09 ❌ → 4.895 ✅**；其余底 4.67~6.53 全部 ≥4.5 |
| `Track` / `Text` / 全部语义色 / 五个模式色 | — | **一个数值都没动** | — | `Text` 12.20~16.40 ✅ |

**几何也一并同构**（不只颜色）：
- 输入族圆角 `Dpi/192×12`（96 DPI＝6px）→ **4 逻辑像素**（`UiVisualStyle.CornerRadiusDevicePx`，与夜间同值）；
- 按钮圆角 `EffectiveCornerRadius` 日间不再"原样返回"，改为同样 `min(配置值, 8)` ⇒ 日间写 12/16/10 的按钮 6/8/5px → **4px**；
- 分段/独立按钮面日间由 `Input`（纯白）改为 `ButtonFace`（`RButton.OnPaint`）⇒ 日间只剩**一套**按钮面；
- 悬停色日间不再走"朝黑 16%"（纯白底上会一下压到 214），改为按钮面家族统一取 `ButtonFaceHover`。

**日间同构后仍然遗留的两项（本轮**未**纳入，如实登记）**：
1. ~~**选中态仍是两套做法**：夜间＝"强调色描边 + 淡底"（§4.3 做法 A），日间仍＝"实心强调色"（做法 B）。~~
   → **✅ 0.291 beta22 二次修订已解决**：用户追加口径「边框高亮 和性能模式浅色填充依旧没有」后，
   `RButton` 那两处 `&& UiVisualStyle.IsNightMode` 门控已删除 ⇒ 日间也走"描边 + 底"：
   非性能模式组＝**中性底 `ButtonFace` + 强调蓝描边**，性能模式五行＝**模式色 22% 浅填充 + 加深 30% 的模式色描边**，
   文字一律 `Text`。夜间口径不变（只描边、不做浅填充）。完整数值与对比度见 **§4.7**。
2. **`SetThemeButtonState`（「界面主题」日/夜两颗、快捷睡眠 AC/DC）仍是实心强调块**
   （普通 `Button` 的 `BackColor = Accent`，两主题都一样）：它们不属于 RButton 选中族，本轮未动，
   若要一并改成描边式需要换到 `RButton` 那一套（见 §4.7 末尾的提示）。



---

## 3. 层级关系（最重要的一节）

### 3.1 从深到浅的顺序（夜间，实测相对亮度递增）

```
INPUT 20/20/20 (0.0070)      ← 最暗：凹陷 / 可输入
  ↓ 1.081 对 Window
WINDOW 28/28/28 (0.0116)     ← 页面底
  ↓ 1.156
SURFACE 40/40/40 (0.0212)    ← 卡片
  ↓ 1.220
SURFACE_RAISED 54/54/54 (0.0369)
  ↓
TRACK 46/46/46 (0.0273)      ← 滑轨：夹在 Window 与 SurfaceRaised 之间，对 Window 1.255
  ↓
BUTTON_FACE 62/62/62 (0.0482) ← 按钮面（浮起）
  ↓ 1.169
BUTTON_FACE_HOVER 72/72/72 (0.0648)
  ↓ 1.246
BUTTON_FACE_SELECTED 86/86/86 (0.0931)  ← 选中：最浅
```

边色（不参与填充级差，只贴在某一层上）：`Border 56`(0.0395) → `BorderStrong 74`(0.0685) → `ButtonEdge 82`(0.0844)。

**两条硬规则**：
1. **`Input` 必须比 `Surface` 更暗**（凹陷语义）。夜间 1.250 ✅；日间 1.000 ❌（见 §2.4）。
2. **按钮面必须比它所在的卡片更浅**（浮起语义）。夜间 `ButtonFace/Surface` 1.378 ✅。
   日间 `ButtonFace/Surface` 1.158 ✅（但按钮之间的三档无级差 ❌）。

### 3.2 相邻层的 WCAG 目标区间

| 相邻关系 | 目标区间 | 理由 | 夜间实测 | 达标 | 日间实测 | 达标 |
|---|---|---|---|---|---|---|
| `Window` → `Surface` | **≥1.15 且 ≤1.35** | 下限＝大面积平色能"一眼分块"的经验阈；上限＝不要变成两种灰拼贴 | **1.156** | ✅（贴下限） | **1.167** | ✅ |
| `Surface` → `SurfaceRaised` | ≥1.15 且 ≤1.35 | 同容器层级，只在需要分带时用 | **1.220** | ✅ | 1.158 | ✅ |
| `Input` → `Surface` | **≥1.20**（越暗越明确） | 凹陷必须比卡片明显，不能靠边来提示"这里能输入" | **1.250** | ✅ | **1.237** | ✅ |
| `Surface` → `ButtonFace` | **≥1.30**（浅底上放宽到 ≥1.15） | 按钮面浮起要明确（用户明确抱怨过"我们是反的"）；**日间卡片＝纯白是上限，按钮只能靠"比卡片深一档 + 一圈硬边"表达浮起**，1.30 在浅色主题里不可达 | **1.378** | ✅ | 1.158 | ✅（放宽后） |
| `ButtonFace` → `ButtonFaceHover` | **≥1.10 且 ≤1.30** | 悬停要能看见，但不能变成"换了一颗按钮" | **1.169** | ✅ | **1.120** | ✅ |
| `ButtonFace` → `ButtonFaceSelected` | **≥1.20**（夜间另定 1.35~1.60） | 选中要与未选中明确分开；**日间方向相反（越深＝越强调），且受纯白天花板限制** | **1.457** | ✅ | **1.208** | ✅ |
| `ButtonEdge` → `ButtonFace` | **≥1.30** | 边要"硬"（用户抱怨过发虚）；ΔRGB ≥20 是经验值 | **1.369**（Δ20） | ✅ | **1.619** | ✅ |
| `Border` → `Window` | ≥1.40 | 普通边必须看得见 | 1.453 | ✅ | 1.276（对 `Surface` 1.488） | ✅（对卡片量） |

### 3.3 文字对比（AA 门槛 4.5；大字/次要文字 3.0）

| 底 | `Text` 夜 | `Text` 日 | `Muted` 夜 | `Muted` 日 |
|---|---|---|---|---|
| `Window` | 14.95 ✅ | 14.06 ✅（改前 15.00） | 6.36 ✅ | 5.59 ✅（改前 4.99） |
| `Surface` | 12.94 ✅ | 16.40 ✅ | 5.50 ✅ | 6.53 ✅（改前 5.46） |
| `SurfaceRaised` | 10.60 ✅ | 14.17 ✅ | 4.51 ✅（临界） | 5.64 ✅（改前 4.71） |
| `Input` | 16.17 ✅ | 13.27 ✅（改前 16.40） | 6.88 ✅ | 5.28 ✅（改前 5.46） |
| `ButtonFace` | 9.39 ✅ | 14.17 ✅ | **3.99 ❌**（夜间仍待修） | 5.64 ✅（改前 4.71） |
| `ButtonFaceHover` | 8.03 ✅ | 12.91 ✅（改前 14.17） | **3.41 ❌**（夜间仍待修） | 5.05 ✅（改前 4.71） |
| `ButtonFaceSelected` | 6.44 ✅ | 11.95 ✅（改前 14.17） | **2.74 ❌**（夜间仍待修） | 4.67 ✅（改前 4.71） |
| `Track` | 11.92 ✅ | 12.30 ✅ | 5.07 ✅ | **4.90 ✅**（改前 **4.09 ❌**，0.291 修好） |
| `Accent` | `Text` 2.30 ❌ → 必须用 `AccentText` 7.16 ✅ | `Text` 4.18 ❌ → 必须用 `AccentText` 4.76 ✅ | — | — |

**结论**【现状·不一致】：
- **夜间 `Muted` 不能画在 `ButtonFace` 及更浅的按钮面上**（3.99 / 3.41 / 2.74 全部低于 AA）。
  这正是"禁用态文字发灰看不见"的成因（禁用态现状 = 底色不变 + `Muted`）。
- **`Text` 绝不能画在 `Accent` 上**（2.30 / 4.18）。`Accent` 上只能用 `AccentText`。
- 日间 `Muted` 在 `Track` 上 4.09，属"次要文字勉强"，建议日间同构时把 `Track` 调亮（§2.4）。

---

## 4. 按钮规范

**范围**：性能模式行、显卡模式行、屏幕刷新率行、屏幕其它行、电池档位行、核显省电行、mini-chip 小胶囊、
二次按钮（主题/官方控制台等）。**不含**下拉框（§7）。

**几何现状**【现状】（`source\MechrevoLite.UI\RButton.cs`）：
- 边宽：**1 逻辑像素**，由唯一工厂 `UiVisualStyle.OutlinePen(control, color, 1f, 1f)` 给出
  ⇒ 100%→1、125%→1、150%→2、175%→2、200%→2 设备像素（**整数**、`MidpointAlignment.Inset`）。
  ⚠️ 2026-10-09（P0.5）之前这里是硬编码 `new Pen(color, 1f)`（恒定 1 设备像素、**不随 DPI 变粗**），
  且 DPI 取自绘制 DC 的 `Graphics.DpiX`（PerMonitorV2 下可能回 96）；现已统一为
  **`Control.DeviceDpi` + 整数设备像素**（`DrawCrispEdge` / `DrawSegmentBorder` / `DrawTopHighlight` 三处共用）。
  字段 `borderSize = 3` 只影响"内缩量/父色描边宽度" `num2 = round((Dpi/192 × 3 − 1)/2) × 2 + 1`：
  100% 与 125% DPI 下 = **1**，150% 及以上 = **3**（已实测 96/120/144/168/192/240 DPI）。
- 半径：`设备半径 = round(BorderRadius × Dpi/192)`；`EffectiveCornerRadius` 在夜间 `min(配置值, 8)`，**只收不放**，日间原样返回。
- **Region 外扩量（P1，2026-10-09 统一）**：日间与夜间**都是 2 设备像素**（`UiVisualStyle.RegionOutsetDevicePx`）——
  此前日间只有 1 ⇒ 圆角处 AA 羽化仍被二值裁剪啃掉一条、看着比夜间"方"。外扩出来的那一圈两主题都
  由 `ContextBackColor`（父链第一个实色容器底色）铺满 ⇒ 不可见。
- **PixelOffsetMode（P3，2026-10-09 统一）**：全仓**不设**（GDI+ 默认 `None`）。
  `HighQuality`/`Half` 会把采样网格整体偏移半个像素，与我们"整数坐标 + 整数设备像素边"的画法相冲
  （一条硬边被摊成两列各 50% ＝ 发虚）。此前 `HardwareOverlay` 显式设过 `HighQuality`，已删除。
- 行高（逻辑像素）：性能模式行 `D(34)`、显卡模式行 `D(34)`、屏幕行 `26 / 34 / 28 / 34 / 34`、perf 配置行 `D(26)`、核显省电行 `D(30)`。
- 独立按钮间距：`IndependentGapLogical = 6`（两侧各分一半，行首行尾不加缩进）。
- 半径硬约束：`min(半径, min(宽,高)/2)`，代码里已经夹紧 —— **规范要求：半径不得超过行高一半**（行高最小 26 ⇒ 上限 13）。

### 4.1 圆角：三个备选，请圈一个

单位说明：本文档用**逻辑像素**（100% DPI 下的物理像素）描述；代码里的 `BorderRadius` 单位是"缩放前单位"，
换算 = `设备半径 = round(BorderRadius × Dpi/192)`，即 **配置值 = 逻辑半径 × 2**。

| 备选 | 逻辑半径（100% DPI） | 代码 `BorderRadius` | 150% DPI 实际 | 观感描述 | 适用场景 | 现状对照 |
|---|---|---|---|---|---|---|
| **R4**（推荐） | **4 px** | `8` | 6 px | 方正、硬朗、像"按键"而不是"胶囊"；小控件上不会吃掉直角 | 现状夜间全部按钮与输入族 | 【现状】夜间＝4px（⑦ 起）；`ApplyIndependentRow`(10→8)、`ApplySegmentGroup`(16→8)、原 `12` 的都归到 4px |
| **R6** | **6 px** | `12` | 9 px | 比 R4 柔和一档，仍算"方"；小按钮（高 26）上已快到半高 | 想更"现代圆润"但仍要硬边 | 【现状】日间 RButton 走 `BorderRadius/2` ⇒ 12 单位＝6px；日间输入族也是 6px |
| **R8** | **8 px** | `16` | 12 px | 明显圆角，接近"药丸"的前一步 | 大按钮（≥32 高）、对话框按钮 | 【现状】改前 `ApplySegmentGroup` 用的就是 16 单位（8px）；首启引导窗曾如此 |

**⚠️【现状·不一致】**：因为 `EffectiveCornerRadius` **只收不放**，配置里原来写 `5` 的按钮
（`buttonSilent/Balanced/Turbo`、`buttonEco/Standard/Ultimate`、`buttonOverlay`、`buttonStopGPU`、
`button60Hz/120Hz`、`buttonMiniled/FHD`、`buttonPeripheral1~3`、`buttonHDRControl` 在设计器里都是 `5`）
若**没**被 `ApplyIndependentRow` 覆盖，夜间半径是 `round(0.5 × 5) = 3px`，**比目标 4px 还小**；
而写 `10/12/16` 的反而都归到 4px。**同一个界面里同时存在 3px 与 4px 两种半径**，这就是"看着有点不齐"的来源。
→ 规范落地时应把"按钮面家族"的半径**显式统一**成用户圈定的那一个值，而不是靠 `min()` 兜。

**DPI 规则**（必须照此实现）：`设备半径 = round(逻辑半径 × Dpi / 96)`，结果**必须取整**（不允许落在半像素上）。
100/125/150/175/200% 下 R4 → 4/5/6/7/8 px，R6 → 6/8/9/11/12 px，R8 → 8/10/12/14/16 px。

### 4.2 边框：六种状态

**硬规则：一个方向只允许一层边。** 选中态那圈彩边就是"这个按钮的边"，
按钮自身那圈普通边**必须被抑制**（`RButton.OnPaint` 里 `!outlineSelected` 那个门控就是干这个的，不许绕过）。

| 状态 | 线宽 | 颜色（夜 / 日） | 内缩 Inset？ | 现状 |
|---|---|---|---|---|
| **未选中**（分段/Single 行：性能模式、显卡模式、刷新率） | 1 设备 px | 夜 `ButtonEdge` 82 / 日 `Border` 201,213,227 | **是**（`DrawSegmentBorder` 恒 Inset，画在最外一行/列） | 【现状】 |
| **未选中**（独立/None 且显式给了 `BorderColor`：核显省电 OFF/ON/AUTO、MiniLED、HDR） | 1 设备 px | **该 `BorderColor` 原色**（如 `colorEco` `6,180,138`）｜日 同色但走 Center+内缩 1 | 夜 是 / 日 否（Center） | 【现状·不一致】夜间同一行里三颗按钮是绿/蓝/红三种边，见 §6.4 |
| **未选中**（独立/None 且**无** `BorderColor`、底色属按钮面家族：电池档位、mini-chip、二次按钮） | 1 设备 px | 夜 `ButtonEdge` 82 / 日 **无边框** | 夜 是 | 【现状·不一致】日间补不出边，与夜间不同 |
| **悬停** | **与未选中完全相同**（只换填充） | 同未选中 | 同未选中 | 【现状】边不动，只 `ButtonFace→ButtonFaceHover` |
| **按下** | **与悬停完全相同** | 同未选中 | 同未选中 | 【现状·不一致】**RButton 根本没有"按下"这个视觉**（`FlatAppearance.MouseDownBackColor` 被自绘覆盖，`OnPaint` 只看 `_hover`），按下＝悬停 |
| **选中**（夜间，`ActivatedOutline = true`） | **2 设备 px**（`max(2, 2×Dpi/96)`，150%→3、200%→4） | `SelectionOutlineColor(强调色)`＝强调色朝白提亮 12% | **是**（画在按钮自身圆角路径上；选中时抑制自身普通边） | 【现状】性能模式/显卡模式/刷新率/电池档位/核显省电行 |
| **选中**（夜间，`ActivatedOutline = false` —— **分段**分支：Single/First/Middle/Last） | 1 设备 px 自身边（`ButtonEdge`）＋ 内部内缩 `round(Dpi/192×8)`（100% 下 4px）、半径 4px 的**实心 `ActivatedColor` 药丸** | 药丸＝`ActivatedColor`（空则 `Accent`），药丸内缩、外圈边 Inset | `AccentText` | 【现状】SettingsForm 内已无按钮走这条（全部开了 outline），属**潜伏路径**（首启引导窗/自定义模式窗仍可能走） |
| **选中**（夜间，`ActivatedOutline = false` —— **独立**分支 None：MiniLED/FHD/HDR） | 1 设备 px 自身 `BorderColor`（FHD 为 `Transparent` ⇒ **连边都没有**）；**不额外画任何药丸、不画高光** | — | `AccentText`＝`8,18,31`（近黑）压在 `ButtonFace` 62 上 ⇒ **1.76** ❌ | 【现状·不一致】见 §6.4 第 4 条；不是"⑦残留"而是"从没人给它们开 outline" |
| **选中**（日间，全部） | 无描边 | **分段**分支：实心 `ActivatedColor` 药丸，内缩 `round(Dpi/192×8)`＝100% 下 4px，半径 `min(12,8)→4px`；**电池档位行**不走药丸，由 `SettingsForm` 直接改 `BackColor=Accent` + `ForeColor=Surface`；**None** 分支（MiniLED/FHD/HDR）同样不画药丸 | 药丸内缩 / 电池行无 | 【现状】 |
| **待生效（棕）** | **2 设备 px** | `SelectionOutlineColor(Warn)` = `235,188,81` | 是 | 【现状】仅显卡模式行 |
| **禁用** | **完全没有边** | — | — | 【现状·不一致】`if (Enabled && !Borderless)` 直接不画边；底色也不变暗，只有文字变 `Muted`（对比 3.99 ❌） |

**【提案】** 把上表里三处"不一致"补成规范：

| 缺口 | 【提案】 | 理由 |
|---|---|---|
| 按下无反馈 | 新增 `ButtonFacePressed`，取 `ButtonFaceSelected`（86）但**不加彩边**；或 `Mix(ButtonFaceHover, Black, 0.08)`；边仍与未选中同 | 按下必须在视觉上"更实"；直接复用最浅一档省一个令牌 |
| 禁用无区分 | 底 = `Window`（沉下去），文字 = `Muted`（对 `Window` **6.36 ✅**），无边框 | 用"凹陷"表达不可用，而不是把文字调灰到看不见 |
| 夜间/日间补边规则不同 | 统一为：**只要底色属于按钮面家族，未选中一律画 1 设备 px 的边**；边色 = `ButtonEdge`（夜）/`ButtonEdge`(日提案值) | 消除"日间某些按钮没有边"的不齐 |

### 4.3 填充（选中态两种做法，请选一个）

| 状态 | 底（夜 / 日） | 现状 |
|---|---|---|
| 未选中 | `ButtonFace` / 日 分段按钮取 `Input`（纯白）、普通按钮取 `ButtonFace` | 【现状·不一致】日间两种按钮面 |
| 悬停 | `ButtonFaceHover` / 日 同 `ButtonFace`（无反馈） | 【现状】 |
| 按下 | **无独立值**（＝悬停） | 【现状·不一致】见 §4.2 |
| 选中（做法 A：描边＋淡底） | `ModeActivatedFill(强调色)` = `Mix(ButtonFaceSelected, 强调色, 0.18)` | 【现状】夜间 |
| 选中（做法 B：实心强调色） | `ActivatedColor` 原色 | 【现状】日间全部按钮；夜间未开 `ActivatedOutline` 的按钮（分段分支走内缩药丸、独立分支什么都不画，见 §4.2） |
| 禁用 | ＝未选中（不变暗） | 【现状·不一致】 |

**两种选中做法，各给一个备选**：

| | **做法 A：强调色描边 + 淡底** 【现状·夜间】 | **做法 B：实心强调色** 【现状·日间】 |
|---|---|---|
| 几何 | 底＝`Mix(ButtonFaceSelected, 强调色, 0.18)`；边＝同色相提亮 12% 的 **2px** 描边；无第二层边 | 底＝`ActivatedColor` **实心**，无描边（或仅 1px 同色边） |
| 观感 | 立体、克制；一眼能看出"这颗被选中"，模式色由**边**承载；同一屏多选几颗也不炸眼 | 强、直白；一眼锁定，但大面积饱和色在深色页面上"跳"，多个模式并列时整行发花 |
| 文字 | `Text`（240,240,240）—— 因为底是深色淡调 | `AccentText` / 模式配对文字（随底亮度取深/浅） |
| 实测对比 | 文字/底：静音 5.50、平衡 5.34、静音狂暴 5.35、狂暴 6.49、自定义 4.85（**全部 ≥4.5 ✅**）；描边/自身底 1.67~3.22（**低，靠色相 + 对页面底的反差 2.41~6.23 读出来**） | 文字/底：夜 5.23~9.69 ✅、日 4.73~8.10 ✅ |
| 风险 | 描边对自身底的对比偏低（狂暴大红只 1.67），在低质量面板/强环境光下"边看不清" | 大面积饱和；夜间选中一颗就把卡片撕成两半；与"浮起"的浅色阶方向相反（实心饱和色比 `ButtonFaceSelected` **更暗或更艳**） |
| 适用范围 | 夜间（深底） | 日间（浅底） |

> **【提案·推荐】**：**夜间用 A、日间用 B**（＝维持现状分工），并把 A 的描边从"提亮 12%"改为
> **"提亮 12% + 保证描边/自身底 ≥ 2.5"**：即对大红这类底，描边改为朝白提亮 18%（避免"边糊在底里"）。
> 这是**唯一**需要改数值的地方，其余不动。

### 4.4 顶部高光（1px）

| 项 | 值 | 现状 |
|---|---|---|
| 颜色 | `ButtonFaceHighlight` = `Mix(ButtonFace, White, 0.16)` = `93,93,93` | 【现状】 |
| 线宽/位置 | **1 物理像素**（不随 DPI 加粗）；`y = Top + inset + 0.5`，`inset = 1`；左右端点各内缩一个圆角半径；不 Fill、不设 Region | 【现状】 |
| 绘制条件 | `IsNightMode && IsButtonFace(底色) && !activated` | 【现状】 |
| 对 `ButtonFace` 对比 | 1.624（可见但不刺眼） | 【现状】 |

**保留与否，两个备选**：

- **保留（推荐）**：它承担"浮起"的一半观感（另一半是 `ButtonFace` 比卡片浅）。
- **去掉**：更干净、更"平"，代价是按钮少一点立体，需要把 `ButtonFace/Surface` 的级差从 1.378 提到 ≥1.45 来补偿。

**风险与硬约束**（无论保留与否都成立）：
1. **不许在按钮之外形成"孤立亮线"**：只在底色确属按钮面家族时画（继承页面底的按钮不画）。
2. **不许越出按钮边界**：靠按钮自身的圆角 Region 裁剪兜底，且左右各内缩一个半径（碰不到圆角）。
3. 高光与最外那圈 1px 边之间必须有 1 行的间隔（边画在第 1 行，高光画在第 2 行）——不许重合。

### 4.5 待生效 = 棕

**语义**：控件**已被选中/已请求**，但**需要重启（或稍后）才生效**。
表现 = 与"选中"**同一套几何**，只换色相为棕（`Warn`）。不额外加图标、不加闪烁、不加第二层边。

| 项 | 值 | 对比度 |
|---|---|---|
| 语义色 | `Warn` `232,179,57` | 对 `Window` 8.88 |
| 描边（2px） | `SelectionOutlineColor(Warn)` = `235,188,81` | 对 `ButtonFace` 6.03 |
| 淡底 | `ModeActivatedFill(Warn)` = `112,103,81`（R>G>B，仍是暖棕） | `Text` 在其上 **4.92 ✅** |
| 文字 | `Text` `240,240,240` | — |
| 触发点 | 仅显卡模式行（`SettingsForm.VisualiseGPUMode` 在 `HasPendingGpuRestart()` 时下发 `ActivatedColor = Warn`） | 【现状】 |

**规则**：棕色的 `R > G > B` 且 `B` 最低这一条不许破坏（否则会读成"灰"或"黄绿"）；
棕**不与** `Accent` 蓝、`Danger` 红、`Warn` 之外的语义色混用。

### 4.6 模式色与"选中态派生色"精确值（可直接照抄）

> ⚠️ **本表已被 §4.7 取代**（下面这版推导是 ⑤ 时代的 `Mix(86,86,86, 0.18)`；0.291 beta22 起夜间是
> `Mix(ButtonFace 62, 模式色, 0.12)`、日间是 `Mix(ButtonFace 233,239,246, 模式色, 0.22)`，**以 §4.7 的实测表为准**）。
> 保留此表仅为记录"提亮 12% / 18% 之争"的来龙去脉。

推导：`淡底 = Mix(86,86,86 → 模式色, 0.18)`；`描边 = Mix(模式色 → 255,255,255, 0.12)`。

| 模式 | 模式色（＝`ActivatedColor`） | 描边（2px） | 淡底 | `Text`/淡底 | 描边/淡底 | 描边/`ButtonFace` |
|---|---|---|---|---|---|---|
| 静音（绿） | `52,168,106` | `76,178,124` | `80,101,90` | 5.50 ✅ | 2.38 | 4.06 |
| 平衡（蓝） | `77,163,255` | `98,174,255` | `84,100,116` | 5.34 ✅ | 2.61 | 4.59 |
| 静音狂暴（浅红） | `230,128,120` | `233,143,136` | `112,94,92` | 5.35 ✅ | 2.54 | 4.46 |
| 高能狂暴（大红） | `206,44,40` | `212,69,66` | `108,78,78` | 6.49 ✅ | **1.67 ⚠️** | **2.41 ⚠️** |
| 自定义（浅蓝） | `122,200,236` | `138,207,238` | `92,107,113` | 4.85 ✅ | 3.22 | 6.23 |
| 待生效（棕） | `232,179,57` | `235,188,81` | `112,103,81` | 4.92 ✅ | 3.16 | 6.03 |

> ⚠️ 高能狂暴（大红）那一行是本规范里**唯一**两处对比不足的地方（描边 1.67 / 对按钮面 2.41）。
> 见 §4.3 的推荐：把它单独提到朝白 18% ⇒ 描边变 `218,82,79`，描边/淡底 ≈ 1.94、对 `ButtonFace` ≈ 2.75，仍偏弱，
> 因此**也接受**"用模式色做描边、但把淡底比例从 18% 降到 12%"这条备选（描边对自身底可到 ≈2.2）。

### 4.7 夜间 vs 日间：**选中态的差异**（0.291 beta22 定稿）✅

> 用户 2026-10-09 追加口径：「**边框高亮 和性能模式浅色填充依旧没有**」（日间截图里仍是实心饱和蓝块）
> ＋ 此前的夜间口径：「在深色模式下 高亮选中就边框 **填充浅色很怪**」。
> 两句话把两个主题的选中态**明确分工**如下 —— 几何**完全同一套**，只有"底"的策略相反。

**共用几何（两主题一字不差）**：铺满整颗客户区（`FillPath` bounds = `RButton.ClientRectangle`）＋ 描边画在
**按钮自身的圆角路径**上、线宽 `max(2, round(2×Dpi/96))` **整数设备像素**、`PenAlignment.Inset` ⇒ 单层边、
抗锯齿平滑；选中时抑制按钮自身那圈普通边（`DrawSegmentBorder` / `BorderColor` 都不画）；
文字与底色在**所有铺底之后**绘制。代码落点：`RButton.DrawActivatedOutline`（`RButton.cs` 300-321），
门控 `outlineSelected = activated && activatedOutline`、`outlineSelectedNone = activated && activatedOutline`
（**0.291 起不再带 `&& UiVisualStyle.IsNightMode`** —— 那两处门控就是"日间永远走实心药丸"的成因）。

| 项 | **夜间**（用户：只描边，不要浅填充） | **日间**（用户：描边 + 浅色填充） |
|---|---|---|
| 非性能模式组（显卡行非待生效／刷新率行／电池三档／核显省电／MiniLED/FHD/HDR） | 底 = `NeutralActivatedFill` = `ButtonFace 62,62,62`（**与未选中同级、无浅填充**）；边 = `Accent 77,163,255` | 底 = **同一个 `ButtonFace 233,239,246`（中性底，与未选中同级）**；边 = **同一个 `Accent 31,111,224`** |
| 性能模式五行 | 底 = `ModeActivatedFill` = `Mix(ButtonFace 62 → 模式色, **0.12**)`（深底淡调，仅让色相可读，对比 1.03~1.15）；边 = `SelectionOutlineColor` = `Mix(模式色 → 白, **0.12**)` | 底 = `ModeActivatedFill` = `Mix(ButtonFace 233,239,246 → 模式色, **0.22**)`（**浅色填充**，对未选中面 1.12~1.42）；边 = `SelectionOutlineColor` = `Mix(模式色 → 黑, **0.30**)`（加深以抵消浅底，见下） |
| 「待生效＝棕」（显卡行） | 底 = `Mix(ButtonFace 62 → Warn, 0.12)` = `71,66,56`（R>G>B 仍是棕）；边 = `Mix(Warn → 白, 0.12)` = `153,114,44` | 底 = `Mix(ButtonFace 233,239,246 → Warn, 0.22)` = `212,207,195`（**R>G>B，B 最低**，仍是棕）；边 = `Mix(Warn → 黑, 0.30)` = `97,66,10` |
| 选中态文字 | `Text 240,240,240` | `Text 22,32,46` |

**日间实测（脚本复算，WCAG 公式与本项目 `ContrastRatio()` 相同）**：

| 组 / 模式 | 底 | 边 | `Text`/底 | 边/底 | 底/未选中面 | 边/未选中面 |
|---|---|---|---|---|---|---|
| 非性能模式组（中性） | `233,239,246` | `31,111,224` | **14.17** ✅ | **4.12** ✅ | 1.000（只有边） | 4.12 |
| 静音（绿） | `187,215,209` | `17,92,53` | 10.74 ✅ | 5.28 ✅ | 1.319 | 6.96 |
| 平衡（蓝） | `189,211,241` | `22,78,157` | 10.74 ✅ | 5.27 ✅ | 1.319 | 6.95 |
| 静音狂暴（浅红） | `231,212,215` | `158,81,74` | 11.56 ✅ | 3.97 ✅ | 1.226 | 4.86 |
| 高能狂暴（大红） | `224,195,200` | `134,27,27` | 10.01 ✅ | 5.86 ✅ | 1.416 | 8.30 |
| 自定义（浅蓝） | `208,229,243` | `83,134,162` | 12.64 ✅ | **3.05** ✅（最低） | 1.121 | 3.42 |
| 待生效（棕） | `212,207,195` | `97,66,10` | 10.56 ✅ | 5.90 ✅ | 1.343 | 7.92 |

**夜间实测（不变，供对照）**：中性组 `Text`/底 9.39、边/底 4.07；五个模式色底 `57,70,64`~`82,68,67`（对未选中面 1.03~1.15）、
边 `52,147,97`~`229,133,124`（边/底 2.12~4.66）。

**为什么日间的边要"加深 30%"而不是像夜间那样"提亮 12%"**：日间模式色里的**浅红 `226,116,106`／浅蓝 `118,192,232`**
本身就接近浅底，用原色当描边时"边/底"只有 **1.5~2.2**（糊在一起）；加深 30% 后升到 **3.05~5.90**（全部 ≥3:1，
满足 WCAG 1.4.11 对非文字 UI 边界的下限），且色相不变（浅红→红、浅蓝→蓝、棕→深棕 `R>G>B`）。
夜间方向相反（浅底→深底），所以是"提亮 12%"。

> ⚠️ **仍未纳入**：`SettingsForm.SetThemeButtonState`（「界面主题」日/夜两颗、快捷睡眠 AC/DC）走的是普通 `Button`
> 的 `BackColor = Accent`，**两个主题都是实心强调块**（不属于本节的 RButton 选中族）。本轮按"只改用户点名的行"处理，
> 未改动；若要一并改成"描边式"，需要把这两处从 `SetThemeButtonState` 换到 RButton 那一套。

---

## 5. 状态矩阵（一张表）

全部用**令牌名**表示；`—` 表示"不画"。日间列只在 §2.4 被采纳后才有意义，否则日间仍走现状。

### 5.1 按钮（分段/独立，"按钮面家族"）

| 控件状态 | 底 | 边 | 文字 |
|---|---|---|---|
| 默认 | `ButtonFace` | `ButtonEdge`（1 设备 px，Inset） | `Text` |
| 悬停 | `ButtonFaceHover` | `ButtonEdge` | `Text` |
| 按下 | 【提案】`ButtonFacePressed`（＝`ButtonFaceSelected`）；现状＝悬停 | `ButtonEdge` | `Text` |
| 选中（做法 A，夜） | `ModeActivatedFill(强调色)` | `SelectionOutlineColor(强调色)`（2 设备 px，Inset） | `Text` |
| 选中（做法 B，日） | `ActivatedColor`（实心） | — | `AccentText` / 模式配对文字 |
| 待生效（棕） | `ModeActivatedFill(Warn)` | `SelectionOutlineColor(Warn)`（2 设备 px，Inset） | `Text` |
| 禁用 | 现状 `ButtonFace`；【提案】`Window` | 现状 —；【提案】— | 现状 `Muted`（3.99 ❌）；【提案】`Muted`（6.36 ✅） |

### 5.2 输入族（输入框 / 数字框 / 下拉闭合态）

| 控件状态 | 底 | 边 | 文字 |
|---|---|---|---|
| 默认 | `Input` | `BorderStrong`（夜 2 设备 px，Inset；日 1px，Center） | `Text` |
| 悬停 | `Input` | `BorderStrong` | `Text` |
| 聚焦 | `Input` | 【提案】`Accent`（1 设备 px） | `Text` |
| 禁用 | `Input` | 【提案】`Border` | `Muted` |
| 展开列表·未选中行 | `Input` | — | `Text` |
| 展开列表·选中行 | `ButtonFaceSelected` | — | `Text` |
| 展开列表·不可用行 | `Input` | — | `Muted` |

### 5.3 开关 / 滑轨（`Track` 家族）

**现状**（`RCheckBox.DrawSwitch` / `RSlider`）：滑轨底与"已走过部分"是**两层**，不是一个底。

| 控件 | 滑轨底（未走过部分） | 已走过部分 | 圆钮 | 边框 | 文字 |
|---|---|---|---|---|---|
| `RCheckBox` 开 | `Track`（夜）/ `Input`（日），或调用方显式给的 `TrackColor` | `Accent` | `白色`（硬编码 `KnobOnColor`） | **不画**（历史上那圈淡框线已被移除） | 标签 `Text`；状态字 "ON" 用 `Accent` |
| `RCheckBox` 关 | 同上 | 不画（宽度 0） | `Muted` | **不画** | 标签 `Text`；状态字 "OFF" 用 `Muted` |
| `RCheckBox` 禁用 | 同上 | `Muted` | `Surface`（开）/ `Muted`（关） | **不画** | `Muted` |
| `RCheckBox` 聚焦 | — | — | — | 【现状】`Accent` 1px 圆角矩形（`RoundedRect(1,1,W-3,H-3)`，半径 `min(6×scale, (H-1)/2)`） | — |
| `RSlider` | `Track` | `Accent`（禁用 `Muted`） | `ThumbFill`（禁用 `Surface`） | 【现状】`Accent` 描边 | — |

> 【现状·不一致】`RCheckBox` / `RSlider` 的"已走过部分"用 `Accent` 实心，属于 §4.3 里的**做法 B**，
> 与夜间按钮的**做法 A** 不是同一套语言。若第 2 题选"分工（夜 A 日 B）"，建议**开关/滑轨保持做法 B**
> （它们本来就小、面积不大，实心色在这里是"进度"语义而不是"选中"语义）。

### 5.4 卡片 / 容器

| 状态 | 底 | 边 | 文字 |
|---|---|---|---|
| 页面 | `Window` | — | `Text` / `Muted` |
| 卡片 | `Surface` | 【现状】无边框（`BufferedPanel` 卡片边框未恢复） | `Text` |
| 抬起带 | `SurfaceRaised` | — | `Text` |

---

## 6. 一致性规则

### 6.1 三个家族 + 判断标准

| 家族 | 令牌 | 现有成员 | 判断标准（新增控件时按顺序问） |
|---|---|---|---|
| **按钮面家族** | `ButtonFace` → `ButtonFaceHover` → `ButtonFaceSelected`（＋`ButtonEdge` 作边） | `RButton` 分段/独立行（性能/显卡/刷新率/电池档位/核显省电）、`ApplySecondaryButton`（主题按钮、官方控制台按钮等）、`mini-chip`、`RForm.buttonMain`（`ControlHelper.AdjustControls` 下发给**所有**非 `Secondary` 的 `RButton`） | ① **可点击** 且 ② **按下/悬停要有反馈** 且 ③ 它是"浮"在卡片上的？→ 是 ⇒ 按钮面 |
| **输入家族** | `Input` ＋ `BorderStrong`（夜 2px / 日 1px） | `RTextBox`、`RNumericUpDown`、`RComboBox`、`RComboDisplay` | ① **能输入或容纳可变文字** 且 ② 语义是"凹进去、可编辑"？→ 是 ⇒ 输入家族 |
| **滑轨家族** | `Track` ＋ `Accent`/`Muted` | `RCheckBox` 关闭态滑轨、`RSlider` 滑槽 | 它表示"一个连续量的位置"？→ 是 ⇒ 滑轨家族 |
| **卡片/容器** | `Window`（页面底）/ `Surface`（卡片）/ `SurfaceRaised`（抬起带） | 窗体与行的底 = `Window`；`BufferedPanel` 卡片 = `Surface`；分组带/表头 = `SurfaceRaised` | 它只是**把内容分组**、本身不可点也不可输入？→ 是 ⇒ 容器（按嵌套层级取 Window/Surface/SurfaceRaised） |
| **语义色** | `Accent`/`AccentText`、`Ok`、`Warn`、`Danger`、`AccentBlue`、五个模式色 | 主操作、开关开、待生效、错误、模式高亮 | 它的颜色要表达**状态含义**（成功/警告/危险/某种模式）？→ 是 ⇒ 语义色（**且必须成对使用**：`Accent` 配 `AccentText`，模式色配它自己的 `Foreground`） |

**反例（不许这么选）**：
- ❌ 用 `Input`（凹陷）当按钮底 —— 语义相反（用户在 ⑤ 之前看到的"按钮比背景还暗"就是这个）。
- ❌ 给卡片画一圈 `Border` 来"分层" —— 层次靠底色分级，不靠画框（§1 第 1 条）。
- ❌ 用 `Muted` 当按钮底再配 `AccentText`（现存 `solid-chip`）—— 能过对比度，但语义上"次要文字色"不该当底；
  若确认要保留这个 chip，应改为显式令牌（如 `ChipSolid`）。

### 6.2 两条上色路径（实现时必须知道）

| 路径 | 入口 | 行为 |
|---|---|---|
| ① `UiVisualStyle.ApplyTree` | `ApplyWindow` / `ApplySection` / `ApplyRow` | 按控件类型下发：`RButton` 未打 tag 的 → `ApplySecondaryButton`（`ButtonFace` + `Border` 1px）；`mini-chip`/`solid-chip`/`footer-ghost` 各自一套；`TextBoxBase`/`NumericUpDown` → `Input`；`ComboBox` → `StyleComboBox` |
| ② `ControlHelper.AdjustControls` | `RForm.InitTheme`（**在 ApplyTree 之前**跑） | **无条件**把所有 `RButton` 的 `BackColor` 重写成 `RForm.buttonMain`（＝`ButtonFace`）、`ForeColor` 重写成 `Text`、`FlatAppearance.BorderColor` 重写成 `Border`；`Secondary=true` 的用 `buttonSecond`（＝`Surface`） |

**⚠️ 结论（必须遵守）**：任何"按行/按控件不同的底色或边色"，**不能只在构造时赋值一次** ——
要么在主题切换之后重新下发（现有做法：`ApplyThemeMode` 里重刷），要么用 `RButton.BorderColor`
这个**不被 ControlHelper 触碰**的属性承载语义边色。

### 6.3 语义边色 vs 中性边色

`RButton.BorderColor` 有两个语义（同一属性、两种用法）：

| 用法 | 谁在用 | 规范判定 |
|---|---|---|
| **中性硬边**（＝"这个按钮的轮廓"） | 写 `Border` 值的（`mini-chip`）、或留空让夜间补 `ButtonEdge` 的 | ✅ 符合规范 |
| **语义边色**（＝"这个按钮代表哪种模式"） | `buttonSilent=colorEco`、`buttonBalanced=colorStandard`、`buttonTurbo=colorTurbo`、`button60Hz/120Hz/ScreenAuto=colorGray`、`buttonMiniled/Ultimate/HDR=colorTurbo`、`buttonSilentTurbo=colorTurbo`、`buttonCustomMode=colorCustom(255,128,0)` | ⚠️ 见 §6.4 |

### 6.4 【现状·不一致】清单（后续实现要修，供用户决定）

| # | 现象 | 代码依据 | 实测影响 |
|---|---|---|---|
| 1 | **同一行里未选中按钮的边是三种不同颜色**（核显省电 OFF 绿 / ON 蓝 / AUTO 红；`RButton` 的 `None` 分支直接把 `BorderColor` 当边色，`DrawCrispEdge` 只把"恰好等于 `Border` 的"换成 `ButtonEdge`） | `RButton.cs` 344、460-474；`SettingsForm.cs` 4280/4300/4315 | 未选中态视觉噪音；与"模式色只在选中态出现"的原则冲突 |
| 2 | **分段/独立两条分支的边色规则不同**：`Single/First/Middle/Last`（性能/显卡/刷新率行）夜间**无条件**用 `ButtonEdge`（`BorderColor` 被完全忽略）；`None`（核显省电/MiniLED/HDR/FHD）用 `BorderColor` 原色 | `RButton.cs` 562 vs 460-474 | 同一屏里"有彩边的按钮"和"灰边的按钮"并存，规则不可预测 |
| 3 | **`buttonCustomMode` 未选中边是橙色 `255,128,0`，选中却是浅蓝 `122,200,236`** | `SettingsForm.cs` 3559 vs 912 | 同一个按钮上两种色相，语义自相矛盾 |
| 4 | **MiniLED / FHD / HDR 三个按钮的"选中"在夜间是死的**：`ActivatedOutline` 从未设置 ⇒ `outlineSelected=false` ⇒ 走 `activated && !outlineSelected` 分支取 `AccentText`（`8,18,31`）当文字，而底仍是 `ButtonFace`（62） | `RButton.cs` 506、533-537；`SettingsForm.cs` 4925 是唯一给电池行开 outline 的地方 | **`AccentText`/`ButtonFace` = 1.76（近黑字压在 62 灰上）**，几乎看不见；FHD 连边都没有（`BorderColor=Transparent`） |
| 5 | **日间同类问题更极端**：日间 `ActivatedOutline` 全部门控失效 ⇒ `AccentText=White` 压在日间 `ButtonFace`（0.291 起 `233,239,246`）上 | 同上 | **✅ 0.291 beta22 已修**：`RButton` 里那两处 `&& UiVisualStyle.IsNightMode` 门控已删除 ⇒ 日间也走"描边 + 底"（中性组＝中性底、性能模式行＝模式色浅填充），文字一律 `Text`（对日间底 10.0~14.2 ✅）。同时给"经典刷新率三颗"（自动/60/120）补了 `UseOutlineSelection`（此前它们连夜间都是近黑字压 62 灰＝1.76）。详见 §4.7 |
| 6 | **禁用态**：不画边、底不变亮/变暗，只有文字变 `Muted` | `RButton.cs` 461、506 | 夜间 `Muted`/`ButtonFace` = **3.99 < 4.5**，禁用文字"糊" |
| 7 | **按下态等于悬停态**（`RButton` 自绘不读 `MouseDownBackColor`） | `RButton.cs` 436（只看 `_hover`） | 按下没有任何确认感 |
| 8 | **半径不齐**：`EffectiveCornerRadius` 只收不放 ⇒ 配置 5 的按钮夜间 3px、配置 10/12/16 的都 4px | `RButton.cs` 429；`SettingsForm.cs` 9092 等 | 见 §4.1 |
| 9 | ~~**日间 `Input`＝`Surface`＝纯白（1.000）**，输入框与卡片同色；`ButtonFace/Hover/Selected` 三者同值~~ | `UiVisualStyle.cs` `DayPalette` | **✅ 0.291 beta22 已修**：`Input` `224,232,241`（对卡片 1.237）、`Hover` `218,227,238`（1.120）、`Selected` `209,219,233`（1.208），另 `Window↔Surface` 1.094→1.167、`BorderStrong`/`ButtonEdge` 各自独立（1.810 / 1.619）；分段按钮面改取 `ButtonFace`（不再两套按钮面）。详见 §2.4 |
| 10 | **`solid-chip` 用 `Muted`（次要文字色）当底色**再配 `AccentText` | `UiVisualStyle.cs` 928-929 | 语义错位（值本身能过对比：7.02 / 5.46） |
| 11 | ~~**代码注释与实值不符**：`RComboBox.cs` 头注释写"`Input` 13/13/13、`BorderStrong` 92/92/92、选中行 `ButtonFaceSelected` 84/84/84"~~ | `RComboBox.cs` 头注释 | **✅ 0.291 beta22 已修**：头注释随本轮下拉重写改为"...与 `UiVisualStyle` 令牌同名同值"，不再写死数值 |

---

## 7. 下拉框（**0.291 beta22 起由我们自己做 —— 已完成**）✅

> 历史：更早一轮用户说「**下拉框自己做** 其他可以继续打磨」，本节当时只做交接。
> 2026-10-09 用户改口：「**下拉框好像没改过来 还是旧版的**」⇒ 本节从"交接说明"升级为**已落地的实现说明**。

### 7.1 当前实现（供接手时定位）

| 项 | 现状 |
|---|---|
| 控件 | `source\MechrevoLite.UI\RComboBox.cs`（`public class RComboBox : ComboBox`）；另有一个只读展示型 `RComboDisplay.cs`，但**全仓没有任何实例化点**，改它看不到效果 |
| 原生 `ComboBox` | **✅ 0.291 beta22 起全仓为 0 处**：`CalibrationStartupForm.cs` 的 `_customIccCombo` 已由原生 `ComboBox` 改为 `MechrevoLite.UI.RComboBox`，构造后**显式**调一次唯一入口 `UiVisualStyle.StyleComboBox`。<br>该窗体是 `sealed class ... : Form`（不走 `RForm.InitTheme` / `ApplyTree`），所以这一句是**必须的**（否则它仍然是"系统绘制按钮格 + 浅色展开列表"）；`RComboBox` 首绘还有一次 `StyleComboBoxIfStale` 自愈。<br>全仓检索 `new ComboBox(` ⇒ **0 处**（其余全部是 `RComboBox`）；`SettingsForm` 内 6 处实例化（`calibCombo` / `_displayEffectCombo` / `lcPumpCombo` / `lcFanCombo` / `comboKeyboard` / perf 配置行）本来就是 `RComboBox` |
| 唯一主题入口 | `UiVisualStyle.StyleComboBox(ComboBox)`。调用点两处：`ApplyTree`（`ControlHelper.Adjust` 之后）与 `RComboBox.PaintFlat` 里的首绘自愈 `StyleComboBoxIfStale`（用 `_themedControls` 里的哨兵串 `"flat-combo"` 保证只做一次） |
| 下发的值 | `RComboBox.BorderColor = BorderStrong`、`ButtonColor = Input`、`ArrowColor = Muted`；`BackColor = Input`、`ForeColor = Text`；`FlatStyle = Flat` |
| 绘制路径 | `WndProc` 拦 `WM_PAINT`（`DropDownStyle != Simple`）→ `PaintFlat`：① 先用 `ContextBackColor(this)` 把客户区填成"父容器实际的底色"（防假边）→ ② `DefWndProc` 让系统把文字画出来 → ③ 再整块自绘覆盖。**0.291 起夜间/日间共用同一段 `PaintSplit`**（改前是"夜 `PaintNightSplit` + 日 else 分支"两套画法） |
| 分体式（夜/日同构） | 输入区＝整块外圆角（半径 = `UiVisualStyle.CornerRadiusDevicePx` = **4 逻辑像素**，日间 0.291 起同为 4px），底 = `BackColor`（`Input`：夜 `20/20/20`、日 `224,232,241`）；箭头格宽 = `ArrowCellLogical = 26` 逻辑像素（DPI 缩放，且不超过客户区一半），**右角圆、左角方**。<br>⚠️ **0.291 的关键改动：箭头格静止底＝与输入区同底**（改前是 `ButtonFace 62` 画在 `Input 20` 上、Δ42 ⇒ 用户说的"老式下拉按钮"那一格的来源）；只有**悬停** `ComboCellHover`（夜 `Mix(Input→白,0.12)=48`／日 `Mix(Input→BorderStrong,0.30)`）与**展开中** `ComboCellActive`（夜 `ButtonFace 62`／日 `Mix(Input→BorderStrong,0.45)`）才变一档（对输入区 1.12~1.36）。<br>1px 竖分隔线画在 `cell.Left + 0.5`（整数像素列、纯色硬线），颜色 = `ComboSeparator`＝`Mix(Input, BorderStrong, 夜 0.42／日 0.28)`（夜 `43,43,43`、日 `204,215,228` ⇒ 远淡于改前的 `BorderStrong 74`）；整块 1px 描边 `PenAlignment.Inset`（夜 `BorderStrong 74`／日 `158,175,196`） |
| 箭头 | `DrawChevron`：两段细线（`ChevronHalfWidth=4`、`ChevronHalfHeight=2` 逻辑像素），线宽 `max(1, min(2, round(scale)))`，半像素对齐 + `AntiAlias`；**不是实心三角** |
| 展开列表 | 给下拉窗口下发 `SetWindowTheme(handle, IsNightMode ? "DarkMode_CFD" : "CFD")`；`WM_CTLCOLORLISTBOX` / `WM_CTLCOLOREDIT` 返回 `CreateSolidBrush(Input)` 的刷子（控件自管 `DeleteObject`）；`OnDrawItem` 整行 `FillRectangle`（选中/悬停行 = `ButtonFaceSelected`：夜 `86,86,86`、日 `209,219,233`；未选中行 = `BackColor` = `Input`），文字一律 `ForeColor`（= `Text`，夜浅日深，对两种底都 ≥4.5 AA），左内缩 8 逻辑像素 |
| 日间 | **0.291 起与夜间同构**：同一段 `PaintSplit`、同一套令牌（只是取日间值：`Input 224,232,241`、`BorderStrong 158,175,196`）、同样 4px 圆角、同样 `Inset` 描边（改前是 `Center` + 6px 圆角）、箭头格同样"与输入区同底 + 极淡分隔线"、悬停/展开同样有反馈（改前日间那四个重写方法直接被 `IsNightMode` 门控掉、完全不重绘）。注：`RComboDisplay`（只读展示型、**全仓无实例化点**）日间仍是实心三角，本轮未动（改了也看不到效果） |

### 7.2 已知问题（历史踩坑记录，接手前请完整读完）

| # | 问题 | 成因（已定位） | 现在的状态 |
|---|---|---|---|
| 1 | **白底下拉** | 曾经有**两条互不相通**的上色路径：`ApplyTree` 与 `ControlHelper.Adjust`（后者在 `InitTheme` 里先跑，把底/边/箭头改写回旧的 `RForm.buttonMain/borderMain/foreMain`），谁后跑谁生效 ⇒ 同一个界面里"有的下拉深色、有的白底" | 已收敛到 `StyleComboBox` 单一入口 |
| 2 | **黑底 + 白色方块 + 黑三角** | 原生 `ComboBox` 的**下拉按钮格**由 CBT 主题引擎绘制，`BackColor` / `FlatStyle` 都管不到它 | 已用"先 `base.WndProc` 铺底、再整块重画一遍覆盖"解决 |
| 3 | **文字被铺底盖住（"文字消失了"）** | `PaintFlat` 先让 `DefWndProc` 把闭合态文字画出来，随后又 `FillPath`「圆角整块底 + 输入区底」把文字整片抹掉 | 已修：**文字补画在最后一次铺底之后**（`UseCustomTextPadding && DrawMode != Normal` 时才补，避免 `Normal` 模式重复绘制） |
| 4 | **Region 环形洞 ⇒ 文字看不见 + 点不动** | `SetRoundedRegion` 曾经把 Region 建成 `new Region(widen 后的路径)`；`GraphicsPath.Widen` 返回的是**环形带**（外轮廓＋由原路径构成的内轮廓），中心是洞。`SetWindowRgn(环形)` 之后控件只剩一圈边是实心的 | 已修：`Region = new Region(path)`（填充实体）**∪** `new Region(widen)`（外扩 1 设备像素保抗锯齿） |
| 5 | **圆角锯齿** | `Control.Region` 交给 `SetWindowRgn` 的是**二值掩码**，抗锯齿画出的那条约 1 设备像素宽羽化边被整条切掉 | 已修：Region 向外让 1 个设备像素（`outset = UiDpi.Paint/96`） |
| 6 | **主题切换后动态创建的下拉漏色**（灯效组的"单色"等） | 这些控件在 `ApplyTree` 之后才被创建，从来没被上过色 | 已修：`RComboBox` 首绘时调 `StyleComboBoxIfStale` 自愈一次 |
| 7 | **`NativeHeight` 与 `ItemHeight` 标定** | `CalibrateItemHeight` 在 `OnHandleCreated` / `OnDpiChangedAfterParent` / `OnFontChanged` 三处触发，`ItemHeight = max(字体高+2, 目标高 - (PreferredHeight - ItemHeight))` | 现存逻辑；改高度相关代码时注意会连带影响展开列表行高 |
| 8 | 代码注释与实值不符 | 头注释写 `Input 13/13/13`、`BorderStrong 92/92/92`、选中行 `ButtonFaceSelected 84/84/84`，实值 `20/20/20`、`74/74/74`、`86/86/86` | 未修（见 §6.4 第 11 条） |
| 9 | **`CalibrationStartupForm` 的 `_customIccCombo` 没被统一** | 它是原生 `ComboBox`，且所在窗体 `: Form`（非 `RForm`）、不调用 `ApplyWindow/ApplySection/ApplyRow` ⇒ 不在 `ApplyTree` 覆盖范围内，也享受不到 `RComboBox` 的整块自绘 | **✅ 0.291 beta22 已修**：控件改为 `RComboBox` + 构造后显式 `UiVisualStyle.StyleComboBox(_customIccCombo)`（该窗体仍不是 `RForm`，所以那句显式调用是它进入统一上色路径的唯一方式）。选项 / 事件 / `DropDownWidth` / 尺寸 / `SelectedIndex` 一个都没动 |

### 7.3 动它时的注意事项（**硬性**）

1. **铺底必须在画文字之前**。任何"后来又盖了一遍"的 `FillPath`/`FillRectangle` 都会把文字抹掉（问题 3 就是这个）。
   若确实需要在之后覆盖，必须在覆盖后**重新** `TextRenderer.DrawText`。
2. **不要用 `Widen` 的单个结果做 `Region`**。它是环形带，中心是洞 ⇒ 文字不可见 + 命中区域被掏空。
   要外扩就 `填充路径 ∪ widen`。
3. **不要取消 `Region`**。子窗口不受父窗口裁剪，取消后圆角那几颗像素会露出自己的方角底色 ⇒
   就是折腾很久的"整块异色矩形/假边"。
4. **圆角外那几颗像素必须填"父容器实际画出来的底色"**（`UiVisualStyle.ContextBackColor(this)`），
   自绘控件填自己的底色会露出一圈"假边"。
5. **1px 线要落在整数像素列/行上**（`x.5` 配 `Inset`），跨半像素会被抗锯齿摊成"两列各 50%"的灰边（发虚）。
6. **只改外观**：不许碰 `Items` / `SelectedIndex` / `DisplayMember` / 事件 / `DropDownWidth` / `Bounds` / `Margin`。
   `WM_PAINT` 拦截与 `OnMouseMove/OnMouseLeave/OnDropDown/OnDropDownClosed` 四个重写都只应调 `base` + `Invalidate`。
7. **`Dispose` 里要 `DeleteObject` 那个列表刷子**（`_listBrush`），否则每次换主题泄漏一个 GDI 对象。
8. **`RComboDisplay` 没有实例化点** —— 改它看不到任何效果，别把时间花在那里。
9. **别以为"下拉都已经统一了"**：`CalibrationStartupForm.cs:100` 那个原生 `ComboBox` 不在任何主题路径上
   （窗体不是 `RForm`、也没有 `ApplyWindow`），要统一它得先解决"这个窗体怎么进主题树"的问题。

### 7.4 【参考·只记录不改】老版本里的自定义下拉画法（2026-10-09 UI 反馈② 检索结果）

> 用户原话：「**之前老版本里面好像有自定义样式的 可以参考一下**」。
> 本轮**只检索 + 记录，一个字节都没改** `RComboBox` 的实现（下拉样式由用户自理）。
> 检索范围：`_upstream\0.290.6\decomp\`、`backup\`、`交付\`、`diagnostics\`、`installer-src\`。

| 版本 / 位置 | 画法要点 | 与现在 `RComboBox` 的差异 | 能否直接参考 |
|---|---|---|---|
| **原作者反编译树** `_upstream\0.290.6\decomp\MechrevoLite.UI\RComboBox.cs`（全文 340 行；核心 `WndProc` 在 **240–321**） | **整块自绘 + 裁剪区**：`WM_PAINT(15)` 且非 `Simple` ⇒ ①`FillRectangle(Parent.BackColor)` 铺圆角外的底色；②`FillRoundedRectangle(bounds, radius, radius)` 铺**整块圆角底**（半径 `num = max(1, round(4×Dpi/192))`，96DPI 下 2px）；③`CreateRectRgn(输入区)` + `SelectClipRgn` **只让系统在输入区里画文字**；④`DefWndProc`；⑤恢复裁剪区后画 **实心三角箭头** `FillPolygon(3 点, 宽 7×高 4)`；⑥`ControlHelper.DrawGradientBorder(g, bounds, BorderColor, radius)` 画**渐变边**（顶亮→底暗，见 `source\ControlHelper.cs:157`） | 现在是**分体式**：输入区（`Input 20/20/20`）+ 1px 竖分隔线 + **26 逻辑像素独立箭头格**（`ButtonFace 62`，悬停/展开换色），箭头是两段细 chevron，边是 1px `BorderStrong` **纯色 + PenAlignment.Inset**；列表另走 `DarkMode_CFD` + `WM_CTLCOLORLISTBOX` | **可参考思路、不建议照搬**：①它把"箭头格"和"输入区"当**一整块**画（靠 `ButtonColor` 铺底 + 三角），拿到现在会与"分体式 + 独立箭头格"打架；②它的箭头是**实心三角**，正是用户上一轮明确要去掉的"白方块 + 黑三角"观感来源；③渐变边在深色中性层下会发灰，与"1px 硬边"的既定方向相反。**唯一值得借鉴**：`SelectClipRgn` 把系统文字限制在输入区内这一手（我们现在是靠 `textRect` 的右边界止于箭头格左沿达到同样效果） |
| **仓库早期备份** `backup\pkgs\RComboBox_20261005_pre_merge_step6_BASELINE-beta21.cs`（`WndProc` 在 **233–314**） | 与上一行**逐字节同源**（`WndProc` 内容一致） | 同上 | 同上（这只是"0.290.6 之前 LM 自己那一版"，不是更早的另一种设计） |
| **已交付给作者的源码包** `交付\LM-源码-提交作者-20261006\source\MechrevoLite.UI\RComboBox.cs` | SHA256 `4DAEA7AF…` ＝ 与 `backup\pkgs\RComboBox_20261006_pre_egroup.cs` **完全相同**，`WndProc` 在 **241–320** | 同上 | 同上 |
| **0.290.5 及更早** | **没有**更早的第三套自定义画法：`backup\pkgs\RComboBox_20261005_pre_merge_step6_BASELINE-beta21.cs` 已是能找到的最老一份，内容与 0.290.6 反编译树一致 | — | — |
| `diagnostics\` / `installer-src\` | 无下拉样式相关产物（`diagnostics` 只有历史截图/日志，`installer-src` 只有 GCU 载荷） | — | — |

**一句话结论**：老版本的"自定义样式"＝**整块圆角底 + 实心三角 + 渐变边 + `SelectClipRgn` 限字**（`_upstream\0.290.6\decomp\MechrevoLite.UI\RComboBox.cs:240-321`）；它是我们**现在这一版的前身**，而不是另一种可横向参考的第三方设计。若想"参考"，有意义的只有两条：**(a) 用 `SelectClipRgn` 而不是自算 `textRect` 来限制文字区；(b) 圆角半径与输入区/箭头格分开算**。其余（实心三角、渐变边）与既定的"细 chevron + 1px 硬边"方向相反，不建议回退。

---

## 7.5 自绘层地基：**不让系统再画一遍**（2026-10-09 P0，**新增硬性约定**）

> 这一节是"一整类反复出现的问题"的共因与唯一入口。历史上那些"淡线 / 异色矩形 / 白块 / 边先粗后细 /
> 突然变方角"多半不是某个颜色取错，而是**系统在我们之外又画了一遍**。

**唯一入口**：`UiVisualStyle.HandleOpaqueChrome(Control control, ref Message m)`（`UiVisualStyle.cs`）。
所有自绘控件的 `WndProc` **第一句**必须是：

```csharp
if (UiVisualStyle.HandleOpaqueChrome(this, ref m)) { return; }
base.WndProc(ref m);
```

它只做两件事，**不碰任何颜色令牌 / 几何 / 布局 / 命中区域**：

1. **接管 `WM_ERASEBKGND`（0x14）**：用**该控件所在位置真实的容器底色**
   （`UiVisualStyle.ContextBackColor(control)`＝沿父链取第一个实色容器的 `BackColor`）填充客户区并 `return 1`。
   ❌ **禁止**依赖 `Control.WndProc` 的默认擦除（它用**控件自己的 `BackColor` 刷子**）——
   控件 `BackColor` 是"控件自己的底色"、不是"该位置真正被画出来的底色"，两者差几个数值时，
   被失效的那条带（常常只有几像素高）就会变成一条**淡线**（悬停重绘后消失，即历史"偶发淡线"的签名）。
2. **吞掉 `WM_NCPAINT`（0x85）**：本项目所有自绘控件非客户区**实测为 0 像素**，本来就没有属于它的像素；
   而系统在某些时序会用**经典样式**重描一圈方角 1px 框，正好盖住我们的圆角描边
   ⇒ 观感就是"边先粗后细 / 突然变成方角"。吞掉后触发一次客户区重绘（`WM_NCPAINT` 只触发 `WM_PAINT`，不递归）。

**已挂载（逐控件）**：`RButton`（`RBadgeButton` / `RColorButton` 继承获得）、`RCheckBox`、`RSlider`、
`RNumericUpDown`、`RComboDisplay`、`RScrollBar`、`RComboBox`。
⚠️ **例外（有意不挂）**：`RTextBox`（原生 `TextBox`，`BorderStyle.FixedSingle` 的非客户区边框在日间是**真实像素**，
吞 `WM_NCPAINT` 会把它擦掉）；`FanCurveForm.CurvePanel`（无条件整块填 `BackColor` 的既有画法，改它会动像素）。

**新增控件的检查项**：任何"整块自绘客户区"的新控件都必须挂上本入口，否则它就会重新引入这一整类问题。

---

## 7.6 DPI 与笔宽：**两个唯一入口**（2026-10-09 P0.5，**新增硬性约定**）

1. **DPI 来源**：一律 **`UiDpi.Paint(control)`（＝`Control.DeviceDpi`，审计时优先 `UiDpi.AuditDpi`）**。
   ❌ **禁止**在界面控件的绘制里用 `Graphics.DpiX`（`Graphics.FromHdc(窗口 DC)` 上的 `DpiX` 是设备能力里的
   `LOGPIXELSX`，PerMonitorV2 /「系统 DPI ≠ 显示器 DPI」时会回 96 ⇒ 圆角被算成直角、文字内边距偏小、
   同族控件边宽各不相同）。`Graphics` 版只允许**没有控件实例**的绘制路径使用（外接覆盖层、菜单渲染器、审计离屏画布）。
2. **笔宽**：一律 **`UiVisualStyle.OutlinePen(control, color, logicalWidth, minDevicePx, inset)`**
   （无控件实例时用 `OutlinePen(Graphics, ...)`）。它负责：
   `设备宽度 = max(minDevicePx, round(逻辑宽 × DPI / 96))`（**整数设备像素**）+ `PenAlignment.Inset`。
   ❌ **禁止**再写裸的 `new Pen(color, 1f)` / `new Pen(color)`（默认线宽也是硬编码 1）。

**两个"家族线宽"常量（改粗细只改这里）**：
| 家族 | 逻辑宽 | 100% | 125% | 150% | 175% | 200% |
|---|---|---|---|---|---|---|
| 按钮族**选中**边 ＝ 下拉族描边（`UiVisualStyle.ModeOutlineLogical = 2`，minDevicePx = 2） | 2 | 2 | 3 | 3 | 4 | 4 |
| 输入族描边 / 按钮未选中边 / 分段边 / chevron（`1f`，minDevicePx = 1） | 1 | 1 | 1 | 2 | 2 | 2 |

> 为什么是"整数设备像素 + Inset"：直线段整条落在最外一列/行像素内 ⇒ **纯色硬边**（不发虚）；
> 圆角处仍由 `SmoothingMode.AntiAlias` 平滑 ⇒ **不锯齿**。这与 §4.7 的"单层边"画法是同一套。

---

## 8. 验收与回退

### 8.1 规范落地后的自检清单

**A. 对比度（脚本重算，不许目测"差不多"）**
- [ ] 所有 `Text` 所在的底都 ≥ 4.5；`Muted` 所在的底都 ≥ 4.5（**夜间 `Muted` 不许出现在 `ButtonFace` 及更浅的面上**）。
- [ ] `Accent` 上的文字只用 `AccentText`（夜间 7.16 / 日间 4.76）；模式色用各自配对的 `Foreground`。
- [ ] 选中态淡底上的 `Text` ≥ 4.5（当前 4.85~6.49 ✅）；禁用态文字 ≥ 4.5（提案 6.36）。
- [ ] 相邻层 WCAG 落在 §3.2 的区间内（夜全部达标；日间按 §2.4 决定是否放开）。

**B. 单层边**
- [ ] 选中态的按钮上**只有**那圈彩边：`ActivatedOutline=true` 时必须走 `outlineSelected` 分支并抑制自身普通边。
- [ ] 没有任何控件同时出现"外框 + 内缩彩框"。
- [ ] 1px 高光与最外那圈边**不重合**（边在第 1 行，高光在第 2 行）。

**C. 层次可辨 / 无假边**
- [ ] 从页面底到选中态，五级依次可见（`Window → Surface → ButtonFace → Hover → Selected`）。
- [ ] `Input` 比 `Surface` 更暗（夜 1.250）。
- [ ] 切主题前后**没有**任何"整块异色矩形"（自绘控件四周与父容器逐分量相等）。

**D. 无锯齿 / 无发虚**
- [ ] 圆角处无阶梯（Region 已外扩 1 设备像素）。
- [ ] 直线段是硬边（`PenAlignment.Inset` + 整数像素列/行）；夜间边色是 `ButtonEdge`（Δ20），不是 `Border`（Δ6）。
- [ ] `Input` 族描边夜间 2px 且 `Inset`（不跨半像素）。
- [ ] 在 100% / 150% / 200% DPI 各看一遍（半径必须是整数设备像素）。

**E. 日间不受影响（若 §2.4 未被采纳）**
- [ ] `DayPalette` / `DayModePalette` 与改动前**逐字符相同**（可用 `Compare-Object` 对备份文件验证）。
- [ ] 所有新画法都由 `UiVisualStyle.IsNightMode` 门控。
- [ ] 切到日间逐屏目视：圆角、边、悬停、选中与改动前一致。

### 8.2 每一轮改动的备份与回退（任何节点都能回退）

**仓库里已有的备份**（`D:\openLM\backup\`，按时间倒序）：

| 备份目录 / 文件 | 对应轮次 | 含文件 |
|---|---|---|
| `backup\snapshots\2026-10\20261009_1000_pre_smallradius\` | UI⑦（小圆角 / 边缘清晰化 / 下拉分体式） | `UiVisualStyle.cs`、`RButton.cs`、`RComboBox.cs`、`RComboDisplay.cs`、`RColorButton.cs` |
| `backup\snapshots\2026-10\20261009_0900_pre_fixregion\` | ⑥ 之后的紧急修复（Region 环形洞 + 文字被盖 + 背景深度回调） | `UiVisualStyle.cs`、`RButton.cs`、`RComboBox.cs`、`RComboDisplay.cs`、`RTextBox.cs`、`RNumericUpDown.cs`、`SettingsForm.cs` |
| `backup\snapshots\2026-10\20261009_0600_pre_singleborder\` | 单层边 | `RButton.cs`、`SettingsForm.cs` |
| `backup\snapshots\2026-10\20261009_0500_pre_uipolish3\` | ③ 独立按钮拆分与推广 | `RButton.cs`、`SettingsForm.cs`、`UiVisualStyle.cs` |
| `backup\snapshots\2026-10\20261009_0400_pre_uipolish2b\` | ②b 夜间色板 + 深底/彩边 | `UiVisualStyle.cs`、`RButton.cs`、`RComboBox.cs`、`RComboDisplay.cs`、`SettingsForm.cs` |
| `backup\snapshots\2026-10\20261009_0200_pre_uipolish1\` | ① 消除"整块异色矩形" | `UiVisualStyle.cs`、`RCheckBox.cs`、`RNumericUpDown.cs`、`RSlider.cs`、`L-Mechrevo.csproj` |
| `backup\pkgs\UiVisualStyle.cs_pre_buttonface` / `backup\pkgs\RButton.cs_pre_buttonface` / `backup\pkgs\SettingsForm.cs_pre_buttonface` / `backup\pkgs\RForm.cs_pre_buttonface` | ⑤ 按钮面（浮起） | 单文件 |
| `backup\pkgs\UiVisualStyle.cs_pre_combopolish_20261009_0800` / `backup\pkgs\RComboBox.cs_pre_combopolish_20261009_0800` / `backup\pkgs\RComboDisplay.cs_pre_combopolish_20261009_0800` / `backup\pkgs\SettingsForm.cs_pre_combopolish_20261009_0800` | ⑥ 下拉统一 | 单文件 |
| `backup\snapshots\2026-10\20261008_0600_pre_uipolish\`、`backup\snapshots\2026-10\uipolish2night_pre_uipolish2night_20261008-095558\` 等更早目录 | 更早的 UI 轮次 | 见目录 |

**回退方法（三步，任何节点通用）**：
1. 从对应 `backup\<轮次>\` 把要回退的 `.cs` 复制回 `source\` 下的原位置（注意：`UiVisualStyle.cs`/`RButton.cs`/`RComboBox.cs`/`RComboDisplay.cs`/`RColorButton.cs` 在 `source\MechrevoLite.UI\`，`SettingsForm.cs` 在 `source\MechrevoLite\`，`ControlHelper.cs` 在 `source\`）。
2. 重新构建：`& 'D:\openLM\dotnet-sdk\dotnet.exe' build 'D:\openLM\source\L-Mechrevo.csproj' -c Release --no-incremental`（应 0 错误）。
3. 提权跑 `替换LM.ps1` 部署（脚本会自动把旧件备份成 `L-Mechrevo.dll.bak-<时间戳>`，这是**第二层**回退保险）。

**备份纪律**：任何一轮改动**开工前**先建 `backup\<yyyyMMdd>_<HHmm>_pre_<主题>\`，把"这一轮打算碰的全部文件"整份拷进去
（不是只拷一个），并在 `修改日志.txt` 里记下"改前值 → 改后值"的对照 —— 那是第三层回退依据。

---

## 9. 待用户拍板的清单

请**逐条**回复编号（可以直接写"1A 2B 3改 4保留…"）。每条都给了我的推荐与一句理由。

**1. 按钮圆角选哪个？**
　A. **R4 ＝ 4 逻辑像素**（代码 `BorderRadius=8`）　B. R6 ＝ 6px（`12`）　C. R8 ＝ 8px（`16`）
　→ **推荐 A**：它与现状夜间一致（改动最小），方正的"按键感"也最接近我们这次要的方向；B 是现状日间的值，C 偏圆。

**2. 选中态用哪种做法？**
　A. **强调色描边 + 淡底**（现状夜间）　B. 实心强调色（现状日间）　C. **夜间 A / 日间 B**（分工）
　→ **推荐 C**：深底上大面积饱和色会"炸"，浅底上实心色反而清爽；两种做法的文字对比度都已达标（≥4.73）。

**3. 日间是否与夜间同构？**（会改动日间观感，需单独点头）
　A. **同构**（按 §2.4 的表改 `DayPalette`）　B. 日间保持现状（本规范只对夜间生效）
　→ **推荐 A**，但要单独一轮 + 逐屏目视：现在日间 `Input`＝`Surface`＝纯白（1.000）、按钮三档同色（1.000），
　　等于"没有凹陷、没有悬停、没有选中底"，这是真实的功能性缺失。
　→ **✅ 已拍板 A 并落地（0.291 beta22，2026-10-09）**：用户原话「浅色的是不是还没升级」
　　⇒ 按 §2.4 的表改完 `DayPalette` 并已部署（实际取值见 §2.4 的"改前 → 改后"对照表）。

**4. 顶部 1px 高光保留否？**
　A. **保留**（`93,93,93`，仅夜间、仅按钮面家族、仅未选中/悬停）　B. 去掉，改为把 `ButtonFace/Surface` 级差提到 ≥1.45
　→ **推荐 A**：它只画一条线、不填区域、不可能造假边，是"浮起"最省的表达。

**5. 未选中按钮的边：统一中性，还是保留"每模式彩边"？**
　A. **统一中性**（夜间 `ButtonEdge 82`；模式色只在选中态出现）　B. 保留现状（核显省电 OFF/ON/AUTO 与 MiniLED/HDR 各是绿/蓝/红边）
　→ **推荐 A**：现在同一屏里"灰边按钮"和"彩边按钮"并存（§6.4 第 1、2 条），规则不可预测；A 之后"彩色 = 有意义"。

**6. 禁用态怎么表现？**
　A. 现状（底不变、无边框、文字 `Muted` → 夜间 3.99 ❌）　B. **提案**（底 `Window`、无边框、文字 `Muted` → 6.36 ✅）
　→ **推荐 B**：用"凹陷"表达不可用，比把文字调灰到看不见更好，且顺手修掉一个 AA 不达标。

**7. "按下"要不要给独立视觉？**
　A. **要**（新增 `ButtonFacePressed`，取 `ButtonFaceSelected`，不加彩边）　B. 不要（现状：按下＝悬停）
　→ **推荐 A**：现在鼠标按下毫无确认感，这是纯缺失、成本只有一行。

**8. 高 DPI 下的 1px 边是否要按 DPI 加粗？**
　A. **不加粗**（恒为 1 物理像素，现状）　B. 按 DPI 缩放（150% → 2px，200% → 3px？）
　→ **推荐 A**：1 物理像素在 150%/200% 下只会"更细"不会消失，加粗反而会让按钮显得笨重；
　　真正需要按 DPI 缩放的是**半径**和**输入族 2px 描边**（这两处现状已经是缩放的）。

**9. "待生效＝棕"是否维持"只换色相、不加图标/动画"？**
　A. **维持**（`Warn` 棕边 + 棕调淡底 + `Text`，与"选中"共用几何）　B. 增加一个图标或文字标记
　→ **推荐 A**：现在棕（R>G>B）与蓝/红/绿都不冲突，一眼可辨；加图标会让行高与对齐变复杂。

**10. 语义色（`Danger` / `Ok` / `Warn` / `AccentBlue`）本轮是否纳入统一？**
　A. **纳入规范但不改值**（登记用途与配对文字，禁止乱用）　B. 一并调整取值
　→ **推荐 A**：它们对 `Window` 的对比都在 5.04~8.88（夜）/ 5.06~5.63（日），没有需要动的理由；
　　真正要管的是"用错地方"（如 `Text` 压在 `Accent` 上 2.30）。

---

### 附：本文档引用的源码位置（只读，未改动）

| 文件 | 关注点 |
|---|---|
| `source\MechrevoLite.UI\UiVisualStyle.cs` | 色板 165/173 行、模式色 185/187 行、`EffectiveCornerRadius` 317、`CornerRadiusDevicePx` 331、`SmallCornerUnit=8` 306、`SmallCornerLogical=4` 309、`ModeActivatedFill` 432、`ModeOutlineWidth` 443、`SelectionOutlineColor` 274、`SetRoundedRegion` 485、`StyleComboBox` 1064、`IndependentGapLogical=6` 720、`ApplyIndependentRow` 722、`UseOutlineSelection` 751 |
| `source\MechrevoLite.UI\RButton.cs` | 半径换算 429、`DrawActivatedOutline` 248、`DrawTopHighlight` 273、`HoverFace` 300、`DrawCrispEdge` 329、`DrawSegmentBorder` 350、`OnPaint` 419 |
| `source\MechrevoLite.UI\RForm.cs` | `colorEco/colorStandard/colorTurbo/colorCustom/colorGray` 12~20、`InitColors` 99 |
| `source\ControlHelper.cs` | `AdjustControls` 61（第二条上色路径） |
| `source\MechrevoLite\SettingsForm.cs` | 模式色下发 906~929、`BorderColor` 批量下发 3504~3513、GPU 待生效 7978~7981、电池档位 4892~4931、核显省电 4279~4317、`ApplySegmentRow` 6019、行高 4162/4241、屏幕行高 1563~1567、`ScreenRowHeights` 180 |
| `source\MechrevoLite.UI\RComboBox.cs` | 全文（§7 交接） |
