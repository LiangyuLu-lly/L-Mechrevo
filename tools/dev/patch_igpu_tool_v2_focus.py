# -*- coding: utf-8 -*-
r"""修 v2 报告的 UX 缺陷：★重点节被 TrayGPU 刷屏淹没 -> 排除它并优先显示真正的动作行"""
import io
import os
import sys

GEN = r'D:\openLM\tools\gen_igpu_capture_tool_v2.py'

OLD = """    [void]$sb.AppendLine('## ★ 重点：切到【核显模式】/ 核显相关的全部记录（最后 60 条）')
    [void]$sb.AppendLine('=' * 72)
    $focus = @($all | Select-String -Pattern 'SwitchGpuMode\\(0\\)|target=0|GpuMode -> 0|IGPU_ONLY|TOGGLE_IGPU|IgpuOnly|核显|集显' -ErrorAction SilentlyContinue)
    if ($focus.Count -eq 0) { [void]$sb.AppendLine('   （未发现 —— 请确认在 LM 里点了【核显模式】并选了【重启切换】）') }
    else { foreach ($h in ($focus | Select-Object -Last 60)) { [void]$sb.AppendLine('   ' + $h.Line.Trim()) } }
"""

NEW = """    [void]$sb.AppendLine('## ★ 重点：切到【核显模式】的关键动作（已排除 TrayGPU 周期性刷屏）')
    [void]$sb.AppendLine('=' * 72)
    $focus = @($all | Select-String -Pattern 'SwitchGpuMode|target=0|GpuMode -> 0|IGPU_ONLY|TOGGLE_IGPU|IgpuOnly|核显|集显' -ErrorAction SilentlyContinue |
               Where-Object { $_.Line -notmatch 'TrayGPU' })
    if ($focus.Count -eq 0) {
        [void]$sb.AppendLine('   （未发现 —— 请确认在 LM 里点了【核显模式】并选了【重启切换】）')
    }
    else {
        $key = '(SwitchGpuMode|TOGGLE_IGPU|IGPU_ONLY_CONNECT_RB_ON|payload\\(s\\)|target=0|route verify|NVRAM display byte|OemDisplayMode|Setting/Control)'
        $prio = @($focus | Where-Object { $_.Line -match $key })
        $rest = @($focus | Where-Object { $_.Line -notmatch $key })
        [void]$sb.AppendLine(('   --- 关键动作行（{0} 条，显示全部/最多 60）---' -f $prio.Count))
        foreach ($h in ($prio | Select-Object -Last 60)) { [void]$sb.AppendLine('   ' + $h.Line.Trim()) }
        if ($rest.Count -gt 0) {
            [void]$sb.AppendLine('')
            [void]$sb.AppendLine(('   --- 其它核显相关行（共 {0} 条，仅显示最后 20 条）---' -f $rest.Count))
            foreach ($h in ($rest | Select-Object -Last 20)) { [void]$sb.AppendLine('   ' + $h.Line.Trim()) }
        }
    }
"""


def main():
    t = io.open(GEN, encoding='utf-8').read()
    if 'TrayGPU 周期性刷屏' in t:
        print('生成器已修过。')
        return 0
    # 生成器里是 python 源码，转义要匹配：先试着直接替换
    for old, new in ((OLD, NEW),):
        if t.count(old) == 1:
            t = t.replace(old, new, 1)
            io.open(GEN, 'w', encoding='utf-8', newline='\n').write(t)
            print('已修补生成器')
            return 0
    print('!! 锚点未命中，需手工处理')
    return 1


if __name__ == '__main__':
    sys.exit(main())
