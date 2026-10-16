# -*- coding: utf-8 -*-
"""
方案 B：彻底删掉残留的「AUTO 自动」显卡模式按钮（buttonOptimized）
================================================================================
用户决策：B —— 用不上 AUTO 按钮，因为 **AUTO 就是混合模式**（功能冗余）。
布局安全：`buttonUltimate`（独显直连）**本来就占第 2 列**，与 buttonOptimized 同列，
          二者只有一个是可见的 -> 删掉 AUTO **不留空列、不改布局** ✅
保留（活代码，别动）：`SwitchAutomaticGpuMode()` / `CreateGpuModePayload(3)`
          —— 那是**插拔电源时自动切**的逻辑（日志可见 `AutoSetting for Online`）。
行尾：SettingsForm.cs 与 GPUModeControl.cs 均为 CRLF（脚本按 CRLF 锚点写，并做校验）。
"""
import io
import os
import sys

F_SF = r'D:\openLM\source\MechrevoLite\SettingsForm.cs'
F_GPU = r'D:\openLM\source\MechrevoLite.Gpu\GPUModeControl.cs'

OPS = []


def op(path, old, new, label, expect=1):
    OPS.append((path, old, new, expect, label))


# ---- 1) WriteEntryControlNames 去掉 buttonOptimized ----
op(F_SF,
   b'\tprivate static readonly string[] WriteEntryControlNames = new string[8] { "sliderBattery", "buttonEco", "buttonStandard", "buttonOptimized", "buttonUltimate", "buttonTurbo", "buttonSilentTurbo", "buttonCustomMode" };\r\n',
   b'\tprivate static readonly string[] WriteEntryControlNames = new string[7] { "sliderBattery", "buttonEco", "buttonStandard", "buttonUltimate", "buttonTurbo", "buttonSilentTurbo", "buttonCustomMode" };\r\n',
   'WriteEntryControlNames 去项')

# ---- 2) 字段 ----
op(F_SF, b'\tprivate RButton buttonOptimized;\r\n', b'', '删除字段声明')

# ---- 3) 初始化区的赋值 ----
op(F_SF, b'\t\tbuttonOptimized.Text = Strings.Optimized;\r\n', b'', '删 Text=Optimized')
op(F_SF, b'\t\tbuttonOptimized.AccessibleName = Strings.Optimized;\r\n', b'', '删 AccessibleName')
op(F_SF, b'\t\tbuttonOptimized.BorderColor = RForm.colorEco;\r\n', b'', '删 BorderColor')
op(F_SF, b'\t\tbuttonOptimized.Text = Strings.AutoShort;\r\n', b'', '删 Text=AutoShort')
op(F_SF, b'\t\ttoolTip.SetToolTip(buttonOptimized, Strings.AutoModeTooltip);\r\n', b'', '删 tooltip')

# ---- 4) VisualiseGPUButtons 去掉 auto 参数 ----
op(F_SF, b'\tpublic void VisualiseGPUButtons(bool eco = true, bool ultimate = true, bool auto = true)\r\n',
   b'\tpublic void VisualiseGPUButtons(bool eco = true, bool ultimate = true)\r\n',
   '签名去 auto')
op(F_SF, b'\t\t\t\tVisualiseGPUButtons(eco, ultimate, auto);\r\n',
   b'\t\t\t\tVisualiseGPUButtons(eco, ultimate);\r\n',
   '递归调用去 auto')
op(F_SF, b'\t\tbuttonOptimized.Visible = false;\r\n', b'', '删 Visible=false', expect=2)
op(F_SF, b'\t\tButtonEnabled(buttonOptimized, enabled: false);\r\n', b'', '删 ButtonEnabled(false)')
op(F_SF, b'\t\tButtonEnabled(buttonOptimized, flag2);\r\n', b'', '删 ButtonEnabled(flag2)')

# ---- 5) 设计器 ----
op(F_SF, b'\t\tthis.buttonOptimized = new MechrevoLite.UI.RButton();\r\n', b'', '删设计器实例化')
op(F_SF, b'\t\tthis.tableGPU.Controls.Add(this.buttonOptimized, 2, 0);\r\n', b'', '删 tableGPU 添加')
op(F_SF,
   b'\t\tthis.buttonOptimized.Activated = false;\r\n'
   b'\t\tthis.buttonOptimized.BackColor = System.Drawing.SystemColors.ControlLightLight;\r\n'
   b'\t\tthis.buttonOptimized.BorderColor = System.Drawing.Color.Transparent;\r\n'
   b'\t\tthis.buttonOptimized.BorderRadius = 5;\r\n'
   b'\t\tthis.buttonOptimized.Dock = System.Windows.Forms.DockStyle.Top;\r\n'
   b'\t\tthis.buttonOptimized.FlatAppearance.BorderSize = 0;\r\n'
   b'\t\tthis.buttonOptimized.FlatStyle = System.Windows.Forms.FlatStyle.Flat;\r\n'
   b'\t\tthis.buttonOptimized.ForeColor = System.Drawing.SystemColors.ControlText;\r\n'
   b'\t\tthis.buttonOptimized.Image = MechrevoLite.Properties.Resources.icons8_project_management_48__1_;\r\n'
   b'\t\tthis.buttonOptimized.ImageAlign = System.Drawing.ContentAlignment.BottomCenter;\r\n'
   b'\t\tthis.buttonOptimized.Location = new System.Drawing.Point(4, 132);\r\n'
   b'\t\tthis.buttonOptimized.Margin = new System.Windows.Forms.Padding(4);\r\n'
   b'\t\tthis.buttonOptimized.Name = "buttonOptimized";\r\n'
   b'\t\tthis.buttonOptimized.Secondary = false;\r\n'
   b'\t\tthis.buttonOptimized.Size = new System.Drawing.Size(188, 120);\r\n'
   b'\t\tthis.buttonOptimized.TabIndex = 7;\r\n'
   b'\t\tthis.buttonOptimized.Text = MechrevoLite.Properties.Strings.Optimized;\r\n'
   b'\t\tthis.buttonOptimized.TextImageRelation = System.Windows.Forms.TextImageRelation.ImageAboveText;\r\n'
   b'\t\tthis.buttonOptimized.UseVisualStyleBackColor = false;\r\n',
   b'', '删设计器属性块')

# ---- 6) 调用点去 auto ----
op(F_SF, b'\t\tVisualiseGPUButtons(flag18, flag19, flag18);\r\n',
   b'\t\tVisualiseGPUButtons(flag18, flag19);\r\n', 'SettingsForm 调用点去 auto')
op(F_GPU, b'\t\t\tsettings.VisualiseGPUButtons(flag, flag2, flag);\r\n',
   b'\t\t\tsettings.VisualiseGPUButtons(flag, flag2);\r\n', 'GPUModeControl:44 去 auto')
op(F_GPU, b'\t\tsettings.VisualiseGPUButtons(num4 >= 0, num5 >= 0, num4 >= 0);\r\n',
   b'\t\tsettings.VisualiseGPUButtons(num4 >= 0, num5 >= 0);\r\n', 'GPUModeControl:93 去 auto')


def main():
    data = {p: io.open(p, 'rb').read() for p in (F_SF, F_GPU)}
    if b'buttonOptimized' not in data[F_SF]:
        print('已经应用过了，无需重复。')
        return 0
    # 自动兼容行尾：锚点若按 CRLF 写但文件是 LF（或反之），自动换一种再试
    resolved = []
    errors = []
    for path, old, new, expect, label in OPS:
        if data[path].count(old) == expect:
            resolved.append((path, old, new, expect, label))
            continue
        alt_old = old.replace(b'\r\n', b'\n') if b'\r\n' in old else old.replace(b'\n', b'\r\n')
        alt_new = new.replace(b'\r\n', b'\n') if b'\r\n' in new else new.replace(b'\n', b'\r\n')
        if data[path].count(alt_old) == expect:
            print('  (行尾适配) ' + label)
            resolved.append((path, alt_old, alt_new, expect, label))
            continue
        errors.append('%-22s 命中 %d/%d 次(期望 %d)  [%s]' % (os.path.basename(path), data[path].count(old), data[path].count(alt_old), expect, label))
    if errors:
        print('=== 校验失败，未写盘 ===')
        for e in errors:
            print('  ! ' + e)
        return 1
    print('=== 锚点校验全部通过（%d 项）===' % len(resolved))
    if '--check' in sys.argv:
        return 0
    for path, old, new, expect, label in resolved:
        data[path] = data[path].replace(old, new, expect)
    for path, blob in data.items():
        io.open(path, 'wb').write(blob)
        print('  写入 ' + path)
    return 0


if __name__ == '__main__':
    sys.exit(main())
