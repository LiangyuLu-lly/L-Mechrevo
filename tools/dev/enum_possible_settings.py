# -*- coding: utf-8 -*-
"""枚举 6 个睡眠省电设置的 PossibleSettings（判断是否多选项）"""
import winreg

BASE = r'SYSTEM\CurrentControlSet\Control\Power\PowerSettings'

SETTINGS = [
    ('sleepnetoff', '', 'f15576e8-98b7-4186-b944-eafa664402d9'),
    ('sleepaway', '238c9fa8-0aad-41ed-83f4-97be242c8f20', '25dfa149-5dd1-4736-b5ab-e8a37b5b8187'),
    ('sleepsysreq', '238c9fa8-0aad-41ed-83f4-97be242c8f20', 'a4b195f5-8225-47d8-8012-9d41369786e2'),
    ('sleeprtcwake', '238c9fa8-0aad-41ed-83f4-97be242c8f20', 'bd3b718a-0680-4d9d-8ab2-e1d2b4ac806d'),
    ('sleepdeep', '2e601130-5351-4d9d-8e04-252966bad054', 'd502f7ee-1dc7-4efd-a55d-f04b6f5c0545'),
    ('sleephibernate', '238c9fa8-0aad-41ed-83f4-97be242c8f20', '9d7815a6-7ee4-497e-8888-515a05f02364'),
]

for key, sub, setting in SETTINGS:
    path = BASE
    if sub:
        path += '\\' + sub
    path += '\\' + setting
    print('===', key, '===')
    try:
        with winreg.OpenKey(winreg.HKEY_LOCAL_MACHINE, path) as k:
            # 设置自身信息
            for v in ('Description', 'FriendlyName', 'Default', 'Type', 'Max', 'Min'):
                try:
                    val, _ = winreg.QueryValueEx(k, v)
                    print('  %s: %r' % (v, val))
                except OSError:
                    pass
        ps = path + '\\PossibleSettings'
        try:
            with winreg.OpenKey(winreg.HKEY_LOCAL_MACHINE, ps) as pk:
                n = winreg.QueryInfoKey(pk)[0]
                print('  PossibleSettings count:', n)
                for i in range(n):
                    name = winreg.EnumKey(pk, i)
                    with winreg.OpenKey(pk, name) as sk:
                        try:
                            fn, _ = winreg.QueryValueEx(sk, 'FriendlyName')
                        except OSError:
                            fn = ''
                        try:
                            v, _ = winreg.QueryValueEx(sk, 'ValueIndex')
                        except OSError:
                            v = '?'
                        print('    [%s] ValueIndex=%r %s' % (name, v, fn))
        except FileNotFoundError:
            print('  no PossibleSettings (自由数值)')
    except FileNotFoundError:
        print('  SETTING NOT FOUND in registry')
    except Exception as e:
        print('  ERROR:', e)
