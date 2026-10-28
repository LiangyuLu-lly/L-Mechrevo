# 官方控制台控灯抓包.ps1 —— 用法速查（README）

> 只读取证：抓"官方控制台怎么控键盘灯"。**不改采集逻辑、不写 EC/注册表/HID、不 PUBLISH**。
> 完整说明看 `-?`：`Get-Help .\官方控制台控灯抓包.ps1 -Full`（脚本头部是注释式帮助，含每个参数与示例）。

## 一、两个 MQTT 抓包窗的默认时长（2026-10-09 缩短，机主不用干等）

| 窗口 | 含义 | 默认时长 | 允许范围 | 为什么是这个长度 |
| --- | --- | --- | --- | --- |
| 第 1 窗 | 控灯前**基线**（MQTT 全帧，只订阅） | **12 秒** | 10~15 秒 | 基线只要证明"不改灯时没有灯效帧"，背景流量几秒就够；这 12 秒里**可以先去把官方控制台打开、翻到键盘灯页面**，只是**先不要改灯**。 |
| 第 2 窗 | **控灯中**（重点窗，MQTT 全帧） | **40 秒** | 30~45 秒 | 这一窗必须覆盖**多次改灯**（换颜色/换模式，每次停 2~3 秒），太短会漏帧；**默认自动跑满 40 秒**，若已改完不想干等也可按【回车】提前结束（提示会写明"会少抓后面的帧"）。 |

其它必须等满/为什么不能盲目缩短：
* 第 2 窗 ≥30 秒是为了让"关→开 + 3~5 个颜色 + 2~3 个模式"这套动作完整落在同一个窗口里；
* 第 1 窗缩到 12 秒是安全的：它没有 keepalive 依赖（keepalive 3 秒、保活间隔 = keepalive/2 即每 2 秒一个 PINGREQ，窗口内照常保活，缩到 12 秒不会掉线）；
* 第 1 窗若被 `-BaselineSeconds` 设得比 5 秒还小，会被自动抬到 5 秒下限（防误传 0/负数）。

## 二、参数（两窗可分别指定，旧参数仍兼容）

```powershell
# 默认（推荐）：第 1 窗 12 秒 + 第 2 窗 40 秒
powershell -ExecutionPolicy Bypass -File ".\官方控制台控灯抓包.ps1"

# 两窗【分别】指定：第 1 窗 15 秒、第 2 窗 45 秒
powershell -ExecutionPolicy Bypass -File ".\官方控制台控灯抓包.ps1" -BaselineSeconds 15 -ControlSeconds 45

# 旧参数 -CaptureSeconds N 仍兼容：= 两个窗都是 N 秒（老命令行为不变）
powershell -ExecutionPolicy Bypass -File ".\官方控制台控灯抓包.ps1" -CaptureSeconds 90

# 只抓 MQTT（跳过 HID/EC）；自测（不连网、不读 EC、不动系统）
powershell -ExecutionPolicy Bypass -File ".\官方控制台控灯抓包.ps1" -SkipHid -SkipEc
powershell -ExecutionPolicy Bypass -File ".\官方控制台控灯抓包.ps1" -SelfTest
```

| 参数 | 默认 | 说明 |
| --- | --- | --- |
| `-BaselineSeconds <int>` | 12 | 第 1 窗（控灯前基线）秒数，10~15 都行 |
| `-ControlSeconds <int>` | 40 | 第 2 窗（控灯中）秒数，30~45 都行 |
| `-CaptureSeconds <int>` | 0（未指定） | **旧参数·兼容**：显式传了就同时把两窗设成同一秒数 |
| `-OutDir` / `-Zip` / `-NoPrompt` | 桌面 / `$true` / 关 | 输出目录 / 是否产 zip / 不等回车（自动化用） |
| `-SkipMqtt` / `-SkipHid` / `-SkipEc` | 关 | 分别跳过某一通道 |
| `-Port` / `-Topics` / `-ClientIds` | 13688 / `#`+已知主题 / **UWPClient_4 → _3 → _2** | 只订阅，绝不 PUBLISH；串行、每个只试一次；username/password 与 clientId 同一个编号 |

## 三、体验细节（2026-10-09 加）

* **可见倒计时**：两个窗在监听期间都会显示 `第 N 窗剩余 X 秒`（每秒刷新一行）。
* **⭐ 零阻塞、自动计时（2026-10-09 r4 改）**：两窗**都不按任何键也会自己跑满**；第 1 窗结束**立刻自动进第 2 窗**，中间**没有任何 `Read-Host`/按键等待**——改灯动作因此落在窗口**里面**，不会再掉进两窗之间的空档。
* **两个窗都可以按【回车】提前结束**：倒计时那行会写当前窗的提示；第 2 窗的提示是"按回车可提前结束本窗：会【少抓后面的帧】，建议改满 40 秒；若已改完可回车"。
* **最短门槛 5 秒（防误触）**：窗口开始后 **前 5 秒内的回车一律忽略**（倒计时那行会写"还差 N 秒才接受回车提前结束"），避免连按/误触把窗口瞬间收掉等于没抓。
* **窗口报告里如实记时长**：`本窗时长：计划 N 秒，实际 M 秒（机主按回车提前结束）`；`00-概要.txt` 也标注两窗各自"跑满 / 被提前结束"，便于判断"没抓到帧"是否只是因为窗口太短。
* **⭐ 读超时不再掐断窗口（2026-10-09 r4 修）**：空闲 1 秒的读超时现在只计数并 **continue 继续轮询**到时长满；只有**真断线/对端关闭**才结束窗口，并在产物里写清 `读超时续轮询 N 次` 与 `本窗结束原因`。

## 四、机主最关心的三个"不要再等"点

1. 第 1 窗**不是"什么都别做"**：可以去开官方控制台、翻到键盘灯页面，只是**先不要改灯**。
2. 第 1 窗**只有 12 秒**，还嫌长就按回车提前结束（前 5 秒内的回车会被忽略）。
3. 第 2 窗**要持续改灯**（换颜色/换模式，每次停 2~3 秒）；**不用按任何键**也会自动跑满 40 秒，改完不想干等才按回车。

## 五、⭐ 2026-10-09 r3：修 MQTT CONNECT 拼包缺陷（默认与旧版同命令）

**症状**：TCP 连得上 127.0.0.1:13688，但永远读不到 CONNACK ⇒ 两次真实抓包都白跑（`PUBLISH 0 条`）。

**根因**（两处，都在 CONNECT 报文里）：

1. **漏了"协议名长度"两字节 `00 04`**：原来发出 `10 15 4D 51 54 54 04 02 …`，broker 把 `4D 51` 当成协议名长度（`0x4D51`）⇒ 直接不回 CONNACK。
2. **没带用户名/密码**：真实成功的抓包都带凭据（依据 `archive\核显省电抓包\gpu_saving_capture.py:77-88`）：
   `UWPClient_N` 配 `UWPClient_User_N` / `UWPClient_Pwd888881772688_N` ⇒ 连接标志由 `0x02` 改成 **`0xC2`**（username|password|cleanSession）。

**修好后的默认尝试（第一个 clientId = `UWPClient_4`，逐字节）**：

```
10 46 00 04 4D 51 54 54 04 C2 00 03
00 0B 55 57 50 43 6C 69 65 6E 74 5F 34              UWPClient_4
00 10 55 57 50 43 6C 69 65 6E 74 5F 55 73 65 72 5F 34   UWPClient_User_4
00 1B 55 57 50 43 6C 69 65 6E 74 5F 50 77 64 38 38 38 38 38 31 37 37 32 36 38 38 5F 34   UWPClient_Pwd888881772688_4
```

* `10` CONNECT / `46` 剩余长度 70 / `00 04` 协议名长度 / `4D 51 54 54` = MQTT / `04` 级别 3.1.1 / `C2` 连接标志 / `00 03` keepalive 3 秒；
* 载荷三段各带 2 字节长度：clientId `00 0B`(11) + username `00 10`(16) + password `00 1B`(27)，`10+13+18+29 = 70` 与剩余长度自洽，总长 72。
* 不带凭据的兜底形态（自定义 clientId）仍是原来那条合法包：`10 17 00 04 4D 51 54 54 04 02 00 03 00 0B 55 57 50 43 6C 69 65 6E 74 5F 32`。

**自检**：`-SelfTest` 新增 CONNECT 字节级反解校验（协议名长度/协议名/级别/flags/keepalive/clientId/username/password 逐段 + 剩余长度自洽 + 四组凭据配套 + 无凭据形态），本机实跑 `OK=10 FAIL=0`。

## 六、⭐ 2026-10-09 r4：修"窗口被 1 秒空闲提前掐断" + 改掉"改灯落在两窗空档"的交互

**症状（第 3 次真实抓包）**：`CONNACK rc=0x00`、`SUBACK 12/12`（CONNECT 拼包已修好），但两个抓包窗都被提前掐断 —— 计划 12 秒 / 40 秒，实际只跑了 **1.6 秒 / 1.1 秒**，用户的改灯动作全落在两窗之间的空档里，**一条控灯帧都没抓到**。

**根因**：PowerShell 把 .NET 异常包成 `MethodInvocationException`（报错前缀就是 `Exception calling "ReadByte" with "0" argument(s): "…did not properly respond after a period of time…"`），所以旧代码对 `$_.Exception` 做 `-is [System.IO.IOException]` 判定**恒为 false**，连里面的消息文本兜底也一起被跳过 ⇒ `RecvTimeoutMs=1000` 的**正常读超时**（`SocketError.TimedOut`）没被识别成"读超时"，于是没走到 `if ($pk2.TimedOut) { continue }`，而是当成"连接中断"直接 `break` 收窗。两窗都死在第一次 1 秒空闲上。

**改法**：新增 `Resolve-MqttReadErrorKind`，**沿 `InnerException` 链**找 `System.Net.Sockets.SocketException` 并比 `SocketErrorCode -eq TimedOut`；链上**根本没有** SocketException 时才用消息文本兜底 `did not properly respond|timed out|timedout`（真断线的 `forcibly closed / aborted` 文本不会命中）。**读超时 ⇒ `continue` 继续轮询**；只有 `ConnectionReset/Aborted/Closed`/EOF 才结束窗口并如实写原因。产物新增两行：`读超时续轮询 ：N 次`、`本窗结束原因 ：…`。

**交互**：第 1 窗结束**立刻自动进第 2 窗**（不再等回车、无任何 `Read-Host`）；"现在就去改灯"的提示改到**窗口已经开始之后**由 `-StartNotice` 打印（不阻塞、不占窗口时间）；两窗都可不按键跑满，都可按回车提前结束（最短门槛 5 秒）。自测：隔离本地 socket 真实读超时 = `MethodInvocationException > IOException > SocketException` 判 `TimedOut`；对端关闭判非 `TimedOut`（收窗）；9/9 通过。⚠ **本机 13688 未监听（GCU 未运行）⇒ 未做在线验证**。

**同步与哈希**：`tools\官方控制台控灯抓包.ps1`、`C:\Users\xiaohan\Desktop\官方控制台控灯抓包.ps1`、`C:\Users\xiaohan\Desktop\LM控灯抓包-发给机主\LM-light-capture.ps1` 三处**逐字节一致**：168,783 B / SHA256 `CA6E083CE0F52F2F57A9406AFCB6FF425840EAC93D6C10AE304913BC1E483187`（r3 为 157,779 B / `3E1BA6CE…`）。
⚠️ **三份都必须保持 `EF BB BF`（UTF-8 BOM）开头**：本环境按 ANSI 解码无 BOM 的 ps1，会报几百个假语法错（本次改完已逐份核对首 3 字节，`Parser::ParseFile` 0 错，`-SelfTest` `OK=10 FAIL=0`）。

---

**同步与哈希（r3 记录，保留）**：三处逐字节一致：157,779 B / SHA256 `3E1BA6CEE5754153BA2CC7E9E3D117AF793D65273B3F8240EFB275D20E00ADCD`。
⚠️ **三份都必须保持 `EF BB BF`（UTF-8 BOM）开头**：本环境按 ANSI 解码无 BOM 的 ps1，会报几百个假语法错（本次改完已逐份核对首 3 字节）。
