# -*- coding: utf-8 -*-
r"""
修 NVRAM 显示模式字节的平台解码 bug
================================================================================
现象：本机（AMD）日志把 @0x62=1 标成 "intel-decoded=IgpuOnly"，
      但当时真实路由是 Direct（AMD 编码 Direct=1 / Hybrid=0 / Igpu=2）。
      同时 @0x62=0（= AMD 的 Hybrid）被标成 "unrecognized"（Intel 表里没有 0）。
根因：UefiDisplayModeDiagnostics.cs:157 把平台写死
      `GpuRouteInference.DecodeOemDisplayMode(value, isAmd: false)`
修法：
  1) Describe() -> Describe(bool isAmd)，解码与标签都按真实平台
  2) 两个调用点传 `Program.hw?.IsAmdPlatformForLimits ?? false`
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


# ---- 1) Describe() -> Describe(bool isAmd) ----
op(UEFI,
   '\tinternal static string Describe()\n',
   '\t/// <summary>兼容旧签名（默认按 Intel 解码）。</summary>\n'
   '\tinternal static string Describe()\n'
   '\t{\n'
   '\t\treturn Describe(isAmd: false);\n'
   '\t}\n'
   '\n'
   '\t/// <summary>按真实平台解码 NVRAM 显示模式字节。</summary>\n'
   '\tinternal static string Describe(bool isAmd)\n',
   'Describe 增加 isAmd 重载')

# ---- 2) 解码与标签按平台 ----
op(UEFI,
   '\t\t\t\tbyte value = array[98];\n'
   '\t\t\t\tGpuRoute? gpuRoute = GpuRouteInference.DecodeOemDisplayMode(value, isAmd: false);\n'
   '\t\t\t\treturn $"NVRAM display byte @0x{98:X}={value} (len={firmwareEnvironmentVariableW}, intel-decoded={gpuRoute?.ToString() ?? "unrecognized"}; offset unverified)";\n',
   '\t\t\t\tbyte value = array[98];\n'
   '\t\t\t\t// 2026-10-05 fix: 平台原先被写死成 Intel，导致 AMD 机器上\n'
   '\t\t\t\t// Direct(1) 被误标成 IgpuOnly、Hybrid(0) 被标成 unrecognized。\n'
   '\t\t\t\tGpuRoute? gpuRoute = GpuRouteInference.DecodeOemDisplayMode(value, isAmd);\n'
   '\t\t\t\treturn $"NVRAM display byte @0x{98:X}={value} (len={firmwareEnvironmentVariableW}, {(isAmd ? "amd" : "intel")}-decoded={gpuRoute?.ToString() ?? "unrecognized"}; offset unverified)";\n',
   '解码与标签按平台')

# ---- 3) 两个调用点 ----
op(VER,
   '\t\t\tLogger.WriteLine($"GPU route at startup: {value}; {UefiDisplayModeDiagnostics.Describe()}");\n',
   '\t\t\tLogger.WriteLine($"GPU route at startup: {value}; {UefiDisplayModeDiagnostics.Describe(Program.hw?.IsAmdPlatformForLimits ?? false)}");\n',
   '调用点 1 传平台')

op(VER,
   '\t\t\tLogger.WriteLine($"GPU route verify: target={target} actual={readback.Route} ({readback}) verdict={gpuRestartVerdict}; {UefiDisplayModeDiagnostics.Describe()}");\n',
   '\t\t\tLogger.WriteLine($"GPU route verify: target={target} actual={readback.Route} ({readback}) verdict={gpuRestartVerdict}; {UefiDisplayModeDiagnostics.Describe(Program.hw?.IsAmdPlatformForLimits ?? false)}");\n',
   '调用点 2 传平台')


def main():
    if 'Describe(bool isAmd)' in io.open(UEFI, encoding='utf-8-sig', errors='replace').read():
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
        errors.append('命中 %d/%d 次(期望 %d)  [%s]  %s' % (data.count(old), data.count(ao), expect, label, path))
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
