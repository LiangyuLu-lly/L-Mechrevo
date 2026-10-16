# -*- coding: utf-8 -*-
import re

# ============ 1) RButton.cs: 加 ActivatedColor（待重启高亮用橙色） ============
p = r'D:\openLM\source\MechrevoLite.UI\RButton.cs'
t = open(p, encoding='utf-8', errors='replace').read()

old_act = '''	public bool Activated
	{
		get
		{
			return activated;
		}
		set
		{
			if (activated != value)
			{
				Invalidate();
			}
			activated = value;
			AccessibleDescription = (activated ? "Active" : null);
		}
	}'''
new_act = '''	public bool Activated
	{
		get
		{
			return activated;
		}
		set
		{
			if (activated != value)
			{
				Invalidate();
			}
			activated = value;
			AccessibleDescription = (activated ? "Active" : null);
		}
	}

	private Color activatedColor = Color.Empty;

	public Color ActivatedColor
	{
		get
		{
			return activatedColor;
		}
		set
		{
			if (activatedColor != value)
			{
				Invalidate();
			}
			activatedColor = value;
		}
	}'''
assert t.count(old_act) == 1, 'Activated prop not found'
t = t.replace(old_act, new_act, 1)

old_brush = 'using Brush brush4 = new SolidBrush(UiVisualStyle.Accent);'
assert t.count(old_brush) == 1, 'accent brush not found'
t = t.replace(old_brush, 'using SolidBrush brush4 = new SolidBrush(activatedColor.IsEmpty ? UiVisualStyle.Accent : activatedColor);', 1)
open(p, 'w', encoding='utf-8', newline='').write(t)
print('RButton.cs: ActivatedColor added')

# ============ 2) resx: GpuRestartPrompt 三选项文案 ============
def patch_resx(path, key, new_value):
    t2 = open(path, encoding='utf-8', errors='replace').read()
    pat = re.compile(r'(<data name="' + key + r'" xml:space="preserve">.*?<value>).*?(</value>.*?</data>)', re.S)
    m = pat.search(t2)
    assert m, key + ' not found in ' + path
    t2 = t2[:m.start()] + m.group(1) + new_value + m.group(2) + t2[m.end():]
    open(path, 'w', encoding='utf-8', newline='').write(t2)
    print(path.split('\\')[-1], key, 'patched')

zh_txt = '''切换到{0}模式必须重启电脑才能生效。

「是」= 发送切换指令并立即重启；
「否」= 先发送切换指令，重启后生效（界面以橙色高亮待切换状态）；
「取消」= 不做任何更改。'''
en_txt = '''Switching to {0} mode requires a reboot to take effect.

"Yes" = send switch command and reboot now;
"No" = send switch command now, takes effect after reboot (target highlighted in orange);
"Cancel" = do nothing.'''

patch_resx(r'D:\openLM\source\MechrevoLite.Properties.Strings.zh-CN.resx', 'GpuRestartPrompt', zh_txt)
patch_resx(r'D:\openLM\source\MechrevoLite.Properties.Strings.resx', 'GpuRestartPrompt', en_txt)
print('RESX OK')
