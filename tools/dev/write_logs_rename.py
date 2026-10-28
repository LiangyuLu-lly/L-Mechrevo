# -*- coding: utf-8 -*-
def append(path, text):
    with open(path, 'a', encoding='utf-8') as f:
        f.write(text)

mod = '''【2026-10-03 中文术语统一：集显 → 核显模式（移植 beta21 高亮体系时引入的术语不一致）】
- 现象：显卡模式选项卡显示"集显/混合模式/独显直连"；同概念在代码里已有两种叫法——IgpuMode（右键菜单）与 SwitchToIgpuMode 已是"核显模式"，而 IgpuShort（选项卡按钮）、IgpuModeTooltip（按钮悬停）、GpuRouteIgpu（重启确认模式名）、GpuRouteIgpuTip、GpuIgpuNeedsStandard 仍是"集显/集显模式"
- 修改（Strings.zh-CN.resx 5 处 + 诊断 1 处）：
  1. IgpuShort "集显" → "核显模式"（选项卡按钮文字，用户要求三档为 核显模式/混合模式/独显直连）
  2. IgpuModeTooltip "集显模式" → "核显模式"
  3. GpuRouteIgpu "集显模式" → "核显模式"（GpuRestartVerifier.ModeName(0) 重启提示）
  4. GpuRouteIgpuTip "切换到集显模式" → "切换到核显模式"（对齐已有 SwitchToIgpuMode 文案）
  5. GpuIgpuNeedsStandard "…无法切换到集显，请先…" → "…无法切换到核显模式，请先…"
  6. DiagnosticSystemInfo.cs "集显模式: " → "核显模式: "（诊断包术语统一）
- 英文 resx 未动（iGPU-only mode / iGPU 已是规范）；IgpuMode/SwitchToIgpuMode 本就是"核显模式"无需改
- 编译 0 错误（1134 既有警告）；提权替换完成（DLL 1895936B @11:27:14，LM PID 28952）
- 按用户既定规则：此文案小修只记修改日志，不记更新日志

'''

append(r'D:\openLM\修改日志.txt', mod)
print('log ok')
