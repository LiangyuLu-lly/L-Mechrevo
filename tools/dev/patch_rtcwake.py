# -*- coding: utf-8 -*-
"""sleeprtcwake 关闭值 1 -> 2（仅重要唤醒源）"""
import io

p = r'D:\openLM\source\MechrevoLite.Helpers\PowerSleepGuard.cs'
s = io.open(p, encoding='utf-8').read()

old = '''        // 禁用RTC唤醒定时器：Allow wake timers（0=禁用）
        ("238c9fa8-0aad-41ed-83f4-97be242c8f20", "bd3b718a-0680-4d9d-8ab2-e1d2b4ac806d", 0, 1),'''
new = '''        // 禁用RTC唤醒定时器：Allow wake timers（0=禁用，2=仅重要唤醒源）
        ("238c9fa8-0aad-41ed-83f4-97be242c8f20", "bd3b718a-0680-4d9d-8ab2-e1d2b4ac806d", 0, 2),'''

assert s.count(old) == 1, 'match failed'
io.open(p, 'w', encoding='utf-8', newline='').write(s.replace(old, new))
print('OK rtcwake OffValue 1 -> 2')
