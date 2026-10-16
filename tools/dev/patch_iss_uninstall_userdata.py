# -*- coding: utf-8 -*-
r"""
iss 改造：卸载全删用户数据、升级保留（欢迎界面在"卸载后全新安装"时重新出现）
================================================================================
用户要求：「卸载全删 升级保留」（欢迎界面 = FirstRunGuideForm，门控键 onboarding_version）

关键陷阱：Inno 升级时会**自动调用旧版卸载程序**。
  -> 不能把 {userappdata}\MechrevoLite 放进 [UninstallDelete]（那样升级也会被清空）
采用「双保险 + 确定性判定」：
  ① 卸载时（CurUninstallStepChanged / usPostUninstall）：
       `if not UninstallSilent() then DelTree({userappdata}\MechrevoLite)` ——
       用户手动卸载 = 非静默 -> 全删 ✅；升级触发的静默卸载 -> 保留 ✅
  ② 全新安装时（[Run] + Check）：用 `FileExists({app}\L-Mechrevo.exe)` 判定
       （InitializeSetup 在任何文件复制之前运行；旧版还在 = 升级 -> 保留；
         旧版已卸载 = 不存在 -> 全新安装 -> 删除残留用户数据，让首次引导重新出现 ✅）
       —— 该判定不依赖 AppId，两个 iss 通用 ✅

改两个文件：install_script.iss、install_script_nogcu.iss
  1) 在 [Code] 的 InitializeSetup 之前插入 var GFreshInstall + IsFreshInstallFlag() + CurUninstallStepChanged()
  2) 在 InitializeSetup 的 begin 之后插入 GFreshInstall := not FileExists(...)
  3) 在 [Run] 段首插入一条 runhidden 的 PowerShell 删除命令（Check: IsFreshInstallFlag）
"""
import io
import re
import sys

FILES = [r'D:\openLM\installer-src\install_script.iss',
         r'D:\openLM\installer-src\install_script_nogcu.iss']

CODE_BLOCK = '''// 2026-10-05：全新安装（旧版不存在）时清空用户数据目录，使「首次运行引导」重新出现；
// 覆盖升级时保留。判定不依赖 AppId：InitializeSetup 在任何文件复制之前运行，
// 所以 {app}\\L-Mechrevo.exe 是否存在就等价于"是否在升级"。
var
  GFreshInstall: Boolean;

function IsFreshInstallFlag(): Boolean;
begin
  Result := GFreshInstall;
end;

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
begin
  if CurUninstallStep = usPostUninstall then
  begin
    // 升级时 Setup 会以静默方式调用旧卸载程序 —— 那种情况必须保留用户数据。
    // 只有用户手动卸载（非静默）才全删。
    if not UninstallSilent() then
      DelTree(ExpandConstant('{userappdata}\\MechrevoLite'), True, True, True);
  end;
end;

'''

RUN_LINE = ('Filename: "{sys}\\WindowsPowerShell\\v1.0\\powershell.exe"; '
            'Parameters: "-NoProfile -ExecutionPolicy Bypass -Command ""Remove-Item -LiteralPath '
            '(Join-Path $env:APPDATA ''MechrevoLite'') -Recurse -Force -ErrorAction SilentlyContinue"""; '
            'Flags: runhidden; Check: IsFreshInstallFlag\n')

RUN_COMMENT = '; 2026-10-05：全新安装前清空用户数据（升级时不执行，见 Check）。卸载全删、升级保留。\n'


def patch(path):
    raw = io.open(path, 'rb').read()
    if 'GFreshInstall'.encode('utf-8') in raw:
        print('  已应用过: ' + path)
        return 0
    crlf = b'\r\n' in raw
    text = raw.decode('utf-8-sig')
    if text.startswith('\ufeff'):
        text = text[1:]
    norm = text.replace('\r\n', '\n')

    # --- 1) [Code]：在 InitializeSetup 之前插入代码块 ---
    a1 = 'function InitializeSetup(): Boolean;\n'
    if norm.count(a1) != 1:
        print('  !! InitializeSetup 锚点命中 %d 次' % norm.count(a1))
        return 1
    norm = norm.replace(a1, CODE_BLOCK + a1, 1)

    # --- 2) InitializeSetup 开头赋初值 ---
    m = re.search(r'(function InitializeSetup\(\): Boolean;\nbegin\n)', norm)
    if not m:
        print('  !! 找不到 InitializeSetup 的 begin')
        return 1
    inject = ('begin\n'
              '  // 2026-10-05：旧版可执行文件还在 = 覆盖升级 -> 保留用户数据；否则视为全新安装\n'
              '  GFreshInstall := not FileExists(ExpandConstant(\'{app}\\L-Mechrevo.exe\'));\n')
    norm = norm[:m.start(1)] + inject + norm[m.end(1):]

    # --- 3) [Run] 段首插入删除命令 ---
    a3 = '[Run]\n'
    if norm.count(a3) != 1:
        print('  !! [Run] 锚点命中 %d 次' % norm.count(a3))
        return 1
    norm = norm.replace(a3, '[Run]\n' + RUN_COMMENT + RUN_LINE, 1)

    out = norm.replace('\n', '\r\n') if crlf else norm
    io.open(path, 'w', encoding='utf-8-sig', newline='').write(out)
    print('  写入 ' + path)
    return 0


def main():
    rc = 0
    for f in FILES:
        print(f)
        rc |= patch(f)
    return rc


if __name__ == '__main__':
    sys.exit(main())
