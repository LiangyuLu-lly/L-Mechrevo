# -*- coding: utf-8 -*-
r"""
修英文模式下配置下拉框被裁（"Conf..."）
================================================================================
现象：英文模式下三个配置下拉框显示 "Conf..."（宽度 D(72) 装不下 "Config 1"）
分析：`Strings.ModeConfigShort` 用在 4 处：
  1) SettingsForm:5214  下拉框文案            <- 窄，需短标签
  2) SettingsForm:3927  编辑按钮 "Config Edit" <- 改成 Cfg 会很怪
  3) CustomModeForm:1579 编辑器标题
  4) CustomModeForm:1587 编辑器提示
-> 不动 ModeConfigShort，**新增窄用短标签 ModeConfigTiny**：
   en = "Cfg"，zh-CN = "配置"（中文宽度本来就够），仅下拉框改用它
行尾：en resx 是 UTF-8 BOM + LF；zh-CN resx 与 Strings.cs 由脚本自动适配
"""
import io
import sys

EN = r'D:\openLM\source\MechrevoLite.Properties.Strings.resx'
ZH = r'D:\openLM\source\MechrevoLite.Properties.Strings.zh-CN.resx'
CS = r'D:\openLM\source\MechrevoLite.Properties\Strings.cs'
SF = r'D:\openLM\source\MechrevoLite\SettingsForm.cs'

OPS = []


def op(path, old, new, label, expect=1):
    if isinstance(old, str):
        old = old.encode('utf-8')
    if isinstance(new, str):
        new = new.encode('utf-8')
    OPS.append((path, old, new, expect, label))


# ---- 1) en resx：在 ModeConfigShort 之后插入 ModeConfigTiny ----
op(EN,
   '<data name="ModeConfigShort" xml:space="preserve">\n    <value>Config</value>\n  </data>\n',
   '<data name="ModeConfigShort" xml:space="preserve">\n    <value>Config</value>\n  </data>\n'
   '<!-- 2026-10-05: short form used only inside the narrow perf-config combo (Width = D(72));\n'
   '     "Config 1" is clipped to "Conf..." there. Keep ModeConfigShort for buttons/titles. -->\n'
   '<data name="ModeConfigTiny" xml:space="preserve">\n    <value>Cfg</value>\n  </data>\n',
   'en resx 新增 ModeConfigTiny')

# ---- 2) zh-CN resx ----
op(ZH,
   '<data name="ModeConfigShort" xml:space="preserve">\n    <value>配置</value>\n  </data>\n',
   '<data name="ModeConfigShort" xml:space="preserve">\n    <value>配置</value>\n  </data>\n'
   '<!-- 2026-10-05: 仅用于窄下拉框（Width = D(72)）；中文此宽度足够，故与 Short 同值。 -->\n'
   '<data name="ModeConfigTiny" xml:space="preserve">\n    <value>配置</value>\n  </data>\n',
   'zh-CN resx 新增 ModeConfigTiny')

# ---- 3) Strings.cs 访问器 ----
op(CS,
   '\tinternal static string ModeConfigShort => ResourceManager.GetString("ModeConfigShort", resourceCulture);\n',
   '\tinternal static string ModeConfigShort => ResourceManager.GetString("ModeConfigShort", resourceCulture);\n'
   '\n'
   '\t/// <summary>2026-10-05: 窄下拉框专用短标签（en "Cfg" / zh "配置"），见 PerfConfigComboText。</summary>\n'
   '\tinternal static string ModeConfigTiny => ResourceManager.GetString("ModeConfigTiny", resourceCulture);\n',
   'Strings.cs 访问器')

# ---- 4) 下拉框改用短标签 ----
op(SF,
   '\t\treturn Strings.ModeConfigShort + " " + (profileIndex + 1);\n',
   '\t\t// 2026-10-05: 下拉框宽 D(72)，英文 "Config 1" 会被裁成 "Conf..."，故用短标签。\n'
   '\t\treturn Strings.ModeConfigTiny + " " + (profileIndex + 1);\n',
   '下拉框改用短标签')


def main():
    # 幂等
    if b'ModeConfigTiny' in io.open(EN, 'rb').read():
        print('已经应用过了，无需重复。')
        return 0
    resolved = []
    errors = []
    for path, old, new, expect, label in OPS:
        data = io.open(path, 'rb').read()
        if data.count(old) == expect:
            resolved.append((path, old, new, expect, label))
            continue
        if b'\r\n' in old:
            alt_old = old.replace(b'\r\n', b'\n')
            alt_new = new.replace(b'\r\n', b'\n')
        else:
            alt_old = old.replace(b'\n', b'\r\n')
            alt_new = new.replace(b'\n', b'\r\n')
        if data.count(alt_old) == expect:
            print('  (行尾适配) ' + label)
            resolved.append((path, alt_old, alt_new, expect, label))
            continue
        errors.append('命中 %d/%d 次(期望 %d)  [%s]  %s' % (data.count(old), data.count(alt_old), expect, label, path))
    if errors:
        print('=== 校验失败，未写盘 ===')
        for e in errors:
            print('  ! ' + e)
        return 1
    print('=== 锚点校验全部通过（%d 项）===' % len(resolved))
    if '--check' in sys.argv:
        return 0
    for path, old, new, expect, label in resolved:
        data = io.open(path, 'rb').read()
        io.open(path, 'wb').write(data.replace(old, new, expect))
        print('  写入 ' + path)
    return 0


if __name__ == '__main__':
    sys.exit(main())
