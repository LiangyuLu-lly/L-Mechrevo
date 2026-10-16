# -*- coding: utf-8 -*-
def append(path, text):
    with open(path, 'a', encoding='utf-8') as f:
        f.write(text)

mod = '''【2026-10-03 路由层热切换判断整理（判断唯一化，承接核显省电能力位修正）】
- 现状核对：SettingsForm:6323 调用 GpuSwitchPolicy.Resolve 时第 4 参 supportsHotSwap 传的是 Program.hw.CanOfferIgpuOnly——与核显省电行（CanOfferIgpuOnly && GpuMode==1）为同一判断依据，判断重复且参数名误导
- 修改（行为零变化）：
  1. GpuSwitchPolicy.Resolve 删除 supportsHotSwap 第 4 参；`(num==1 && targetMode==0)` 条件无条件化（能走到混合→集显的机型必然 CanOfferIgpuOnly=true，删参后行为不变）
  2. SettingsForm:6323 调用同步改 4 参
- 为什么不能直接删分支：else 会落到 Direct 即时切换 → SwitchGpuMode(0) 发 IGPU_ONLY_CONNECT_RB_ON——等于恢复热切换执行（上轮已删），故保留 Restart 分支仅删冗余参数
- 布局层 GpuRowLayout=HotSwap（GpuRowLayouts.cs:15-18 / SettingsForm:6913,6995）：能力位 GpuHotSwapSwitchSupport/lgpuHotSwapSwitchStatus 两类 GCU 均不上报 → 恒 false 死代码；与核显省电判断不同源（CanOfferGpuHotSwap），用户上一轮明确保留，本轮未动，待用户决策
- 编译 0 错误（1134 既有警告）；提权替换完成（PID 13556）；备份 L-Mechrevo_20261003_110758.dll

'''

upd = '''【2026-10-03】〇、显卡切换路由判断整理
- 路由层热切换判断与核显省电判断依据重复（均为 CanOfferIgpuOnly），已删除冗余参数并统一"混合→集显走重启后应用"路径，判断唯一化，行为不变

'''

append(r'D:\openLM\修改日志.txt', mod)
append(r'D:\openLM\更新日志.txt', upd)
print('logs ok')
