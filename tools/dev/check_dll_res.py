# -*- coding: utf-8 -*-
import io

data = open(r'C:\Program Files\L-Mechrevo\L-Mechrevo.dll', 'rb').read()
pats = [
    'QuickSleepNetOff', 'QuickSleepDeep', 'QuickGroupSleep',
    'Disconnect network on standby', '待机断开网络', '睡眠省电',
]
for p in pats:
    b = p.encode('utf-8')
    print(p, 'FOUND' if b in data else 'missing')
print('--- ascii only check for resources ---')
for p in ['QuickDeepSleep', 'Power & system', 'QuickStartup']:
    print(p, 'FOUND' if p.encode() in data else 'missing')
