# -*- coding: utf-8 -*-
"""
解除"40 系及以后 平衡/狂暴/安静 不可改配置"的限制 —— 但**仅在固件配置模式下**
================================================================================
LM 原逻辑（CustomModeForm.SetOptionsEnabled :1219）：
    bool locked = hw != null && IsGen40Plus(hw) && (mode == null || !mode.IsCustom);
    // 40 系及以后：非自定义档 -> 锁掉 OptionControls() 全部参数控件
    // （原因：40+ 机型有专门的自定义模式，预设档不允许改）
本改动：追加 `&& !_firmwareMode`
  - 正常使用（40+ 改预设档）：行为**完全不变**，仍锁定
  - 30 系「配置编辑」（固件配置模式）：解锁，可编 平衡/狂暴/安静的固件配置
    —— 这本来就是 30 系的正确行为（官方 30 系控制台允许改预设档配置）
注意：CustomModeForm.cs 是 LF。
"""
import io
import os
import sys

F = r'D:\openLM\source\MechrevoLite\CustomModeForm.cs'

OLD = b'\t\tbool locked = hw != null && IsGen40Plus(hw) && (mode == null || !mode.IsCustom);\n'
NEW = (b'\t\t// 2026-10-04: firmware-profile mode edits a *preset* firmware config, which is the\r\n'
       b'\t\t// normal 30-series behaviour. Keep the Gen40+ preset lock for regular LM use.\r\n'
       b'\t\tbool locked = hw != null && IsGen40Plus(hw) && (mode == null || !mode.IsCustom) && !_firmwareMode;\n')


def main():
    data = io.open(F, 'rb').read()
    if b'&& !_firmwareMode;' in data:
        print('已经应用过了，无需重复。')
        return 0
    n = data.count(OLD)
    if n != 1:
        print('=== 校验失败：锚点命中 %d 次(期望 1)，未写盘 ===' % n)
        return 1
    print('=== 锚点校验通过 ===')
    if '--check' in sys.argv:
        return 0
    data = data.replace(OLD, NEW, 1).replace(b'\r\n', b'\n')
    io.open(F, 'wb').write(data)
    print('  写入 ' + F)
    return 0


if __name__ == '__main__':
    sys.exit(main())
