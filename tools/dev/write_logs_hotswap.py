# -*- coding: utf-8 -*-
import datetime

def append(path, text):
    with open(path, 'a', encoding='utf-8') as f:
        f.write(text)

mod = '''【2026-10-03 热切换执行层删除（清理死代码）】
- 用户决策：热切换（50 系免重启切集显）与核显省电功能重复，删除执行层；布局判定（GpuRowLayout=HotSwap）+ 显卡高亮体系保留
- 事实澄清：热切换执行逻辑为 18.6 群友版原有（非 beta21 引入，已在 18.6 源码验证 21 处同名逻辑）；beta21 引入的仅为布局判定+高亮体系。用户仍维持删除决定
- 删除清单：
  1. GPUModeControl.cs：hotIgpuSwitch 判定+独显应用预检分支、OfferRebootFallbackAsync 调用；NotifyAutomaticHotSwitchBlocked / CloseDgpuApplicationsForManualSwitchAsync / OfferRebootFallbackAsync 三方法；SwitchGpuMode 调用改 3 参
  2. SettingsForm.cs：DgpuPreflightResult 枚举、case GpuSwitchRoute.HotSwitch 分支、PrepareDgpuApplicationsForHotSwitchAsync / DescribeDgpuApplications 两方法
  3. MechrevoService.cs：SwitchGpuMode 签名删 keepRegisterOnHotSwitchTimeout；hotSwitchRequest / requiresFreshHotSwitchResult 声明与赋值；热切换确认轮询（HotSwitchStatusPollLimit / ShouldRetryHotSwitchPoll / _longRunningSwitchCts 热切换写入 / 未确认回滚块 / TargetReached requiresFresh 分支）；RollbackFailedHotSwitchAsync / ShouldRetryHotSwitchPoll 方法；HotSwitchStatusRetryEveryPolls 常量；CreateGpuSwitchPayload 删 useHotSwitch 参数
  4. GpuRouteCommandLayer.cs：BuildSwitchCommand 删 useHotSwitch 参数
  5. MechrevoHw.cs：SetGpuMode 删热切换 flag（恒 false）
  6. GpuSwitchRoute.cs：HotSwitch 枚举删除
  7. 文件删除：DgpuApplicationCoordinator.cs / DgpuApplicationSnapshot.cs（删除后无引用；DgpuApplication / DgpuApplicationSafety 因 NvidiaGpuControl / ElevatedProcessKiller 仍引用而保留）
- 保留不动：CanOfferGpuHotSwap / SupportsGpuHotSwap / Capabilities.GpuHotSwap 能力位、GPU_HOTSWAP_ON/OFF 词汇表（DisplayRouteMatrix）、GpuRowLayout=HotSwap 布局判定、TryApplyAutomaticHotSwitchAsync（自动插拔切换 mode 3，本就不走热切换）、GpuSwitchPolicy HotSwitch 分支（保留返回 Restart = 热切换机型切集显走重启、40 系三模走 Direct 即时，分流不变）
- 核显省电验证：SetIGpuOnlySaveMode 独立路径（IGPU_ONLY_CONNECT_RB_ON/OFF/AUTO + GETSTATUS，MechrevoService:1204），与 SwitchGpuMode / hotSwitchRequest 零交集，完全不受影响
- 行为变化：50 系热切换机型点集显 → 弹"重启以应用"；40 系三模点集显 → 即时（Direct）。快速切换性能模式逻辑不受影响
- 编译 0 错误（1134 既有 warning）；残留 grep 0 命中；提权替换完成（elevated_replace_hotswap.ps1，DLL 1896448B @10:39，运行 PID 20900）
- 备份：删除前 D:\\openLM\\backup\\source_20261003_102652\\（7 文件副本）；替换前 DLL D:\\openLM\\backup\\L-Mechrevo_20261003_104037.dll（1913344B）
- 备注：zh-CN satellite 本轮 resx 未变，未替换；Strings.HotSwitchTimeoutPrompt 键保留在 resx（无代码引用，无害）

'''

upd = '''【2026-10-03】〇、热切换执行层删除
- 与核显省电功能重复的免重启热切换（50 系点集显即时切换）执行逻辑已整体删除
- 混合→集显统一走"重启后应用"（50 系热切换机型）/ 即时（40 系三模，分流不变）
- 显卡切换高亮/布局体系（GpuRowLayout=HotSwap、路由回读高亮、重启确认闭环）不受影响
- 核显省电（混合模式静默独显，IGPU_ONLY_CONNECT_RB_* 独立路径）不受影响
- 代码层面：删除约 6 个方法/分支 + 2 个参数签名 + 2 个无引用文件，热切换确认/回滚死代码全清

'''

append(r'D:\openLM\修改日志.txt', mod)
append(r'D:\openLM\更新日志.txt', upd)
print('logs written')
