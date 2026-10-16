# tools\ —— 工具目录说明

> 整理于 2026-10-09（用户原话「D:\openLM\tools能不能整理一下」）。
> 本次**只整理目录结构 + 写本文件**：未改任何工具内部逻辑、未部署、未打包、未运行任何脚本、未动 `installer-src\`（一个字节都没改）。
> 备份：`D:\openLM\backup\tools_pre_toolstidy_20261009\tools\`（整理前 655 文件全量副本；SHA256 清单见同目录 `hasheset_before.txt` / `hasheset_after.txt`）。
> ⭐ **追加（2026-10-09，用户原话「5归档」）**：把 6 个 30 系性能模式实验开关归档进 `archive\20261009\30perf-实验开关\` 并同步修正其中的硬编码路径。见第二、四、七节。

**顶层只放"要发给机主的成品工具"；开发辅助进 `dev\`，已归档/被取代的进 `archive\20261009\`。**

---

## 一、⭐ 给机主的三个工具（发货就看这三个）

| 文件 | 用途 | 运行前提 | 桌面副本 |
|---|---|---|---|
| **`GCU抓包.ps1`** | 「GCU 连不上」一键诊断抓包：回环抓包 + GCU 底座日志 + 装后自检，产出 `LM-GCU抓包-<时间戳>.zip` 发回 | ⚠ **管理员**（pktmon/netsh trace 必须；非管理员会明确提示并自动降级为只收文本证据） | ⚠ **本机桌面当前没有**（`苍龙复现抓包说明.md` 假定它在机主桌面）⇒ 发货前记得拷到桌面 |
| **`官方控制台控灯抓包.ps1`** | 抓「官方控制台怎么控键盘灯」：注册表快照 / HID 枚举与只读探测 / EC 0x740·0x766 只读 / MQTT 全帧，产出 zip。**两个 MQTT 窗时长可分别指定**：第 1 窗（控灯前基线）默认 **12 秒**（10~15 都行）、第 2 窗（控灯中）默认 **40 秒**（30~45 都行）；旧参数 `-CaptureSeconds N` 仍兼容（= 两窗都 N 秒）。**⭐ 2026-10-09 r3 修 MQTT CONNECT 拼包**（补协议名长度 `00 04`；按 clientId 编号配套 username/password，flags `0x02`→`0xC2`）。**⭐ 2026-10-09 r4 修"窗口被 1 秒空闲提前掐断"**（PowerShell 把读超时包成 `MethodInvocationException`，旧 `-is [System.IO.IOException]` 恒 false ⇒ 两窗只跑了 1.6s/1.1s、漏掉全部控灯帧；现沿 `InnerException` 链判 `SocketError.TimedOut` ⇒ **读超时 `continue` 继续轮询**，只有真断线才收窗），并把交互改成**零阻塞自动计时**（第 1 窗结束**立刻**进第 2 窗、中间无 `Read-Host`；两窗不按键也会跑满、都可按回车提前结束、前 5 秒门槛防误触）。速查见 `官方控制台控灯抓包-README.md`，完整用法 `Get-Help .\官方控制台控灯抓包.ps1 -Full`。⚠ **本文件必须保持 UTF-8 带 BOM**（`EF BB BF`）：丢了 BOM，本环境会按 ANSI 解码，报几百个假语法错 | ⚠ **管理员**（读 EC 必须；非管理员提示并降级） | ✅ 有：`C:\Users\xiaohan\Desktop\官方控制台控灯抓包.ps1` ＋ `C:\Users\xiaohan\Desktop\LM控灯抓包-发给机主\LM-light-capture.ps1`，**三处逐字节一致**（SHA256 `CA6E083C…`、168,783 B；2026-10-09 r4 修"窗口被 1 秒空闲提前掐断" + 零阻塞自动计时交互后已重新同步，三份均 `EF BB BF` 开头） |
| **`SRGB诊断.ps1`** | 「切 SRGB 跳回默认」只读诊断抓包，产出 zip | ✅ **不需要管理员**（个别 HKLM 项读不到会在报告里写明"需管理员"） | 无 |

**第四个（专项，同样发给机主）**：**`诊断-GCU连不上.ps1`** —— GCU 连不上 / 多屏刷新率现场取证。默认**只读**、不自动提权、不弹 UAC；`-Fix`（可选，机主自己决定）需管理员。`机主验证说明-3台.md` 第 2 节就是指名让机主跑这个。**无桌面副本。**

---

## 二、顶层其余文件（都不是"随手发机主"的）

| 文件 | 用途 | 给谁用 | 前提 |
|---|---|---|---|
| `苍龙复现抓包说明.md` | 给机主的照做说明（配合 `GCU抓包.ps1`：拔插电/睡眠唤醒/切显卡模式三场景复现后抓包） | 机主 | 无 |
| `官方控制台控灯抓包-README.md` | `官方控制台控灯抓包.ps1` 的用法速查：两窗默认时长与为什么这么长、`-BaselineSeconds`/`-ControlSeconds`/`-CaptureSeconds` 用法、倒计时与两窗回车提前结束（前 5 秒门槛）说明、**r3 的 CONNECT 拼包修复（`00 04` + 凭据 `0xC2`）、r4 的窗口超时判定修复 + 零阻塞自动计时交互、与哈希/BOM 核对** | 我方 + 机主 | 无 |
| `GCU快照.ps1` | GCU 安装前后快照（**严格只读**：不写注册表、不启停服务、不连 13688），输出到 `-OutDir`，用于 diff | 我们自己诊断用 | 无管理员要求；建议 GCU 已装好时跑 |
| `Get-IccProfile-fixed.ps1` | 拉本机显示器 ICC 校色包（只下载、只解压到本地，不安装不写系统） | 我方 / 专项 | 需联网（aistone 8001/3081）；⚠ **文件必须保持 UTF-8 带 BOM**，改写会丢 BOM 导致 PS 5.1 解析失败 |
| `igpu-capture\` | 「核显切换采集工具」的成品内容（`collect.ps1` + 第一步/第二步 bat + `说明.txt`） | 机主（采集核显切换证据） | 按 `说明.txt` 的两步走 |
| `LM-核显切换采集工具.zip` | 上面那份的打包件（实际发出去的） | 机主 | 无 |
| `dgpu-silence\`、`dgpu-silence.7z` | GCU 显卡工具箱（独显热切 / 静音，含 `使用说明.txt`、`双击运行-显卡工具箱.cmd`、`GCU独显热切项目报告.md`） | ⚠ **只给受控实验机** | ⚠ 会碰 NVRAM/EC，别随手发给普通机主 |
| `merge_and_clean.py` | ⚠ **原地保留**：`installer-src\打包须知.txt`（第 35 行打包命令）直接引用 `D:\openLM\tools\merge_and_clean.py --apply` | 打包流程 | 见 `打包须知.txt` |
| `publish_lm.rsp` | ⚠ **原地保留**：`打包须知.txt`（第 31 行）明确提到 `@tools\publish_lm.rsp` | 打包流程 | 见 `打包须知.txt` |
| ~~`enable_30series_perf_mode.ps1` / `.cmd`、`restore_gcu_variant.ps1` / `.cmd`、`run_with_30perf.cmd`、`run_without_30perf.cmd`~~（6 个） | ⭐ **已归档（2026-10-09）**：30 系性能模式 / GCU 变体切换的**实验开关**（写 `HKLM\...\GcuVariant`，或只设 `HKCU` 环境变量 `LMECHREVO_FORCE_30PERF`）。⭐ 结论＝**设备不认 `ProfileIndex`** ⇒ 30 系性能模式这条**不成立**；当天已删用户级环境变量 `LMECHREVO_FORCE_30PERF`。⚠ 归档后**不应再被当作可用手段**，只作历史留档 | ⚠ **不要发机主**，仅本机实验 | ⚠ **已不在顶层** ⇒ `archive\20261009\30perf-实验开关\`（细节见该目录 `归档说明.md`） |

> 这 6 个实验开关**已于 2026-10-09 归档**到 `archive\20261009\30perf-实验开关\`（用户原话「5归档」）：本次只做「**移动 + 同步改掉因移动而失效的路径**」—— 2 处 `.cmd` 的 `SCRIPT=` 硬编码与 2 处 `.ps1` 的 `D:\openLM\tools\gcu-variant-backup.txt` 备份依赖（共 **4 处代码路径**，改成 `%~dp0` / `Join-Path $PSScriptRoot`），另 **5 处仅注释 / 运行时提示文案**里的旧路径。`.ps1` 仍**无 BOM**、6 个文件仍**纯 ASCII（0 非 ASCII 字节）**、行尾**原样 LF**（⚠ 这 6 个文件原本就是 LF，**没有**做 CRLF 转换）。为什么归档、实验结论取证、**复活前要先做什么** ⇒ 见该目录 `归档说明.md`。

---

## 三、`dev\`（开发 / 一次性辅助，544 文件）⚠ **不要发给机主**

- 434 个 `.py` + 22 个 `.ps1`：一次性补丁脚本 / 探针 / 分析脚本（`patch_*`、`fix_*`、`probe_*`、`wcs_*`、`append_log_*`、`d1_*`、`elevated_*` 等）+ `wcs_probe*.cs`。
- 4 个三方逆向工具目录：`innoextract\`、`procmon\`、`ghidra_proj\`、`egroupshot\`（含 `bin\obj` 构建产物）。
- 它们的数据/日志：`power_settings_map.json`、`reg_before/after.json`、各种 `*_dump.txt`、`_*_marker.txt` 等。
- ⚠ **注意**：脚本里写死的 `D:\openLM\tools\xxx` 绝对路径**没有**被改成 `tools\dev\xxx`（改路径＝改脚本内容，本次不允许）。它们都是**已经执行完**的历史一次性脚本；要重跑请先自行改路径。

## 四、`archive\20261009\`（已归档，81 文件；本批 +7）⚠ **不要发给机主**

- `归档说明.md` —— 每个归档件"为什么被取代、何时归档、还有没有用"。
- `enable_30series_mode_profile.ps1` —— 被 `enable_30series_perf_mode.ps1` 取代（旧版无备份、无回滚）；两者现在都已归档（后者在下面的 `30perf-实验开关\`）。
- **`30perf-实验开关\`（7 文件，2026-10-09 本批新归档）** —— `enable_30series_perf_mode.ps1` / `.cmd`、`restore_gcu_variant.ps1` / `.cmd`、`run_with_30perf.cmd`、`run_without_30perf.cmd` ＋ `归档说明.md`。⭐ 归档原因：**设备不认 `ProfileIndex` ⇒ 30 系性能模式这条不成立**，用户级 `LMECHREVO_FORCE_30PERF` 当天已删。⚠ 里面是会**提权写 `HKLM`** 的脚本，**别在这里误双击**。
- `build_*.txt` / `iscc_*.txt` / `publish_*.txt`（11 个）—— 0.290.7 那批的构建 / 发布日志，已被后续版本取代。
- `历史补丁\`（62 文件）—— 2026-10-01 ~ 10-03 的早期一次性补丁（原本就叫"历史补丁"）。

---

## 五、打包基线（⚠ 本次一个字节没动）

`installer-src\打包须知.txt` 里登记的基线与哈希**只针对** `installer-src\files\GCU\*.ps1` 与 `payload` 覆盖层，**不含 `tools\` 下任何文件** ⇒ 本次整理**不影响任何基线**，`installer-src\` 未改动。

## 六、桌面副本（整理时未动）

| 桌面文件 | 状态 |
|---|---|
| `官方控制台控灯抓包.ps1` | ✅ 与 `tools\官方控制台控灯抓包.ps1` 逐字节一致（同一 SHA256），保留不动 |
| `gcu_monitor.lnk` | 指向 `D:\GCUmonitor\gcu_monitor.exe`，不是 `tools\` 里的东西，未动 |
| 已归档 / 被取代工具对应的桌面副本 | **无**（桌面只有上面这一个 `tools\` 类脚本） |

## 七、本次整理的验证

- 整理前全量备份 655 文件 → `backup\tools_pre_toolstidy_20261009\tools\`。
- 移动前后 **SHA256 + 文件长度 + BOM 标志的集合完全一致（655 / 655）**；6 个顶层 `.ps1` 仍为 `EF BB BF` 开头（`GCU抓包`/`SRGB诊断`/`官方控制台控灯抓包`/`GCU快照`/`诊断-GCU连不上`/`Get-IccProfile-fixed`），其余 `.ps1` 维持原有"无 BOM"状态（含 `enable_30series_*`、`restore_gcu_variant`）。
- **未删除任何文件**（临时产物一律归档，不删）。

### 2026-10-09 追加：30perf 实验开关归档（用户原话「5归档」）

- 6 个实验开关移入 `archive\20261009\30perf-实验开关\`：**移动后（改路径前）6 / 6 SHA256 与移动前逐字节一致**；随后按第二节说明改了 **4 处代码路径 + 5 处注释 / 提示文案**，`run_without_30perf.cmd` **一个字节未动**（哈希与移动前完全相同）。
- 编码保真：两个 `.ps1` **仍无 BOM**（本来就无 BOM，⛔ 没有强加 BOM）；6 个文件**纯 ASCII，0 个非 ASCII 字节**（`.cmd` 不被 cmd 代码页毁掉的底线）；行尾**原样 LF**（⚠ 这 6 个文件原本就是 **LF**，**没有**做任何 CRLF 转换）。
- 语法：`[Parser]::ParseFile` 对两个 `.ps1` = **0 错**（纯 ASCII ⇒ 不存在"无 BOM 被按 ANSI 解码报假错"的风险）。
- 归档后 `tools\` 顶层文件 19 → **13**；`archive\20261009\` 递归 74 → **81** 文件。（旧文写"73"是漏数，以实际为准。）
- ⚠ 依赖文件 `tools\gcu-variant-backup.txt` **全仓不存在**（只有引用、没有实体），所以没有可一起归过来的备份件；改用 `Join-Path $PSScriptRoot` 后，`enable_*` 写 / `restore_*` 读都落在归档目录内，不再往 `tools\` 顶层写东西。
- 边界：**未打包、未部署、未跑任何修复脚本、未 kill / 重启 LM、未连 13688、未写注册表 / EC / NVRAM**；未动 `source\` / `installer-src\`，未动其他 `tools\` 脚本（`GCU抓包.ps1` / `SRGB诊断.ps1` / `官方控制台控灯抓包.ps1` / `诊断-GCU连不上.ps1` / `merge_and_clean.py` / `publish_lm.rsp` / 发货件），**桌面副本未动**（桌面本来就没有这 6 个文件的副本）。
