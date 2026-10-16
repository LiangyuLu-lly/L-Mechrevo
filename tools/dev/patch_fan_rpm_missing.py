# -*- coding: utf-8 -*-
r"""
修 30 系 GCU 无转速读取的两个显示问题
================================================================================
现象（用户报）：30 系 GCU 不提供风扇转速
  1) 主界面：CPU/GPU 温度与占空比之间留出一大段空档
  2) 托盘浮窗：显示 "-1 rpm"
根因：
  1) SettingsForm :9227-9232 对 _telemetryCpuRest/_telemetryGpuRest 固定
     `MinimumSize = new Size(D(84), 0)`（按 "1234rpm 100%" 预留）；
     而 LayoutTelemetryPieces 的 Advance() = Max(PreferredWidth, MinimumSize.Width)
     -> 没有转速时那 84 宽仍被占用（且右对齐）-> 空档 + 整体居中偏移
  2) SettingsForm :6929-6932 / :6948-6951 无条件拼 " rpm)"，转速为 -1 时显示 "(-1 rpm)"
修法：
  1) 预留宽度改为"能力感知"：一旦见到过有效转速（>=0）就固定 84，否则只按占空比预留 34
     （Advance 取 Max，所以缩预留永远不会裁字）
  2) 托盘：转速 < 0 时不拼 " (Nrpm)"，只显示百分比
行尾：SettingsForm.cs 是 CRLF（脚本含行尾自动适配）。
"""
import io
import sys

F = r'D:\openLM\source\MechrevoLite\SettingsForm.cs'

OPS = []


def op(old, new, label, expect=1):
    if isinstance(old, str):
        old = old.encode('utf-8')
    if isinstance(new, str):
        new = new.encode('utf-8')
    OPS.append((old, new, expect, label))


# ---- 1) 托盘 CPU ----
op(b'\t\t\tcpuFan = HardwareControl.cpuFan;\r\n'
   b'\t\t\tarray[2] = cpuFan.ToString();\r\n'
   b'\t\t\tarray[3] = "% (";\r\n'
   b'\t\t\tarray[4] = HardwareControl.cpuFanRPM.ToString();\r\n'
   b'\t\t\tarray[5] = " rpm)";\r\n'
   b'\t\t\ttext2 = string.Concat(array);\r\n',
   b'\t\t\tcpuFan = HardwareControl.cpuFan;\r\n'
   b'\t\t\tarray[2] = cpuFan.ToString();\r\n'
   b'\t\t\tif (HardwareControl.cpuFanRPM >= 0)\r\n'
   b'\t\t\t{\r\n'
   b'\t\t\t\tarray[3] = "% (";\r\n'
   b'\t\t\t\tarray[4] = HardwareControl.cpuFanRPM.ToString();\r\n'
   b'\t\t\t\tarray[5] = " rpm)";\r\n'
   b'\t\t\t}\r\n'
   b'\t\t\telse\r\n'
   b'\t\t\t{\r\n'
   b'\t\t\t\t// 30-series GCU does not report RPM; omit the suffix instead of "(-1 rpm)".\r\n'
   b'\t\t\t\tarray[3] = "%";\r\n'
   b'\t\t\t}\r\n'
   b'\t\t\ttext2 = string.Concat(array);\r\n',
   '托盘 CPU 转速')

# ---- 2) 托盘 GPU ----
op(b'\t\t\tcpuFan = HardwareControl.gpuFan;\r\n'
   b'\t\t\tarray2[2] = cpuFan.ToString();\r\n'
   b'\t\t\tarray2[3] = "% (";\r\n'
   b'\t\t\tarray2[4] = HardwareControl.gpuFanRPM.ToString();\r\n'
   b'\t\t\tarray2[5] = " rpm)";\r\n'
   b'\t\t\tgpuFan = string.Concat(array2);\r\n',
   b'\t\t\tcpuFan = HardwareControl.gpuFan;\r\n'
   b'\t\t\tarray2[2] = cpuFan.ToString();\r\n'
   b'\t\t\tif (HardwareControl.gpuFanRPM >= 0)\r\n'
   b'\t\t\t{\r\n'
   b'\t\t\t\tarray2[3] = "% (";\r\n'
   b'\t\t\t\tarray2[4] = HardwareControl.gpuFanRPM.ToString();\r\n'
   b'\t\t\t\tarray2[5] = " rpm)";\r\n'
   b'\t\t\t}\r\n'
   b'\t\t\telse\r\n'
   b'\t\t\t{\r\n'
   b'\t\t\t\t// 30-series GCU does not report RPM; omit the suffix instead of "(-1 rpm)".\r\n'
   b'\t\t\t\tarray2[3] = "%";\r\n'
   b'\t\t\t}\r\n'
   b'\t\t\tgpuFan = string.Concat(array2);\r\n',
   '托盘 GPU 转速')

# ---- 3) 主界面预留宽度：改为能力感知 ----
op(b'\t\tLabel[] array = new Label[2] { _telemetryCpuRest, _telemetryGpuRest };\r\n'
   b'\t\tforeach (Label obj in array)\r\n'
   b'\t\t{\r\n'
   b'\t\t\tobj.TextAlign = ContentAlignment.MiddleRight;\r\n'
   b'\t\t\tobj.MinimumSize = new Size(D(84), 0);\r\n'
   b'\t\t}\r\n',
   b'\t\tLabel[] array = new Label[2] { _telemetryCpuRest, _telemetryGpuRest };\r\n'
   b'\t\tforeach (Label obj in array)\r\n'
   b'\t\t{\r\n'
   b'\t\t\tobj.TextAlign = ContentAlignment.MiddleRight;\r\n'
   b'\t\t\tobj.MinimumSize = new Size(TelemetryRestReserveWidth(), 0);\r\n'
   b'\t\t}\r\n',
   '预留宽度改能力感知')

# ---- 4) 新增字段 + 方法（挂在 BuildTelemetryRow 之前）----
op('\tprivate void BuildTelemetryRow()\r\n',
   '\t/// <summary>是否见过有效风扇转速（30 系 GCU 恒为 -1，见 TelemetryRestReserveWidth）。</summary>\r\n'
   '\tprivate bool _telemetryRpmSeen;\r\n'
   '\r\n'
   '\t/// <summary>\r\n'
   '\t/// _telemetryCpuRest/_telemetryGpuRest 的预留宽度。\r\n'
   '\t/// 见到过有效转速 -> 按 "1234rpm 100%" 预留 D(84)；\r\n'
   '\t/// 从未见到（如 30 系 GCU 不提供转速）-> 只按占空比预留 D(34)，避免留出一大段空档。\r\n'
   '\t/// 注意 LayoutTelemetryPieces 的 Advance() 取 Max(PreferredWidth, MinimumSize.Width)，\r\n'
   '\t/// 所以缩小预留永远不会裁掉文字。\r\n'
   '\t/// </summary>\r\n'
   '\tprivate int TelemetryRestReserveWidth()\r\n'
   '\t{\r\n'
   '\t\treturn _telemetryRpmSeen ? D(84) : D(34);\r\n'
   '\t}\r\n'
   '\r\n'
   '\tprivate void BuildTelemetryRow()\r\n',
   '新增字段与方法')

# ---- 5) 运行时一旦见到转速就升级预留 ----
op(b'\t\t_telemetryLayingOut = true;\r\n'
   b'\t\t_telemetryPanel.SuspendLayout();\r\n'
   b'\t\ttry\r\n'
   b'\t\t{\r\n',
   '\t\t_telemetryLayingOut = true;\r\n'
   '\t\t_telemetryPanel.SuspendLayout();\r\n'
   '\t\ttry\r\n'
   '\t\t{\r\n'
   '\t\t\t// 一次性把预留宽度升级到含转速的版本（30 系 GCU 永远升不上来 -> 不留空档）。\r\n'
   '\t\t\tMechrevoHw? hw = Program.hw;\r\n'
   '\t\t\tif (!_telemetryRpmSeen && hw != null && (hw.CpuFanRpm >= 0 || hw.GpuFanRpm >= 0))\r\n'
   '\t\t\t{\r\n'
   '\t\t\t\t_telemetryRpmSeen = true;\r\n'
   '\t\t\t\tSize reserve = new Size(TelemetryRestReserveWidth(), 0);\r\n'
   '\t\t\t\t_telemetryCpuRest.MinimumSize = reserve;\r\n'
   '\t\t\t\t_telemetryGpuRest.MinimumSize = reserve;\r\n'
   '\t\t\t}\r\n',
   '运行时升级预留')


def main():
    data = io.open(F, 'rb').read()
    if b'_telemetryRpmSeen' in data:
        print('已经应用过了，无需重复。')
        return 0
    resolved = []
    errors = []
    for old, new, expect, label in OPS:
        if data.count(old) == expect:
            resolved.append((old, new, expect, label))
            continue
        alt_old = old.replace(b'\r\n', b'\n') if b'\r\n' in old else old.replace(b'\n', b'\r\n')
        alt_new = new.replace(b'\r\n', b'\n') if b'\r\n' in new else new.replace(b'\n', b'\r\n')
        if data.count(alt_old) == expect:
            print('  (行尾适配) ' + label)
            resolved.append((alt_old, alt_new, expect, label))
            continue
        errors.append('命中 %d/%d 次(期望 %d)  [%s]' % (data.count(old), data.count(alt_old), expect, label))
    if errors:
        print('=== 校验失败，未写盘 ===')
        for e in errors:
            print('  ! ' + e)
        return 1
    print('=== 锚点校验全部通过（%d 项）===' % len(resolved))
    if '--check' in sys.argv:
        return 0
    for old, new, expect, label in resolved:
        data = data.replace(old, new, expect)
    io.open(F, 'wb').write(data)
    print('  写入 ' + F)
    return 0


if __name__ == '__main__':
    sys.exit(main())
