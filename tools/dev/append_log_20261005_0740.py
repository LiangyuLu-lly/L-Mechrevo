# -*- coding: utf-8 -*-
import io

ENTRY = """
[2026-10-05 07:40] 核显模式判据修复【未完成，交棒】—— 三次尝试无效，已加诊断但诊断行自身写错
================================================================================
用户澄清：「这个就是双显三模的GCU」-> `payload\\40`（GCUService 1.0.2.70）**就是**三模档变体 ✅
  -> 我此前"应换 GCU-40-51749 包"的建议**作废** ✅ 核显模式本来就该有 ✅ 只是 LM 判据看不见 ✅
用户另问：「不确定有 igpu 这玩意是不是写死的」-> **不是写死的** ✅，是看 GCU 上报字段 ✅：
  | 字段 | 实测值（处于核显模式时） | LM 是否用于能力判定 |
  |---|---|---|
  | IGpuOnlyConnectionSwitch_Support | NotSupport | ✅ 认了，但是假 |
  | iGPUModeOnlySupport | 缺失 | ✅ 认了，取不到 |
  | **DiscreteGpuDirectConnectionSwitch_Status** | **DGPU_DIRECT_CONNECT_TOGGLE_IGPU** | ❌ **完全没认** <- 就差这里 |
  （`DgpuDirectStatusTokens` :144 里其实**已经包含** `DIRECT_CONNECT_TOGGLE_IGPU` / `DIRECT_CONNECT_IGPU`
    且 `:1480` 已经用它解码路由 ✅ —— 只是**没有拿它当核显能力** ✅）

【已做但无效的尝试】
- v1 `MechrevoDeviceCapabilities.FromValues:271`：igpuOnly 并上路由状态
  -> **无效**：那条路吃的是 `Customize/Info`（ProjectID/KeyboardType/SupportColorCalibration 在那），
     而 `DiscreteGpuDirectConnectionSwitch_Status` 在 `Setting/Status` -> 两条路不通 ✅
- v2 `MechrevoHw`：:415 `SupportsIgpuOnly => Capabilities.IgpuOnly || (IgpuOnlyStatusSupport ?? false)`
  + 解析 text24 时置 `IgpuOnlyStatusSupport = true`
  -> **无效**（TrayGPU 仍 SupIgpu=False）
- v3 加 sticky `_igpuRouteStatusSeen` 只置位不清除 -> **仍无效**
- 备份：backup\\MechrevoDeviceCapabilities_20261005_pre_igpucap.cs、
        backup\\MechrevoHw_20261005_pre_igpucap2.cs
- ⚠️ **这三处改动仍在源码里（理论无害但未生效）** —— 下次要么完成、要么回滚

【诊断行（present）与其自身 bug】
- 加在 `MechrevoHw.cs` text24/text25 之后：
  `Logger.WriteLineIfChanged("igpu-diag", $"IGPU-DIAG: ...")`
- ⚠️ **我把 C# 插值的 `{` 写成了 `{{`**（那是 Python 侧转义考虑，但 Python 串不是 f-string，
  花括号原样进了 C#）-> 日志把**源码字面量**打了出来，没打出真值 ❌
  ```
  IGPU-DIAG: text24={text24 ?? "<null>"} text25={text25 ?? "<null>"} keys={o.Count} ...
  ```
- **但证明了该代码路径确实在运行** ✅（与 TrayGPU 行交替出现）

【下一步（明确的第一个动作）】
1. **修诊断行的花括号**（用单花括号 `{text24}`；`??` 用临时变量避免嵌套引号），重新部署读真值：
   - 若 `text24` 有值 -> 说明 v2/v3 的置位点**不在**同一 `o` 上，改到真正的合并点；
   - 若 `text24` 为 null -> 说明 LM 取该字段的方式/时机不对，改为在收到 Setting/Status 时单独解析
     （`MechrevoHw` 里合并后的字段表，见 `:1355 DescribeResolvedCapabilities` 用的那份）
2. 真值明确后再改判据（**不要再猜** —— 这是本文档 §9.2 的既有教训）

【用户当前处境（重要）】
- 机器**正处于核显模式**（NVRAM @0x62=2，NVIDIA 已屏蔽），而 **LM 界面没有切回按钮** ⚠️
- 切回命令（发完需重启）：
  ```
  cd D:\\openLM
  py -3 tools\\gcu_send_mode.py dgpuoff   # 回混合模式（DGPU_DIRECT_CONNECT_TOGGLE_OFF）
  py -3 tools\\gcu_send_mode.py dgpu     # 回独显直连（DGPU_DIRECT_CONNECT_TOGGLE_ON）
  ```
- 救援自检：`py -3 tools\\post_reboot_check.py`

【可直接复用的成果（本次新增）】
- `tools\\gcu_send_mode.py`：status/on/off/auto/dgpu/dgpuoff/recover/restart，payload 与 LM 一致
- `tools\\post_reboot_check.py`：重启后一条命令自检（GCU 状态 + NVRAM + 结论）
- `tools\\restart_lm.ps1`：提权重启 LM（纯 ASCII；用于产生新的启动日志）
- 已修复并验证：NVRAM 显示字节平台解码（AMD 不再按 Intel 解）✅
- 未打包
"""

with io.open(r'D:\openLM\修改日志.txt', 'a', encoding='utf-8', newline='') as f:
    f.write(ENTRY)
print('appended, bytes =', len(ENTRY.encode('utf-8')))
