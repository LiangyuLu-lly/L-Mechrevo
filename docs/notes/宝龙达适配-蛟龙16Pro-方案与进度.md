# 宝龙达（Baolongda）模具适配 —— 蛟龙16Pro 2022 / 2023 / 2024

> ⭐ **本条线 = 版本线 `0.291.9 / beta22.9`**，工作区 ⭐ `D:\openLM-baolongda\`。
> ⛔ **不在** `D:\openLM\`（那边是 **0.291.8.1 / beta22.8.1**，只做 bug 修复）。

---

## 1. 两条线怎么分（机主 2026-10-16 决定）

| | 目录 | 版本 | 干什么 |
|---|---|---|---|
| **主目录** | `D:\openLM\` | **0.291.8.1 / beta22.8.1** | ⭐ **只做 bug 修复**（根据机主反馈持续修），⭐ **随时可打包** |
| **适配副本** | `D:\openLM-baolongda\` | ⭐ **0.291.9 / beta22.9** | ⭐ **专心做宝龙达适配** |

机主原话：「2022/2023/2024 机械革命**蛟龙16Pro** 准确来说 因为不确定22.8的bug修好没有
**先开一个版本专心做适配 然后22.8根据反馈继续修bug**」。

⚠️ **为什么不共用一份**：适配要动后端探测/能力集，改到一半时**不能打包**；
而 22.8 那条线必须**随时能出包给机主修 bug** ⇒ 分开最安全。

## 2. 目标机型

- ⭐ **机械革命 蛟龙16Pro**，**2022 / 2023 / 2024** 年款
- 特征：**宝龙达（Baolongda）模具** —— ⚠️ **不走同方的 GCU 服务**（GCU = MQTT `127.0.0.1:13688`，同方/AiStone 那套）
- 机主 2026-10-15 说明：「**ACPI 到时候会另外准备一套给宝龙达**」⇒ 后端为那套接口预留

## 3. ⭐ 参考实现（三个仓库，⭐ 2026-10-16 全部克隆并读过）

| 仓库 | 是什么 | 对我们的价值 |
|---|---|---|
| ⭐⭐ `GaoXanSheng/JiaolongControl`（分支 `windows/X86-64`） | ⭐ **蛟龙 16 PRO 笔记本控制中心**（自述：基于 7945HX + RTX 4060 开发，兼容其他 16 PRO [2023]） | ⭐ **就是我们目标机型的现成实现** ⇒ 功能号/协议以它为准 |
| `Aveare/BellatorFanControl` | 从**官方斗战者控制台**逆向出来的协议记录 + 参考实现 | 协议表经官方控制台验证，可交叉核对 |
| `KanzakiK/DOUZHANZHE-Control` | 斗战者控制台（MIT） | 另一份独立实现，可交叉核对 |

- ⭐ 克隆都在 `D:\openLM-baolongda\_refs\`（⚠️ 属第三方代码，⭐ 不入我们的 git）
- ⚠️ 本机 `github.com` **直连被阻断** ⇒ ⭐ 走 `gh-proxy.com` 代理克隆（⭐ 命令见接口笔记第 7 节）
- ⭐⭐ **三份在核心协议上完全一致** ⇒ 可信度很高 ✅
  （实例名 `ACPI\PNP0C14\MIFS_0` · `InData[1]=250/251` · `InData[3]=功能号` · 8/13/20/21/22 含义）
- ⚠️ **唯一不一致**：性能模式编号语义 ⇒ ⭐ 见接口笔记 4.1（⭐ 会导致静音/狂暴颠倒，⚠️ 必须先处理）

## 4. 已有基础（架构早就为这一天准备好了）

```
⭐ `source\MechrevoLite.Hardware\MechrevoBackend.cs`
   ⭐ 枚举 `MechrevoBackendKind { None=0, Gcu=1, LegacyMifs=2, ⭐ **Baolongda=3** }` —— 值早就预留
   ⭐ 探测是**数据驱动**的：`MechrevoBackendOrder.Candidates` 数组，⭐ **加一项即可**
   ⭐ 文件头原话：「以后新增后端（例如宝龙达模具那套另行准备的 ACPI 驱动）**只需加一项**：
      ① 枚举值；② 写一个『返回非 null 即成功』的探测委托；③ 加进 Candidates 数组。
      ⛔ MechrevoHw 里不用改任何 else/if」
⭐ 模板：`LegacyMifsWmi.TryCreate()`（老机型 `root\WMI:MICommonInterface`）——
   ⭐ WMI 类 + **GUID 校验** + **2 s 超时** + 试读一次档位，⭐ 失败一律返回 null
⭐ 接线点：`MechrevoHw.ConnectAsync` 里 `MechrevoBackendOrder.Probe()` 之后按 kind 分支，
   ⭐ 仿 `ApplyLegacyBackendAsync`（设 `_activeBackend` + 能力集 + 起定时器 + `NotifyConnectionReady`）
```

## 5. 本轮（0.291.9 第 1 步）做了什么

✅ **已做**：

1. 新建 ⭐ `source\MechrevoLite.Hardware\BaolongdaBackend.cs`（**骨架**）
   - `BaolongdaBackend.TryCreate()` —— 探测入口，⚠️ **默认恒返回 null**（⛔ 不影响任何现有机型）
   - `IsImplemented = false` —— 明示"还没接协议"
   - 实验开关 ⭐ `LMECHREVO_BAOLONGDA_BACKEND=1` —— 将来真机调试用；**即使打开也只记一行日志、仍不接管**
   - `BuildCapabilities()` —— 与 `ApplyLegacyBackendAsync` 同口径的**最小能力集**
2. `MechrevoBackendKind.Baolongda` 注释更新（从"预留占位"改为"已开始适配"）
3. ⭐ 候选表加了第 4 项（**放在 GCU 无条件兜底之前**，否则永远轮不到它）
4. ✅ 编译 **0 错误**（1450 警告 = 基线 1449 + 新增文件 1）
5. ✅ 用 `ilspycmd` 确认 `MechrevoLite.Hardware.BaolongdaBackend` **已编进程序集**

⚠️ **刻意没做**（等资料到位）：

- ⛔ 没写真实探测（现在**没有机器、也没有那套 ACPI 接口说明** ⇒ 盲写会"探中但用不了"，反而挤掉可用路径）
- ⛔ 没写档位读写
- ⛔ 没在 `MechrevoHw.ConnectAsync` 加分支（等探测真能成功再加）

## 6. 下一步（按顺序）

```
⭐ ① ⭐ **拿到资料**（需要机主提供，任选其一即可开工）：
      · 宝龙达那套 **ACPI / WMI 接口说明**（类名、GUID、方法、数据布局）；或
      · 一台 **蛟龙16Pro（22/23/24 款）** 上的 **抓包 / 诊断包**；或
      · ⭐ `DOUZHANZHE-Control` 仓库**下载到本地**（本会话访问不了 github.com）
⭐ ② ⭐ 在 `TryCreate()` 里实现**真实探测**（仿 `LegacyMifsWmi.TryCreate`）
⭐ ③ ⭐ 实现档位读/写（`ReadMode` / `WriteMode`）+ 补全能力集
⭐ ④ ⭐ 加进候选表（**已就位** ✅）+ 在 `MechrevoHw.ConnectAsync` 加 `ApplyBaolongdaBackendAsync` 分支
⭐ ⑤ ⭐ 真机验证通过后 **去掉实验开关**，改为正常探测
```

## 7. 两个工作区怎么同步

⚠️ **不是 git**，所以改动**不会自动同步**。规矩：

- ⚠️ 适配分支将来要合回主线时：⭐ `git merge baolongda`，⭐ 冲突（尤其版本号）**由人决定**

## 7b. ⭐ 两条线怎么协作（⭐ 现在有 git 了，2026-10-16 起）

⚠️ ~~以前是两个目录手工同步~~ ⇒ ⭐ **现在用 git 分支** ✅

```
⭐ 仓库   : ⭐ `D:\openLM\`（⭐ 已初始化本地 git，⛔ 无远程）
⭐ 分支   : ⭐ `main` = **0.291.8.1**（修 bug 线，随时能打包）
            ⭐ `baolongda` = **0.291.9 / beta22.9**（本条适配线）
⭐ 切分支 : ⭐ `git checkout main` / ⭐ `git checkout baolongda`
⭐ 提交   : ⭐ 每完成一小块就 `git add -A` ＋ `git commit -m "…"`
⭐⭐ 铁律  : ⭐ ① 切分支前先 `git status` ② ⭐ **打包前先 `git branch --show-current`** ③ 每完成一小块就提交
⭐ 小抄   : ⭐ `docs\notes\git-小抄.md`（⭐ 切/提交/挑拣/合并/撤销/冲突处理 ✅）
⚠️ 备份副本 `D:\openLM-baolongda\` ⭐ **保留**（⭐ 冗余 + ⭐ 参考仓库 `_refs` 的家 ✅），
   ⭐ 但**不再是主力工作区** —— ⭐ 干活在 `D:\openLM\` 的 `baolongda` 分支上 ✅
⚠️ ⚠️ 教训：⭐ 同步副本时**方向别搞反** —— ⭐ 从 `D:\openLM\` → ⭐ `D:\openLM-baolongda\`（⭐ 以 git 仓库为准 ✅），
   ⛔ 反过来会把改过的内容**覆盖掉**（⭐ 已踩过一次 ✅）
```

## 8. 变更记录

| 日期 | 变更 |
|---|---|
| 2026-10-16 | 建副本 `D:\openLM-baolongda\`（0.291.9/beta22.9）；补 `lm_pkg_files`；加后端骨架 + 候选表第 4 项；编译 0 错误 |
| 2026-10-16 | 初始化本地 git（`main`=0.291.8.1 ＋ 标签 `v0.291.8.1`；`baolongda`=0.291.9）；适配骨架迁到 `baolongda` 分支；写 git 小抄 |
| 2026-10-16 | 做独立抓包工具（`抓包工具-宝龙达\`，已实测跑通） |
| 2026-10-16 | ⭐ 克隆并读完**三个**参考仓库；⭐ 拿到**蛟龙16Pro 的完整协议**（功能号全表 8~23、风扇布局、CPUPower 子命令、EC 直写协议与寄存器表）⇒ ⭐ 见接口笔记；⚠️ 发现**性能模式编号语义冲突**（必须先处理）；抓包脚本加 MIFS 实例探测与只读试调用 |
