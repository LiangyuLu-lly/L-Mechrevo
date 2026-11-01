# -*- coding: utf-8 -*-
import io

ENTRY = """
[2026-10-05 12:10] 🎯 极光 50 系诊断报告到手（关键证据）+ 第二类清理（核显省电命令脱离三档）
================================================================================
用户提供：D:\\Downloads\\LM-核显采集-报告-20261005_115654.txt（他们用新工具采的）

【🎯 50 系关键证据 —— 悬了很久的问题解决了】
```
机型 : JIGUANG Series    board=KUANGSHI-JIGUANG Series-X6AR55xW   family=ARL
CPU  : Intel Core Ultra 7 255HX      GPU: RTX 5070 Ti Laptop
BIOS : N.1.18MRO24       dgpu_generation = Gen50（source=MarketingName）
TrayGPU: IgpuOnly=True SupIgpu=True SupDgpu=True Switch=True IgpuStatus=True  ← 50 系确实报 IgpuOnly ✅

11:54:48  GPU restart route applied for target=0: 1 payload(s) [DGPU_DIRECT_CONNECT_TOGGLE_IGPU]  ★★★
11:54:48  Requested GCU GPU restart route for target=0
11:54:39  GpuMode -> 2 (dgpu=DGPU_DIRECT_CONNECT_TOGGLE_ON, igpu=IGPU_ONLY_CONNECT_RB_OFF)
```
-> **50 系切核显(target=0) 发的就是 `DGPU_DIRECT_CONNECT_TOGGLE_IGPU`** ✅
-> 今天把 `CreateGpuModePayload(0)` 改成它，**方向与 50 系实测一致** ✅（此前的担心不成立）
-> 而且 **50 系切核显只有 1 条载荷** ✅（与本机 target=2 的 3 条形成对比）
-> 对照：本机(40系) `SupIgpu=False`，极光(50系) `SupIgpu=True` ✅ —— 与"能力位分别"相符
- 注意：极光的 `① LM 实际发出的动作` 为 0 行，说明 **18.4 还没打 `SwitchGpuMode(N) ->` 那行**；
  但 `GPU restart route applied` 把命令记全了 ✅（工具的分桶抓到了 ✅）
- 极光 `log_level=off`（配置）但环境变量 All 生效 ✅ -> 日志有内容 ✅

【第二类清理：核显省电命令彻底脱离三档模式（用户要求）】
用户原话：「我目前是想清理死代码来着 把热切切换（核显省电模式）完全从三档按钮清理掉
          因为已经有独立按钮了」
清理依据：核显省电已自成一套（_igpuSavingRow -> SwitchIgpuSavingAsync ->
          SetIGpuOnlySaveMode -> IGPU_ONLY_CONNECT_RB_{ON,OFF,AUTO}）；
          DisplayRouteMatrix:48 显示官方两族命令并存（§9.6）；
          同文件 CreateGpuRestartTargetPayloads:1736 **已经在用** TOGGLE_OFF
          -> 原表里的 ...RB_OFF 属内部不一致的遗留
改动（tools\\patch_cleanup2_igpu_only_from_modes.py，4 项锚点，编译 0 错误）：
  1. `CreateGpuModePayload(1)`：`IGPU_ONLY_CONNECT_RB_OFF`+SetToWMIEC -> `DGPU_DIRECT_CONNECT_TOGGLE_OFF`
  2. `CreateGpuRestartTargetPayloads(2)`：3 条 `[ON -> CreateGpuModePayload(1) -> ON]`
     -> **1 条** `[DGPU_DIRECT_CONNECT_TOGGLE_ON]`
     （中间那条唯一目的是"清核显省电"，正是要清掉的对象；
       且极光 50 系实测同类切换就是 1 条载荷 ✅）
  3. `MechrevoService:1888-1896` 删除"切 mode 2 前先发 IGPU_ONLY_CONNECT_RB_OFF"整块
  4. `MechrevoHw:3436-3444` 同上（该处是重复实现）
保留不动：`CreateGpuModePayload(3)` = IGPU_ONLY_CONNECT_RB_AUTO（mode 3 = 核显省电 AUTO，
          属**独立按钮**功能；三档按钮已走不到它）、`SetIGpuOnlySaveMode`、`IgpuOnlySaveMode`、
          状态词表、核显省电 UI —— 核显省电功能完整保留 ✅
清理后 MechrevoService 里剩余的 IGPU_ONLY_*：`:1208`(SetIGpuOnlySaveMode ✅)、
          `:1602`(mode3 AUTO ✅)、其余全是注释 ✅；MechrevoHw 里只剩状态解析词表 ✅

【验证状态】
- 编译 0 错误 ✅；产物 1934336 字节 @12:04:38；替换完成 12:04:38
- LM 正常（PID 27624 Responding=True）✅
- LM **没有** GPU 模式的 CLI（只有 --gpu-oc-helper/--official-isolate/--selftest 等）
  -> **需要用户点一次「混合模式」或「独显直连」**，再看日志确认不再出现 IGPU_ONLY_*
- 基线：日志 67345 行（tools\\_clean2_marker.txt）
- 备份：backup\\MechrevoService_20261005_pre_clean2.cs、MechrevoHw_20261005_pre_clean2.cs
- 未打包（用户要求）
"""

with io.open(r'D:\openLM\修改日志.txt', 'a', encoding='utf-8', newline='') as f:
    f.write(ENTRY)
print('appended, bytes =', len(ENTRY.encode('utf-8')))
