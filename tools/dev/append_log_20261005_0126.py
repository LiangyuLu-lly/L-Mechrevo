# -*- coding: utf-8 -*-
"""追加一条修改日志（避免因会话压缩导致 edit 工具需先 read）"""
import io

ENTRY = """
[2026-10-05 01:26] 新报 bug：显卡模式切换后**弹了两次重启提示**（调查中，未改代码）
- 用户原话：「我起先切换混合模式，发现没自动重启（貌似是30系GCU不支持自动重启切换），
  就切回独显再切混合，结果后面分别弹了两个窗口提示」
- 另一条确认：**「没显示核显省电」** ✅ 符合预期（30 系 GCU 无 iGPU 能力，
  SupportsGpuHotSwap 应为 false）→ 对应 §9.6 的预期
- 已定位提示逻辑（SettingsForm）：
    :6662 GpuSwitchPlan plan = GpuSwitchPolicy.Resolve(...);
    :6663 if (plan.Route == GpuSwitchRoute.NoChange)
    :6665     if (HasPendingGpuRestart()) await ShowGpuRestartPromptAsync(...);   // (1) 已有待重启 -> 弹
    :6671 switch (plan.Route)
    :6673   case GpuSwitchRoute.Restart:
    :6674     if (DisplayRouteMatrix.AllowsRestart(hw.DgpuGeneration))
    :6676       await ShowGpuRestartPromptAsync(...);                            // (2) 需要重启 -> 弹
    :6679     await Program.service.SwitchGpuMode(targetMode);                   // 不支持重启时直接切
  HasPendingGpuRestart() :6723 -> GpuRestartVerifier.HasPendingThisBoot()
  ShowGpuRestartPromptAsync :6734（MessageBox.Show(Strings.GpuRestartPrompt, ..., YesNoCancel)）
- 待查（两个假设，需数据区分）：
  1. MessageBox 模态但带消息循环 -> 提示开着时后续点击仍被处理 ->
     前一个没关、后一个已排队 -> 看起来"分别弹了两个"
  2. 30 系 DisplayRouteMatrix.AllowsRestart(Gen30) 可能为 false ->
     走 SwitchGpuMode 直接切且不标记待重启 -> 用户以为没生效又切一次 ->
     第二次命中 NoChange + HasPendingGpuRestart() -> 弹窗
- 建议下一步：先加**提示去重守卫**（"已在显示提示"时抑制重复），
  并补一行日志记录 plan.Route / AllowsRestart(gen) / HasPendingGpuRestart() 真值
  （沿用 §9.2 的教训：**先打真值日志，别猜**）
- 未打包
"""

with io.open(r'D:\openLM\修改日志.txt', 'a', encoding='utf-8', newline='') as f:
    f.write(ENTRY)
print('appended, bytes =', len(ENTRY.encode('utf-8')))
