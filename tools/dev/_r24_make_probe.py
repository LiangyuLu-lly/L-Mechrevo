# -*- coding: utf-8 -*-
r"""r24: build installer-src\_probe\probe_browserchain_r24.iss by EXTRACTING the production
functions out of installer-src\install_script.iss (so the probe runs a byte-identical copy),
then assert the copy is identical after generation.

Run: python _r24_make_probe.py
"""
import hashlib, io, os, re, sys

sys.stdout.reconfigure(encoding='utf-8', errors='replace')

ISS = r'D:\openLM\installer-src\install_script.iss'
PROBE = r'D:\openLM\installer-src\_probe\probe_browserchain_r24.iss'
FUNCS = ['NetTierNote', 'NetResolveExplorerExe', 'NetResolveDefaultBrowserExe', 'OpenNet10DownloadPage']


def extract(text, name):
    """From 'procedure|function NAME(' (column 0) to the matching line 'end;' at column 0."""
    m = re.search(r'^(?:procedure|function) ' + name + r'\(', text, re.M)
    assert m, 'not found: ' + name
    start = m.start()
    m2 = re.search(r'^end;\r?\n', text[start:], re.M)
    assert m2, 'no closing end; for ' + name
    return text[start:start + m2.end()]


iss = io.open(ISS, encoding='utf-8', newline='').read()
blocks = [extract(iss, n) for n in FUNCS]
for n, b in zip(FUNCS, blocks):
    assert len(b) > 80, n
assert "ExpandConstant('{win}\\explorer.exe')" in blocks[1]
assert "ShellExec('open', ExplorerExe" in blocks[3]
assert blocks[3].count('ShellExec(') >= 3 and 'ShellExecAsOriginalUser(' in blocks[3]

HEAD = '''// ================================================================================================
// r24 只读探针：安装器「点下载 -> 打开浏览器」的档位链（含 r23 的 explorer 错路径回归断言）
// ================================================================================================
// 用法（产物一律落 %TEMP%，不在仓库里留裸 exe）：
//   ISCC.exe /O%TEMP%\\lm_r24_probe_out installer-src\\_probe\\probe_browserchain_r24.iss
//   %TEMP%\\lm_r24_probe_out\\browserchain-r24-probe.exe /VERYSILENT /SUPPRESSMSGBOXES
//   结果文件：installer-src\\_probe\\probe_browserchain_r24_result.txt
//
// ★ 本文件里 NetTierNote / NetResolveExplorerExe / NetResolveDefaultBrowserExe /
//   OpenNet10DownloadPage 四个函数是 installer-src\\install_script.iss 的【逐字副本】
//   （由 tools\\dev\\_r24_make_probe.py 抽取生成；生成后脚本会再次断言逐字节相同）。
// 影子声明：本地的 ShellExec / ShellExecAsOriginalUser 盖住内建同名函数
//   => 不真的拉起浏览器、不写剪贴板、不弹窗；
//   FileExists / ExpandConstant / RegQueryStringValue / GetEnv 等一律用【真的】（探针跑在真 Inno 里）。
// InitializeSetup 返回 False => 在任何安装动作之前退出（不写注册表 / 不复制文件 / 不建卸载项）。
[Setup]
AppName=LM Browser Chain Probe r24
AppId={{9E1B0C33-7A44-4F0B-9C77-0000000000B0}
AppVersion=0.0.1
AppPublisher=probe
DefaultDirName={tmp}\\LMBrowserChainProbeR24NeverUsed
DisableDirPage=yes
DisableProgramGroupPage=yes
Uninstallable=no
CreateUninstallRegKey=no
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
OutputDir={tmp}
OutputBaseFilename=browserchain-r24-probe
SetupLogging=no
Compression=none

[Code]
const
  // 与 install_script.iss 的 Net10DownloadDirectUrl 逐字一致
  Net10DownloadDirectUrl = 'https://aka.ms/dotnet/10.0/windowsdesktop-runtime-win-x64.exe';

var
  // 生产代码里的全局（在 iss 里声明于 NetTierNote 之前，所以这里也单独声明一份）
  NetBrowserTierReport: String;
  ProbeLog: String;
  CaseName: String;
  FailList: String;        // 逗号分隔的档位代码（'1','2','3','4a','4b'）=> 该档返回 False
  ThrowList: String;       // 逗号分隔的档位代码 => 该档抛异常
  Seq: String;             // 实际调用序列（如 '1,2,3'）
  Rec3File, Rec3Params: String;
  Rec4File, Rec4Params: String;
  Err1, Err2, Err3, Err4a, Err4b: Integer;
  Ok: Boolean;
  HasBrowser: Boolean;
  UrlCopies: Integer;      // 用 URL 直接调用 ShellExec 的次数（第1档必须只有 1 次）

procedure P(const S: String);
begin
  SaveStringToFile(ProbeLog, S + #13#10, True);
end;

// 生产代码里的 Log() 被引到探针日志 => 「每一档都写日志」这条要求被【观测到】（不是只看代码）
procedure Log(const S: String);
begin
  P('    [chain Log] ' + S);
end;

function B2S(B: Boolean): String;
begin
  if B then Result := 'True' else Result := 'False';
end;

function CK(const Name: String; Cond: Boolean): Boolean;
begin
  if Cond then P('  PASS ' + Name) else P('  FAIL ' + Name);
  Result := Cond;
end;

function InList(const Code, List: String): Boolean;
begin
  Result := (List <> '') and (Pos(',' + Code + ',', ',' + List + ',') > 0);
end;

procedure Mark(const Code: String);
begin
  if Seq <> '' then Seq := Seq + ',';
  Seq := Seq + Code;
end;

function IsUrl(const Filename: String): Boolean;
begin
  Result := LowerCase(Filename) = LowerCase(Net10DownloadDirectUrl);
end;

function IsExplorerPath(const Filename: String): Boolean;
begin
  Result := Pos('explorer.exe', LowerCase(Filename)) > 0;
end;

// ---- 影子 1：ShellExec（第1档=URL / 第3档=explorer / 第4b档=浏览器 exe）------------------
function ShellExec(const Verb, Filename, Params, WorkingDir: String; const ShowCmd: Integer;
  const Wait: TExecWait; var ResultCode: Integer): Boolean;
var
  Code: String;
begin
  if IsUrl(Filename) then
  begin
    Code := '1';
    UrlCopies := UrlCopies + 1;
  end
  else if IsExplorerPath(Filename) then
  begin
    Code := '3';
    Rec3File := Filename; Rec3Params := Params;
  end
  else
  begin
    Code := '4b';
    Rec4File := Filename; Rec4Params := Params;
  end;
  Mark(Code);
  P('  [档' + Code + ' ShellExec] verb=' + Verb + ' file=' + Filename + ' params=[' + Params + ']');
  if Code = '1' then
  begin
    ResultCode := 9001; Err1 := ResultCode;
  end
  else if Code = '3' then
  begin
    ResultCode := 9003; Err3 := ResultCode;
    P('  [档3] explorer 路径是否存在 = ' + B2S(FileExists(Filename)));
  end
  else
  begin
    ResultCode := 9005; Err4b := ResultCode;
  end;
  if InList(Code, ThrowList) then
    RaiseException('probe: 档' + Code + ' 故意抛异常');
  Result := not InList(Code, FailList);
end;

// ---- 影子 2：ShellExecAsOriginalUser（第2档=URL / 第4a档=浏览器 exe）----------------------
function ShellExecAsOriginalUser(const Verb, Filename, Params, WorkingDir: String; const ShowCmd: Integer;
  const Wait: TExecWait; var ResultCode: Integer): Boolean;
var
  Code: String;
begin
  if IsUrl(Filename) then
  begin
    Code := '2';
    ResultCode := 9002; Err2 := ResultCode;
  end
  else
  begin
    Code := '4a';
    ResultCode := 9004; Err4a := ResultCode;
    Rec4File := Filename; Rec4Params := Params;
  end;
  Mark(Code);
  P('  [档' + Code + ' ShellExecAsOriginalUser] verb=' + Verb + ' file=' + Filename + ' params=[' + Params + ']');
  if InList(Code, ThrowList) then
    RaiseException('probe: 档' + Code + ' 故意抛异常');
  Result := not InList(Code, FailList);
end;

// ================================================================================================
// ★★★ 以下四个函数是 install_script.iss 的【逐字副本】（由 _r24_make_probe.py 抽取）★★★
// ================================================================================================
'''

TAIL = '''
// ================================================================================================
// 用例
// ================================================================================================
procedure ResetCase(const Name, Fails, Throws: String);
begin
  CaseName := Name;
  FailList := Fails; ThrowList := Throws;
  Seq := ''; UrlCopies := 0;
  Rec3File := ''; Rec3Params := ''; Rec4File := ''; Rec4Params := '';
  Err1 := -1; Err2 := -1; Err3 := -1; Err4a := -1; Err4b := -1;
  P('');
  P('=== CASE ' + Name + ' (Fails=[' + Fails + '] Throws=[' + Throws + ']) ===');
end;

function InitializeSetup(): Boolean;
var
  R: Boolean;
  Ex, Br, Rep, AllSeq: String;
begin
  Result := False;
  ProbeLog := 'D:\\openLM\\installer-src\\_probe\\probe_browserchain_r24_result.txt';
  SaveStringToFile(ProbeLog, '=== browser chain probe r24 (installer OpenNet10DownloadPage) ===' + #13#10, False);
  Ok := True;

  // ---- 前置：路径解析（r24 新增的两个函数，用真的 FileExists 观察）----
  P('=== 0. 路径解析（真文件系统）===');
  Ex := NetResolveExplorerExe();
  Br := NetResolveDefaultBrowserExe();
  HasBrowser := (Br <> '');
  P('  NetResolveExplorerExe()       = [' + Ex + ']  存在=' + B2S((Ex <> '') and FileExists(Ex)));
  P('  NetResolveDefaultBrowserExe() = [' + Br + ']  存在=' + B2S((Br <> '') and FileExists(Br)));
  Ok := CK('0a explorer 解析结果非空', Ex <> '') and Ok;
  Ok := CK('0b ★ explorer 路径在磁盘上真实存在', (Ex <> '') and FileExists(Ex)) and Ok;
  Ok := CK('0c ★ explorer 路径不是 ...\\system32\\explorer.exe（r23 的错路径）',
    (Ex <> '') and (Pos('\\system32\\explorer.exe', LowerCase(Ex)) = 0)) and Ok;
  Ok := CK('0d explorer 取的是 {win}（C:\\Windows\\explorer.exe）',
    LowerCase(Ex) = LowerCase(ExpandConstant('{win}') + '\\explorer.exe')) and Ok;
  if HasBrowser then
    AllSeq := '1,2,3,4a,4b'
  else
    AllSeq := '1,2,3';

  // ---- A：第1档（主档）成功 => 立刻停 ----
  ResetCase('A 第1档（主档 ShellExec 直链）成功', '', '');
  R := OpenNet10DownloadPage();
  Ok := CK('A1 返回 True', R) and Ok;
  Ok := CK('A2 调用序列 = 1（成功即停，不碰后面几档）', Seq = '1') and Ok;
  Ok := CK('A3 只调用了一次 ShellExec(URL)', UrlCopies = 1) and Ok;
  P('  info 档1 errcode=' + IntToStr(Err1));

  // ---- B：第1档失败 -> 第2档（原用户身份）----
  ResetCase('B 第1档失败 -> 第2档 ShellExecAsOriginalUser', '1', '');
  R := OpenNet10DownloadPage();
  Ok := CK('B1 返回 True', R) and Ok;
  Ok := CK('B2 ★ 档位顺序 = 1,2（r24 把能用的主档排在最前）', Seq = '1,2') and Ok;
  P('  info 档1 errcode=' + IntToStr(Err1) + ' / 档2 errcode=' + IntToStr(Err2));

  // ---- C：第1/2档失败 -> 第3档 explorer（★ r24 的重点断言）----
  ResetCase('C 第1/2档失败 -> 第3档 explorer.exe', '1,2', '');
  R := OpenNet10DownloadPage();
  Ok := CK('C1 返回 True', R) and Ok;
  Ok := CK('C2 档位顺序 = 1,2,3', Seq = '1,2,3') and Ok;
  Ok := CK('C3 ★ 第3档传进去的 explorer 路径【真实存在】', (Rec3File <> '') and FileExists(Rec3File)) and Ok;
  Ok := CK('C4 ★ 不是 ...\\system32\\explorer.exe', Pos('\\system32\\explorer.exe', LowerCase(Rec3File)) = 0) and Ok;
  Ok := CK('C5 explorer 参数 = 带引号的直链（网址整体一个参数）', Rec3Params = '"' + Net10DownloadDirectUrl + '"') and Ok;
  P('  info 第3档 file=[' + Rec3File + '] params=[' + Rec3Params + '] errcode=' + IntToStr(Err3));

  // ---- D：第1/2/3档失败 -> 第4a档（显式默认浏览器，原用户身份）----
  if HasBrowser then
  begin
    ResetCase('D 第1/2/3档失败 -> 第4a档 显式默认浏览器', '1,2,3', '');
    R := OpenNet10DownloadPage();
    Ok := CK('D1 返回 True', R) and Ok;
    Ok := CK('D2 档位顺序 = 1,2,3,4a', Seq = '1,2,3,4a') and Ok;
    Ok := CK('D3 第4a档的浏览器 exe 真实存在', (Rec4File <> '') and FileExists(Rec4File)) and Ok;
    P('  info 第4a档 file=[' + Rec4File + '] params=[' + Rec4Params + '] errcode=' + IntToStr(Err4a));
  end
  else
    P('  [skip] CASE D：本机注册表里没有可用的默认浏览器 exe => 第4档整体跳过（生产代码同一条分支）');

  // ---- E：全部失败 => False + 逐档结果进报告（含 errcode）----
  ResetCase('E 全部档失败 => False + 报告含逐档 errcode', AllSeq, '');
  R := OpenNet10DownloadPage();
  Rep := NetBrowserTierReport;
  Ok := CK('E1 返回 False（调用方据此弹可见提示）', not R) and Ok;
  Ok := CK('E2 调用了全部档位：' + AllSeq, Seq = AllSeq) and Ok;
  Ok := CK('E3 报告含 第1档[失败] 且带 errcode 9001',
    (Pos('第1档[失败]', Rep) > 0) and (Pos('9001', Rep) > 0)) and Ok;
  Ok := CK('E4 报告含 第2档 / 第3档（带 errcode 9002 / 9003）',
    (Pos('第2档', Rep) > 0) and (Pos('第3档', Rep) > 0) and (Pos('9002', Rep) > 0) and (Pos('9003', Rep) > 0)) and Ok;
  if HasBrowser then
    Ok := CK('E5 报告含 第4a/第4b档（9004 / 9005）',
      (Pos('第4a档', Rep) > 0) and (Pos('第4b档', Rep) > 0) and (Pos('9004', Rep) > 0) and (Pos('9005', Rep) > 0)) and Ok;
  Ok := CK('E6 报告含第5档兜底说明（剪贴板 + 可见提示）', Pos('第5档[兜底]', Rep) > 0) and Ok;
  P('  info NetBrowserTierReport = ' + Rep);

  // ---- F：第2档抛异常（「以管理员身份运行」的典型现象）=> 必须继续走第3档 ----
  ResetCase('F 第2档抛异常 -> 仍继续第3档', '1', '2');
  R := OpenNet10DownloadPage();
  Ok := CK('F1 返回 True（第3档成功）', R) and Ok;
  Ok := CK('F2 序列 = 1,2,3（异常被 except 吃掉后继续）', Seq = '1,2,3') and Ok;

  P('');
  if Ok then P('RESULT: ALL-PASS') else P('RESULT: HAS-FAILURE');
  P('DONE-ALL-OK');
end;
'''

out = (HEAD.replace('procedure P(const S: String);', 'procedure PLog(const S: String);').replace("P('", "PLog('")
       + '\r\n'.join(b.replace('\r\n', '\n').rstrip('\n') for b in blocks)
       + '\r\n' + TAIL.replace("P('", "PLog('"))
io.open(PROBE, 'w', encoding='utf-8', newline='').write(out)
print('wrote %s  %d B' % (PROBE, os.path.getsize(PROBE)))

# ---- 断言：探针里的四个函数与生产 iss 逐字节相同 ----
pt = io.open(PROBE, encoding='utf-8', newline='').read()
for n in FUNCS:
    a = extract(iss, n).replace('\r\n', '\n')
    b = extract(pt, n).replace('\r\n', '\n')
    assert a == b, 'COPY MISMATCH: ' + n
    print('  IDENTICAL  %-28s %d B  sha %s' % (n, len(a), hashlib.sha256(a.encode('utf-8')).hexdigest()[:16].upper()))
print('ALL-DONE')
