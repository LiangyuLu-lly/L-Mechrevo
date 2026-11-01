# -*- coding: utf-8 -*-
# Program.cs：开机自动应用校色 999 分支（按行号定位，沿用源码缩进）
import io

pp = r'D:\openLM\source\MechrevoLite\Program.cs'
lines = io.open(pp, encoding='utf-8-sig').read().split('\n')

start = None
for i, ln in enumerate(lines):
    if 'AppConfig.Is("calib_apply_on_startup") && AppConfig.Get("calib_mode", 0) > 0' in ln:
        start = i
        break
assert start is not None, 'anchor line not found'
assert 'SetColorCalibration(calibMode)' in lines[start + 5], 'unexpected block shape: ' + lines[start + 5]

ind = lines[start][:len(lines[start]) - len(lines[start].lstrip())]
ind1 = ind + '\t'
ind2 = ind1 + '\t'

new_block = [
    ind + 'if (AppConfig.Is("calib_apply_on_startup") && AppConfig.Get("calib_mode", 0) > 0)',
    ind + '{',
    ind1 + 'try',
    ind1 + '{',
    ind2 + 'int calibMode = AppConfig.Get("calib_mode", 0);',
    ind2 + 'if (calibMode == 999)',
    ind2 + '{',
    ind2 + '\t// 自定义 ICC：经 Windows 颜色管理应用，不走 GCU',
    ind2 + '\tstring? iccName = AppConfig.GetString("calib_custom_icc");',
    ind2 + '\tstring iccPath = string.IsNullOrEmpty(iccName) ? "" : Path.Combine(IccHelper.CustomIccDir, iccName);',
    ind2 + '\tbool iccApplied = !string.IsNullOrEmpty(iccPath) && File.Exists(iccPath) && await Task.Run(() => IccHelper.ApplyIcc(iccPath));',
    ind2 + '\tLogger.WriteLine($"校色开机应用(自定义 ICC): file={iccName ?? "(空)"} applied={iccApplied}");',
    ind2 + '}',
    ind2 + 'else',
    ind2 + '{',
    ind2 + '\tbool calibApplied = await service.SetColorCalibration(calibMode);',
    ind2 + '\tLogger.WriteLine($"校色开机应用: mode={calibMode} applied={calibApplied}");',
    ind2 + '}',
    ind1 + '}',
    ind1 + 'catch (Exception ex)',
    ind1 + '{',
    ind2 + 'Logger.WriteLine("校色开机应用 fail: " + ex.Message);',
    ind1 + '}',
    ind + '}',
]

lines[start:start + 13] = new_block
io.open(pp, 'w', encoding='utf-8-sig').write('\n'.join(lines))
print('replaced lines', start + 1, '-', start + 13)
print('999 branch present:', any('calibMode == 999' in x for x in new_block))
