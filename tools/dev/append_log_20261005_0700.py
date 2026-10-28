# -*- coding: utf-8 -*-
import io

ENTRY = """
[2026-10-05 07:00] 修 NVRAM 显示模式字节的平台解码 bug（AMD 被当 Intel 解）
- 起因：查"核显指令是否处于待重启状态"时，读 LM 日志发现
  `NVRAM display byte @0x62=1 (intel-decoded=IgpuOnly; offset unverified)`
  而同一时刻真实路由是 `route=Direct` -> 说明 `1` 其实是 **Direct**（AMD 编码）
- 编码表（`OemDisplayModeEncoding` / `MySettingManager.cs:1229-1271`）：
  **AMD**   Direct=1  Hybrid=0  Igpu=2      <- 本机
  **Intel** Direct=2  Hybrid=4  Igpu=1
- 根因：`UefiDisplayModeDiagnostics.cs:157`
  `GpuRouteInference.DecodeOemDisplayMode(value, isAmd: false)` —— **平台写死成 Intel**
  -> AMD 机器上 Direct(1) 被误标 IgpuOnly；Hybrid(0) 被标 unrecognized
- 修法（tools\\patch_nvram_decode_platform.py，4 项锚点）：
  1) `Describe()` -> 新增 `Describe(bool isAmd)`，旧无参签名保留并转调 `Describe(false)`
  2) 解码用 `isAmd`，标签按平台输出 `amd-decoded=` / `intel-decoded=`
  3) 两个调用点（`GpuRestartVerifier.cs:110` / `:124`）传
     `Program.hw?.IsAmdPlatformForLimits ?? false`
- 编译 0 错误；部署后重启 LM 验证：
  `02:56:40 ... NVRAM display byte @0x62=0 (len=180, **amd-decoded=Hybrid**; offset unverified)`
  -> 与真实路由 `route=Hybrid` **一致** ✅（修复前是 `intel-decoded=unrecognized` ❌）
- 产物 1935872 字节 @02:56:09；LM PID 4180 Responding=True
- 备份：backup\\UefiDisplayModeDiagnostics_20261005_pre_amd.cs、
        backup\\GpuRestartVerifier_20261005_pre_amd.cs

【同一轮的另一项实测结论：核显指令确实无效】
- 按用户要求直接发 `IGPU_ONLY_CONNECT_RB_ON`（payload 与 LM 完全一致：
  `{"Action":"IGPU_ONLY_CONNECT_RB_ON","SetToWMIEC":"OK"}` -> `Setting/Control`）
- 结果：**没有任何痕迹** —— GCU 状态字段没变、**NVRAM @0x62 也没变**、无自动重启
  -> **不是"已发送待重启"** ✅ 重启也不会生效
- 对照：独显直连指令一发，状态字段**立刻**变（`TOGGLE_ON`/`TOGGLE_OFF`），重启才切路由
- 结论：`payload\\40`（GCUService 1.0.2.70）**确实没实现核显模式** ✅
  -> **LM 隐藏核显模式按钮是正确行为** ✅（不是判据的 bug）
  -> 用户若需要核显模式，须换另一个 40 系 GCU 包（`GCU-40-51749` 系，带双显三模档）
  -> ⚠️ 因此《核显模式判据设计与三方对照.md》里"能力位不可靠"的结论**需更正**为
     "能力位对本机是可信的；10-02 的 True 是当时装了带三模档的 GCU 包"
- 新增工具：`tools\\gcu_send_mode.py`（status/on/off/auto/dgpu/recover/restart）、
            `tools\\restart_lm.ps1`（提权重启 LM，用于产生新的启动日志）
- 未打包
"""

with io.open(r'D:\openLM\修改日志.txt', 'a', encoding='utf-8', newline='') as f:
    f.write(ENTRY)
print('appended, bytes =', len(ENTRY.encode('utf-8')))
