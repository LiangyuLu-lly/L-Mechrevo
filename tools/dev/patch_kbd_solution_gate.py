# -*- coding: utf-8 -*-
"""
补丁：用 GCU 自报的 Keyboard/Status.solution 替代/补充注册表判断
================================================================================
背景：IsGcu50 / IsGcu40Keyboard 读注册表 HKLM\SOFTWARE\L-Mechrevo\GcuVariant，
      而该键只有 LM 自带安装器会写。用官方 GCU / NoGCU 版安装时该键缺失 → 版本判断失效。

更可靠信号：GCU 在 Keyboard/Status 里自报 solution：
      - "EC"   → 键盘灯由 EC 驱动（走 Keyboard/Ctrl 的灯效字段，需要 MonochromeIndex + ColorBuffer ID）
      - "ITE"  → 键盘灯走 ITE/HID（附加字段不适用）
      实测：本机（30 系 / EC）；苍龙·耀世（50 系 / ITE）

改动：
  1. MechrevoHw：新增 KeyboardSolution 属性，OnKeyboardStatus 解析 solution 并写进日志
  2. MechrevoService.SetLightEffect：30 系分支再加 !kbIteSolution（solution=ITE 时保持原行为）

保留 !IsGcu50 作为双保险：LM 安装器装的 50 系有注册表标记，仍被排除。
solution 未上报/为空时不排除 —— 避免"首个包因状态未到而丢失修复"。
"""
import io
import os
import sys

SRC = r'D:\openLM\source'
F_HW = os.path.join(SRC, 'MechrevoLite.Hardware', 'MechrevoHw.cs')
F_SVC = os.path.join(SRC, 'MechrevoLite.Hardware', 'MechrevoService.cs')

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


# ---- 1. MechrevoHw：属性 ----
op(F_HW,
   '\tpublic bool KeyboardPower { get; private set; } = true;\n',
   '\tpublic bool KeyboardPower { get; private set; } = true;\n'
   '\n'
   '\t/// <summary>GCU 上报的键盘灯驱动方案：EC=由 EC 驱动（走 Keyboard/Ctrl 灯效字段）；ITE=走 ITE/HID。</summary>\n'
   '\tpublic string KeyboardSolution { get; private set; } = "";\n',
   'KeyboardSolution 属性')

# ---- 2. MechrevoHw：解析 solution + 日志 ----
op(F_HW,
   '\t\tInterlocked.Increment(ref _keyboardStatusVersion);\n'
   '\t\tLogger.WriteLineIfChanged("kb-status", $"KB: effect={KeyboardEffect} light={KeyboardLight} '
   'brightness={KeyboardBrightness}% speed={KeyboardSpeed} direction={KeyboardDirection} power={KeyboardPower}");\n',
   '\t\tstring text7 = o["solution"]?.ToString();\n'
   '\t\tif (text7 != null)\n'
   '\t\t{\n'
   '\t\t\tKeyboardSolution = text7;\n'
   '\t\t}\n'
   '\t\tInterlocked.Increment(ref _keyboardStatusVersion);\n'
   '\t\tLogger.WriteLineIfChanged("kb-status", $"KB: solution={KeyboardSolution} effect={KeyboardEffect} '
   'light={KeyboardLight} brightness={KeyboardBrightness}% speed={KeyboardSpeed} '
   'direction={KeyboardDirection} power={KeyboardPower}");\n',
   'OnKeyboardStatus 解析 solution')

# ---- 3. MechrevoService：加入 solution 判断 ----
op(F_SVC,
   '\t\t\t\t// 2026-10-04 收窄：排除 50 系（苍龙等单分区机型未实机验证，保持原行为）\n'
   '\t\t\t\tif (isSingleZoneKeyboard && !isGcu40Keyboard && !IsGcu50)\n',
   '\t\t\t\t// 2026-10-04 收窄：排除 40 系（上方分支已处理）、50 系（注册表标记），\n'
   '\t\t\t\t// 以及 GCU 自报 solution=ITE（键盘灯走 ITE/HID 而非 EC）的单分区机型 —— 这些保持原行为。\n'
   '\t\t\t\t// solution 未上报/为空时不排除，避免首个包因状态未到而丢失修复。\n'
   '\t\t\t\tbool kbIteSolution = string.Equals(_hw.KeyboardSolution, "ITE", StringComparison.OrdinalIgnoreCase);\n'
   '\t\t\t\tif (isSingleZoneKeyboard && !isGcu40Keyboard && !IsGcu50 && !kbIteSolution)\n',
   'SetLightEffect 加 solution 判断')


def main():
    if '--check' in sys.argv:
        print('（仅校验模式）')
    text_hw, _, _ = load(F_HW)
    if 'KeyboardSolution' in text_hw:
        print('已经应用过了（发现标记 KeyboardSolution），无需重复。')
        return 0
    errors = []
    for path, old, new, expect, label in OPS:
        text, _, _ = load(path)
        n = text.count(old)
        if n != expect:
            errors.append('%-24s 命中 %d 次(期望 %d)  [%s]' % (os.path.basename(path), n, expect, label))
    if errors:
        print('=== 校验失败，未写盘任何文件 ===')
        for e in errors:
            print('  ! ' + e)
        return 1
    print('=== 锚点校验全部通过（共 %d 项）===' % len(OPS))
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
        print('  写入 %s' % path)
    print('=== 完成 ===')
    return 0


if __name__ == '__main__':
    sys.exit(main())
