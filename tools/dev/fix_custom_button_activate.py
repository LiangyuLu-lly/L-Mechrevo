# -*- coding: utf-8 -*-
# 主窗"自定义"按钮：对齐 18.6 —— 显示窗口的同时真正激活自定义档
p = r'D:\openLM\source\MechrevoLite\SettingsForm.cs'
t = open(p, encoding='utf-8').read()

old = '''				if (!Program.UiAuditMode && !customModeForm.IsDisposed)
				{
					customModeForm.BindMode(PerfModeService.Instance?.DisplayedModeId ?? PerfModeStore.ActiveModeId);
					ShowMode(MechrevoService.ToVisualMode(3));
					SetContextMenu();
				}'''
new = '''				if (!Program.UiAuditMode && !customModeForm.IsDisposed)
				{
					string customId = PerfModeStore.LastCustomId ?? PerfModeDefinition.CustomId(1);
					if (await customModeForm.SelectModeAsync(customId))
					{
						ShowMode(MechrevoService.ToVisualMode(3));
						SetContextMenu();
					}
					else
					{
						customModeForm.BindMode(PerfModeService.Instance?.DisplayedModeId ?? PerfModeStore.ActiveModeId);
					}
				}'''
n = t.count(old)
print('match:', n)
if n != 1:
    raise SystemExit('ABORT')
open(p, 'w', encoding='utf-8', newline='').write(t.replace(old, new))
print('OK')
