# -*- coding: utf-8 -*-
import io

LOG = r'D:\openLM\修改日志.txt'
DOC = r'D:\openLM\核显模式显示策略-方案存档.md'

ENTRY = """
[2026-10-05 15:05] ✅ 决策：机器作用域选 (a) 接受现状（不恢复休眠的迁移机制）
================================================================================
背景（子代理发现）：真正实现"按机型隔离"的函数
  `ModelScopedConfig.MigrateToModelScope / Plan / IsMachineScopedKey`、`AppConfig.GetModel()`
  在 source/ 里**没有任何调用点** -> 该机制**休眠**，列在 `ExactMachineScopedKeys` 里的键
  实际等同普通全局键。受影响键：`charge_limit` / `gpu_mode` / `ec_charge_limit`
  + 本次新增的 `igpu_only_ui` / `igpu_only_asked`。

两个选项：
  (a) 接受现状：开关按"安装"生效（全局），单机完全够用；代价 0
  (b) 恢复机器作用域：键按机型分开存；但属中等偏大改动，**会改变既有三个键的行为**
      （机型字符串一变 -> 充电限制/显卡模式可能"重置" ⚠️），且当初为何停用无记录

**用户决定：(a)** —— 理由：「我觉得很小有概率会迁移文件到不同机器」✅

补充依据（让 (a) 更稳）：
- **50 系不受影响** ✅：`CanOfferIgpuOnly` 的优先级里 `SupportsIgpuOnly -> true` 在
  `igpu_only_ui == hide` **之前**，所以 50 系永远显示按钮，配置里的 hide 不生效
- 仅剩的极端场景是「40 系 A 机设 hide -> 整目录复制到 40 系 B 机」，
  而 B 机上**打开设置里的开关即可一键修正** ✅
- 若将来真需要，作为**独立任务**处理（先查清当初停用原因 + 配套迁移测试）更稳妥

【本次一并记录：两个小点维持现状，无行为危害】
1. `show` 分支保留了底线 `DgpuGeneration != NoDgpu`
   （防止"枚举成功但无独显"的机型凭空出现显卡面板/托盘项）—— 采信子代理的保守处理
2. §3④ 的 50 系识别依赖"已连接并上报 SupportsIgpuOnly"，启动早期 GCU 未连接时
   50 系可能被多问一次；回答会被可靠分支覆盖 -> 无行为影响

【当前形态小结（本策略 ①~⑥ 已实现并部署）】
- 配置键： igpu_only_ui = ask|show|hide（默认 ask）；igpu_only_asked = 0|1（默认 0）
- 首次引导第 4 步（三选一：有->show / 没有->hide / 不确定->show，fail-safe）
- 升级用户由 `ShowIgpuOnlyGuideIfNeeded()` 单独问一次（UiAuditMode 跳过）
- 设置对话框新增「显示核显模式」开关，切换即时生效
- 单问对话框已两轮收紧：最终客户区 ~420×175 逻辑像素、纵向空白 0；
  完整引导（4 步）像素级未变（18/18 截图 SHA256 一致）
- 部署：产物 1936384 字节 @14:46:44，LM PID 14100 Responding=True，启动无异常 ✅
- ⑦（读官方控制台配置作佐证）仍存档不做 ✅
- 遗留：临时核验工具 D:\\openLM\\tmp_compact_shot\\（含渲染截图），待用户决定留删
- 未打包（用户要求）
"""

DOC_APPEND = """

---

## 7. 实现落地记录与决策（2026-10-05）

### 7.1 已实现 ①~⑥（⑦不做）

| 项 | 落地情况 |
|---|---|
| 配置键 | `igpu_only_ui`（ask/show/hide，默认 ask）、`igpu_only_asked`（0/1，默认 0） |
| 判定 | `MechrevoHw.CanOfferIgpuOnly` 按 `Gen30 -> false` / `SupportsIgpuOnly -> true` / `hide -> false` / `show -> true` / `ask -> 代理规则` 的优先级 |
| 设置开关 | 设置对话框「显示核显模式」，切换即时写键并刷新 |
| 首次引导第 4 步 | 三选一「有 / 没有 / 不确定」；落盘 有->show、没有->hide、不确定/未选/X/Esc->show（fail-safe），并置 `asked=1` |
| 升级用户 | `SettingsForm.ShowIgpuOnlyGuideIfNeeded()` 单问一次（`UiAuditMode` 跳过） |
| 字符串 | 两个 resx（UTF-8 带 BOM）+ `Strings.cs` 同步；校验 710/710/710 一致 |

单问对话框经两轮收紧：最终客户区 **~420×175 逻辑像素**、纵向空白 **0**；
完整引导（4 步）呈现**像素级未变**（改动前后 18/18 截图 SHA256 一致）。

### 7.2 决策：(a) 接受「机器作用域」休眠

实现过程中发现：`ModelScopedConfig.MigrateToModelScope / Plan / IsMachineScopedKey`
与 `AppConfig.GetModel()` **没有任何调用点** → 机器作用域机制**休眠**，
列在 `ExactMachineScopedKeys` 里的键（含既有 `charge_limit` / `gpu_mode` / `ec_charge_limit`）
实际等同**全局键**。

**决定：接受现状（不恢复）。** 理由：
- 用户判断「迁移配置文件到不同机器的概率很小」；
- 恢复属中等偏大改动，且**会改变既有三个键的行为**（机型串一变可能导致设置"重置"）；
- 影响可被**设置里的开关**一键修正；
- **50 系不受影响**（`SupportsIgpuOnly -> true` 优先于 `hide`）；
- 将来若确有必要，作为**独立任务**处理（先查清当初停用原因 + 配套迁移测试）。
"""

with io.open(LOG, 'a', encoding='utf-8', newline='') as f:
    f.write(ENTRY)
with io.open(DOC, 'a', encoding='utf-8', newline='') as f:
    f.write(DOC_APPEND)

print('log  =', len(ENTRY.encode('utf-8')), 'bytes appended')
print('doc  =', len(DOC_APPEND.encode('utf-8')), 'bytes appended')
