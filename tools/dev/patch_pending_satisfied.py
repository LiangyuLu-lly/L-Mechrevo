# -*- coding: utf-8 -*-
r"""
修「待生效」残留：NVRAM 待生效路由 == 实际路由 时视为没有待生效
（v2：统一把文件规范化成 LF 再匹配，最后按原行尾写回 —— 修掉 v1 的行尾回退 bug）
"""
import io
import sys

F = r'D:\openLM\source\MechrevoLite.Gpu\GpuRestartVerifier.cs'

HELPER = '''\t/// <summary>
\t/// 2026-10-05：当 NVRAM 的待生效显示路由**等于**当前实际路由时，说明没有真正待生效的变更
\t/// （典型场景：切过去又切回来、或已生效但配置里的 pending 标记尚未清），
\t/// 此时不应再显示「重启以应用」与棕色高亮。
\t/// 与 PendingTarget 里「不同则以 NVRAM 为准」互为补充。
\t/// </summary>
\tinternal static bool PendingAlreadySatisfied()
\t{
\t\tif (!AppConfig.Is("gpu_restart_pending"))
\t\t{
\t\t\treturn false;
\t\t}
\t\tif (!UefiDisplayModeDiagnostics.TryGetPendingRoute(Program.hw?.IsAmdPlatformForLimits ?? false, out GpuRoute? pending) || !pending.HasValue)
\t\t{
\t\t\treturn false;
\t\t}
\t\tGpuRoute actual = GpuRouteMonitor.Snapshot().Route;
\t\treturn actual != GpuRoute.Unknown && pending.Value == actual;
\t}

'''

OPS = [
    ('\tinternal static int PendingTarget\n',
     HELPER + '\tinternal static int PendingTarget\n',
     '新增 PendingAlreadySatisfied()'),

    ('\t\t\tif (!AppConfig.Is("gpu_restart_pending"))\n'
     '\t\t\t{\n'
     '\t\t\t\treturn -1;\n'
     '\t\t\t}\n'
     '\t\t\t// 2026-10-05：外部命令（不经 LM）也会写 NVRAM 的待生效显示路由。\n',
     '\t\t\tif (!AppConfig.Is("gpu_restart_pending"))\n'
     '\t\t\t{\n'
     '\t\t\t\treturn -1;\n'
     '\t\t\t}\n'
     '\t\t\t// 2026-10-05：NVRAM 待生效路由 == 实际路由 -> 没有真正待生效的变更\n'
     '\t\t\tif (PendingAlreadySatisfied())\n'
     '\t\t\t{\n'
     '\t\t\t\treturn -1;\n'
     '\t\t\t}\n'
     '\t\t\t// 2026-10-05：外部命令（不经 LM）也会写 NVRAM 的待生效显示路由。\n',
     'PendingTarget 加守卫'),

    ('\tinternal static bool HasPendingThisBoot()\n'
     '\t{\n'
     '\t\tif (!AppConfig.Is("gpu_restart_pending"))\n'
     '\t\t{\n'
     '\t\t\treturn false;\n'
     '\t\t}\n',
     '\tinternal static bool HasPendingThisBoot()\n'
     '\t{\n'
     '\t\tif (!AppConfig.Is("gpu_restart_pending"))\n'
     '\t\t{\n'
     '\t\t\treturn false;\n'
     '\t\t}\n'
     '\t\t// 2026-10-05：NVRAM 待生效路由 == 实际路由 -> 视为没有待生效（修掉"切过去又切回来"的残留）\n'
     '\t\tif (PendingAlreadySatisfied())\n'
     '\t\t{\n'
     '\t\t\treturn false;\n'
     '\t\t}\n',
     'HasPendingThisBoot 加守卫'),
]


def main():
    raw = io.open(F, 'rb').read()
    if 'PendingAlreadySatisfied'.encode('utf-8') in raw:
        print('已经应用过了。')
        return 0

    crlf = b'\r\n' in raw
    text = raw.decode('utf-8-sig')
    if text.startswith('\ufeff'):
        text = text[1:]
    norm = text.replace('\r\n', '\n')

    print('文件行尾: %s   规范化为 LF 后 %d 字符' % ('CRLF' if crlf else 'LF', len(norm)))

    for old, new, label in OPS:
        c = norm.count(old)
        print('  [%s] 命中 %d 次' % (label, c))
        if c != 1:
            print('=== 校验失败，未写盘 ===')
            return 1

    if '--check' in sys.argv:
        return 0

    for old, new, label in OPS:
        norm = norm.replace(old, new, 1)

    out = norm.replace('\n', '\r\n') if crlf else norm
    io.open(F, 'w', encoding='utf-8-sig', newline='').write(out)
    print('  写入 ' + F)
    return 0


if __name__ == '__main__':
    sys.exit(main())
