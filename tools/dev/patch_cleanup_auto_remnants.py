# -*- coding: utf-8 -*-
r"""
清理 AUTO 显卡模式的死代码残余
================================================================================
背景：AUTO 显卡模式按钮（buttonOptimized）此前已移除（"AUTO 就是混合模式"）。
      现在只剩下三档（核显/混合/独显直连）。
实测残余（全部无引用/不可达）：
  SettingsForm:120   private ToolStripMenuItem menuOptimized;        <- 从不创建
  SettingsForm:5875  ... = (menuOptimized = null)                    <- 死引用
  SettingsForm:6261  ButtonOptimized_MouseHover（空体）              <- 无 += 引用
  SettingsForm:6645  ButtonOptimized_Click -> SwitchGpuModeFromUi(3, Strings.AutoShort)  <- 无 += 引用
  SettingsForm:7261  if (menuOptimized != null) menuOptimized.Visible = false;          <- 恒为 null
⚠️ 不动：Program.cs:885/1694 gpuControl.AutoGPUMode(optimized:false,...)（电源自动切换，活的）
⚠️ 不动：Strings.Optimized / OptimizedGPUTooltip / AutoShort —— 本次先只清代码，
         字符串（0 引用）留待确认后再删（要动 Strings.cs + 两个 resx）
"""
import io
import sys

F = r'D:\openLM\source\MechrevoLite\SettingsForm.cs'

OPS = []


def op(old, new, label, expect=1):
    if isinstance(old, str):
        old = old.encode('utf-8')
    if isinstance(new, str):
        new = new.encode('utf-8')
    OPS.append((old, new, expect, label))


# 1) 字段声明
op('\tprivate ToolStripMenuItem menuOptimized;\n'
   '\n'
   '\tprivate DonateControl donateControl;\n',
   '\tprivate DonateControl donateControl;\n',
   'menuOptimized 字段')

# 2) null 赋值链
op('\t\tmenuEco = (menuStandard = (menuUltimate = (menuOptimized = null)));\n',
   '\t\tmenuEco = (menuStandard = (menuUltimate = null));\n',
   'menuOptimized null 赋值')

# 3) 空 hover 处理器
op('\tprivate void ButtonOptimized_MouseHover(object? sender, EventArgs e)\n'
   '\t{\n'
   '\t}\n'
   '\n'
   '\tprivate void ButtonGPU_MouseLeave(object? sender, EventArgs e)\n',
   '\tprivate void ButtonGPU_MouseLeave(object? sender, EventArgs e)\n',
   'ButtonOptimized_MouseHover')

# 4) Click 处理器（唯一使用 Strings.AutoShort 处）
op('\tprivate async void ButtonOptimized_Click(object? sender, EventArgs e)\n'
   '\t{\n'
   '\t\tawait SwitchGpuModeFromUi(3, Strings.AutoShort);\n'
   '\t}\n'
   '\n'
   '\tprivate async Task SwitchGpuModeFromUi(int targetMode, string modeName)\n',
   '\tprivate async Task SwitchGpuModeFromUi(int targetMode, string modeName)\n',
   'ButtonOptimized_Click')

# 5) 恒为 null 的判断
op('\t\tif (menuOptimized != null)\n'
   '\t\t{\n'
   '\t\t\tmenuOptimized.Visible = false;\n'
   '\t\t}\n'
   '\t\tif (menuStandard != null)\n',
   '\t\tif (menuStandard != null)\n',
   'menuOptimized Visible 判断')


def main():
    data0 = io.open(F, 'rb').read()
    if b'menuOptimized' not in data0:
        print('已经清理过了。')
        return 0
    resolved, errors = [], []
    for old, new, expect, label in OPS:
        data = io.open(F, 'rb').read()
        if data.count(old) == expect:
            resolved.append((old, new, expect, label))
            continue
        if b'\r\n' in old:
            ao, an = old.replace(b'\r\n', b'\n'), new.replace(b'\r\n', b'\n')
        else:
            ao, an = old.replace(b'\n', b'\r\n'), new.replace(b'\n', b'\r\n')
        if data.count(ao) == expect:
            print('  (行尾适配) ' + label)
            resolved.append((ao, an, expect, label))
            continue
        errors.append('命中 %d/%d 次(期望 %d)  [%s]' % (data.count(old), data.count(ao), expect, label))
    if errors:
        print('=== 校验失败，未写盘 ===')
        for e in errors:
            print('  ! ' + e)
        return 1
    print('=== 锚点校验通过（%d 项）===' % len(resolved))
    if '--check' in sys.argv:
        return 0
    for old, new, expect, label in resolved:
        data = io.open(F, 'rb').read()
        io.open(F, 'wb').write(data.replace(old, new, expect))
    print('  写入 ' + F)
    return 0


if __name__ == '__main__':
    sys.exit(main())
