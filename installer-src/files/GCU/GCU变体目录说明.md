# GCU 变体目录说明（为「外置 GCU 挂载包」做参考）

本目录文件：`installer-src\files\GCU\gcu-variants.json`（机器可读，**纯 ASCII**）+ 本说明。
用途：把**五个 GCU 载荷变体**（`1020` / `30` / `40` / `50` / `WJXY`）的身份（文件数、字节数、7z 体积）、
安装期解压/规整后的形态、以及变体判据，集中成一份**可复核**的目录，作为将来做「外置 GCU 挂载包」的准备与参考。

> ⚠️ **2026-10-14 结构变更（本轮）**：随包载荷从「base 树 + 覆盖层」改成 **5 个独立完整 7z**
> （每代一份、无共用树），安装期由 `Install-Gcu.ps1` 按选中变体解压并**直接落地**到
> `{app}\GCU\AiStoneService\...`。**40B 覆盖层整体已移除，40 系一律走 `40`。**
>
> ⭐⭐ **2026-10-14 落地口径纠偏（owner 原话，本段为准）**：
> 「修改安装逻辑解压出来的 GCU 应该放在 安装目录的这个文件夹
> `C:\Program Files\L-Mechrevo\GCU\AiStoneService\` 而不是 shard，而 payload 应该作为临时文件
> 而不是安装到安装目录里面」·「ps1 留 卸载用到的 7z 不留 没用到的 GCU 也不留」·
> 「logo 放在 GCU 文件夹」·「按你建议来 如果 bat 确实内容重复就不要」。
> ⇒ ⛔ 不再有 `{app}\GCU\payload\<变体名>\` 这一层；5 个 7z 与 `7za.exe` 是**安装期临时文件**
> （`[Files] Flags: dontcopy` → 解到 `{tmp}` → 解压完即删），装完不留；`shared\` 这个层级本身
> 也不再落盘（内容按目的地铺开）。
>
> **装完之后 `{app}\GCU\` 下只有**：`AiStoneService\`（解压出来的完整 GCU 本体）·
> `logo\Language\`（与 AiStoneService 同级 —— 旧结构 `payload-old\GCU\` 下就是
> `AiStoneService\` / `logo\` / `UWACPIDriver\` 三个同级目录）· `README.txt` · `NOTICE.txt`
> （同样照旧结构落在 base 层根下）· 3 个 `.ps1`。
> ⛔ 没有 `payload\`、⛔ 没有 `7za.exe`、⛔ 没有 `shared\`、⛔ 没有没用到的变体、
> ⛔ 没有厂商的 `install.bat` / `uninstall.bat` / `清理官方控制台.bat`（内容与我们的 ps1 重复，已删）。
> `{app}\UWACPIDriver\` 三件在**应用根目录**（不是 GCU 里）。

---

## 一、随包形态（`installer-src\files\GCU\`）

| 路径 | 体积 | 说明 |
|------|------|------|
| `payload\1020.7z` | 9,167,136 B | 10/16/20 系（GamingCenterU 世代，内部 `20\UniwillService`） |
| `payload\30.7z` | 8,802,015 B | 30 系（内部 `30\UniwillService`） |
| `payload\40.7z` | 14,444,731 B | 40 系（内部 `40\AiStoneService`）——**40 系唯一载荷** |
| `payload\50.7z` | 15,477,703 B | 50 系（内部 `50\AiStoneService`，另有同级 `logo\` / `UWACPIDriver\`，**都不铺**） |
| `payload\WJXY.7z` | 17,484,769 B | XINGYAO / LNL 机器专用（内部 `WJXY\AiStoneService`） |
| `7za.exe` | 1,335,808 B | 7-Zip **26.04 x64** console；安装期解压工具（`dontcopy`，用完即删） |
| `logo\Language\` | 2 文件 / 58,503 B | 由 owner 定位于此（与 `AiStoneService` 同级），落 `{app}\GCU\logo\Language\` |
| `shared\` | 2 文件 / 2,706 B | 只剩 `README.txt` / `NOTICE.txt`（另 3 个 `.bat` 已删），落 `{app}\GCU\` |
| `payload-old\` | 281 文件 / 118.4 MB | ⛔ **回退用旧目录树，只读，勿删勿改** |

⚠️ 上表 6 个安装期文件（5 个 7z + `7za.exe`）在 `[Files]` 里是 `Flags: dontcopy`：
**它们不属于安装文件集**，只在 setup 进程存活期间被解到 `{tmp}` 使用，装完 `{app}` 里不会出现。

`shared\` 现存清单（每项均与旧 base 树逐字节相同，已实测 `same=True`）：

| 相对路径 | 字节 |
|----------|------|
| `shared\UWACPIDriver\UWACPIDriver.inf` | 2,034 |
| `shared\UWACPIDriver\uwacpidriver.cat` | 11,342 |
| `shared\UWACPIDriver\UWACPIDriver.sys` | 46,352 |
| `shared\README.txt` | 2,252 |
| `shared\NOTICE.txt` | 454 |

⛔ **已删（内容重复，owner 拍板「如果 bat 确实内容重复就不要」）**：
`shared\install.bat`（2,951 B，robocopy + 签名 + pnputil + `sc create`，且硬编码
`%ProgramFiles%\L-Mechrevo\GCU` 并假设驱动在 GCU 目录下；`Install-Gcu.ps1` 全都有且更强）·
`shared\uninstall.bat`（454 B，`sc stop/delete` + 删防火墙规则；`Uninstall-Gcu.ps1` 更强）·
`shared\清理官方控制台.bat`（2,391 B；`Install-Gcu.ps1` 的 `Remove-VendorConsole` 覆盖同样名单且更全）。
它们原本也从不被我们的流程调用。

---

## 二、安装期「解压 + 落地 + 回灌」契约

`Install-Gcu.ps1`（`[Run]` 里执行）：

1. **`[3/8]` 先备份用户数据**（`UserPofiles` / `UserFanTables` →
   `%TEMP%\.lm-gcu-userdata-<时间戳>\`；⛔ 不再落在安装目录里）；
2. **选 7z**：由 `Select-GcuPayload.ps1 -Variant Auto` 判定 → `Get-GcuPayloadArchive -Variant <V>` 得到
   `payload\<V>.7z`（在 `-StagingRoot` = `{tmp}` 下）；
3. **解压到临时目录**（`7za.exe x -o<tmp> -y <archive>`），断言内层根与目录一致，并检查
   `GCUService.exe` / `GCUBridge.exe` 齐备；
4. **`[4/8]` 先验签**（`Assert-SignedFile`：`GCUBridge.exe` / `GCUService.exe` / `UWACPIDriver.sys`），
   **验签通过之后**才把解压出来的服务目录**整体复制**到 `{app}\GCU\AiStoneService\`；
5. **回灌用户数据**（备份里的文件胜出），并**删掉 `{tmp}` 里的 7z 与 `7za.exe`**。

⚠️ **两个顺序都是本轮的核心保证**：
* **「备份 → 解压/覆盖 → 回灌」** 不能乱 —— 否则 7z 里的默认风扇表
  （`UserFanTables\M1T1.json` …）会把用户自己调过的曲线覆盖掉。
* **「验签 → 再落地」** 也是必须的：解压产物与目的地现在是**同一个目录名**，若先复制再验签，
  一个损坏/截断的 7z 就会把一个本来能用的载荷覆盖成坏树，然后才中止 —— 这正是上一版
  留下的「只剩风扇表、没有 GCU 本体」那个坏状态。先验签 ⇒ 坏包在动安装目录之前就中止。

⚠️ **内层根与变体名不一致**：`1020.7z` 内是 `20\`、`30.7z` 内是 `30\`，两者内部服务目录都还叫
`UniwillService`。**落地时统一改成 `AiStoneService`**（见 `Install-Gcu.ps1` 的
`Get-GcuServiceLeafName` 及其理由注释）。`1020/30` 的注册表 `GcuServiceDir` 也随之写
`AiStoneService` —— 落盘的名字就是注册表里写的名字。

⚠️ **为什么改名安全（已实测）**：厂商二进制**不读自己的目录名** —— `service.ini` 全文只有
`[serviceinit]` + `process=True`，没有任何路径；5 个 7z 里唯一写死产品路径的地方是
`MyControlCenter\Command\ControlCenter.bat` 里的
`C:\Program Files\OEM\Control Center\AiStoneService\...`（厂商自己的安装位置，**本来就是
AiStoneService**）。⇒ 改名不会打断自引用，反而让老的 `UniwillService` 包与厂商自己到处用的
那个名字一致。另外，改名还消除了「源与目的地同形 ⇒ 复制后嵌套出
`AiStoneService\AiStoneService\`」这一整类失败。

---

## 三、变体判据（2026-10-14 定稿，实现见 `Select-GcuPayload.ps1`）

判定顺序：

1. `-Variant <1020|30|40|50|WJXY>` 显式指定 → 直接用，跳过硬件判定；
   其他任何值（含 `Auto`、未知值如 `99`）→ 落到第 2 步。
2. **机型串含 `XINGYAO` 或 `LNL`（大小写不敏感）→ `WJXY`**；
3. 否则走既有的 30/40/50 判据（GPU 营销名 / NVIDIA PCI device-id 高位）；
4. 什么都判不出来 → **走现有兜底（50 系）并记日志**，⛔ **不静默乱选**。

⛔ **40B 已整体移除**：`-Variant 40B` 现在按 `40` 处理（只打印一条 "retired" 日志）。
`GcuVariantFamily.cs` 仍保留 `Resolve("40B") = "40"`，仅为兼容上一版在机器上写下的旧注册表值。

判据日志格式（一行，纯 ASCII，装机主导包可直接采集）：

```
GcuVariantSelection | variant=<V> | archive=<file.7z> | note=<机型串 + 命中关键字 + 选中变体 + 解压了哪个 7z>
```

例：

```
GcuVariantSelection | variant=WJXY | archive=WJXY.7z | note=machine string 'MECHREVO XINGYAO Series' contains a WJXY keyword -> WJXY special variant
GcuVariantSelection | variant=50 | archive=50.7z | note=GPU generation judge: UNKNOWN (...) | GPU generation unresolved -> safety floor 50 payload (no silent guessing)
```

---

## 四、身份口径（沿用原口径，供外置挂载包参考）

`Get-GcuPayloadIdentity -ServiceDir <某个 AiStoneService 目录>`：逐文件一行
`相对路径|字节数|sha256`，相对路径**相对服务目录**、小写、反斜杠；按 **Ordinal** 排序；
以 **LF** 连接；整体再 SHA256（UTF-8、小写十六进制）。排除叶子名
`readme.txt / notice.txt / license.txt / readme.md`。

⚠️ **别用变体目录当根**：根级 `README.txt` / `NOTICE.txt` / 厂商 `.bat` 在服务目录之外，
用整棵树算会得到不同值。7z 化之后，每个 7z 是一个**完整**变体，所以身份仍然按它的
服务目录（`AiStoneService` 或 `UniwillService`）算，不能用 7z 文件本身的 SHA 当载荷身份。

复核命令（只读，几秒）：

```powershell
. 'D:\openLM\installer-src\files\GCU\Install-Gcu.ps1' -StagingRoot x -TargetDir y   # 只为过 param 绑定
$v = Get-GcuPayloadIdentity -ServiceDir 'D:\openLM\installer-src\files\GCU\payload-old\40\AiStoneService'
$v.Sha256
```

---

## 五、已移除 / 已作废（勿再引用）

* `payload\GCU`（base 共用树）与 `payload\40B` / `payload2`：结构已废，随包树只剩 `payload\*.7z`。
  `install_script.iss` 中指向它们的 `Source:` 已全部删除（旧树在 `payload-old\` 只读保留）。
* 40B 询问（"是否支持核显模式"）：`install_script.iss` 的 `AskGcuVariantFromUser` /
  `IsGcu40BPayloadUsable` 已整个删除，40 系直接走 `40`。
* `install_script.iss` 卸载项 `{app}\GCU\payload2` 已删；`{app}\GCU\payload` 保留（5 个 7z +
  解压产物都在它下面）。
* `install_script.iss` 升级回灌里的「变体级 `payload\<变体>\…\UserPofiles|UserFanTables`」
  写入已移除（7z 解压晚于该步，写过去只会打到上一轮的旧树甚至打空）。
