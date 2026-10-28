# -*- coding: utf-8 -*-
r"""
诊断：打印 text24 / text25 的真实取值，确认 LM 到底读到了什么
（三次修改无效 -> 不再猜，先看真值）
"""
import io
import sys

F = r'D:\openLM\source\MechrevoLite.Hardware\MechrevoHw.cs'

OLD = ('\t\tstring text24 = FirstField(o, "DiscreteGpuDirectConnectionSwitch_Status", "DGpuDirectConnectionSwitch_Status", "DGPU_DIRECT_STATUS")?.ToString();\n'
       '\t\tstring text25 = FirstField(o, "IGpuOnlyConnectionSwitch_Status", "IGPUOnlyConnectionSwitch_Status", "IGPU_ONLY_STATUS")?.ToString();\n').encode('utf-8')

NEW = ('\t\tstring text24 = FirstField(o, "DiscreteGpuDirectConnectionSwitch_Status", "DGpuDirectConnectionSwitch_Status", "DGPU_DIRECT_STATUS")?.ToString();\n'
       '\t\tstring text25 = FirstField(o, "IGpuOnlyConnectionSwitch_Status", "IGPUOnlyConnectionSwitch_Status", "IGPU_ONLY_STATUS")?.ToString();\n'
       '\t\t// 2026-10-05 diagnostic: 核显判据三次修改无效 -> 打真值（字段是否读到、键是否同名）\n'
       '\t\tLogger.WriteLineIfChanged("igpu-diag", $\"IGPU-DIAG: text24={{text24 ?? \\"<null>\\"}} text25={{text25 ?? \\"<null>\\"}} keys={{o.Count}} has24={{o.ContainsKey(\\"DiscreteGpuDirectConnectionSwitch_Status\\")}} has25={{o.ContainsKey(\\"IGpuOnlyConnectionSwitch_Status\\")}} sup24={{o.GetValueOrDefault(\\"DiscreteGpuDirectConnectionSwitch_Support\\")}} seen={{_igpuRouteStatusSeen}}\");\n').encode('utf-8')


def main():
    data = io.open(F, 'rb').read()
    if b'IGPU-DIAG' in data:
        print('已经加过诊断了。')
        return 0
    if data.count(OLD) != 1:
        alt = OLD.replace(b'\r\n', b'\n')
        if data.count(alt) == 1:
            print('  (行尾适配)')
            io.open(F, 'wb').write(data.replace(alt, NEW.replace(b'\r\n', b'\n'), 1))
            print('  写入 ' + F)
            return 0
        print('=== 锚点命中 %d 次，未写盘 ===' % data.count(OLD))
        return 1
    print('=== 锚点校验通过 ===')
    if '--check' in sys.argv:
        return 0
    io.open(F, 'wb').write(data.replace(OLD, NEW, 1))
    print('  写入 ' + F)
    return 0


if __name__ == '__main__':
    sys.exit(main())
