# -*- coding: utf-8 -*-
r"""主 iss 定向补丁（它的 InitializeSetup 带 var 块）：全新安装清用户数据 / 升级保留"""
import io
import sys

F = r'D:\openLM\installer-src\install_script.iss'

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

RUN_COMMENT = '; 2026-10-05：全新安装前清空用户数据（升级时不执行，见 Check）。卸载全删、升级保留。\n'
RUN_LINE = ('Filename: "{sys}\\WindowsPowerShell\\v1.0\\powershell.exe"; '
            'Parameters: "-NoProfile -ExecutionPolicy Bypass -Command ""Remove-Item -LiteralPath '
            '(Join-Path $env:APPDATA ''MechrevoLite'') -Recurse -Force -ErrorAction SilentlyContinue"""; '
            'Flags: runhidden; Check: IsFreshInstallFlag\n')

OLD_INIT = ('function InitializeSetup(): Boolean;\n'
            'var\n'
            '  OfficialUninst: String;\n'
            'begin\n'
            '  Result := True;\n')

NEW_INIT = ('function InitializeSetup(): Boolean;\n'
            'var\n'
            '  OfficialUninst: String;\n'
            'begin\n'
            '  // 2026-10-05：旧版可执行文件还在 = 覆盖升级 -> 保留用户数据；否则视为全新安装\n'
            '  GFreshInstall := not FileExists(ExpandConstant(\'{app}\\L-Mechrevo.exe\'));\n'
            '  Result := True;\n')


def main():
    raw = io.open(F, 'rb').read()
    if 'GFreshInstall'.encode('utf-8') in raw:
        print('已应用过。')
        return 0
    crlf = b'\r\n' in raw
    text = raw.decode('utf-8-sig')
    if text.startswith('\ufeff'):
        text = text[1:]
    norm = text.replace('\r\n', '\n')

    checks = [
        ('function InitializeSetup(): Boolean;\n', 1),
        (OLD_INIT, 1),
        ('[Run]\n', 1),
    ]
    for a, want in checks:
        c = norm.count(a)
        print('  锚点命中 %d 次（期望 %d）: %s' % (c, want, a.split('\n')[0][:50]))
        if c != want:
            print('=== 校验失败，未写盘 ===')
            return 1
    if '--check' in sys.argv:
        return 0

    norm = norm.replace('function InitializeSetup(): Boolean;\n',
                        CODE_BLOCK + 'function InitializeSetup(): Boolean;\n', 1)
    norm = norm.replace(OLD_INIT, NEW_INIT, 1)
    norm = norm.replace('[Run]\n', '[Run]\n' + RUN_COMMENT + RUN_LINE, 1)

    out = norm.replace('\n', '\r\n') if crlf else norm
    io.open(F, 'w', encoding='utf-8-sig', newline='').write(out)
    print('  写入 ' + F)
    return 0


if __name__ == '__main__':
    sys.exit(main())
