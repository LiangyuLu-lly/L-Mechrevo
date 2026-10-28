# -*- coding: utf-8 -*-
"""
功能：充电上限「安装目录非受保护」一次性提示 + 一键收紧目录权限
================================================================================
背景（极光Pro 2024 诊断包发现）：
  安装目录若允许非管理员写入（如 D:\\L-Mechrevo，从盘符根继承了
  "Authenticated Users: 修改"），LM 会拒绝以 SYSTEM 身份注册"充电上限"开机任务
  （否则任何普通用户程序都能替换 exe → 开机以 SYSTEM 执行 = 提权漏洞）。
  原实现只写一行日志就放弃，用户完全无感知：
    - 界面无提示
    - 平时开关机看不出问题（EC 有电），只有"彻底断电"后上限才悄悄回到 100%

改法（用户选方案 B）：
  1. ExecutableTrust.TryHardenImageDirectory()：把程序所在目录收紧为
     SYSTEM/Administrators 完全控制 + Users 读取与执行（断开继承、清空旧 ACE），
     并用 IsProtectedLocation 复核；子文件靠继承自动生效
  2. Startup：安装目录不合规时，弹一次性确认框（父窗口 = Program.settingsForm），
     选"是"→ 收紧权限 → 成功后自动重试注册充电任务；
     选"否"→ 记 AppConfig charge_task_location_prompt_dismissed=1，不再打扰
  3. 新增 4 条中英文案

原型验证（2026-10-04，在 D:\\openLM\\tmp_acl_test 上实测）：
  收紧前：目录与 exe 均为 "S-1-5-11(Authenticated Users): Modify" → 不安全
  收紧后：目录 SYSTEM(OI)(CI)(F) / Administrators(OI)(CI)(F) / Users(OI)(CI)(RX)
          exe 自动继承为 SYSTEM(I)(F) / Administrators(I)(F) / Users(I)(RX) → 通过
"""
import io
import os
import sys

SRC = r'D:\openLM\source'
F_TRUST = os.path.join(SRC, 'MechrevoLite.Helpers', 'ExecutableTrust.cs')
F_STARTUP = os.path.join(SRC, 'Startup.cs')
F_RESX_EN = os.path.join(SRC, 'MechrevoLite.Properties.Strings.resx')
F_RESX_ZH = os.path.join(SRC, 'MechrevoLite.Properties.Strings.zh-CN.resx')
F_STRINGS = os.path.join(SRC, 'MechrevoLite.Properties', 'Strings.cs')

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


# ============================================================ 1. ExecutableTrust
TRUST_ANCHOR = '\tprivate static bool TryReadDirectoryRules(string directory, out List<AccessRuleSnapshot> rules, out string error)\n'

TRUST_NEW = '''\t/// <summary>
\t/// 把当前程序所在目录收紧为"仅管理员可写"：SYSTEM 与 Administrators 完全控制，
\t/// Users 仅读取与执行（与 Program Files 的有效权限一致）。用于安装目录不满足保护的机器上
\t/// 一键修好"充电上限以 SYSTEM 注册开机任务"的前置条件。
\t/// 返回 true 时已用 IsProtectedLocation 复核通过；目录内文件靠继承自动生效。
\t/// 需要管理员权限（调用方负责确认）。
\t/// </summary>
\tinternal static bool TryHardenImageDirectory(out string message)
\t{
\t\tstring imagePath;
\t\ttry
\t\t{
\t\t\timagePath = Application.ExecutablePath.Trim();
\t\t}
\t\tcatch (Exception ex)
\t\t{
\t\t\tmessage = "无法确定当前程序镜像路径：" + ex.Message;
\t\t\treturn false;
\t\t}
\t\tstring directory;
\t\ttry
\t\t{
\t\t\tdirectory = Path.GetDirectoryName(Path.GetFullPath(imagePath));
\t\t}
\t\tcatch (Exception ex2)
\t\t{
\t\t\tmessage = "无法解析镜像所在目录：" + ex2.Message;
\t\t\treturn false;
\t\t}
\t\tif (string.IsNullOrWhiteSpace(directory))
\t\t{
\t\t\tmessage = "无法解析镜像所在目录：" + imagePath;
\t\t\treturn false;
\t\t}
\t\ttry
\t\t{
\t\t\tDirectoryInfo directoryInfo = new DirectoryInfo(directory);
\t\t\tif (!directoryInfo.Exists)
\t\t\t{
\t\t\t\tmessage = "目录不存在：" + directory;
\t\t\t\treturn false;
\t\t\t}
\t\t\tDirectorySecurity accessControl = directoryInfo.GetAccessControl(AccessControlSections.Access);
\t\t\t// 1) 断开继承且不保留继承项 —— 必须去掉从盘符根继承来的"Authenticated Users: 修改"
\t\t\taccessControl.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);
\t\t\t// 2) 清空全部显式项，避免残留"非管理员可写"的旧 ACE
\t\t\tforeach (FileSystemAccessRule rule in accessControl.GetAccessRules(includeExplicit: true, includeInherited: false, typeof(SecurityIdentifier)))
\t\t\t{
\t\t\t\taccessControl.RemoveAccessRuleSpecific(rule);
\t\t\t}
\t\t\t// 3) 只装标准三项（与 Program Files 的有效权限一致）
\t\t\tInheritanceFlags inheritanceFlags = InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit;
\t\t\taccessControl.AddAccessRule(new FileSystemAccessRule(new SecurityIdentifier("S-1-5-18"), FileSystemRights.FullControl, inheritanceFlags, PropagationFlags.None, AccessControlType.Allow));
\t\t\taccessControl.AddAccessRule(new FileSystemAccessRule(new SecurityIdentifier("S-1-5-32-544"), FileSystemRights.FullControl, inheritanceFlags, PropagationFlags.None, AccessControlType.Allow));
\t\t\taccessControl.AddAccessRule(new FileSystemAccessRule(new SecurityIdentifier("S-1-5-32-545"), FileSystemRights.ReadAndExecute, inheritanceFlags, PropagationFlags.None, AccessControlType.Allow));
\t\t\tdirectoryInfo.SetAccessControl(accessControl);
\t\t}
\t\tcatch (Exception ex3)
\t\t{
\t\t\tmessage = "写入目录权限失败：" + ex3.Message;
\t\t\treturn false;
\t\t}
\t\t// 4) 复核
\t\tif (!IsProtectedLocation(imagePath, out string reason))
\t\t{
\t\t\tmessage = "收紧后仍未通过校验：" + reason;
\t\t\treturn false;
\t\t}
\t\tmessage = reason;
\t\treturn true;
\t}

'''
op(F_TRUST, TRUST_ANCHOR, TRUST_NEW + TRUST_ANCHOR, 'ExecutableTrust 新增 TryHardenImageDirectory')

# ============================================================ 2. Startup
op(F_STARTUP,
   'using MechrevoLite.Helpers;\n',
   'using MechrevoLite.Helpers;\nusing MechrevoLite.Properties;\n',
   'Startup 补 using MechrevoLite.Properties')

op(F_STARTUP,
   'using System.Text;\n',
   'using System.Text;\nusing System.Threading;\n',
   'Startup 补 using System.Threading')

op(F_STARTUP,
   '\t\tif (!ExecutableTrust.IsCurrentImageInProtectedLocation(out string reason))\n'
   '\t\t{\n'
   '\t\t\tLogger.WriteLine("Refusing to register the SYSTEM charge limit task: " + reason + ". Install into a directory that only administrators can write (for example Program Files).");\n'
   '\t\t\treturn;\n'
   '\t\t}\n',
   '\t\tif (!ExecutableTrust.IsCurrentImageInProtectedLocation(out string reason))\n'
   '\t\t{\n'
   '\t\t\tLogger.WriteLine("Refusing to register the SYSTEM charge limit task: " + reason + ". Install into a directory that only administrators can write (for example Program Files).");\n'
   '\t\t\tOfferChargeTaskLocationFix(reason);\n'
   '\t\t\treturn;\n'
   '\t\t}\n',
   'ScheduleCharge 调用一次性提示')

STARTUP_METHOD = '''\tprivate const string ChargeTaskPromptDismissedKey = "charge_task_location_prompt_dismissed";

\tprivate static int _chargeTaskLocationPromptShown;

\t/// <summary>
\t/// 2026-10-04：安装目录不满足"仅管理员可写"时，原来只写日志就放弃注册充电上限开机任务，
\t/// 用户完全无感知（表现为彻底断电后充电上限悄悄回到 100%）。
\t/// 现改为一次性提示，并支持一键收紧目录权限；选择"否"后不再打扰。
\t/// 无可用窗口时保持原行为（只记日志），等下次有窗口时再提示。
\t/// </summary>
\tprivate static void OfferChargeTaskLocationFix(string reason)
\t{
\t\ttry
\t\t{
\t\t\tif (AppConfig.Is(ChargeTaskPromptDismissedKey))
\t\t\t{
\t\t\t\treturn;
\t\t\t}
\t\t\tif (Interlocked.Exchange(ref _chargeTaskLocationPromptShown, 1) == 1)
\t\t\t{
\t\t\t\treturn;
\t\t\t}
\t\t\tSettingsForm settingsForm = Program.settingsForm;
\t\t\tif (settingsForm == null || settingsForm.IsDisposed || !settingsForm.IsHandleCreated)
\t\t\t{
\t\t\t\tInterlocked.Exchange(ref _chargeTaskLocationPromptShown, 0);
\t\t\t\treturn;
\t\t\t}
\t\t\tsettingsForm.BeginInvoke((Action)delegate
\t\t\t{
\t\t\t\ttry
\t\t\t\t{
\t\t\t\t\tDialogResult dialogResult = MessageBox.Show(settingsForm, string.Format(Strings.ChargeTaskLocationBody, reason), Strings.ChargeTaskLocationTitle, MessageBoxButtons.YesNo, MessageBoxIcon.Warning);
\t\t\t\t\tif (dialogResult != DialogResult.Yes)
\t\t\t\t\t{
\t\t\t\t\t\tAppConfig.Set(ChargeTaskPromptDismissedKey, 1);
\t\t\t\t\t\tLogger.WriteLine("Charge task location hardening declined by the user; will not ask again.");
\t\t\t\t\t\treturn;
\t\t\t\t\t}
\t\t\t\t\tif (ExecutableTrust.TryHardenImageDirectory(out string fixMessage))
\t\t\t\t\t{
\t\t\t\t\t\tLogger.WriteLine("Charge task location hardened: " + fixMessage);
\t\t\t\t\t\tToastForm.ShowNotice(Strings.ChargeTaskLocationFixed);
\t\t\t\t\t\tScheduleCharge();
\t\t\t\t\t}
\t\t\t\t\telse
\t\t\t\t\t{
\t\t\t\t\t\tLogger.WriteLine("Charge task location hardening failed: " + fixMessage);
\t\t\t\t\t\tToastForm.ShowFailure(Strings.ChargeTaskLocationFailed);
\t\t\t\t\t}
\t\t\t\t}
\t\t\t\tcatch (Exception ex)
\t\t\t\t{
\t\t\t\t\tLogger.WriteLine("Charge task location prompt failed: " + ex.Message);
\t\t\t\t}
\t\t\t});
\t\t}
\t\tcatch (Exception ex2)
\t\t{
\t\t\tLogger.WriteLine("Charge task location prompt dispatch failed: " + ex2.Message);
\t\t}
\t}

'''
op(F_STARTUP, '\tpublic static void ScheduleCharge()\n',
   STARTUP_METHOD + '\tpublic static void ScheduleCharge()\n', 'Startup 新增 OfferChargeTaskLocationFix')

# ============================================================ 3. 资源
ZH_BODY = ('检测到 L-Mechrevo 安装在非受保护目录：\\n{0}\\n\\n'
           '该目录允许非管理员改写程序文件，因此 L-Mechrevo 拒绝以 SYSTEM 身份注册「充电上限」的'
           '开机任务（否则任何普通用户程序都可能被替换成开机自动执行，存在提权风险）。\\n\\n'
           '后果：充电上限在彻底断电后不会自动恢复，可能悄悄回到 100%。平时开关机看不出来。\\n\\n'
           '是否现在收紧该目录权限？（收紧后仅管理员可写；普通用户仍可读取和运行 L-Mechrevo）')
EN_BODY = ('L-Mechrevo is installed in a directory that is not protected:\\n{0}\\n\\n'
           'Non-administrators can replace the program files there, so L-Mechrevo refuses to register '
           'the charge-limit boot task as SYSTEM (otherwise any normal-user program could be swapped in '
           'and run automatically at boot - a privilege-escalation risk).\\n\\n'
           'Consequence: after a full power loss the charge limit will not be restored automatically and '
           'may silently revert to 100%. Normal restarts hide the problem.\\n\\n'
           'Tighten this directory\'s permissions now? (only administrators will be able to write; normal '
           'users can still read and run L-Mechrevo)')


def resx_keys(body, title, fixed, failed):
    return ('<data name="ChargeTaskLocationTitle" xml:space="preserve">\n'
            '    <value>' + title + '</value>\n'
            '  </data>\n'
            '<data name="ChargeTaskLocationBody" xml:space="preserve">\n'
            '    <value>' + body + '</value>\n'
            '  </data>\n'
            '<data name="ChargeTaskLocationFixed" xml:space="preserve">\n'
            '    <value>' + fixed + '</value>\n'
            '  </data>\n'
            '<data name="ChargeTaskLocationFailed" xml:space="preserve">\n'
            '    <value>' + failed + '</value>\n'
            '  </data>\n</root>')


op(F_RESX_ZH, '</root>', resx_keys(
    ZH_BODY,
    '充电上限 · 安装位置',
    '已收紧目录权限，充电上限的开机自动恢复已启用',
    '收紧目录权限失败，请手动把 L-Mechrevo 安装到 C:\\Program Files'), 'zh 新增 4 条文案')

op(F_RESX_EN, '</root>', resx_keys(
    EN_BODY,
    'Charge limit · install location',
    'Directory permissions tightened; the charge-limit boot task is now registered',
    'Could not tighten directory permissions. Please install L-Mechrevo into C:\\Program Files'), 'en 新增 4 条文案')

ACCESSORS = ('\tinternal static string ChargeTaskLocationTitle => ResourceManager.GetString("ChargeTaskLocationTitle", resourceCulture);\n'
             '\n'
             '\tinternal static string ChargeTaskLocationBody => ResourceManager.GetString("ChargeTaskLocationBody", resourceCulture);\n'
             '\n'
             '\tinternal static string ChargeTaskLocationFixed => ResourceManager.GetString("ChargeTaskLocationFixed", resourceCulture);\n'
             '\n'
             '\tinternal static string ChargeTaskLocationFailed => ResourceManager.GetString("ChargeTaskLocationFailed", resourceCulture);\n')

op(F_STRINGS,
   '\tinternal static string ChargeModeWorkstation => ResourceManager.GetString("ChargeModeWorkstation", resourceCulture);\n',
   '\tinternal static string ChargeModeWorkstation => ResourceManager.GetString("ChargeModeWorkstation", resourceCulture);\n'
   '\n' + ACCESSORS,
   'Strings.cs 新增 4 个访问器')


# ============================================================ 校验 + 写盘
def main():
    for path, _, _, _, _ in OPS:
        load(path)
    if 'ChargeTaskLocationBody' in load(F_STRINGS)[0]:
        print('已经应用过了，无需重复。')
        return 0
    errors = []
    for path, old, new, expect, label in OPS:
        text = load(path)[0]
        n = text.count(old)
        if n != expect:
            errors.append('%-26s 命中 %d 次(期望 %d)  [%s]' % (os.path.basename(path), n, expect, label))
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
