# -*- coding: utf-8 -*-
import io, re, sys
sys.stdout.reconfigure(encoding='utf-8', errors='replace')
SF = r'D:\openLM\source\MechrevoLite\SettingsForm.cs'
UG = r'D:\openLM\source\MechrevoLite.UI\UiGlyph.cs'
t = io.open(SF, encoding='utf-8-sig', errors='replace').read()
for k in ('wifi', 'bt', 'touchpad', 'touchpadtoggle'):
    print('SettingsForm case "%-15s" = %d' % (k, len(re.findall(r'case\s+"' + k + r'"', t))))
u = io.open(UG, encoding='utf-8-sig', errors='replace').read()
for k in ('Wifi', 'Bluetooth', 'Touchpad', 'Refresh'):
    print('UiGlyph Kind.%-10s = %d' % (k, len(re.findall(r'\bKind\.' + k + r'\b', u))))
