# -*- coding: utf-8 -*-
"""
修正：模式编号混用导致的「配置」存储键整体错位
================================================================================
官方/LM 有三套编号，我第一版把前两套混了：

  A. ProfileName 的 Mode{N}（官方文件与风扇表命名）
     1=游戏(Gaming) / 2=办公(Office) / 3=狂飙(Turbo)     ← VendorModeNumber(kind)
  B. 控制台 OperatingMode / MQTT 动作编号
     0=办公(Office) / 1=游戏(Gaming) / 2=狂飙(Turbo)     ← PowerModeMapping.FromVisualMode
  C. LM 视觉模式号（UI 与 SwitchPerformanceModeAsync 用）
     0=平衡(游戏) / 1=狂暴(狂飙) / 2=静音(办公)

实测（日志）：FromVisualMode: C0->B1, C1->B2, C2->B0

第一版错误：ModeProfileConfig 的 KeyFor 按 C 编号写（0=游戏/1=狂飙/2=办公），
            但 ResolveModeProfileIndex 传进去的是 B 编号 → 三个键整体旋转：
              B2(狂飙) 读写了 mode_cfg_office  ← 错
              B1(游戏) 读写了 mode_cfg_turbo   ← 错
              B0(办公) 读写了 mode_cfg_gaming  ← 错

修法：统一用 C（视觉模式号）—— KeyFor 已是 C 编号，UI 也传 C 编号，
      只需让 ResolveModeProfileIndex 改用 visualMode 而不是 consoleMode。
"""
import io
import os
import sys

F_HW = r'D:\openLM\source\MechrevoLite.Hardware\MechrevoHw.cs'
F_MPC = r'D:\openLM\source\MechrevoLite.Mode\ModeProfileConfig.cs'

OPS = []
CACHE = {}


def load(path):
    if path not in CACHE:
        raw = io.open(path, 'rb').read()
        bom = raw.startswith(b'\xef\xbb\xbf')
        text = raw.decode('utf-8-sig')
        crlf = '\r\n' in text
        if crlf:
            text = text.replace('\r\n', '\n')
        CACHE[path] = (text, bom, crlf)
    return CACHE[path]


def op(path, old, new, label, expect=1):
    OPS.append((path, old, new, expect, label))


# ---- 1. 解析器改用视觉模式号 ----
op(F_HW,
   '\t\tif (hw.SupportsModeProfileConfig && ModeProfileConfig.IsBindable(consoleMode))\n'
   '\t\t{\n'
   '\t\t\tint num = ModeProfileConfig.Get(consoleMode);\n'
   '\t\t\tLogger.WriteLine($"ModeProfileConfig: visualMode={visualMode} consoleMode={consoleMode} profile={num} count={ModeProfileConfig.Count}");\n'
   '\t\t\treturn num;\n'
   '\t\t}\n',
   '\t\t// 注意：ModeProfileConfig 用 **视觉模式号**（0=平衡/游戏 1=狂暴/狂飙 2=静音/办公），\n'
   '\t\t// 与控制台 OperatingMode（0=办公 1=游戏 2=狂飙）不是同一套编号 —— 勿混用。\n'
   '\t\tif (hw.SupportsModeProfileConfig && ModeProfileConfig.IsBindable(visualMode))\n'
   '\t\t{\n'
   '\t\t\tint num = ModeProfileConfig.Get(visualMode);\n'
   '\t\t\tLogger.WriteLine($"ModeProfileConfig: visualMode={visualMode} consoleMode={consoleMode} profile={num} count={ModeProfileConfig.Count}");\n'
   '\t\t\treturn num;\n'
   '\t\t}\n',
   'ResolveModeProfileIndex 改用 visualMode')

op(F_HW,
   '\t/// 支持「模式 × 配置」的机型 → 该模式自己记的配置号（ModeProfileConfig）；\n',
   '\t/// 支持「模式 × 配置」的机型 → 该模式自己记的配置号（ModeProfileConfig，按**视觉模式号**）；\n',
   '解析器注释更正')

# ---- 2. ModeProfileConfig 注释更正为视觉模式号 ----
op(F_MPC,
   '/// 控制台模式号约定（与 MechrevoService/MechrevoHw 的 Action 映射一致）：\n'
   '///   0 = 游戏(Gaming/平衡) / 1 = 狂飙(Turbo/狂暴) / 2 = 办公(Office/静音) / 3 = 自定义\n',
   '/// 编号约定 —— 本类一律用 **LM 视觉模式号**（与 UI / SwitchPerformanceModeAsync 同号）：\n'
   '///   0 = 平衡/游戏(Gaming) / 1 = 狂暴/狂飙(Turbo) / 2 = 静音/办公(Office) / 3 = 自定义\n'
   '///\n'
   '/// ⚠️ 不要与控制台 OperatingMode / MQTT 动作编号混用，那是另一套：\n'
   '///   0 = 办公(Office) / 1 = 游戏(Gaming) / 2 = 狂飙(Turbo) / 3 = 自定义\n'
   '///   二者换算：PowerModeMapping.FromVisualMode（0->1, 1->2, 2->0）\n'
   '/// 也不要与 ProfileName 的 Mode{N} 混用（1=游戏 / 2=办公 / 3=狂飙）。\n',
   'ModeProfileConfig 注释更正为视觉模式号')

op(F_MPC,
   '/// <summary>该控制台模式是否适用「模式 × 配置」绑定（排除自定义模式 3）。</summary>\n'
   '\tinternal static bool IsBindable(int consoleMode)\n'
   '\t{\n'
   '\t\treturn consoleMode == 0 || consoleMode == 1 || consoleMode == 2;\n'
   '\t}\n',
   '/// <summary>该**视觉模式**是否适用「模式 × 配置」绑定（排除自定义模式 3）。</summary>\n'
   '\tinternal static bool IsBindable(int visualMode)\n'
   '\t{\n'
   '\t\treturn visualMode == 0 || visualMode == 1 || visualMode == 2;\n'
   '\t}\n',
   'IsBindable 参数改名')

op(F_MPC,
   'internal static string? KeyFor(int consoleMode)\n'
   '\t{\n'
   '\t\treturn consoleMode switch\n',
   'internal static string? KeyFor(int visualMode)\n'
   '\t{\n'
   '\t\treturn visualMode switch\n',
   'KeyFor 参数改名')

op(F_MPC,
   'internal static int Get(int consoleMode)\n'
   '\t{\n'
   '\t\tstring? text = KeyFor(consoleMode);\n',
   'internal static int Get(int visualMode)\n'
   '\t{\n'
   '\t\tstring? text = KeyFor(visualMode);\n',
   'Get 参数改名')

op(F_MPC,
   'internal static void Set(int consoleMode, int profileIndex)\n'
   '\t{\n'
   '\t\tstring? text = KeyFor(consoleMode);\n',
   'internal static void Set(int visualMode, int profileIndex)\n'
   '\t{\n'
   '\t\tstring? text = KeyFor(visualMode);\n',
   'Set 参数改名')


def main():
    for path, _, _, _, _ in OPS:
        load(path)
    if 'ModeProfileConfig 用 **视觉模式号**' in load(F_HW)[0]:
        print('已经应用过了，无需重复。')
        return 0
    errors = []
    for path, old, new, expect, label in OPS:
        n = load(path)[0].count(old)
        if n != expect:
            errors.append('%-26s 命中 %d 次(期望 %d)  [%s]' % (os.path.basename(path), n, expect, label))
    if errors:
        print('=== 校验失败，未写盘 ===')
        for e in errors:
            print('  ! ' + e)
        return 1
    print('=== 锚点校验全部通过（%d 项）===' % len(OPS))
    if '--check' in sys.argv:
        return 0
    for path, old, new, expect, label in OPS:
        text, bom, crlf = load(path)
        CACHE[path] = (text.replace(old, new, expect), bom, crlf)
    for path, (text, bom, crlf) in CACHE.items():
        out = text.replace('\n', '\r\n') if crlf else text
        data = out.encode('utf-8')
        if bom:
            data = b'\xef\xbb\xbf' + data
        io.open(path, 'wb').write(data)
        print('  写入 ' + path)
    print('=== 完成 ===')
    return 0


if __name__ == '__main__':
    sys.exit(main())
