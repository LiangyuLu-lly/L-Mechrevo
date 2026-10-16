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

## 3. 参考实现

- `https://github.com/KanzakiK/DOUZHANZHE-Control`（斗战胜佛 Control，机主提供）
- ⚠️ **本会话访问不了 github.com**（域名解析被阻断）⇒ 需要先把仓库**下载到本地**再读

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

- 宝龙达改动 → ⭐ **只在 `D:\openLM-baolongda\` 里做**
- 22.8 的 bug 修复 → ⭐ **只在 `D:\openLM\` 里做**
- ⭐ 若某修复**两边都要**（例如通用 bug）⇒ **两边各改一次**，或从主目录复制那个文件过去
- ⚠️ 适配分支将来要合回主线时：⭐ **按文件挑**，⛔ 别整目录覆盖（会把 22.8 的修复冲掉）

## 8. 变更记录

| 日期 | 变更 |
|---|---|
| 2026-10-16 | 建副本 `D:\openLM-baolongda\`（0.291.9/beta22.9）；补 `lm_pkg_files`；加后端骨架 + 候选表第 4 项；编译 0 错误 |
