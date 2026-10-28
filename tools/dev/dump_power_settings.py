import winreg, json

# 枚举 PowerSettings 全树：子组 -> 设置 -> FriendlyName/GUID/可能取值
BASE = r'SYSTEM\CurrentControlSet\Control\Power\PowerSettings'

def get_str(k, name):
    try:
        v, _ = winreg.QueryValueEx(k, name)
        return v
    except OSError:
        return None

out = {}
k = winreg.OpenKey(winreg.HKEY_LOCAL_MACHINE, BASE, 0, winreg.KEY_READ | winreg.KEY_WOW64_64KEY)
i = 0
while True:
    try:
        sub = winreg.EnumKey(k, i)
    except OSError:
        break
    i += 1
    sk = winreg.OpenKey(winreg.HKEY_LOCAL_MACHINE, BASE + '\\' + sub)
    grp_name = get_str(sk, 'Description') or ''
    grp_friendly = get_str(sk, 'FriendlyName') or ''
    settings = {}
    j = 0
    while True:
        try:
            ssub = winreg.EnumKey(sk, j)
        except OSError:
            break
        j += 1
        try:
            ss = winreg.OpenKey(winreg.HKEY_LOCAL_MACHINE, BASE + '\\' + sub + '\\' + ssub)
        except OSError:
            continue
        fn = get_str(ss, 'FriendlyName') or ''
        desc = get_str(ss, 'Description') or ''
        # PossibleSettings 枚举
        poss = []
        try:
            pk = winreg.OpenKey(winreg.HKEY_LOCAL_MACHINE, BASE + '\\' + sub + '\\' + ssub + '\\' + 'DefaultPowerSchemeValues')
            # 不枚举 PossibleSettingIndex 名字，只读 index 0.. 的存在性（通过 0/1/2 子键）
        except OSError:
            pass
        settings[ssub] = {'friendly': fn, 'desc': desc[:80]}
        winreg.CloseKey(ss)
    out[sub] = {'group': grp_friendly or grp_name, 'settings': settings}
    winreg.CloseKey(sk)

with open(r'D:\openLM\tools\power_settings_map.json', 'w', encoding='utf-8') as f:
    json.dump(out, f, ensure_ascii=False, indent=1)

# 打印所有设置，供快速检索
for g, info in out.items():
    print('== SUBGROUP %s : %s' % (g, info['group']))
    for s, si in info['settings'].items():
        print('   %s | %s' % (s, si['friendly']))
