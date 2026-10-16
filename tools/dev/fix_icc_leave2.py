# -*- coding: utf-8 -*-
# 清理判断改为 calib_mode==999（下拉直选自定义也会写入 999），兼容 calib_custom_on
import io

p = r'D:\openLM\source\MechrevoLite\SettingsForm.cs'
t = io.open(p, encoding='utf-8').read()

# 通用判断表达式
was_custom = '(AppConfig.Exists("calib_mode") && AppConfig.Get("calib_mode") == 999) || AppConfig.Is("calib_custom_on")'

# 1) 总开关关闭分支
old1 = '''				if (!requested && AppConfig.Is("calib_custom_on"))'''
new1 = '''				if (!requested && (''' + was_custom + '''))'''
print('[switch-off]', t.count(old1))
assert t.count(old1) == 1
t = t.replace(old1, new1)

# 2) 下拉切离分支
old2 = '''					if (AppConfig.Is("calib_custom_on"))'''
new2 = '''					if (''' + was_custom + ''')'''
print('[combo-else]', t.count(old2))
assert t.count(old2) == 1
t = t.replace(old2, new2)

io.open(p, 'w', encoding='utf-8', newline='').write(t)
print('WRITTEN')
