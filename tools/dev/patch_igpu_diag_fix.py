# -*- coding: utf-8 -*-
r"""
修 IGPU-DIAG 诊断行（花括号写成了双层 -> 打出源码字面量）
并增强：把所有含 "pu" 的键名打出来，直接看 LM 拿到了哪些字段
"""
import io
import sys

F = r'D:\openLM\source\MechrevoLite.Hardware\MechrevoHw.cs'

OLD = ('\t\tLogger.WriteLineIfChanged("igpu-diag", $"IGPU-DIAG: text24={{text24 ?? \\"<null>\\"}} '
       'text25={{text25 ?? \\"<null>\\"}} keys={{o.Count}} '
       'has24={{o.ContainsKey(\\"DiscreteGpuDirectConnectionSwitch_Status\\")}} '
       'has25={{o.ContainsKey(\\"IGpuOnlyConnectionSwitch_Status\\")}} '
       'sup24={{o.GetValueOrDefault(\\"DiscreteGpuDirectConnectionSwitch_Support\\")}} '
       'seen={{_igpuRouteStatusSeen}}");\n')

NEW = ('\t\tstring diag24 = text24 ?? "<null>";\n'
       '\t\tstring diag25 = text25 ?? "<null>";\n'
       '\t\tstring diagKeys = string.Join(",", o.Properties().Select((Newtonsoft.Json.Linq.JProperty p) => p.Name).Where((string k) => k.IndexOf("pu", StringComparison.OrdinalIgnoreCase) >= 0).Take(40));\n'
       '\t\tLogger.WriteLineIfChanged("igpu-diag", $"IGPU-DIAG: text24={diag24} text25={diag25} keys={o.Count} has24={o.ContainsKey("DiscreteGpuDirectConnectionSwitch_Status")} has25={o.ContainsKey("IGpuOnlyConnectionSwitch_Status")} gpuKeys=[{diagKeys}] seen={_igpuRouteStatusSeen}");\n')


def main():
    data = io.open(F, 'rb').read()
    ob = OLD.encode('utf-8')
    nb = NEW.encode('utf-8')
    n = data.count(ob)
    if n == 0:
        if b'gpuKeys=[' in data:
            print('已经修过了。')
            return 0
        alt = ob.replace(b'\r\n', b'\n')
        if data.count(alt) == 1:
            print('  (行尾适配)')
            io.open(F, 'wb').write(data.replace(alt, nb.replace(b'\r\n', b'\n'), 1))
            print('  写入 ' + F)
            return 0
        print('=== 锚点未命中（%d），未写盘 ===' % n)
        print('  前 200 字节期望: %r' % ob[:120])
        return 1
    print('=== 锚点校验通过 ===')
    if '--check' in sys.argv:
        return 0
    io.open(F, 'wb').write(data.replace(ob, nb, 1))
    print('  写入 ' + F)
    return 0


if __name__ == '__main__':
    sys.exit(main())
