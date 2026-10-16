# -*- coding: utf-8 -*-
# 修复 WCS P/Invoke 参数顺序：scope 应为第一参数
import io

def edit(path, old, new, tag):
    t = io.open(path, encoding='utf-8').read()
    n = t.count(old)
    print('[' + tag + '] 匹配:', n)
    if n != 1:
        raise SystemExit('ABORT ' + tag + ': ' + str(n))
    io.open(path, 'w', encoding='utf-8', newline='').write(t.replace(old, new))
    print('[' + tag + '] OK')

p = r'D:\openLM\source\MechrevoLite.Display\IccHelper.cs'

# 1) 声明：scope 移为第一参数
edit(p,
'private static extern bool WcsAssociateColorProfileWithDevice(string profileName, string deviceName, uint scope);',
'private static extern bool WcsAssociateColorProfileWithDevice(uint scope, string profileName, string deviceName);',
'decl-assoc')

edit(p,
'private static extern bool WcsDisassociateColorProfileFromDevice(string profileName, string deviceName, uint scope);',
'private static extern bool WcsDisassociateColorProfileFromDevice(uint scope, string profileName, string deviceName);',
'decl-disassoc')

# 2) 调用处同步
edit(p,
'WcsAssociateColorProfileWithDevice(profilePath, device, WCS_PROFILE_MANAGEMENT_SCOPE_CURRENT_USER)',
'WcsAssociateColorProfileWithDevice(WCS_PROFILE_MANAGEMENT_SCOPE_CURRENT_USER, profilePath, device)',
'call-assoc')

edit(p,
'WcsDisassociateColorProfileFromDevice(file, device, WCS_PROFILE_MANAGEMENT_SCOPE_CURRENT_USER)',
'WcsDisassociateColorProfileFromDevice(WCS_PROFILE_MANAGEMENT_SCOPE_CURRENT_USER, file, device)',
'call-disassoc')

print('ALL OK')
