# -*- coding: utf-8 -*-
r"""
版本号 18.3 -> 0.290.3-beta21.3
================================================================================
AssemblyInfo.cs:
  AssemblyFileVersion        "18.3"        -> "0.290.3"
  AssemblyInformationalVersion "18.3-内测版" -> "0.290.3-beta21.3"
  AssemblyVersion            "18.3"        -> "0.290.3"
显示（Program.NormalizeRelease* + SettingsForm:3355）：
  ReleaseVersion = "0.290.3" ; ReleaseLabel = "beta21.3"
  -> labelVersion.Text = "0.290.3 beta21.3" ✅
"""
import io
import sys

F = r'D:\openLM\source\Properties\AssemblyInfo.cs'

OPS = [
    ('[assembly: AssemblyFileVersion("18.3")]\n',
     '[assembly: AssemblyFileVersion("0.290.3")]\n',
     'AssemblyFileVersion'),
    ('[assembly: AssemblyInformationalVersion("18.3-内测版")]\n',
     '[assembly: AssemblyInformationalVersion("0.290.3-beta21.3")]\n',
     'AssemblyInformationalVersion'),
    ('[assembly: AssemblyVersion("18.3")]\n',
     '[assembly: AssemblyVersion("0.290.3")]\n',
     'AssemblyVersion'),
]


def main():
    data = io.open(F, 'rb').read()
    if b'0.290.3' in data:
        print('已经改过了。')
        return 0
    resolved = []
    errors = []
    for old, new, label in OPS:
        ob, nb = old.encode('utf-8'), new.encode('utf-8')
        if data.count(ob) == 1:
            resolved.append((ob, nb, label))
            continue
        alt = ob.replace(b'\n', b'\r\n')
        alq = nb.replace(b'\n', b'\r\n')
        if data.count(alt) == 1:
            print('  (行尾适配) ' + label)
            resolved.append((alt, alq, label))
            continue
        errors.append('命中 %d 次  [%s]' % (data.count(ob), label))
    if errors:
        print('=== 校验失败，未写盘 ===')
        for e in errors:
            print('  ! ' + e)
        return 1
    print('=== 锚点校验通过（%d 项）===' % len(resolved))
    if '--check' in sys.argv:
        return 0
    for ob, nb, label in resolved:
        data = data.replace(ob, nb, 1)
    io.open(F, 'wb').write(data)
    print('  写入 ' + F)
    return 0


if __name__ == '__main__':
    sys.exit(main())
