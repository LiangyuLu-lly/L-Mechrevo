# -*- coding: utf-8 -*-
# 修复 fileName 重复声明（编译错误 CS0128）
import io

p = r'D:\openLM\source\MechrevoLite.Display\IccHelper.cs'
t = io.open(p, encoding='utf-8').read()

old = '''		// 注册表自洽：注册项键名=文件名 -> spool 完整路径，并写入传统 DISPLAY 关联槽，
		// 否则 Windows 设置-颜色配置文件按关联的文件名找不到注册项，判定无配置文件
		string fileName = Path.GetFileName(profilePath);
		string spoolPath = Path.Combine(SpoolColorDir, fileName);'''
new = '''		// 注册表自洽：注册项键名=文件名 -> spool 完整路径，并写入传统 DISPLAY 关联槽，
		// 否则 Windows 设置-颜色配置文件按关联的文件名找不到注册项，判定无配置文件
		string spoolPath = Path.Combine(SpoolColorDir, fileName);'''
n = t.count(old)
print('[dup-decl]', n)
assert n == 1
t = t.replace(old, new)

io.open(p, 'w', encoding='utf-8', newline='').write(t)
print('WRITTEN')
