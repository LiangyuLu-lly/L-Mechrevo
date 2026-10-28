# -*- coding: utf-8 -*-
p = r'D:\openLM\source\MechrevoLite\CustomModeForm.cs'
t = open(p, encoding='utf-8').read()
# 文件末尾应为 catch{} + 方法} + 类}，当前只有方法}，补一个类结束的}
if not t.rstrip().endswith('}'):
    print('ABORT: 文件末尾不是 }')
    raise SystemExit(1)
open(p, 'w', encoding='utf-8', newline='').write(t.rstrip() + '\n}\n')
print('OK: 补齐类结束大括号')
