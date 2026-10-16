# 存档 · GCU 载荷外置 +「预压 7z」方案（⛔ 仅存档 / 未开工）

> ⚠️ **状态：2026-10-12 存档，⛔ 未开工。**
> 本轮**只写了这份文档**，并**只追加**了 `修改日志.txt` 一条、`待办.txt` 一条：
> ⛔ 未改任何 `.iss`、⛔ 未改任何脚本（`Install-Gcu.ps1` / `Select-GcuPayload.ps1` / `Uninstall-Gcu.ps1` 一律未动）、
> ⛔ 未改/未复制任何载荷、⛔ 未动版本号、⛔ 未构建、⛔ 未跑 ISCC、⛔ 未出包。
> 本文档的唯一目的：把**设计想法 + 今天查实的事实 + 建议顺序**记下来，免得以后重新推一遍。

---

## ① 用户原话（⛔ 逐字引用）

1. 「这体积膨胀厉害 我目前思考是吧10-20 30 40 50的GCU打成7-zip 通过软件内置7zip组件安装时候解压对应的GCU怎么说,也可以为以后分离GCU开源做准备」
2. 「只是一个想法 没叫你开工」
3. 「对,我就思考把ACPI载体外置,可以为LM改造预留接口」

---

## ② 目标

- ⚠️ **抑制包体积膨胀**。参照物：`1020` 那棵树**单项**就 **42,504,669 B / 179 文件**
  （实测：`_upstream\0.290.6\extract\{app}\GCU\payload\1020` = 179 文件 / 42,504,669 B，
  顶层为 `ACPIDriver\` + `UniwillService\` + `NOTICE.txt`；
  `archive\unpacks\beta21\_extract\{app}\GCU\payload\1020` 与之同为 179 / 42,504,669 B）
  —— ⚠️ 而**我们仓库现在完全没有它**：`installer-src\files\GCU\payload\` 下只有
  `30` / `40` / `40B` / `50` / `GCU` 五棵，**没有 `1020`**。
- ⭐ 将来把 GCU 载荷**分离**出去：外部存放、**带版本与哈希清单**、可按需获取。
- ⭐ 为「分离 / 开源**我们自己的胶水**」做准备（胶水 = 我们的安装脚本、选择器、校验与安装动作）。

---

## ③ 今天（2026-10-12）已查实的事实

### 3.1 ⚠️ Inno 自己的压缩设置（⭐ 这条决定了"预压 7z"有没有意义）

- ⭐ `installer-src\install_script.iss:20` → **`Compression=lzma2`**
  （同目录另两份 iss 取值相同：`install_script_nogcu.iss:20` = `Compression=lzma2`、
  `install_script_LMEXP_gen30on40b.iss:52` = `Compression=lzma2`）
- ⚠️ **`SolidCompression=` 在 `installer-src\` 下的任何 `.iss` / `.txt` 里都【不存在】**
  （本轮全文检索 0 命中）⇒ ⭐ 实际取 Inno 的**默认值**。
- ⭐ 依 Inno Setup 6 官方文档：
  - `Compression` 默认 = **`lzma2/max`** ⇒ 写 `Compression=lzma2` 等于**默认档，没调过**
    （<https://jrsoftware.org/ishelp/topic_setup_compression.htm>）
  - `SolidCompression` 默认 = **`no`** ⇒ **当前包是「逐文件」压缩，不是 solid**
    （<https://jrsoftware.org/ishelp/topic_setup_solidcompression.htm>）
  - ⚠️ 官方文档同时写明：LZMA2 对**不可压缩数据**只膨胀约 **0.005%**（原版 LZMA 是 1.35%）
    ⇒ ⭐ 把已压好的 7z 再交给 Inno 压一次，**代价极小**。
- ⚠️ 本轮**未编译实测**上述默认值（⛔ 未跑 ISCC）。本机装有
  `C:\Program Files (x86)\Inno Setup 6\ISCC.exe`，但按用户边界要求未使用它。
- ⭐ **由 3.1 得出的结论（要点）**：
  1. 「预压成 7z」**不是**在重复 Inno 已经在做的事 —— Inno 现在是**逐文件** lzma2/max，
     而 7z 是 **solid**（跨文件共享字典）⇒ ⭐ **理论上确有增益空间**；
  2. ⚠️ **但增益幅度必须【先实测】**，不能拍脑袋。三个变体分别测、再测整包，
     并与 Inno 自身 LZMA2 的结果对比；
  3. ⭐ 另有一条**更省事**的对照路线：给 iss 加 `SolidCompression=yes`（一行），
     直接看包能小多少 —— ⚠️ 代价是解压不再能随机访问、单文件出错重试要回退整条流。
- ⭐ 现包基线（实测）：
  `installer-src\Output\LM-0.291.7-beta22.7-setup.exe` = **69,619,142 B** /
  SHA-256 `C2C775A35CD65199498575FAFB2AC589A0DEB30B4C689DDD70C07F8F133669D4` /
  mtime **2026-10-12 15:40:25**。

### 3.2 ⚠️ `Install-Gcu.ps1` 目前**只支持一种布局**：「base `payload\GCU` + 覆盖层」

- ⭐ 脚本头注释自己写死了这一点（`installer-src\files\GCU\Install-Gcu.ps1:14-21`，
  尤其 `:17-18`「Base+overlay is therefore the **ONLY** layout again for every variant」）。
- ⭐ 落点与目录来源：
  - `:1264` `$baseServiceSource = …StagingRoot 'payload\GCU' + $selection.ServiceDir`（base 树）
  - `:1265` `$trioServiceSource = …StagingRoot $selection.StagedDir + $selection.ServiceDir`（覆盖层）
  - `:1267-1268` `$driverSource` / `$driverTarget`（`payload\GCU\UWACPIDriver` → `{app}\GCU\UWACPIDriver`）
- ⭐ 拷贝顺序（先 base 后覆盖层，同名者后者胜）：
  - `:1431` `Copy-Tree -Source $baseServiceSource …`
  - `:1432` `Copy-Tree -Source $trioServiceSource …`
  - `:1433` `Copy-Tree -Source $driverSource -Destination $driverTarget`
- ⚠️ 而 1020 载荷是**自成一体的** `UniwillService` + `ACPIDriver` 布局
  （实测顶层：`ACPIDriver\` + `UniwillService\` + `NOTICE.txt`）——
  ⛔ **与 base+覆盖层完全不同形**，脚本里**没有任何代码路径**能消费它。
- ⭐⭐ **布局泛化必须先做**：
  ⛔ 否则把 `Select-GcuPayload.ps1` 的 legacy 分流改指 `payload\1020`，
  会让 10/20 系机器在**安装期硬失败**（⚠️ 比现状更糟）。
- ⭐ 补充更正（⚠️ 重要）：**现状里根本没有 `payload\1020` 这条分支可改**。
  `installer-src\files\GCU\Select-GcuPayload.ps1` 的 bundle 表只有
  `GCU / 50 / 40 / 30 / 40B`（`:136-142`），legacy（pre-30 NVIDIA）现在是
  **落到 30 系补丁**（`:379` `elseif ($detected.Generation -eq 'legacy') { '30' }`，
  reason 见 `:386`）⇒ 「legacy 分流改指 `payload\1020`」是**假设中的将来改动**，不是现状。

### 3.3 ⭐⭐ ACPI 驱动是**真正装进 Windows** 的（不是同目录加载）

- ⭐ `installer-src\files\GCU\Install-Gcu.ps1:973`
  → **`pnputil.exe /add-driver $inf /install`**（在 `Install-UwacpiDriver` 内，函数体 `:966-990`；
  `$inf` 见 `:968`，找不到即 throw `:969-971`）。
- ⭐ 调用点：`:1459-1460` `[5/8] installing UWACPI kernel driver` → `Install-UwacpiDriver -DriverDir $driverTarget`。
- ⭐ 先 `Copy-Tree` 到 `{app}\GCU\UWACPIDriver`（`:1268` 定义目标，`:1433` 实际拷贝）。
- ⭐ `installer-src\files\GCU\payload\GCU\UWACPIDriver\UWACPIDriver.inf` 关键行：
  - `:12` `PnpLockdown=1`
  - `:15` `DefaultDestDir = 12`
  - `:34` 硬件 ID `ACPI\INOU0000`（行形如 `%UWACPIDriver.DeviceDesc%=UWACPIDriver_Device, ACPI\INOU0000`）
  - `:52` `ServiceBinary  = %12%\UWACPIDriver.sys`
- ⭐ 本轮**只读实测**落点（⛔ 未 pnputil、⛔ 未装卸驱动）：
  - `C:\Windows\System32\drivers\UWACPIDriver.sys` 存在，**46,352 B**（与载荷内 sys 同尺寸）；
  - DriverStore：`…\FileRepository\uwacpidriver.inf_amd64_1ed39a93aac84c8d`（2026-10-04 16:50:54）
    与 `uwacpidriver.inf_amd64_a5ddd772c5a173a3`（2026-10-05 17:11:04）**两份**（历史上装过两版）；
  - 节点 `ACPI\INOU0000\0`：`Status=OK` / `Class=System` / **`Service=UWACPIDriver`** /
    `DriverProvider=Uniwill` / `DriverVersion=10.48.11.331` / `DriverInfPath=oem0.inf` /
    `DriverDesc=ACPIDriver`（⚠️ DeviceDesc 字符串本身就是 "ACPIDriver"，所以 FriendlyName 显示 ACPIDriver）；
  - ⭐ DriverStore 里**没有** legacy `acpidriver.inf_amd64*`（`acpidriver.inf` 未进过本机驱动库）。
  - ⇒ ⭐ 与「节点由 ACPI 总线枚举，`pnputil` 只装载/绑定到**已存在**的节点」一致。
- ⭐ 因此：**临时解压目录在 `/install` 成功之后可以删**。
- ⚠️ **但**现流程依赖 `{app}\GCU\` **常驻副本**做三件事，改「解压后删」必须把它们挪进解压目录内完成：
  1. `:1451` `Assert-SignedFile -Path $driverSys -Label 'UWACPIDriver'`（签名校验；
     同批还有 `:1449` GCUBridge、`:1450` GCUService）；
  2. 载荷落地 / 哈希校验（`:1320-1322` 一族的 `Assert-SignedFile` + 载荷清单身份，
     见脚本头 `:8-13` 与 `Get-GcuPayloadIdentity` / `Merge-GcuPayloadManifest`）；
  3. 每次安装从该目录**重跑 pnputil 的幂等分支**（`:976-986`：exit≠0 时
     `pnputil /enum-drivers` 找 `uwacpidriver.inf`，找到则记 "already present … continuing"，
     否则 throw `:984`）。

### 3.4 ⚠️ 两个 ACPI 驱动的体积（结论：**从载荷里删掉它们不可行**）

- ⭐ 我们的 `UWACPIDriver` 三件（`installer-src\files\GCU\payload\GCU\UWACPIDriver\`）：
  `uwacpidriver.cat` 11,342 + `UWACPIDriver.inf` 2,034 + `UWACPIDriver.sys` 46,352 = **59,728 B**
- ⭐ legacy `ACPIDriver` 三件（`_upstream\0.290.6\extract\{app}\GCU\payload\1020\ACPIDriver\`）：
  `acpidriver.cat` 10,103 + `ACPIDriver.inf` 1,585 + `ACPIDriver.sys` 36,184 = **47,872 B**
- ⭐ 两套合计 **107,600 B ≈ 105.08 KiB**，占现包 69,619,142 B 的 **≈0.15%**
  ⇒ ⛔ **删掉「不可行」且毫无意义**：它们是**安装源**（`pnputil` 要从这里面取 inf/cat/sys），
  省下的量对包体积没有可见影响。

### 3.5 ⚠️ 这类驱动**必须签名**（未签名 = 装不上）

⇒ ⭐ 「为 LM 改造预留接口」的现实形态是：**只接受已签名且硬件 ID 兼容的驱动包**
（⛔ 不是"随便塞一个 sys"）。现流程已有 `Assert-SignedFile`（`Install-Gcu.ps1:82`，
调用点 `:1320-1322` / `:1449-1451`）作为这一约束的执行点。

---

## ④ 建议顺序（⭐ 写下来备查）

1. ⭐ **先实测 7z 增益**：各变体分别打成 7z + 整包打成 7z，
   并与 **Inno 自身 LZMA2（逐文件 vs `SolidCompression=yes`）** 的结果对比。
   ⚠️ 没有这一步的数据，后面三步都不该动。
2. ⭐ **泛化 `Install-Gcu.ps1` 的布局支持**（含 1020 那种「自成一体的变体树」布局），
   让 `Select-GcuPayload.ps1` 将来才有可能安全地分流到非 base+overlay 的树。
3. ⭐ **抽象「载荷来源」**：让安装期只消费一个**解析出来的目录** ——
   来源可以是 ① 包内、② 本地外置目录、③ 以后按需下载的工件。
   （现有基础：`installer-src\files\GCU\gcu-variants.json` 47,272 B、`GCU变体目录说明.md` 13,902 B。）
4. ⭐ **再谈外置 / 分离**：变体选择规则 + 清单与 SHA-256 + 各变体的安装动作
   做成**数据而非硬编码**。

---

## ⑤ 状态与边界（⭐ 明确标注）

- ⭐ **状态 = 仅存档，未开工。**
- ⛔ **代码 / 脚本 / 载荷 / 版本号一律未改**：`install_script.iss`（及另两份 iss）、
  `Install-Gcu.ps1`、`Select-GcuPayload.ps1`、`Uninstall-Gcu.ps1`、
  `installer-src\files\GCU\payload\**`（含 `UWACPIDriver` 三件）、版本号 —— **全部零改动**。
- ⛔ 未构建、⛔ 未跑 ISCC、⛔ 未出包、⛔ 未动 `installer-src\Output\`。
- ⛔ `_upstream\` 只读（仅做了尺寸统计与内容读取，未写入）。
- ⛔ 未动 `payload\` / `backup\` / `archive\` / `diagnostics\`。
- ⛔ 未动 `docs\交接-WiFi与蓝牙磁贴图标.md`（用户明令：一个字都不动）。
- ⛔ 未改电源计划、⛔ 未装/卸驱动（对 `ACPI\INOU0000\0` 与 DriverStore 只做了**只读查询**）、
  ⛔ 未创建/删除显示设备、⛔ 未写注册表/EC/NVRAM、⛔ 未发 SMU 命令、⛔ 未写 MSR、
  ⛔ 未手工发任何真实 EC/GCU 命令、⛔ 未连 13688。

### ⚠️ 本文档中「未实测 / 不确定」的部分

- ⚠️ 7z 预压到底能省多少 —— **完全未实测**（本文档 ③.1 只给出"有增益空间"的**推论**，不是实测值）。
- ⚠️ `Compression` / `SolidCompression` 的**默认值**取自 Inno 官方文档，
  **未在本机编译验证**（⛔ 未跑 ISCC）。
- ⚠️ 1020 载荷「自成一体的 UniwillService + ACPIDriver 布局」的**安装动作**
  （谁注册服务、`InstallUtil` 还是别的、要不要 pnputil）**本文档未查实、未记录**。
- ⚠️ 3.3 里 DriverStore 的**两份** `uwacpidriver.inf_amd64_*` 分别对应哪次安装、是否可清理 —— 未查。
