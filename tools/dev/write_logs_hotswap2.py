# -*- coding: utf-8 -*-
def append(path, text):
    with open(path, 'a', encoding='utf-8') as f:
        f.write(text)

mod = '''【2026-10-03 更正：诊断"显卡热切换"判断收窄（用户纠正：40 系三模是冷切换核显，无免重启）】
- 上一轮（11:38 版）误用 GpuHotSwap = igpuOnly（合并判断），会把 40 系三模（iGPUModeOnlySupport）也判为"显卡热切换: 是"——错误
- 用户纠正：40 系三模切核显是**冷切换**（重启生效），免重启热切换仅 50 系专属
- 修正（MechrevoDeviceCapabilities.cs:310）：GpuHotSwap = Flag("IGpuOnlyConnectionSwitch_Support")——只认 50 系 GCU 上报的 Enable（与核显省电支持判断一致：静默独显=免重启=热切换）
- 新显示效果：
  - 50 系 + 50 系 GCU（ConnectionSwitch=Enable）→ "显卡热切换: 是"（免重启，与核显省电能力一致）
  - 50 系 + 40 系 GCU（NotSupport，如翼龙测试场景）→ "否"（反映当前 GCU 配置状态；换 50 系 GCU 恢复"是"）
  - 40 系三模（iGPUModeOnlySupport，冷切换）→ "否"（正确：无免重启）
  - 无核显模式机型 → "否"
- 编译 0 错误（1134 既有警告）；提权替换完成（主 DLL + zh-CN 资源均 @11:41:45，LM PID 33164）
- 更新日志同步更正上一条表述（40 系三模不计入热切换）

'''

upd = '''【2026-10-03】〇、诊断显卡热切换判断修正（更正）
- 热切换（免重启切集显）仅 50 系专属，判断只认 50 系 GCU 上报的 IGpuOnlyConnectionSwitch_Support=Enable（与核显省电支持同一判断）
- 40 系三模为冷切换（重启生效），诊断显示"显卡热切换: 否"——不计入热切换
- 更正上一条更新日志中"40 系三模显示是"的表述

'''

append(r'D:\openLM\修改日志.txt', mod)
append(r'D:\openLM\更新日志.txt', upd)
print('logs ok')
