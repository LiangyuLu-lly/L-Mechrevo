# -*- coding: utf-8 -*-
p = r'D:\openLM\source\MechrevoLite\SettingsForm.cs'
t = open(p, encoding='utf-8').read()

old = '''		if (HardwareControl.gpuTemp > 0 && hwSensors != null && hwSensors.IsSensorInfoFresh(cpu: false, fanInfoMaximumAge, -1L))
		{
			gpuTemp = ": " + TempHelper.FormatTemp(HardwareControl.gpuTemp);
		}'''
new = '''		if (HardwareControl.gpuTemp > 0 && hwSensors != null)
		{
			gpuTemp = (hwSensors.IsSensorInfoFresh(cpu: false, fanInfoMaximumAge, -1L) ? ": " + TempHelper.FormatTemp(HardwareControl.gpuTemp) : ": —");
		}'''

print('match:', t.count(old))
if t.count(old) != 1:
    print('匹配数异常，中止')
    raise SystemExit(1)
t = t.replace(old, new)
open(p, 'w', encoding='utf-8', newline='').write(t)
print('OK')
