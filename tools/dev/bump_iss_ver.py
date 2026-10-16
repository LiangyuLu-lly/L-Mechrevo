# -*- coding: utf-8 -*-
p = r'D:\openLM\installer-src\install_script.iss'
t = open(p, encoding='utf-8').read()
reps = [
    ('AppVerName=L-Mechrevo 0.290.0-beta21.1', 'AppVerName=L-Mechrevo 0.290.2-beta21.2'),
    ('AppVersion=0.290.0-beta21.1', 'AppVersion=0.290.2-beta21.2'),
    ('OutputBaseFilename=LM-0.290.0-beta21.1-setup', 'OutputBaseFilename=LM-0.290.2-beta21.2-setup'),
    ('VersionInfoVersion=0.290.0', 'VersionInfoVersion=0.290.2'),
    ('VersionInfoProductVersion=0.290.0', 'VersionInfoProductVersion=0.290.2'),
    ('UninstallDisplayName=L-Mechrevo 0.290.0-beta21.1', 'UninstallDisplayName=L-Mechrevo 0.290.2-beta21.2'),
    ('-InstallerVersion ""0.290.0""', '-InstallerVersion ""0.290.2""'),
]
for a, b in reps:
    if a in t:
        t = t.replace(a, b)
        print('OK:', a[:40])
    else:
        print('MISS:', a[:40])
open(p, 'w', encoding='utf-8', newline='\n').write(t)
# 校验无残留
t2 = open(p, encoding='utf-8').read()
print('残留 0.290.0:', t2.count('0.290.0'))
print('新版本出现次数 beta21.2:', t2.count('beta21.2'))
