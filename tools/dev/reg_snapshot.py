import winreg, json, sys

# 快照电源设置域相关注册表，输出为 JSON 供前后对比
DOMAINS = [
    (winreg.HKEY_LOCAL_MACHINE, r'SYSTEM\CurrentControlSet\Control\Power'),
    (winreg.HKEY_LOCAL_MACHINE, r'SYSTEM\CurrentControlSet\Control\Session Manager\Power'),
    (winreg.HKEY_LOCAL_MACHINE, r'SOFTWARE\Microsoft\Windows\CurrentVersion\Policies\Power'),
    (winreg.HKEY_LOCAL_MACHINE, r'SYSTEM\CurrentControlSet\Control\Power\User\PowerSchemes'),
]

def enum_tree(hive, path, depth=0):
    out = {}
    try:
        k = winreg.OpenKey(hive, path, 0, winreg.KEY_READ | winreg.KEY_WOW64_64KEY)
    except OSError:
        return None
    try:
        vals = {}
        i = 0
        while True:
            try:
                name, val, typ = winreg.EnumValue(k, i)
                vals[name] = {'t': typ, 'v': val if isinstance(val, int) else str(val)}
                i += 1
            except OSError:
                break
        if vals:
            out['.values'] = vals
        subs = {}
        i = 0
        while True:
            try:
                sub = winreg.EnumKey(k, i)
                subs[sub] = enum_tree(hive, path + '\\' + sub, depth + 1)
                i += 1
            except OSError:
                break
        if subs:
            out['.subkeys'] = subs
    finally:
        winreg.CloseKey(k)
    return out

result = {}
for hive, path in DOMAINS:
    key = ('HKLM\\' + path)
    result[key] = enum_tree(hive, path)

with open(sys.argv[1], 'w', encoding='utf-8') as f:
    json.dump(result, f, ensure_ascii=False, indent=1)
print('snapshot written:', sys.argv[1])
