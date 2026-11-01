# -*- coding: utf-8 -*-
import datetime

def append(path, text):
    with open(path, 'a', encoding='utf-8') as f:
        f.write(text)

mod = '''【2026-10-03 核显省电判断修正（GCU 能力位对齐，结合热切换能力链思路）】
- 背景：核显省电按钮显示条件 = CanOfferIgpuOnly（SupportsIgpuOnly + 代数放行）&& GpuMode==1；而 SupportsIgpuOnly = Capabilities.IgpuOnly = Flag("iGPUModeOnlySupport","IGpuOnlyModeSupport","IntegratedGpuOnlySupport")——但抓包证实两类 GCU 均不上报 iGPUModeOnlySupport 系列字段：
  - 50 系 GCU（耀世/苍龙 ProjectID 26）：IGpuOnlyConnectionSwitch_Support = "Enable"
  - 40 系 GCU（翼龙 ProjectID 25）：IGpuOnlyConnectionSwitch_Support = "NotSupport"
  → 50 系机主核显省电/核显模式判断失效（字段名不匹配）
- 修改（仅 1 行）：Capabilities.IgpuOnly 候选名追加 "IGpuOnlyConnectionSwitch_Support"
  - Number 语义：字符串 "Enable"/"enabled" → 1（true）；"NotSupport"/"Disable" → 0（false）——天然兼容
- 效果：50 系 GCU 正确识别核显能力（Enable→IgpuOnly=true）→ 核显省电按钮恢复显示；40 系 GCU/无核显模式机型（NotSupport/缺失→false）行为不变
- 零新增属性、零冗余：完全复用现有能力链（GCU 上报字段 → Capabilities.IgpuOnly → SupportsIgpuOnly → CanOfferIgpuOnly → UI 判断），未新增 IgpuOnlySave 等重复属性
- 编译 0 错误（1134 既有警告）；提权替换完成（DLL 1896448B @11:01，运行 PID 10740）；备份 L-Mechrevo_20261003_110142.dll
- 备注：热切换能力位（GpuHotSwapSwitchSupport/lgpuHotSwapSwitchStatus）在两类 GCU 抓包中均无上报——Capabilities.GpuHotSwap 实际恒 false，与"显卡热切换: 否"诊断一致；不影响本修改

'''

upd = '''【2026-10-03】〇、核显省电判断修正
- 50 系 GCU 上报字段为 IGpuOnlyConnectionSwitch_Support=Enable（核显能力，而非 iGPUModeOnlySupport），已将其加入核显能力位候选名
- 修复后 50 系机型核显省电按钮/核显模式判断恢复正常；40 系与无核显模式机型行为不变

'''

append(r'D:\openLM\修改日志.txt', mod)
append(r'D:\openLM\更新日志.txt', upd)
print('logs ok')
