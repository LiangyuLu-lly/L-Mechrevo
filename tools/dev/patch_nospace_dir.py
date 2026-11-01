# -*- coding: utf-8 -*-
import io
p = r'D:\openLM\tools\build_cc30_gmonly.py'
s = io.open(p, encoding='utf-8-sig').read()
s = s.replace('DefaultDirName={commonpf}\\OEM\\Control Center Lite',
              'DefaultDirName={commonpf}\\OEM\\ControlCenterLite')
s = s.replace('DefaultGroupName=Control Center Lite',
              'DefaultGroupName=ControlCenterLite')
io.open(p, 'w', encoding='utf-8-sig').write(s)
print('patched:', 'Control Center Lite' in s)
