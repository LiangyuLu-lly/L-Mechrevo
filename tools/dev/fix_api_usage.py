# -*- coding: utf-8 -*-
# 修复 API 用法：GetBool -> Is，Set(bool) -> Set(int)
import io

p = r'D:\openLM\source\MechrevoLite\SettingsForm.cs'
t = io.open(p, encoding='utf-8').read()

t = t.replace('AppConfig.GetBool("calib_custom_on")', 'AppConfig.Is("calib_custom_on")')
t = t.replace('AppConfig.Set("calib_custom_on", false);', 'AppConfig.Set("calib_custom_on", 0);')
print('replaced: GetBool->Is, Set(false)->Set(0)')
print('GetBool left:', t.count('GetBool("calib_custom_on")'), '| Set false left:', t.count('Set("calib_custom_on", false)'))

io.open(p, 'w', encoding='utf-8', newline='').write(t)
print('WRITTEN')
