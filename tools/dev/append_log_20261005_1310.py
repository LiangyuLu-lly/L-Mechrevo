# -*- coding: utf-8 -*-
import io

ENTRY = """
[2026-10-05 13:10] 🎯 修根：显示路由解析不再借用【核显省电】字段（mode 3 来源被拔除）
================================================================================
用户质疑：「GCU有上报3的能力吗？我怀疑是没有的」—— **怀疑正确** ✅

【查证（MechrevoHw.cs:1480 ResolveGpuModeStatus 原文）】
```
text  = DiscreteGpuDirectConnectionSwitch_Status   （显示路由字段）
text2 = IGpuOnlyConnectionSwitch_Status            （**核显省电**字段）
:1484  text == TOGGLE_ON  -> 2
:1488  text == TOGGLE_IGPU-> 0
:1492  int num = text2 含 RB_ON?0 : RB_AUTO?3 : RB_OFF?1 : -1     ★ mode 3(AUTO) 的真正来源
:1493  text == TOGGLE_OFF -> 1
:1497  num < 0 ? currentMode : num
```
-> **mode 3 是从【核显省电 AUTO】字段推出来的**，不是 GCU 上报的"路由 AUTO" ✅
-> 而显示路由字段**根本没有 AUTO 取值**：GCU 只会给 TOGGLE_ON / TOGGLE_OFF / TOGGLE_IGPU；
   DisplayRouteMatrix 里 Gen50 官方控制台反编译的动作词汇也只有
   `DGPU_DIRECT_CONNECT_TOGGLE_ON/OFF/IGPU`，**无 AUTO 变体** ✅

【为什么这是病根（§9.6 功能混淆）】
- 核显省电是独立功能：独立字段（IgpuOnlySaveMode）、独立发送（SetIGpuOnlySaveMode）、独立 UI 行
- 把它的 RB_ON/RB_AUTO/RB_OFF 反推成显示路由 0/3/1 是**跨功能污染**
- 删掉「自动切换显卡模式」后 mode 3 已无归宿；**50 系**把核显省电设成 AUTO 时
  （IGpuOnlyConnectionSwitch_Status = IGPU_ONLY_CONNECT_RB_AUTO）
  -> GpuMode 会变成 3 -> 三档高亮与切换策略错乱 ⚠️
- 这也让子代理报的「差异点 E」失去前提（E 假设"GCU 会上报 3"，实际是 LM 自己算的）
  -> **E 不需要"回补只读反推"，因为 mode 3 从根上不再产生** ✅

【改动（tools\\patch_gpumode_no_igpusave.py，2 项锚点）】
1. `ResolveGpuModeStatus(int currentMode, string? directStatus)` —— 去掉 `igpuStatus` 参数
   与整条 text2 反推；只保留显示路由字段的三档判断，认不出则 `return currentMode`
2. 调用点 `:2852` -> `ResolveGpuModeStatus(GpuMode, text24);`
   （text25/核显省电字段仍用于 `ResolveIgpuOnlySaveMode` ✅，未受影响）

【验证】
- 编译 0 错误 ✅
- 复查 mode 3 来源：`GpuMode = 3` / `? 3` / `return 3` 全仓仅剩
  · `MechrevoHw:1454`（ResolveIgpuOnlySaveMode 的核显省电 AUTO=3 ✅ 正确，喂 IgpuOnlySaveMode）
  · `MechrevoHw:4628` / `MechrevoService:1492`（**色域**解析 sRGB=2/P3=3/Adobe=4 ✅ 无关）
  · 其余为自定义模式/RGB/更新自检等无关项
  -> **作为"显卡模式"的 mode 3 已无任何来源** ✅
- 部署：产物 1928704 字节 @12:56:29；替换完成 12:56:29；LM PID 21484 Responding=True ✅
- 启动后回读正常：`TrayGPU: IgpuOnly=True SupIgpu=False SupDgpu=True Switch=True` ✅
- 备份：backup\\MechrevoHw_20261005_pre_moderesolve.cs
- 未打包（用户要求）

【遗留待办】
- **欢迎界面**（iss 改动）—— 方案已定但**尚未实施**：
  在 [Code] 加 `IsFreshInstall()`（检查卸载注册项是否存在；不存在=卸载后再装=全新安装）
  -> 通过 [Run] 里的 PowerShell 复位 config.json 的 `onboarding_version`（+ usage_notice_version）
  -> 达成「卸载后全新安装再出现欢迎界面 / 升级不出现」
  ⚠️ 不可把 AppData 加进 [UninstallDelete]：Inno 升级时会自动调用旧版卸载程序，
     那样**升级也会被清空**，与需求相反
  ⚠️ 待用户定：只复位"欢迎/匿名提示"标记，还是连整个 %APPDATA%\\MechrevoLite 一起删（会丢风扇曲线等设置）
"""

with io.open(r'D:\openLM\修改日志.txt', 'a', encoding='utf-8', newline='') as f:
    f.write(ENTRY)
print('appended, bytes =', len(ENTRY.encode('utf-8')))
