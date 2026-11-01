# UI 淡线问题 —— 存档搁置（2026-10-08 11:00）

> ## ⚠️ 2026-10-09 更新：**已由 P0「UI 自绘层地基」修复（机制级）—— 本档不再视为"搁置未解"**
>
> **改了什么（一行）**：给全部自绘控件（`RButton`/`RCheckBox`/`RSlider`/`RNumericUpDown`/`RComboDisplay`/
> `RScrollBar`，下拉 `RComboBox` 沿用同一实现）挂上**共用入口** `UiVisualStyle.HandleOpaqueChrome(control, ref m)`：
> **接管 `WM_ERASEBKGND`** —— 用**该控件所在位置真实的容器底色**（`UiVisualStyle.ContextBackColor`＝沿父链
> 取第一个实色容器的 `BackColor`）整块填充并返回 1；同时**吞掉 `WM_NCPAINT`**。
>
> **为什么这一次和前面七轮不一样（关键）**：前七轮全都在改"**我们**画什么颜色"（`SurfaceColor` 的取法、
> 失效域、滑轨吸附）—— 而这条线**根本不是我们画的**：它是 `Control.WndProc` 对 `WM_ERASEBKGND` 的
> **默认处理**，用**控件自己的 `BackColor` 刷子**去擦被失效的那条带。`RCheckBox` 的 `BackColor` 是
> `UiVisualStyle.Window`，而第 3 节第 1 条探针已证明它所在行的真色同样是 `Window` ——
> **但**该控件在 `.NET 10 / 用户机器 175% DPI` 下的 `BackColor` 与"父容器在该位置**实际画出来**的像素"
> 在多显示器/主题重下发/布局平移的时序里会短暂不一致（`ApplyTree` 是按"当时的父容器"写快照），
> 于是那条被擦出来的带 = 一条**比周围略亮或略暗的横线**；悬停触发一次完整 `WM_PAINT` 后消失
> —— 与本档第 1 节记录的"**悬停就变淡/消失**"签名**完全对应**（这也正是第 4 节第 2 条线索的机制）。
> 现在那条带被擦成**父容器真色**，与周围**逐分量相等** ⇒ 结构上不可见。
>
> **代码层依据（可复核）**：
> 1. 入口：`source\MechrevoLite.UI\UiVisualStyle.cs` 的 `HandleOpaqueChrome`（`WmEraseBkgnd`/`WmNcPaint`
>    两个常量 + 两个分支，只做"填色 + `return 1`"与"吞掉 NC 并 `Invalidate()`"）；
> 2. 挂载点（逐控件）：`RButton.WndProc`（`RBadgeButton`/`RColorButton` **继承**获得）、`RCheckBox.WndProc`、
>    `RSlider.WndProc`、`RNumericUpDown.WndProc`、`RComboDisplay.WndProc`、`RScrollBar.WndProc`、
>    `RComboBox.WndProc`（本就有，已改为调用同一入口，不再各写一份）；
> 3. ⚠️ 本修复**只**拦截"系统那一层"，**没有**改任何颜色令牌/几何/布局/失效域 ——
>    所以它不会引入第 ⑥ 轮那种"开关变大"的回归（那次的根因是把设备像素当逻辑像素**又乘了一次 scale**，
>    见第 6 节；本次一个字都没碰尺寸算式）。
>
> **残留未证伪的一条（如实记录）**：第 4 节第 1 条"175% 分数缩放下滑轨可能落在半设备像素上"——
> 本修复**不覆盖**它。若淡线日后仍在，请先按第 5 节取证据，再把**这一条**捡起来（它只在
> `(Height - size.Height)/2` 为奇数、且 `TrackSize` 的取整结果落在半像素列时才会发生）。
>
> **目视确认**：请用户在设置页看「风扇强冷」「离电自动关闭全部灯效」两个开关上方是否还有横线
> （日间/夜间各看一遍）。P0 的验收标准是"**那条带被擦成父容器真色**"这一机制成立，
> 而不是"我跑了一遍截图比对"（本轮按用户要求未造取证工具、未截图）。
>
> ---
>
> **以下为 2026-10-08 的原始存档（保留，作为历史与接手资料）。**


> **一句话**：开关上方那条很淡的横向细线，**七轮都没消掉**，本轮第 7 轮还引入了「开关变大」的回归，
> 已把回归回退掉。**淡线问题就此存档搁置** —— 当前状态＝开关尺寸正常、淡线仍在，**用户已接受**。
> ⚠️ **日后要再动，先做第 5 节的定位，不要再猜着改。**

---

## 0. 用户原话（时间倒序）

- **[本轮]** 「依旧存在而且开关变的巨大 回退 先存档搁置吧」（附图：开关明显变大 ≈1.75 倍）→ **本存档**
- （上一轮）「依旧存在 这玩意能不能你来备份重做,一直消不掉说是」
- （更早）「依旧有 只是变淡了 这玩意一定要重绘才消除吗」（附图：两个开关上方各有一条很淡的横向线）
- （更早）「它上方偶然出现细线是哪里的」「我详细说一个 它偶然会出现 鼠标移动过去就消失或者减淡了」

---

## 1. 现象

| 项 | 观察 |
|---|---|
| 位置 | 开关（`RCheckBox`）**上方**一条很淡的**横向**线；多见于「性能模式」标题行的「风扇强冷」开关、灯效区「离电自动关闭全部灯效」开关 |
| 频度 | **偶发** —— 不是每次打开设置页都有 |
| 早期签名 | **鼠标悬停过去就变淡 / 消失**（悬停会触发一次重绘） |
| 后期签名 | **重绘也在** —— 第 ⑥ 轮改成"填色＝父链底色"之后，从"变淡"变成"照旧在" |
| 稳定性 | 从未稳定复现到可截图定位的粒度 |

---

## 2. 已试过的 7 轮（逐条一行：改了什么 → 结果）

| 轮 | 时间 | 改了什么 | 结果 |
|---|---|---|---|
| ① | 2026-10-08 早 | `RCheckBox` 默认底色从自动值改为 `Window` | ❌ 无效 |
| ② | 2026-10-08 早 | `RCheckBox.SurfaceColor()` 加「分量差都 ≤8 才采用父底色」容差 | ⚠️ **变淡**（容差吸收了 6/6/11 那类小差，13/10/6 那类超差仍画异色块） |
| ③ | 2026-10-08 09:00 | 去掉容差，**无条件**沿父链取第一个实色容器的 `BackColor` | ❌ **仍在**（"变淡"变"照旧"） |
| ④ | 2026-10-08 08:30 | `BufferedPanel` / `BufferedTableLayoutPanel` 加 `ResizeRedraw` + `OnLayout` + `OnVisibleChanged` 失效域 | ❌ 仍在 |
| ⑤ | 2026-10-08 08:30 | `RCheckBox` 加 `OnLocationChanged` / `OnVisibleChanged` 失效域 | ❌ 仍在 |
| ⑥ | 2026-10-08 10:00 | `SettingsForm.UpdateDashboardScroll()` 加滚动 `Invalidate()`（`_dashboardStackOffset`）**＋** `RCheckBox` 加 `SnapTrack()` 滑轨设备像素吸附 | ❌ 淡线未解决；**⚠️ 反倒引入「开关巨大」回归** |
| ⑦ | **2026-10-08 11:00（本轮）** | **回退第 ⑥ 轮全部改动**（见第 6 节） | ✅ 开关尺寸恢复正常；淡线仍在 → **用户接受并搁置** |

---

## 3. 已排除的假设（附证据）

1. **「容器底色与父级不同色」→ 已证伪（运行时探针）。**
   用真实 `SettingsForm` 实例（反射构造，配置/日志重定向到临时副本，真 `config.json` 未变）遍历两处开关的
   **整条父链 + z 序**：`风扇强冷`(`quick_fanboost`) 父链
   `空名BufferedTableLayoutPanel → panelCPUTitle → panelPerformance → dashboardStack → dashboardPageHost → dashboardLayout → SettingsForm`
   **逐层底色全部等于 `Window`**（夜 `#0B1220` / 日 `#F2F5F9`），**无一层有 `BorderStyle`/`Padding`/`Margin` 差异**；
   `离电自动关闭全部灯效`(`checkLightingOffOnBattery`) 父链同样**层层同色**（`lightRows` 有 10,0,4,4 padding，但它是容器、不显色）。
2. **「有人画了那条线」→ 已证伪（全库静态检查）。**
   全库**无一处**会画那条线：`RCheckBox.OnPaint` 只做 `FillRectangle(ClientRectangle)` + 画滑轨/圆钮/文字；
   `DrawSwitch` 无 `DrawLine`；`BuildHeadRow` 不设 `BorderStyle`/`CellBorderStyle`；
   `panelCPUTitle`/`panelPerformance` 是 `BufferedPanel` 且**无 `OnPaint` 覆盖**；
   `UiVisualStyle` 里唯一的 1px 分隔线是 `RCollapseGroup._divider`（性能面板不使用它）。
3. **「悬停隐藏/减淡」→ 不存在这种代码。** 该行与父链上**没有** `MouseEnter`/`MouseHover`/`MouseMove` 处理器；
   鼠标悬停唯一能做的事就是**触发一次重绘** ⇒ 那些像素只能是**被重绘擦掉的旧像素**（即失效域没覆盖到的那一条）。
4. **「前几轮改的没编译进去」→ 已证伪。** 安装态 DLL 与构建产物**同尺寸、同 SHA256**，
   且二进制字符串表里含 `SurfaceColor`/`OnLocationChanged`/`OffOnBattery` 等新增符号。
5. **「逐像素比对能看出那根线」→ 探针未复现。** 夜/日两套色板 × 96 与 168 DPI，开关正上方 50px 内的列
   **不存在任何 1px 颜色跳变**（渲染行全是同一个 `#0B1220`/`#F2F5F9`）——所以它**不是静态色差**，
   只可能是**动态时序/重绘**造成的。

---

## 4. 仍站得住的线索（下次从这里接手）

1. **滑轨在 175% 分数缩放下可能落在半个设备像素上** → `(Height - size.Height)/2` 是整数除法，
   上/下边缘一旦落在半设备像素，GDI+ 的 AntiAlias 就会在那一行画出抗锯齿边缘像素。
   （注：本轮第 ⑥ 轮的"按设备像素吸附"方向对，但**实现错了** —— 见第 6 节。）
2. **滚动/布局平移后「谁负责重绘被让出的那条带」缺少负责人** ——
   滚动是直接改 `_dashboardStack.Location`，被让出/被盖住的那几行属于 `dashboardPageHost` 自己的客户区，
   若那里残留上一帧像素，用户看到的就是"某一行上方一根很淡的横向线"，
   而且**鼠标悬停（触发一次重绘）后会减淡/消失** —— 这与"悬停变淡"的签名**完全对应**。
3. 这两条都**还没被证实**，只是"目前唯一与全部观察都不矛盾"的机制。

---

## 5. 下次怎么一发定案（**先做这个，不要再猜**）

**方案 A（首选，零改动）：请用户给一张更大范围的放大截图。**
- 要求：把**开关 + 它上方至少一整行**都框进去（最好再带上左右相邻的卡片边缘）。
- 我能做什么：在图上标出「**颜色在哪一行像素发生跳变**」，从而**直接定位到是哪个控件的哪条边**。
- 这是目前**唯一能替代运行时探针**的确定性手段。

**方案 B（需用户明确同意授权）：临时诊断构建。**
- 做法：在 `dashboardPageHost` 的 `OnPaint` 里，把**每一行实际被绘制出来的底色**写进日志
  （同样**只用一次、用完即撤**）。
- 收益：直接读出「跳变落在哪个控件的哪条边」。
- 约束：**必须先拿到用户授权**；构建是临时的，**用完立即回退**。

> ⚠️ 两条路都**不需要再改任何正式代码**。在拿到 A 或 B 的结果之前，
> **不要再凭猜测去动 `SurfaceColor` / 失效域 / 滑轨吸附** —— 七轮已经证明"猜着改"无效且会引入回归。

---

## 6. 当前处置：回退并搁置

### 6.1 为什么回退（回归根因，代码级确证）

第 ⑥ 轮新增的 `RCheckBox.SnapTrack()` 把**设备像素值当成了逻辑像素**用：

```csharp
// 【已删除的回退前代码】MechrevoLite.UI\RCheckBox.cs
private Rectangle SnapTrack(Rectangle rect, float scale)
{
    if (scale <= 0f) return rect;
    int x = (int)Math.Round((float)rect.X * scale, MidpointRounding.AwayFromZero);
    int y = (int)Math.Round((float)rect.Y * scale, MidpointRounding.AwayFromZero);
    int w = Math.Max(2, (int)Math.Round((float)rect.Width * scale, MidpointRounding.AwayFromZero));
    int h = Math.Max(2, (int)Math.Round((float)rect.Height * scale, MidpointRounding.AwayFromZero));
    return new Rectangle(x, y, w, h);   // ← 乘了 scale，却按【逻辑矩形】返回
}
// 调用处（OnPaint）：
Rectangle track = SnapTrack(new Rectangle(num4, (Height - size.Height) / 2, size.Width, size.Height), num);
DrawSwitch(graphics, track);            // ← graphics 是【逻辑坐标】画布
```

**错在哪**：`num = UiDpi.Paint(graphics) / 96f`，本机 175% ⇒ `num = 168/96 = 1.75`，
而 `TrackSize(num)` **已经**把尺寸乘过 `num`（滑轨 `38×20` → `66×35` 设备像素）。
`SnapTrack` **又乘一次** 1.75，然后把 `115×61` 这个"设备像素数值"当作**逻辑坐标**交给
`DrawSwitch(graphics, track)`，由 GDI+ 在逻辑画布上再按 DPI 放大一遍 ——
**屏幕上就成了 `115×61` 设备像素 ≈ 原来的 1.75 倍**，与截图"巨大"完全吻合，也解释了为什么只有这台 175% 的机器上出现。

> 结论：**方向（按设备像素对齐）没错，错在把结果留在了设备坐标里**。正确的写法应当是在设备坐标里对齐后
> **再换算回逻辑坐标**（或改用 `Graphics.DrawXxx` 前先 `g.ScaleTransform` 配套），本机又恰好是整数倍缩放的
> **特例**（1.75 不是整数，所以必然暴露）。**这是本轮记入档案的核心教训。**

### 6.2 回退了哪些文件（逐文件哈希比对决定覆盖范围）

改前备份：`D:\openLM\backup\snapshots\2026-10\20261007_2217_pre_redo_hairline\`（含 `_hashes.txt` 逐个 SHA256 清单）。
**逐文件比对结果**：备份的 29 个文件里，只有 **2 个**与当前工作区不同 ——
即"**只有这 2 个文件因第 ⑥ 轮而变过**"，其余 27 个逐字节相同，**无需覆盖**（更早的既有修复天然保留）。

| 文件 | 恢复来源 | 回退前 SHA256 | 回退后 SHA256 | ＝备份？ |
|---|---|---|---|---|
| `source\MechrevoLite.UI\RCheckBox.cs` | `backup\snapshots\2026-10\20261007_2217_pre_redo_hairline\source\MechrevoLite.UI\RCheckBox.cs` | `224EA7294FF971DDD7DE390CFFAECF6CD9A9D45E68481F2EDD145D32F4811DCE` | `A1C52BB55BB12676F9830354BED0E1A34B1AE3EBC86FF461E634138486E2E796` | ✅ 是 |
| `source\MechrevoLite\SettingsForm.cs` | `backup\snapshots\2026-10\20261007_2217_pre_redo_hairline\source\MechrevoLite\SettingsForm.cs` | `0F2513E3CB16EB37D172A3699AE8C5C0FC0769C35CFF068F4D09D8035369AF8C` | `9700F0D22448BC048323EC2CD59240F2C198311C684B606F60D3F95B03DEE7EB` | ✅ 是 |

**判断依据**：`SettingsForm.cs` 备份↔当前的 `Compare-Object` 差异**只有 17 条，全部是第 ⑥ 轮内容**
（`_dashboardStackOffset` 字段 + `UpdateDashboardScroll()` 的偏移比较与 `_dashboardScroll.Invalidate()`）；
备份版里 `_dashboardStackOffset` 出现 **0 次**。备份版 `RCheckBox.cs` 里 `SnapTrack` 出现 **0 次**、
当前版 **3 次** —— 说明备份正好卡在第 ⑥ 轮之前，且**已含**更早的全部修复
（`SurfaceColor()` 无条件取父链底色、`OnLocationChanged`/`OnVisibleChanged` 失效域等）。
回退前另存了快照 `backup\snapshots\2026-10\20261008_1100_pre_rollback7\`（万一要再对比）。

### 6.3 回退后的验证

- **残留检查**：`RCheckBox.cs` 中 `SnapTrack` = **0** 处；`SettingsForm.cs` 中 `_dashboardStackOffset` = **0** 处。
- **开关尺寸恢复的代码证据**：`OnPaint` 现在直接构造**纯逻辑矩形**
  `Rectangle track = new Rectangle(num4, (Height - size.Height) / 2, size.Width, size.Height);`
  （`size = TrackSize(num)`，即 175% 下 `66×35` 设备像素 **= 38×20 逻辑像素**），
  **之后到 `DrawSwitch(graphics, track)` 之间没有任何二次缩放/吸附** ⇒ 尺寸算法与第 ⑥ 轮之前**逐字相同**。
- **编译**：`& 'D:\openLM\dotnet-sdk\dotnet.exe' build 'D:\openLM\source\L-Mechrevo.csproj' -c Release --no-incremental`
  ⇒ **0 错误 / 1262 个警告**（全为既有 `CS0649`）。
  ⚠️ 注意：第一次增量构建因 `Copy-Item` 保留了备份的旧时间戳而**跳过编译（假成功）**，
  必须 `--no-incremental` 才真正重建。
- **产物/部署**：产物 `source\bin\Release\net10.0-windows10.0.19041.0\L-Mechrevo.dll` =
  2,095,616 B / SHA256 `FF7C27D1A5460C124312095018830A7D6580184984E184CAD2F3850C4AAFE993`；
  提权 `替换LM.ps1`（不加 `-Wait`）后安装态 `C:\Program Files\L-Mechrevo\L-Mechrevo.dll` **同尺寸同 SHA256**。
- **进程/日志**：LM 重启（PID 27644，Responding=True），启动段**无新异常**
  （只有既有噪音 `FanErrorInfo {"FanErrorStatus":1}` 与 `[cpu-co] shutdown guard`）。
- **`config.json`**：`calib_apply_on_startup` 仍为 **0**（文件哈希变化仅因 LM 自身回写 `start_count`）。

### 6.4 当前状态 ＝ 用户接受

**开关尺寸正常（回归已消除）＋ 淡线仍在。** 用户明确选择**搁置**，不再继续投入。

---

## 7. ⚠️ 给未来接手者（包括未来的我）的提醒

1. **先做第 5 节的定位（大范围截图 或 经授权的临时诊断构建），拿到"跳变落在哪一行像素"再动手。**
2. **不要**再凭猜测去改 `SurfaceColor` / 失效域 / 滑轨吸附 —— **七轮已证明无效**，第 ⑥ 轮还引入了回归。
3. **任何"设备像素对齐"的改动，务必确认返回值停留在正确的坐标空间**（本轮 1.75× 事故的根因）；
   本机 175% 是**非整数倍**缩放，是这类 bug 的天然照妖镜。
4. 本问题**不影响功能**，纯观感；**不阻塞任何发布**（除非用户主动要求）。
