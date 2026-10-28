# -*- coding: utf-8 -*-
import io

ENTRY = """
[2026-10-05 12:00] 第一类清理（模式切换去核显省电命令）+ AUTO 模式残余清理（含死字符串）
================================================================================
用户：「第一类做 然后你看看之前的AUTO模式是不是还有残余 现在只有三档位了反正」

【① 第一类清理：模式切换路径不再引用核显省电命令】
依据：核显省电是**完全独立**的一套（UI `_igpuSavingRow` / 服务 `SetIGpuOnlySaveMode` /
      状态 `IgpuOnlySaveMode` / 门控 `SupportsGpuHotSwap && GpuMode==1` / 文案 `IgpuSaving*`）；
      `DisplayRouteMatrix:48`（Gen50 官方控制台反编译）显示官方**同时具备两族命令**
      -> 正是 §9.6 的区分，模式切换不该用 IGPU_ONLY_*
改动（tools\\patch_cleanup_igpu_only_in_modeswitch.py，2 项锚点）：
  1. `MechrevoHw:427` CanOfferIgpuOnly 的许可判据
     "IGPU_ONLY_CONNECT_RB_ON" -> "DGPU_DIRECT_CONNECT_TOGGLE_IGPU"
  2. `MechrevoService:1596` CreateGpuModePayload(0)（核显模式）
     `IGPU_ONLY_CONNECT_RB_ON` + `SetToWMIEC` -> `DGPU_DIRECT_CONNECT_TOGGLE_IGPU`
     （与 LM 实测发送一致，实测载荷里没有 SetToWMIEC）
  50 系同样安全：Gen50 词汇表里 TOGGLE_IGPU 也在（DisplayRouteMatrix:48 实证）
清理后剩余 IGPU_ONLY_*（**正好只剩该保留的** ✅）：
  `:1208` SetIGpuOnlySaveMode（核显省电，正确 ✅）
  `:1599` CreateGpuModePayload(1)（混合；牵动 50 系，无样本不盲改 ✅ 保留）
  `:1603` CreateGpuModePayload(3)（核显省电 AUTO，正确 ✅）
  `:1892` 切 mode 2 时先发 ...RB_OFF（离开混合时清核显省电，合理联动 ✅ 保留）

【② AUTO 模式残余清理】
先确认：AUTO 显卡模式按钮此前已移除（"AUTO 就是混合模式"）；现在只有三档。
实测残余（**全部无引用/不可达**）：
  `SettingsForm:120`  `private ToolStripMenuItem menuOptimized;`（**从不创建**）
  `SettingsForm:5875` `... = (menuOptimized = null)`
  `SettingsForm:6261` `ButtonOptimized_MouseHover`（空体，无 `+=` 引用）
  `SettingsForm:6645` `ButtonOptimized_Click` -> `SwitchGpuModeFromUi(3, Strings.AutoShort)`
  `SettingsForm:7261` `if (menuOptimized != null) menuOptimized.Visible = false;`（恒 null）
死代码清理（tools\\patch_cleanup_auto_remnants.py，5 项锚点）-> SettingsForm 已无
  `menuOptimized` / `ButtonOptimized` 残留 ✅
死字符串清理（tools\\patch_cleanup_dead_strings.py，正则删 resx 块）：
  `Optimized` / `OptimizedGPUTooltip` / `AutoShort`（此时引用数均为 **0** ✅）
  从 `Strings.cs` + `Strings.resx` + `Strings.zh-CN.resx` 各删 3 处（共 9 处）✅
  ⚠️ `AutoShort` 的 resx 块含**尾随空格**，精确匹配不可靠 -> 改用正则 `.*?` + re.S ✅
⚠️ **保留不动**：`Program.cs:885/1694` `gpuControl.AutoGPUMode(optimized:false, ...)`
  （电源自动切换，**活的** ✅）、`GPUModeControl.AutoGPUMode`、`SwitchAutomaticGpuMode`

【验证】
- 三次改动分别编译 0 错误 ✅；产物 1934848 字节 @11:32:07；替换完成 11:32:07
- LM 正常（PID 8660 Responding=True）✅
- **资源核验（用 ResourceReader，不搜字节 —— 上次的教训）**：
  主 DLL 键数 **703 -> 700** ✅、`Optimized=False`、`AutoShort=False` ✅
  zh-CN 卫星键数 **703 -> 700** ✅、`OptimizedGPUTooltip=False` ✅
- 备份：backup\\MechrevoHw_20261005_pre_clean1.cs、MechrevoService_20261005_pre_clean1.cs、
        SettingsForm_20261005_pre_autoclean.cs、Strings.cs_20261005_pre_deadstr.cs、
        Strings_20261005_pre_deadstr.resx、Strings.zh-CN_20261005_pre_deadstr.resx

【待办（第二类，等有 50 系样本）】
- `CreateGpuModePayload(1)`（混合）对 50 系发的是 `IGPU_ONLY_CONNECT_RB_OFF` —— 可能应为
  `DGPU_DIRECT_CONNECT_TOGGLE_OFF`，但无 50 系机器可验证 -> 不动 ✅
- 切 mode 2 时的 `...RB_OFF` 清理是否仍必要（若核显省电完全独立则可评估移除）✅
- 热切换残留字段（`hotSwitchResultVersion`、`GpuSwitchResult`、
  `CheckDGpuStatusforIGpuOnly{OnSuccess,Switch}`）—— 热切换已删除，可评估清理 ✅
- 未打包（用户要求）
"""

with io.open(r'D:\openLM\修改日志.txt', 'a', encoding='utf-8', newline='') as f:
    f.write(ENTRY)
print('appended, bytes =', len(ENTRY.encode('utf-8')))
