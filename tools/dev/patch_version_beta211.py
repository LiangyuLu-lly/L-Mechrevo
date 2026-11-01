# -*- coding: utf-8 -*-
# 1) AssemblyInfo: InformationalVersion 加 beta21.1 标签（文件/程序集版本不动）
p1 = r'D:\openLM\source\Properties\AssemblyInfo.cs'
t1 = open(p1, encoding='utf-8', errors='replace').read()
old1 = '[assembly: AssemblyInformationalVersion("0.290.0")]'
new1 = '[assembly: AssemblyInformationalVersion("0.290.0-beta21.1")]'
assert t1.count(old1) == 1, 'InfoVersion anchor'
t1 = t1.replace(old1, new1, 1)
open(p1, 'w', encoding='utf-8', newline='').write(t1)
print('AssemblyInfo -> 0.290.0-beta21.1')

# 2) Program.NormalizeReleaseVersion: 剥离 -beta 标签，保持纯数字版本（更新比较/诊断包/遥测用）
p2 = r'D:\openLM\source\MechrevoLite\Program.cs'
t2 = open(p2, encoding='utf-8', errors='replace').read()
old2 = '''	internal static string NormalizeReleaseVersion(string informationalVersion)
	{
		return informationalVersion.Split('+')[0];
	}'''
new2 = '''	internal static string NormalizeReleaseVersion(string informationalVersion)
	{
		return informationalVersion.Split('+')[0].Split('-')[0];
	}'''
assert t2.count(old2) == 1, 'NormalizeReleaseVersion anchor'
t2 = t2.replace(old2, new2, 1)
open(p2, 'w', encoding='utf-8', newline='').write(t2)
print('NormalizeReleaseVersion strips beta label')

# 3) SettingsForm: 底栏版本显示 "0.290.0 beta21.1"（有标签时拼版本+标签）
p3 = r'D:\openLM\source\MechrevoLite\SettingsForm.cs'
t3 = open(p3, encoding='utf-8', errors='replace').read()
old3 = '		labelVersion.Text = Program.ReleaseLabel;'
new3 = '''		labelVersion.Text = (Program.ReleaseLabel == Program.ReleaseVersion) ? Program.ReleaseLabel : Program.ReleaseVersion + " " + Program.ReleaseLabel;'''
assert t3.count(old3) == 1, 'labelVersion anchor'
t3 = t3.replace(old3, new3, 1)
open(p3, 'w', encoding='utf-8', newline='').write(t3)
print('labelVersion shows 0.290.0 beta21.1')
