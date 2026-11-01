# -*- coding: utf-8 -*-
import io

PATH = r'D:\openLM\source\MechrevoLite\SettingsForm.cs'
with io.open(PATH, 'r', encoding='utf-8') as f:
    src = f.read()

old = """			Control? qsRoot = control2.Controls["quickSwitchRoot"];
			Control? sleepPanel = qsRoot?.Controls["quickGroup_sleepnetoff"];"""
new = """			TableLayoutPanel? qsRoot = control2.Controls["quickSwitchRoot"] as TableLayoutPanel;
			Control? sleepPanel = qsRoot?.Controls["quickGroup_sleepnetoff"];"""
assert src.count(old) == 1
src = src.replace(old, new)

with io.open(PATH, 'w', encoding='utf-8', newline='') as f:
    f.write(src)
print('OK: TableLayoutPanel cast')
