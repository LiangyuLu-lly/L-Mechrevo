# -*- coding: utf-8 -*-
# 版本号更新 0.290.2-beta21.2 -> 0.290.3-beta21.3（仅改版本号，不打包）
def edit(path, old, new, tag, enc='utf-8-sig'):
    t = open(path, encoding=enc).read()
    n = t.count(old)
    print('[' + tag + '] 匹配:', n)
    assert n >= 1, 'no match: ' + old
    open(path, 'w', encoding=enc, newline='').write(t.replace(old, new))
    print('[' + tag + '] OK')

# 1) 安装器 iss：先 beta 标签后数字版本，避免交叉替换
p1 = r'D:\openLM\installer-src\install_script.iss'
edit(p1, 'beta21.2', 'beta21.3', 'iss-beta')
edit(p1, '0.290.2', '0.290.3', 'iss-ver')

# 2) 运行时 AssemblyInfo
p2 = r'D:\openLM\source\Properties\AssemblyInfo.cs'
edit(p2, 'beta21.2', 'beta21.3', 'asm-beta', enc='utf-8')
edit(p2, '0.290.2', '0.290.3', 'asm-ver', enc='utf-8')

print('ALL OK')
