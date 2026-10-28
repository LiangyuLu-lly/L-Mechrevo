# -*- coding: utf-8 -*-
import re
p = r'D:\openLM\source\MechrevoLite\SettingsForm.cs'
t = open(p, encoding='utf-8', errors='replace').read()

# 1) 移除 WordWrap 行（Label 无此属性，任意缩进）
t2, n = re.subn(r'[ \t]*WordWrap = false,\r?\n', '', t)
assert n == 1, f'wordwrap removals: {n}'
t = t2

# 2) 状态 Label 块：改 AutoSize + Anchor=Right（禁折行、宽度按内容自适应）
pat = re.compile(r'(_officialConsoleStatus = new Label\s*\{)([^{}]*?)(\})', re.S)
m = pat.search(t)
assert m, 'status label block not found'
block = m.group(2)
new_props = '''			{
				Text = Strings.Detecting,
				AutoSize = true,
				Dock = DockStyle.None,
				Anchor = AnchorStyles.Right,
				TextAlign = ContentAlignment.MiddleRight,
				AutoEllipsis = true,
				
			'''
# 用行级处理：替换 Dock = DockStyle.Fill 并注入 AutoSize/Anchor
block2 = re.sub(r'[ \t]*Dock = DockStyle\.Fill,\r?\n', '', block)
if 'AutoSize = true,' not in block2:
    block2 = block2.replace('Text = Strings.Detecting,', 'Text = Strings.Detecting,\n\t\t\t\tAutoSize = true,\n\t\t\t\tDock = DockStyle.None,\n\t\t\t\tAnchor = AnchorStyles.Right,')
t = t[:m.start()] + m.group(1) + block2 + m.group(3) + t[m.end():]

open(p, 'w', encoding='utf-8', newline='').write(t)
print('SettingsForm.cs fixed (status label)')

# 3) Strings.cs 增加 QuickCpuAdvPerfTip 访问器
p2 = r'D:\openLM\source\MechrevoLite.Properties\Strings.cs'
t2s = open(p2, encoding='utf-8', errors='replace').read()
anchor = 'internal static string QuickCpuAdvPerf => ResourceManager.GetString("QuickCpuAdvPerf", resourceCulture);'
assert t2s.count(anchor) == 1, 'Strings.cs anchor not found'
if 'QuickCpuAdvPerfTip' not in t2s:
    t2s = t2s.replace(anchor, anchor + '\n\tinternal static string QuickCpuAdvPerfTip => ResourceManager.GetString("QuickCpuAdvPerfTip", resourceCulture);', 1)
    open(p2, 'w', encoding='utf-8', newline='').write(t2s)
    print('Strings.cs accessor added')
else:
    print('Strings.cs accessor already present')
