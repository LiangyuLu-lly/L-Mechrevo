# -*- coding: utf-8 -*-
# 版本号改为 18.3 内测版（AssemblyInfo + iss）
import io

# 1) AssemblyInfo.cs
p = r'D:\openLM\source\Properties\AssemblyInfo.cs'
t = io.open(p, encoding='utf-8').read()
for old, new in [
    ('[assembly: AssemblyFileVersion("0.290.3")]', '[assembly: AssemblyFileVersion("18.3")]'),
    ('[assembly: AssemblyInformationalVersion("0.290.3-beta21.3")]', '[assembly: AssemblyInformationalVersion("18.3-内测版")]'),
    ('[assembly: AssemblyVersion("0.290.3")]', '[assembly: AssemblyVersion("18.3")]'),
]:
    print('[asm]', old, t.count(old))
    assert t.count(old) == 1
    t = t.replace(old, new)
io.open(p, 'w', encoding='utf-8', newline='').write(t)

# 2) install_script.iss
p = r'D:\openLM\installer-src\install_script.iss'
t = io.open(p, encoding='utf-8').read()
for old, new in [
    ('AppVerName=L-Mechrevo 0.290.3-beta21.3', 'AppVerName=L-Mechrevo 18.3 内测版'),
    ('AppVersion=0.290.3-beta21.3', 'AppVersion=18.3'),
    ('OutputBaseFilename=LM-0.290.3-beta21.3-setup', 'OutputBaseFilename=LM-18.3内测版-setup'),
    ('VersionInfoVersion=0.290.3', 'VersionInfoVersion=18.3'),
    ('VersionInfoProductVersion=0.290.3', 'VersionInfoProductVersion=18.3'),
    ('UninstallDisplayName=L-Mechrevo 0.290.3-beta21.3', 'UninstallDisplayName=L-Mechrevo 18.3 内测版'),
    ('-InstallerVersion ""0.290.3""', '-InstallerVersion ""18.3""'),
]:
    print('[iss]', old, t.count(old))
    assert t.count(old) == 1
    t = t.replace(old, new)
io.open(p, 'w', encoding='utf-8', newline='').write(t)
print('DONE')
