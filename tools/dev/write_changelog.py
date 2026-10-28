# -*- coding: utf-8 -*-
# 追加 10/04 屏幕校色自定义 ICC 修复记录到修改日志
import io

log = r'D:\openLM\修改日志.txt'
entry = '''

==== 2026-10-04 屏幕校色自定义 ICC 修复收尾（版本待定：0.290.3 beta21.3 归属） ====

背景：自定义 ICC 功能（设置弹窗"自定义开关 + ICC 下拉 + 导入按钮"、主界面校色下拉"自定义"档
value=999，经 Windows 颜色管理关联、不经 GCU）整体已实现，本日完成最后一轮排障闭环：

1. 页面不显示配置文件（设置-颜色管理"找不到配置文件"）：
   - 根因：设置页显示依赖 HKCU ProfileAssociations\\Display\\{4d36e96e-...}\\000x 槽的
     ICMProfile = MultiString 完整文件名；而 WCS WcsAssociateColorProfileWithDevice 会把槽
     写成逐字符数组（B||O||E||0||...）破坏格式，导致设置页丢失配置。
   - 修复：弃用 WCS Associate API，ApplyIcc 改为直接写 HKCU 显示器类槽
     （0000-0008）ICMProfile = MultiString{完整文件名}；另写用户颜色目录副本
     %LOCALAPPDATA%\\Microsoft\\Windows\\Color\\ + HKCU ICM\\RegisteredProfiles\\<文件名>
     空子键注册标记 + 值 <文件名>=spool 路径。页面即显示 NE153QDM + (Current SDR profile)。
   - spool 文件已存在则跳过复制（避免被系统占用抛 IOException）。

2. 关闭/切离不清理（关闭后页面仍挂着配置）：
   - 根因：关闭走 GCU SetColorCalibration(off) 分支，不触发清理；且清理判断用了
     calib_custom_on（用户下拉直选自定义时该开关可能为 false）。
   - 修复：清理判断改为 (calib_mode==999) || calib_custom_on；下拉切到非自定义档、
     总开关关闭时均自动调用 ClearAllIcc（直接清空 HKCU 关联槽 ICMProfile + 删注册标记），
     不再依赖 WcsDisassociate（清不干净）。

3. 总开关关闭连带关闭自定义开关：
   - 根因：清理代码里 Set calib_custom_on=0 覆盖了用户设置。
   - 修复：删除该写入，总开关只清理 ICC，自定义开关保持用户设置。

4. 自定义档位记忆（40 系 GCU 无回读场景）：
   - 根因：关闭总开关 SetColorCalibration(0) 成功后 Set calib_mode=0 覆盖了档位选择，
     重启后回显"默认"，再次打开回不到自定义。
   - 修复：关闭时把当前档位存入 calib_mode_saved，calib_mode 归 0；重新打开总开关时
     消费 calib_mode_saved 恢复档位（下拉同步选中 + 应用）；下拉手动切档时清空 saved。
   - 效果：选自定义 → 关总开关 → 重启 → 开总开关，直接回到自定义并应用 ICC。

5. 相关文件：
   - MechrevoLite.Display\\IccHelper.cs：槽位直写/清理（弃 WCS API）、spool 跳过已存在。
   - MechrevoLite\\SettingsForm.cs：999 分支、切离清理（calib_mode==999）、
     档位记忆（calib_mode_saved）、总开关不连带自定义开关。
   - MechrevoLite\\CalibrationStartupForm.cs：580×380 弹窗（开机自动应用+自定义分组）。
   - 本机验证：应用后设置页显示 NE153QDM (Current SDR profile)；切离/关闭后清除；
     档位记忆恢复正常。
'''

t = io.open(log, encoding='utf-8').read()
if '2026-10-04 屏幕校色自定义 ICC' not in t:
    t = t.rstrip('\n') + '\n' + entry
    io.open(log, 'w', encoding='utf-8', newline='').write(t)
    print('APPENDED')
else:
    print('ALREADY PRESENT')

# 回读验证
t2 = io.open(log, encoding='utf-8').read()
print('size now:', len(t2.encode('utf-8')))
print('tail:', t2[-80:].replace('\n', ' | '))
