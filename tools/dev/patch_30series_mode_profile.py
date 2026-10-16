# -*- coding: utf-8 -*-
"""
30 系「模式 × 配置」—— 数据层补丁
================================================================================
用户决策（2026-10-04）：
  判据   = DgpuGeneration == Gen30 || GcuVariant == "30"（或）
  40/50 系不跟随（判据自然排除）
  配置数量 = 按实际（用 LM 已有的固件槽位数 SlotCountFor(CustomizeTarget)，本机 = 5）
  现有切换逻辑不动

背景（抓包证据见 30系性能模式适配方案.md）：
  官方 30 系控制台切模式时，模式+配置在同一条 Fan/Control 里下发：
    {"Action":"OPERATING_OFFICE_MODE","ProfileIndex":"3"}
  LM 原先只在自定义模式（mode==3）下发 ProfileIndex，办公/游戏/狂飙恒发 0。

本补丁（数据层，不含 UI）：
  1. MechrevoService 新增 IsGcu30 静态属性（读 HKLM\\SOFTWARE\\L-Mechrevo\\GcuVariant == "30"）
  2. MechrevoHw 新增 SupportsModeProfileConfig（判据）与 ResolveModeProfileIndex（解析下发值）
  3. 两处 BuildModeSwitchPayloads 调用改用 ResolveModeProfileIndex
  4. 新增 MechrevoLite.Mode\\ModeProfileConfig.cs（每模式配置号持久化；未设置时返回 0 = 行为不变）
"""
import io
import os
import sys

SRC = r'D:\openLM\source'
F_SVC = os.path.join(SRC, 'MechrevoLite.Hardware', 'MechrevoService.cs')
F_HW = os.path.join(SRC, 'MechrevoLite.Hardware', 'MechrevoHw.cs')

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


# ---------------- 1. MechrevoService: IsGcu30 ----------------
ISGCU30 = '''\t/// <summary>
\t/// 是否 30 系 GCU（LM 自带安装器写 HKLM\\SOFTWARE\\L-Mechrevo\\GcuVariant="30"）。
\t/// 2026-10-04：与显卡代际 Gen30 一起作为「模式 × 配置」功能的判据。
\t/// </summary>
\tpublic static bool IsGcu30
\t{
\t\tget
\t\t{
\t\t\ttry
\t\t\t{
\t\t\t\tusing RegistryKey key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\\L-Mechrevo");
\t\t\t\tobject v = key?.GetValue("GcuVariant");
\t\t\t\treturn v != null && string.Equals(v.ToString(), "30", StringComparison.OrdinalIgnoreCase);
\t\t\t}
\t\t\tcatch
\t\t\t{
\t\t\t\treturn false;
\t\t\t}
\t\t}
\t}

'''
op(F_SVC, '\tprivate bool IsGcu40Keyboard\n', ISGCU30 + '\tprivate bool IsGcu40Keyboard\n',
   'MechrevoService 新增 IsGcu30')

# ---------------- 3a. MechrevoService: 下发值改用解析器 ----------------
op(F_SVC,
   '\t\t\t(payload, overclockPayload) = MechrevoHw.BuildModeSwitchPayloads(text, num, (mode == 3) ? Math.Clamp(_hw.CustomProfileIndex, 0, 3) : 0, mode == 3);\n',
   '\t\t\t// 2026-10-04：非自定义模式也下发该模式自己的配置号（30 系「模式 × 配置」；未设置时为 0 = 旧行为）\n'
   '\t\t\t(payload, overclockPayload) = MechrevoHw.BuildModeSwitchPayloads(text, num, MechrevoHw.ResolveModeProfileIndex(_hw, mode, num, mode == 3), mode == 3);\n',
   'MechrevoService.SwitchMode 改用解析器')

# ---------------- 2. MechrevoHw: 判据 + 解析器 ----------------
HWMEMBERS = '''\t/// <summary>
\t/// 该机型是否支持「模式 × 配置」绑定（官方 30 系控制台：三个模式各绑一个固件配置）。
\t/// 判据（2026-10-04 用户拍板）：显卡代际 Gen30 <b>或</b> GcuVariant=="30"。
\t/// 40/50 系不满足该判据 → 行为完全不变。
\t/// </summary>
\tpublic bool SupportsModeProfileConfig
\t{
\t\tget
\t\t{
\t\t\tif (DgpuGeneration == DgpuGenerationKind.Gen30)
\t\t\t{
\t\t\t\treturn true;
\t\t\t}
\t\t\treturn MechrevoService.IsGcu30;
\t\t}
\t}

\t/// <summary>
\t/// 解析某模式该下发的 ProfileIndex：
\t/// 自定义模式 → 固件槽位机制给出的 CustomProfileIndex；
\t/// 支持「模式 × 配置」的机型 → 该模式自己记的配置号（ModeProfileConfig）；
\t/// 其余 → 0（保持 LM 原有行为）。
\t/// </summary>
\tinternal static int ResolveModeProfileIndex(MechrevoHw hw, int visualMode, int consoleMode, bool custom)
\t{
\t\tif (custom)
\t\t{
\t\t\treturn Math.Clamp(hw.CustomProfileIndex, 0, 3);
\t\t}
\t\tif (hw.SupportsModeProfileConfig && ModeProfileConfig.IsBindable(consoleMode))
\t\t{
\t\t\treturn ModeProfileConfig.Get(consoleMode);
\t\t}
\t\treturn 0;
\t}

'''
op(F_HW,
   '\tinternal static (Dictionary<string, object> Fan, Dictionary<string, object> Overclock) BuildModeSwitchPayloads(string action, int operatingMode, int profileIndex, bool custom)\n',
   HWMEMBERS + '\tinternal static (Dictionary<string, object> Fan, Dictionary<string, object> Overclock) BuildModeSwitchPayloads(string action, int operatingMode, int profileIndex, bool custom)\n',
   'MechrevoHw 新增判据 + 解析器')

# ---------------- 3b. MechrevoHw.SetMode: 下发值改用解析器 ----------------
op(F_HW,
   '\t\t(payload, overclockPayload) = BuildModeSwitchPayloads(text, expectedOpMode, flag ? Math.Clamp(CustomProfileIndex, 0, 3) : 0, flag);\n',
   '\t\t(payload, overclockPayload) = BuildModeSwitchPayloads(text, expectedOpMode, ResolveModeProfileIndex(this, mode, expectedOpMode, flag), flag);\n',
   'MechrevoHw.SetMode 改用解析器')


def main():
    for path, _, _, _, _ in OPS:
        load(path)
    if 'SupportsModeProfileConfig' in load(F_HW)[0]:
        print('已经应用过了，无需重复。')
        return 0
    errors = []
    for path, old, new, expect, label in OPS:
        text = load(path)[0]
        n = text.count(old)
        if n != expect:
            errors.append('%-22s 命中 %d 次(期望 %d)  [%s]' % (os.path.basename(path), n, expect, label))
    if errors:
        print('=== 校验失败，未写盘任何文件 ===')
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
