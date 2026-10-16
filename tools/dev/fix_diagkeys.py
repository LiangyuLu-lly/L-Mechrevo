# -*- coding: utf-8 -*-
"""修 diagKeys 那行：o 是 JObject，要用 Properties()"""
import io
import sys

F = r'D:\openLM\source\MechrevoLite.Hardware\MechrevoHw.cs'

OLD = '\t\tstring diagKeys = string.Join(",", o.Keys.Where((string k) => k.IndexOf("pu", StringComparison.OrdinalIgnoreCase) >= 0).Take(40));\n'.encode('utf-8')
NEW = ('\t\tstring diagKeys = string.Join(",", o.Properties().Select((Newtonsoft.Json.Linq.JProperty p) => p.Name).Where((string k) => k.IndexOf("pu", StringComparison.OrdinalIgnoreCase) >= 0).Take(40));\n').encode('utf-8')


def main():
    data = io.open(F, 'rb').read()
    if data.count(OLD) != 1:
        alt = OLD.replace(b'\r\n', b'\n')
        if data.count(alt) == 1:
            io.open(F, 'wb').write(data.replace(alt, NEW.replace(b'\r\n', b'\n'), 1))
            print('  (行尾适配) 已写入')
            return 0
        print('=== 锚点命中 %d 次 ===' % data.count(OLD))
        return 1
    io.open(F, 'wb').write(data.replace(OLD, NEW, 1))
    print('  已写入 ' + F)
    return 0


if __name__ == '__main__':
    sys.exit(main())
