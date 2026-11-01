# -*- coding: utf-8 -*-
# 升级时备份 customICC（卸载不备份）：ssInstall 备份 + ssPostInstall 恢复
import io

p = r'D:\openLM\installer-src\install_script.iss'
s = io.open(p, encoding='utf-8-sig').read()

# 1) BackupAndUpgrade：备份 customICC（旧版仍在 {app}）
old1 = "  Log('LM-UPDATE: 风扇表已备份');"
new1 = """  Log('LM-UPDATE: 风扇表已备份');
  // 2.5) Back up custom ICC profiles (old version is still in {app} at ssInstall).
  if CopyDirContents(ExpandConstant('{app}\\customICC'), BackupRoot + '\\customICC') then
    Log('LM-UPDATE: 自定义校色 ICC 已备份')
  else
    Log('LM-UPDATE: 备份自定义校色 ICC 失败（继续升级）');"""

# 2) ssPostInstall：恢复 customICC
old2 = "      CopyDirContents(BackupRoot + '\\UserFanTables', GcuRoot + '\\UserFanTables');"
new2 = """      CopyDirContents(BackupRoot + '\\UserFanTables', GcuRoot + '\\UserFanTables');
      CopyDirContents(BackupRoot + '\\customICC', ExpandConstant('{app}\\customICC'));"""

c1 = s.count(old1)
c2 = s.count(old2)
s = s.replace(old1, new1).replace(old2, new2)
io.open(p, 'w', encoding='utf-8-sig').write(s)
print('patch1 matched:', c1, ' patch2 matched:', c2)
