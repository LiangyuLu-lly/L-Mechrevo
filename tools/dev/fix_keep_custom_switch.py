# -*- coding: utf-8 -*-
# 关闭校色/切离自定义时只清理 ICC，不连带关闭自定义开关（calib_custom_on 保持用户设置）
import io

p = r'D:\openLM\source\MechrevoLite\SettingsForm.cs'
t = io.open(p, encoding='utf-8').read()

old = '''						AppConfig.Set("calib_custom_on", 0);'''
n = t.count(old)
print('[set-on-0]', n)
assert n >= 1
t = t.replace(old, '')

io.open(p, 'w', encoding='utf-8', newline='').write(t)
print('WRITTEN, removed', n)
