# -*- coding: utf-8 -*-
r"""
修「重启以应用」的待生效目标显示错误
================================================================================
实例（实测）：
  gpu_restart_pending=1 / gpu_restart_target=2（独显直连）  <- 用户 11:05 自己切独显时留下的
  NVRAM @0x62=2 (IgpuOnly) / 实际路由=Direct
  -> 界面把"下次重启会切成什么"显示成【独显直连】❌（实际待生效是【核显模式】）
成因：PendingTarget 只来自 LM 自己写配置（MarkPending）；外部命令写的 NVRAM 不参与。
修法：
  1) UefiDisplayModeDiagnostics 新增 TryReadRoute / PendingRouteSnapshot（带 5s 缓存，
     避免每次 UI 刷新都调固件）
  2) GpuRestartVerifier.PendingTarget：当 NVRAM 待生效路由 != 当前实际路由时，以 NVRAM 为准
     （返回 ToGpuMode(NVRAM 路由)），否则沿用配置值
"""
import io
import sys

UEFI = r'D:\openLM\source\MechrevoLite.Gpu\UefiDisplayModeDiagnostics.cs'
VER = r'D:\openLM\source\MechrevoLite.Gpu\GpuRestartVerifier.cs'

OPS = []


def op(path, old, new, label, expect=1):
    if isinstance(old, str):
        old = old.encode('utf-8')
    if isinstance(new, str):
        new = new.encode('utf-8')
    OPS.append((path, old, new, expect, label))


# ---- 1) UefiDisplayModeDiagnostics：新增读取待生效路由 ----
op(UEFI,
   '\t/// <summary>兼容旧签名（默认按 Intel 解码）。</summary>\n'
   '\tinternal static string Describe()\n',
   '\tprivate static GpuRoute? _pendingRouteCache;\n'
   '\n'
   '\tprivate static long _pendingRouteCacheAt;\n'
   '\n'
   '\t/// <summary>\n'
   '\t/// 读取 NVRAM 里「待生效」的显示路由（与 Describe 同一变量/偏移，只是返回结构化结果）。\n'
   '\t/// 需要管理员权限；失败返回 false（route 为 null 表示读到了但解码不出）。\n'
   '\t/// </summary>\n'
   '\tinternal static bool TryReadRoute(bool isAmd, out GpuRoute? route)\n'
   '\t{\n'
   '\t\troute = null;\n'
   '\t\ttry\n'
   '\t\t{\n'
   '\t\t\tif (!ProcessHelper.IsUserAdministrator() || !EnableSystemEnvironmentPrivilege())\n'
   '\t\t\t{\n'
   '\t\t\t\treturn false;\n'
   '\t\t\t}\n'
   '\t\t\tbyte[] buffer = new byte[4096];\n'
   '\t\t\tuint read = GetFirmwareEnvironmentVariableW("UniWillVariable", "{9f33f85c-13ca-4fd1-9c4a-96217722c593}", buffer, (uint)buffer.Length);\n'
   '\t\t\tif (read <= 98)\n'
   '\t\t\t{\n'
   '\t\t\t\treturn false;\n'
   '\t\t\t}\n'
   '\t\t\troute = GpuRouteInference.DecodeOemDisplayMode(buffer[98], isAmd);\n'
   '\t\t\treturn true;\n'
   '\t\t}\n'
   '\t\tcatch (Exception ex)\n'
   '\t\t{\n'
   '\t\t\tLogger.WriteLine("TryReadRoute failed: " + ex.Message);\n'
   '\t\t\treturn false;\n'
   '\t\t}\n'
   '\t}\n'
   '\n'
   '\t/// <summary>带 5 秒缓存的待生效路由（UI 每次刷新都会问，不能每次都调固件）。</summary>\n'
   '\tinternal static bool TryGetPendingRoute(bool isAmd, out GpuRoute? route)\n'
   '\t{\n'
   '\t\tlong now = Environment.TickCount64;\n'
   '\t\tif (_pendingRouteCacheAt != 0 && now - _pendingRouteCacheAt < 5000)\n'
   '\t\t{\n'
   '\t\t\troute = _pendingRouteCache;\n'
   '\t\t\treturn _pendingRouteCacheAt > 0;\n'
   '\t\t}\n'
   '\t\tbool ok = TryReadRoute(isAmd, out route);\n'
   '\t\t_pendingRouteCache = route;\n'
   '\t\t_pendingRouteCacheAt = ok ? now : -now - 1;\n'
   '\t\treturn ok;\n'
   '\t}\n'
   '\n'
   '\t/// <summary>兼容旧签名（默认按 Intel 解码）。</summary>\n'
   '\tinternal static string Describe()\n',
   'UefiDisplayModeDiagnostics 新增 TryReadRoute/TryGetPendingRoute')

# ---- 2) GpuRestartVerifier.PendingTarget：以 NVRAM 为准 ----
op(VER,
   '\tinternal static int PendingTarget\n'
   '\t{\n'
   '\t\tget\n'
   '\t\t{\n'
   '\t\t\tif (!AppConfig.Is("gpu_restart_pending"))\n'
   '\t\t\t{\n'
   '\t\t\t\treturn -1;\n'
   '\t\t\t}\n'
   '\t\t\treturn AppConfig.Get("gpu_restart_target");\n'
   '\t\t}\n'
   '\t}\n',
   '\tinternal static int PendingTarget\n'
   '\t{\n'
   '\t\tget\n'
   '\t\t{\n'
   '\t\t\tif (!AppConfig.Is("gpu_restart_pending"))\n'
   '\t\t\t{\n'
   '\t\t\t\treturn -1;\n'
   '\t\t\t}\n'
   '\t\t\t// 2026-10-05：外部命令（不经 LM）也会写 NVRAM 的待生效显示路由。\n'
   '\t\t\t// 若 NVRAM 的待生效路由与当前实际路由不同，则以 NVRAM 为准 ——\n'
   '\t\t\t// 否则界面会把「重启以应用」的目标显示错（实测 target 残留 2=独显，\n'
   '\t\t\t// 而 NVRAM 实际待生效是 2(IgpuOnly)=核显模式）。\n'
   '\t\t\tif (UefiDisplayModeDiagnostics.TryGetPendingRoute(Program.hw?.IsAmdPlatformForLimits ?? false, out GpuRoute? pending)\n'
   '\t\t\t\t&& pending.HasValue)\n'
   '\t\t\t{\n'
   '\t\t\t\tGpuRoute actual = GpuRouteMonitor.Snapshot().Route;\n'
   '\t\t\t\tif (actual != GpuRoute.Unknown && pending.Value != actual)\n'
   '\t\t\t\t{\n'
   '\t\t\t\t\tint fromNvram = GpuRouteInference.ToGpuMode(pending.Value);\n'
   '\t\t\t\t\tif (fromNvram >= 0)\n'
   '\t\t\t\t\t{\n'
   '\t\t\t\t\t\treturn fromNvram;\n'
   '\t\t\t\t\t}\n'
   '\t\t\t\t}\n'
   '\t\t\t}\n'
   '\t\t\treturn AppConfig.Get("gpu_restart_target");\n'
   '\t\t}\n'
   '\t}\n',
   'PendingTarget 以 NVRAM 为准')


def main():
    data0 = io.open(UEFI, 'rb').read()
    if b'TryGetPendingRoute' in data0:
        print('已经应用过了。')
        return 0
    resolved, errors = [], []
    for path, old, new, expect, label in OPS:
        data = io.open(path, 'rb').read()
        if data.count(old) == expect:
            resolved.append((path, old, new, expect, label))
            continue
        if b'\r\n' in old:
            ao, an = old.replace(b'\r\n', b'\n'), new.replace(b'\r\n', b'\n')
        else:
            ao, an = old.replace(b'\n', b'\r\n'), new.replace(b'\n', b'\r\n')
        if data.count(ao) == expect:
            print('  (行尾适配) ' + label)
            resolved.append((path, ao, an, expect, label))
            continue
        errors.append('命中 %d/%d 次(期望 %d)  [%s]  %s' % (data.count(old), data.count(ao), expect, label, path.split('\\')[-1]))
    if errors:
        print('=== 校验失败，未写盘 ===')
        for e in errors:
            print('  ! ' + e)
        return 1
    print('=== 锚点校验通过（%d 项）===' % len(resolved))
    if '--check' in sys.argv:
        return 0
    for path, old, new, expect, label in resolved:
        d = io.open(path, 'rb').read()
        io.open(path, 'wb').write(d.replace(old, new, expect))
        print('  写入 ' + path)
    return 0


if __name__ == '__main__':
    sys.exit(main())
