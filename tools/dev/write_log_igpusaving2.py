# -*- coding: utf-8 -*-
with open(r'D:\openLM\修改日志.txt', 'a', encoding='utf-8') as f:
    f.write('''【2026-10-03 核显省电按钮条件收窄为热切换支持】
- 问题：核显省电按钮此前用 CanOfferIgpuOnly（=Capabilities.IgpuOnly，认 iGPUModeOnlySupport/IGpuOnlyModeSupport/IntegratedGpuOnlySupport/IGpuOnlyConnectionSwitch_Support 四键）判断，40 系三模（iGPUModeOnlySupport 上报）也会显示，逻辑过宽
- 修复：新增 MechrevoHw.SupportsGpuHotSwap => Capabilities.GpuHotSwap（只认 50 系 IGpuOnlyConnectionSwitch_Support=Enable）；核显省电按钮显示（BuildDashboardLayout / VisualiseIgpuSaving）与执行拦截（SwitchIgpuSavingAsync）全部改用 SupportsGpuHotSwap
- 效果：40 系三模（冷切换核显）不再显示核显省电按钮；50 系双显三模（支持热切换）显示
- 编译 0 错误；提权替换完成

''')
print('log ok')
