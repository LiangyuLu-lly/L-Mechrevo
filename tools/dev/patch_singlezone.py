# -*- coding: utf-8 -*-
p = r'D:\openLM\source\MechrevoLite.Hardware\MechrevoService.cs'
t = open(p, encoding='utf-8', errors='replace').read()
old = '''				if (isGcu40Keyboard || isSingleZoneKeyboard)
				{
				payload["MqttID"] = null;'''
new = '''				// 2026-10-03：单分区判断只认硬件能力（KeyboardType 上报 1/2）；GCU 版本（IsGcu40Keyboard）仅作指令协议分流（上方 40 系字段），
				// 40 系高端单键 RGB 机型（KeyboardType 非 1/2）走 per-key payload，不再被强制单分区
				if (isSingleZoneKeyboard)
				{
				payload["MqttID"] = null;'''
assert t.count(old) == 1, 'not found'
t = t.replace(old, new, 1)
open(p, 'w', encoding='utf-8', newline='').write(t)
print('OK single-zone 仅按 KeyboardType')
