# -*- coding: utf-8 -*-
"""3h：删除轮询内 hotSwitchRequest 分支（锚点定位）"""
p = r'D:\openLM\source\MechrevoLite.Hardware\MechrevoService.cs'
t = open(p, encoding='utf-8', errors='replace').read()

start_marker = '\t\tif (hotSwitchRequest)'
i = t.find(start_marker)
assert i >= 0, 'start not found'
# 起点行首
line_start = t.rfind('\n', 0, i) + 1
# 找 else 块结束：定位 'flag = flag2;' 之后的 '\n\t\t\t\t}'
j = t.find('flag = flag2;', i)
assert j >= 0, 'flag2 not found'
k = t.find('\n\t\t\t\t}', j)
assert k >= 0, 'end brace not found'
end = k + len('\n\t\t\t\t}')
block = t[line_start:end]
print('=== 待删块 ===')
print(block)
print('=== 块结束 ===')

new = '''\t\t\t\tbool flag2;
\t\t\t\tif (expectedAutoRuntime >= 0)
\t\t\t\t{
\t\t\t\t\tflag2 = attempt == 0;
\t\t\t\t}
\t\t\t\telse
\t\t\t\t{
\t\t\t\t\tbool flag3;
\t\t\t\t\tswitch (attempt)
\t\t\t\t\t{
\t\t\t\t\tcase 1:
\t\t\t\t\tcase 3:
\t\t\t\t\tcase 5:
\t\t\t\t\t\tflag3 = true;
\t\t\t\t\t\tbreak;
\t\t\t\t\tdefault:
\t\t\t\t\t\tflag3 = false;
\t\t\t\t\t\tbreak;
\t\t\t\t\t}
\t\t\t\t\tflag2 = flag3;
\t\t\t\t}
\t\t\t\tflag = flag2;
'''
t = t[:line_start] + new + t[end:]
open(p, 'w', encoding='utf-8', newline='').write(t)
print('3h OK')
