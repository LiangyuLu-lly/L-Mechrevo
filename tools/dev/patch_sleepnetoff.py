# -*- coding: utf-8 -*-
"""sleepnetoff 关闭值 1 -> 2（由Windows管理）"""
import io

p = r'D:\openLM\source\MechrevoLite.Helpers\PowerSleepGuard.cs'
s = io.open(p, encoding='utf-8').read()

old = '''        // 待机断开网络：Networking connectivity in Standby（直接挂在方案下，无子组）
        ("", "f15576e8-98b7-4186-b944-eafa664402d9", 0, 1),'''
new = '''        // 待机断开网络：Networking connectivity in Standby（直接挂在方案下，无子组）On=0(禁用) Off=2(由Windows管理)
        ("", "f15576e8-98b7-4186-b944-eafa664402d9", 0, 2),'''

assert s.count(old) == 1, 'match failed'
io.open(p, 'w', encoding='utf-8', newline='').write(s.replace(old, new))
print('OK OffValue 1 -> 2')
