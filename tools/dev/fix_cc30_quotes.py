# -*- coding: utf-8 -*-
import io
p = r'D:\openLM\rebuild\cc30_lite\install_script.iss'
s = io.open(p, encoding='utf-8-sig').read()
n1 = s.count('Parameters: ""1"" -i"')
n2 = s.count('Parameters: ""1"" -u"')
s = s.replace('Parameters: ""1"" -i"', 'Parameters: """1"" -i"')
s = s.replace('Parameters: ""1"" -u"', 'Parameters: """1"" -u"')
io.open(p, 'w', encoding='utf-8-sig').write(s)
print('fixed -i:', n1, '-u:', n2)
