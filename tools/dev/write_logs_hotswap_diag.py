# -*- coding: utf-8 -*-
def append(path, text):
    with open(path, 'a', encoding='utf-8') as f:
        f.write(text)

mod = '''【2026-10-03 诊断"显卡热切换"判断修正（用户洞察：核显省电=热切换，同一命令）】
- 现象：诊断包"显卡热切换"依赖 Capabilities.GpuHotSwap = Flag("GpuHotSwapSwitchSupport") && Flag("lgpuHotSwapSwitchStatus")——抓包证实两类 GCU（40/50 系）均不上报这两个字段 → 任何机器恒显示"否"（错误判断）
- 依据：热切换（免重启切集显）与核显省电（混合模式静默独显）发同一命令 IGPU_ONLY_CONNECT_RB_ON/OFF——能力判断应对齐核显能力
- 修改（仅 1 行）：MechrevoDeviceCapabilities.cs:310 GpuHotSwap = (Flag("GpuHotSwapSwitchSupport") && Flag("lgpuHotSwapSwitchStatus")) → GpuHotSwap = igpuOnly（复用 IgpuOnly 判断：iGPUModeOnlySupport / IGpuOnlyConnectionSwitch_Support=Enable 任一命中即支持）
- 效果：50 系（ConnectionSwitch=Enable）→ "显卡热切换: 是"；40 系三模（iGPUModeOnlySupport）→ "是"（40 系三模 18.6 时代本就可免重启切换）；无核显模式机型（蛟龙16s）→ "否"
- 未删的"错误代码"相关：FeatureMatrix 的 FeatureBit.GpuHotSwapSwitch/HotSwapStatus 字段定义、DisplayRouteMatrix GPU_HOTSWAP_ON/OFF 词汇与证据行——属路由证据/审计资产（记录 50 系控制台真实存在 GPU_HOTSWAP_ON/OFF），保留不动；执行路径上热切换代码此前已全部删除
- 编译 0 错误（1134 既有警告）；提权替换完成（主 DLL + zh-CN 资源均 @11:38:15，LM 已重启）

'''

upd = '''【2026-10-03】〇、诊断显卡热切换判断修正
- 热切换与核显省电为同一命令链路（IGPU_ONLY_CONNECT_RB_ON/OFF），诊断"显卡热切换"改为按核显能力判断——不再依赖从不被 GCU 上报的 GpuHotSwapSwitchSupport/lgpuHotSwapSwitchStatus 字段
- 修复后：支持核显模式的机型（50 系/40 系三模）显示"是"，无核显模式机型显示"否"

'''

append(r'D:\openLM\修改日志.txt', mod)
append(r'D:\openLM\更新日志.txt', upd)
print('logs ok')
