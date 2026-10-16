# -*- coding: utf-8 -*-
r"""r25 probe builder.

Builds installer-src\_probe\probe_netdl_r25.iss by EXTRACTING the new r25 production
code out of installer-src\install_script.iss (byte-identical copy) and shadowing every
side effect that must not really happen.

⛔⛔ 探针只允许用【死地址】（http://127.0.0.1:9/…）：本脚本把生产里的两条真直链
     （aka.ms / builds.dotnet.microsoft.com）替换成死地址，并断言替换后不含任何真实主机名。
     下载函数在探针里被整体替身掉 => 连 DNS/连接都不会发生。

Run: py -3 tools\dev\_r25_make_probe.py
"""
import hashlib
import io
import os
import re
import sys

sys.stdout.reconfigure(encoding='utf-8', errors='replace')

ISS = r'D:\openLM\installer-src\install_script.iss'
PROBE = r'D:\openLM\installer-src\_probe\probe_netdl_r25.iss'

FUNCS = ['CopyNet10UrlToClipboard', 'NetDlRunInstaller', 'NetDlTryInstallRuntime',
         'HandleNet10MissingAnswer', 'PromptNet10Missing', 'UpdateReadyMemo']

DEAD_PRIMARY = 'http://127.0.0.1:9/lm-net10-PRIMARY-dead.exe'
DEAD_FALLBACK = 'http://127.0.0.1:9/lm-net10-FALLBACK-dead.exe'
DEAD_BASENAME = 'lm-net10-probe-dead-runtime.exe'

REAL_HOSTS = ('aka.ms', 'builds.dotnet.microsoft.com', 'dotnet.microsoft.com',
              'jrsoftware.org', 'https://')


def extract(text, name):
    """'procedure|function NAME(' (column 0) .. first 'end;' at column 0 (same rule as r24)."""
    m = re.search(r'^(?:procedure|function) ' + name + r'\(', text, re.M)
    assert m, 'not found: ' + name
    start = m.start()
    m2 = re.search(r'^end;\r?\n', text[start:], re.M)
    assert m2, 'no closing end; for ' + name
    return text[start:start + m2.end()]


def extract_range(text, start_anchor, end_anchor):
    i = text.index(start_anchor)
    j = text.index(end_anchor, i) + len(end_anchor)
    j = text.index('\r\n', j) + 2
    return text[i:j]


def const_str(iss, name):
    m = re.search(r'^  ' + name + r" = '([^']*)';", iss, re.M)
    assert m, 'const not found: ' + name
    return m.group(1)


def const_int(iss, name):
    m = re.search(r'^  ' + name + r' = (\d+);', iss, re.M)
    assert m, 'const not found: ' + name
    return int(m.group(1))


iss = io.open(ISS, encoding='utf-8', newline='').read()

blocks = {}
for n in FUNCS:
    b = extract(iss, n)
    assert len(b) > 120, n
    blocks[n] = b
blocks['clipboard_externals'] = ''  # r25：生产起改用 clip.exe，不再有剪贴板 API 声明（探针自己声明读侧）
assert 'WinOpenClipboard' not in iss, '生产里还有旧的剪贴板 API 声明'
assert 'clip < ' in blocks['CopyNet10UrlToClipboard'], '生产没走 clip.exe'
assert 'WinGlobalLock' not in blocks['CopyNet10UrlToClipboard']

# --- 生产常量（真值） ---
args = const_str(iss, 'NetDlInstallArgs')
maxwait = const_int(iss, 'NetDlMaxWaitSeconds')
pollms = const_int(iss, 'NetDlPollMs')
donefile = const_str(iss, 'NetDlDoneFileName')
clipfile = const_str(iss, 'NetDlClipFileName')
rt_name = const_str(iss, 'Net10RuntimeName')
rt_hint = const_str(iss, 'Net10RuntimeVerHint')
real_primary = const_str(iss, 'Net10DownloadDirectUrl')
real_fallback = const_str(iss, 'Net10DownloadFallbackUrl')

# 就绪页那条【真实文案】（从生产 [CustomMessages] 抽出来注入探针的 ExpandMsg 替身）
_m = re.search(r'^chinesesimplified\.RuntimeReadyMemo=(.*)$', iss, re.M)
assert _m, 'RuntimeReadyMemo 文案没找到'
ready_memo_msg = _m.group(1).rstrip('\r\n')
assert '附加任务' in ready_memo_msg and '%1' in ready_memo_msg and '%2' in ready_memo_msg, ready_memo_msg
assert "'" not in ready_memo_msg, '文案里有单引号，注入探针要转义'
ready_memo_msg = "'" + ready_memo_msg + "'"

assert args == '/install /repair /passive /norestart', args
assert maxwait == 600, maxwait
assert 'aka.ms' in real_primary and 'builds.dotnet.microsoft.com' in real_fallback
for dead in (DEAD_PRIMARY, DEAD_FALLBACK, DEAD_BASENAME):
    assert dead not in iss, 'dead placeholder leaked into production: ' + dead
# 结构断言（抽到的块里必须真有这些行为）
assert "DownloadTemporaryFile" in iss
assert "NetDownloadPage.Add(Url, BaseName, '')" in iss
assert '!ERRORLEVEL!' in blocks['NetDlRunInstaller']
assert "/v:on /c" in blocks['NetDlRunInstaller']
assert 'taskkill /IM ' in blocks['NetDlRunInstaller']
assert 'NetDlMaxWaitSec' in blocks['NetDlRunInstaller']
assert "IsNetDlSilentMode() then" in blocks['NetDlTryInstallRuntime']
assert 'Net10DownloadDirectUrl' in blocks['NetDlTryInstallRuntime']
assert 'Net10DownloadFallbackUrl' in blocks['NetDlTryInstallRuntime']
assert 'if Answer = IDNO then' in blocks['HandleNet10MissingAnswer']
assert 'OpenNet10DownloadPage()' in blocks['HandleNet10MissingAnswer']
assert 'AskNet10MissingDialog()' in blocks['PromptNet10Missing']
assert 'NetDlTryInstallRuntime()' in blocks['PromptNet10Missing']
assert 'CopyNet10UrlToClipboard(' in blocks['PromptNet10Missing']
assert 'HandleNet10MissingAnswer(Answer, Copied)' in blocks['PromptNet10Missing']
assert 'while not Ready do' in blocks['PromptNet10Missing']
assert 'IsNetDlSilentMode() then' in blocks['PromptNet10Missing']
assert "ExpandMsg('RuntimeReadyMemo')" in blocks['UpdateReadyMemo']
assert 'IsNetDesktop10Installed()' in blocks['UpdateReadyMemo']

HEAD = '''// ================================================================================================
// r25 只读探针：安装器「自己下载 + 静默安装 .NET 10 桌面运行时」这条路 + 失败才弹选择框
// ================================================================================================
// 用法（产物一律落 %TEMP%，不在仓库里留裸 exe）：
//   ISCC.exe /O%TEMP%\\lm_r25_probe_out installer-src\\_probe\\probe_netdl_r25.iss
//   %TEMP%\\lm_r25_probe_out\\netdl-r25-probe.exe /VERYSILENT /SUPPRESSMSGBOXES
//   结果文件：installer-src\\_probe\\probe_netdl_r25_result.txt
//
// ★ 下面这些函数是 installer-src\\install_script.iss 的【逐字副本】（由 tools\\dev\\_r25_make_probe.py
//   抽取生成；生成后会再断言逐字节相同）：
//     CopyNet10UrlToClipboard / NetDlRunInstaller / NetDlTryInstallRuntime /
//     HandleNet10MissingAnswer / PromptNet10Missing / UpdateReadyMemo
//   ⛔ 没有副本、被【替身】掉的（写不出真对象或会真联网/真弹框）：
//     NetDlDownloadToTmp（TDownloadWizardPage 是内建对象，脚本里构造不出替身，只能整个替身）
//     AskNet10MissingDialog（TaskDialogMsgBox 不能被同名影子声明 —— r24 实测）
//     IsNetDlSilentMode（WizardSilent 不能被影子声明 => 生产为此专门留了这一层包装）
//     IsNetDesktop10Installed / OpenNet10DownloadPage / Exec / Log
// ★⛔ 两条真直链在这里被换成【死地址】 http://127.0.0.1:9/…：探针【绝不发起真实下载】，
//   下载替身还会拒绝任何非 127.0.0.1:9 的地址（见到就记 FAIL）。
// ★ InitializeSetup 返回 False => 在任何安装动作之前退出（不写注册表 / 不复制文件 / 不建卸载项）。
[Setup]
AppName=LM Net10 AutoDownload Probe r25
AppId={{9E1B0C33-7A44-4F0B-9C77-0000000000C1}
AppVersion=0.0.1
AppPublisher=probe
DefaultDirName={tmp}\\LMNetDlProbeR25NeverUsed
DisableDirPage=yes
DisableProgramGroupPage=yes
Uninstallable=no
CreateUninstallRegKey=no
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
OutputDir={tmp}
OutputBaseFilename=netdl-r25-probe
SetupLogging=yes
Compression=none

[Code]
const
  // ⛔ 死地址（生产里这两条是 aka.ms / builds.dotnet.microsoft.com 的真直链）
  Net10DownloadDirectUrl = '%(dead_primary)s';
  Net10DownloadFallbackUrl = '%(dead_fallback)s';
  NetDlBaseName = '%(dead_basename)s';
  // 以下与生产【逐字一致】（生成脚本已断言）
  NetDlInstallArgs = '%(args)s';
  NetDlMaxWaitSeconds = %(maxwait)d;
  NetDlPollMs = %(pollms)d;
  NetDlDoneFileName = '%(donefile)s';
  NetDlClipFileName = '%(clipfile)s';
  Net10RuntimeName = '%(rt_name)s';
  Net10RuntimeVerHint = '%(rt_hint)s';

var
  NetDlMaxWaitSec: Integer;      // 生产变量（探针把超时用例调小到 3 秒）
  // 生产全局（HandleNet10MissingAnswer 的可见提示文案里用到；在 iss 里声明于 NetTierNote 之前）
  NetBrowserTierReport: String;
  // ---- 探针自己的观测变量 ----
  ProbeLog: String;
  CaseName: String;
  Ok: Boolean;
  ProbeSilent: Boolean;          // IsNetDlSilentMode 的脚本值
  ProbeRuntimePresent: Boolean;  // IsNetDesktop10Installed 的脚本值
  ProbeDlResults: String;        // 每次下载返回什么：'s'=成功 / 'f'=失败（按序消费）
  ProbeExecMode: String;         // 'ok' / 'rc1218' / 'never'
  AskAnswers: String;            // 弹框答案序列：'R'=重试 / 'B'=用浏览器 / 'L'=稍后
  AskIdx, DlIdx: Integer;
  DlCalls, AskCount, ExecInstallCalls, TaskkillCalls, OpenChainCalls: Integer;
  DlUrls, ExecArgs, TaskkillParams, ChainUrls: String;
  ClipCmdSeen: Boolean;          // 生产有没有真的走「clip.exe」那条写剪贴板的路
  ClipCmd: String;

procedure PLog(const S: String);
begin
  SaveStringToFile(ProbeLog, S + #13#10, True);
end;

// 生产代码里的 Log() 在这里被引到探针日志 => 「每一步都写日志」这条要求被【观测到】
procedure Log(const S: String);
begin
  PLog('    [prod Log] ' + S);
end;

function B2S(B: Boolean): String;
begin
  if B then Result := 'True' else Result := 'False';
end;

// ExpandMsg 在探针里也用替身；就绪页那条【真实文案】由生成脚本从生产的 [CustomMessages] 抽出来注入，
// 所以 G 组断言验的是「真文案 + 真替换」。
function ExpandMsg(Const Name: String): String;
var
  S: String;
begin
  if Name = 'RuntimeReadyMemo' then
    S := @@ready_memo_msg@@
  else
    S := 'PROBE-MSG:' + Name;
  StringChange(S, '{break}', #13#10);
  Result := S;
end;

function CK(const Name: String; Cond: Boolean): Boolean;
begin
  if Cond then PLog('  PASS ' + Name) else PLog('  FAIL ' + Name);
  Result := Cond;
end;

function Tail(const S: String): String;
begin
  if Length(S) <= 220 then Result := S else Result := Copy(S, Length(S) - 219, 220);
end;

// ================================ 探针 import（读侧剪贴板 + clip + 计时）================================
function WinGetClipboardData(uFormat: UINT): THandle;
  external 'GetClipboardData@user32.dll stdcall';
function WinOpenClipboardRO(hWndNewOwner: HWND): Boolean;
  external 'OpenClipboard@user32.dll stdcall';
function WinCloseClipboardRO(): Boolean;
  external 'CloseClipboard@user32.dll stdcall';
function WinGlobalLockRO(hMem: THandle): PAnsiChar;
  external 'GlobalLock@kernel32.dll stdcall';
function WinGlobalUnlockRO(hMem: THandle): BOOL;
  external 'GlobalUnlock@kernel32.dll stdcall';
// clip.exe 是探针里唯一「真跑」的写剪贴板方式（生产也用 clip.exe）
function WinExec(lpCmdLine: PAnsiChar; uCmdShow: UINT): UINT;
  external 'WinExec@kernel32.dll stdcall';
// ⚠️ Inno 6.7.1 的 Pascal Script 里没有 GetTickCount => 自己 import（只为量超时用例耗时）
function WinGetTickCount(): DWORD;
  external 'GetTickCount@kernel32.dll stdcall';

// ================================ 替身（影子）================================
function IsNetDlSilentMode(): Boolean;
begin
  Result := ProbeSilent;
end;

function IsNetDesktop10Installed(): Boolean;
begin
  Result := ProbeRuntimePresent;
end;

// r24 那条五档链在本探针里只验【接线】：真链路由 probe_browserchain_r24.iss 逐档断言
function OpenNet10DownloadPage(): Boolean;
begin
  OpenChainCalls := OpenChainCalls + 1;
  ChainUrls := ChainUrls + '|' + Net10DownloadDirectUrl;
  PLog('  [五档链替身] OpenNet10DownloadPage() 被调用（第 ' + IntToStr(OpenChainCalls) + ' 次）');
  Result := True;
end;

function AskNet10MissingDialog(): Integer;
var
  C: String;
begin
  AskCount := AskCount + 1;
  if AskAnswers = '' then
    Result := IDCANCEL
  else
  begin
    AskIdx := AskIdx + 1;
    C := Copy(AskAnswers, AskIdx, 1);
    if C = 'R' then Result := IDYES
    else if C = 'B' then Result := IDNO
    else Result := IDCANCEL;
  end;
  PLog('  [选择框替身] 第 ' + IntToStr(AskCount) + ' 次弹框 -> 返回 ' + IntToStr(Result));
end;

function NetDlDownloadToTmp(const Url, BaseName: String; var ErrMsg: String): Boolean;
var
  C: String;
begin
  DlCalls := DlCalls + 1;
  DlUrls := DlUrls + '|' + Url;
  if Pos('http://127.0.0.1:9/', Url) <> 1 then
  begin
    PLog('  [下载替身] ★★ 非死地址 —— 探针拒绝并记 FAIL：' + Url);
    ErrMsg := 'probe: 非死地址';
    Result := False;
    Exit;
  end;
  DlIdx := DlIdx + 1;
  C := Copy(ProbeDlResults, DlIdx, 1);
  if C = 's' then
  begin
    SaveStringToFile(ExpandConstant('{tmp}\\' + NetDlBaseName), 'PROBE-FAKE-RUNTIME-SETUP', False);
    Result := True;
    PLog('  [下载替身] 第 ' + IntToStr(DlCalls) + ' 次（死地址）-> 模拟成功 url=' + Url);
  end
  else
  begin
    ErrMsg := 'probe: 死地址模拟下载失败';
    Result := False;
    PLog('  [下载替身] 第 ' + IntToStr(DlCalls) + ' 次（死地址）-> 模拟失败 url=' + Url);
  end;
end;

// Exec 替身：① 安装器调用 => 按 ProbeExecMode 写/不写退出码标记文件；② taskkill => 只记账
function Exec(const Filename, Params, WorkingDir: String; const ShowCmd: Integer;
  const Wait: TExecWait; var ResultCode: Integer): Boolean;
var
  DoneFile: String;
  U2: UINT;
begin
  Result := True;
  ResultCode := 0;
  if Pos('taskkill', LowerCase(Params)) > 0 then
  begin
    TaskkillCalls := TaskkillCalls + 1;
    TaskkillParams := TaskkillParams + '|' + Params;
    PLog('  [Exec 替身] taskkill：' + Params);
    Exit;
  end;
  if Pos('clip', LowerCase(Params)) > 0 then
  begin
    // 生产的写剪贴板 = `cmd /c clip < 文件`：探针【真跑一次】（无害：只改剪贴板内容），
    // 这样「剪贴板里到底有没有那条直链」可以在探针里读回来断言。
    ClipCmdSeen := True;
    ClipCmd := Params;
    PLog('  [Exec 替身] 写剪贴板命令：' + Params);
    U2 := WinExec('cmd.exe ' + Params, 0);
    Sleep(400);
    Exit;
  end;
  if Pos(LowerCase(NetDlBaseName), LowerCase(Params)) > 0 then
  begin
    ExecInstallCalls := ExecInstallCalls + 1;
    ExecArgs := ExecArgs + '|' + Params;
    PLog('  [Exec 替身] 安装器：file=' + Filename + ' params=' + Params);
    DoneFile := ExpandConstant('{tmp}\\' + NetDlDoneFileName);
    if ProbeExecMode = 'ok' then
      SaveStringToFile(DoneFile, '0', False)
    else if Copy(ProbeExecMode, 1, 2) = 'rc' then
      SaveStringToFile(DoneFile, Copy(ProbeExecMode, 3, 8), False);
    // 'never' => 什么都不写 => 生产的超时分支必然触发
    Exit;
  end;
  PLog('  [Exec 替身] 其它：file=' + Filename + ' params=' + Params);
end;

'''

TAIL = '''
// 读回剪贴板正文（探针专用：验证「直链真的进了剪贴板」）。
//   ⚠️ 只读 CF_TEXT(1)：clip.exe 放的是 CF_UNICODETEXT，但 Windows 会按需合成 CF_TEXT 给它。
function ReadClipboardText(): String;
var
  H: THandle;
  P: AnsiString;
begin
  Result := '';
  if not WinOpenClipboardRO(0) then
  begin
    PLog('  [剪贴板] OpenClipboard 失败（被占用？）');
    Exit;
  end;
  try
    H := WinGetClipboardData(1);
    if H = 0 then
    begin
      PLog('  [剪贴板] GetClipboardData(CF_TEXT) = 0 => 拿不到文本');
      Exit;
    end;
    P := WinGlobalLockRO(H);
    if P = '' then
    begin
      PLog('  [剪贴板] GlobalLock 失败/内容为空');
      Exit;
    end;
    Result := P;
    WinGlobalUnlockRO(H);
  finally
    WinCloseClipboardRO();
  end;
end;

procedure ResetCase(const Name, DlResults, ExecMode, Answers: String);
begin
  CaseName := Name;
  ProbeDlResults := DlResults;
  ProbeExecMode := ExecMode;
  AskAnswers := Answers;
  AskIdx := 0;
  DlIdx := 0;
  DlCalls := 0;
  AskCount := 0;
  ExecInstallCalls := 0;
  TaskkillCalls := 0;
  OpenChainCalls := 0;
  DlUrls := '';
  ExecArgs := '';
  TaskkillParams := '';
  ChainUrls := '';
  ClipCmdSeen := False;
  ClipCmd := '';
  NetDlMaxWaitSec := %(maxwait)d;
  PLog('');
  PLog('=== CASE ' + Name + ' ===');
end;

function InitializeSetup(): Boolean;
var
  R, Ready: Boolean;
  Clip, Memo, Memo2: String;
  T0, T1: Cardinal;
  S: String;
begin
  Result := False;
  ProbeLog := 'D:\\openLM\\installer-src\\_probe\\probe_netdl_r25_result.txt';
  SaveStringToFile(ProbeLog,
    '=== netdl probe r25（安装器自动下载 + 静默安装 .NET 10 桌面运行时；全部用死地址）==='
    + #13#10, False);
  Ok := True;

  // ---- 0. 前置：常量对齐 + 死地址 ----
  PLog('=== 0. 前置断言 ===');
  Ok := CK('0a 主直链是死地址（127.0.0.1:9）', Pos('http://127.0.0.1:9/', Net10DownloadDirectUrl) = 1) and Ok;
  Ok := CK('0b 备用直链是死地址', Pos('http://127.0.0.1:9/', Net10DownloadFallbackUrl) = 1) and Ok;
  Ok := CK('0c 主/备是两条【不同】的死地址（自动重试真的换了地址）',
    (Net10DownloadDirectUrl <> Net10DownloadFallbackUrl)
      and (Pos('http://127.0.0.1:9/', Net10DownloadFallbackUrl) = 1)) and Ok;
  Ok := CK('0d 安装参数 = /install /repair /passive /norestart', Net10InstallArgsOk()) and Ok;
  Ok := CK('0e 等待上限默认 = 600 秒（10 分钟）', NetDlMaxWaitSeconds = 600) and Ok;
  PLog('  info 直链主=[' + Net10DownloadDirectUrl + '] 备用=[' + Net10DownloadFallbackUrl + ']');

  // ---- A. 静默安装：不下载 / 不弹框 / 直接继续 ----
  ResetCase('A 静默安装（IsNetDlSilentMode=True）', 'ff', 'never', '');
  ProbeSilent := True;
  R := NetDlTryInstallRuntime();
  Ok := CK('A1 返回 False（没装，交给调用方；调用方在静默下也不弹框）', not R) and Ok;
  Ok := CK('A2 ★ 一次下载都没发起', DlCalls = 0) and Ok;
  Ok := CK('A3 ★ 一次 Exec 都没有（不跑安装器）', ExecInstallCalls = 0) and Ok;
  PromptNet10Missing();
  Ok := CK('A4 ★ 直接调用 PromptNet10Missing：也没弹框', AskCount = 0) and Ok;
  Ok := CK('A5 仍然没下载', DlCalls = 0) and Ok;

  // ---- B. 两次下载都失败 => 弹选择框（用户选「稍后」）----
  ProbeSilent := False;
  ResetCase('B 主/备直链都失败 => 弹框（选稍后）', 'ff', 'never', 'L');
  PromptNet10Missing();
  Ok := CK('B1 ★ 走到了下载这条路：共 2 次（主 + 备用）', DlCalls = 2) and Ok;
  Ok := CK('B2 ★ 第 1 次用主直链', Pos('|' + Net10DownloadDirectUrl, DlUrls) > 0) and Ok;
  Ok := CK('B3 ★ 第 2 次用备用直链（自动重试）', Pos('|' + Net10DownloadFallbackUrl, DlUrls) > 0) and Ok;
  Ok := CK('B4 顺序 = 主 -> 备用', DlUrls = '|' + Net10DownloadDirectUrl + '|' + Net10DownloadFallbackUrl) and Ok;
  Ok := CK('B5 ★ 失败后走到了选择框（恰好 1 次）', AskCount = 1) and Ok;
  Ok := CK('B6 没进安装器（下载都没成功）', ExecInstallCalls = 0) and Ok;
  Clip := ReadClipboardText();
  Ok := CK('B7 ★★ 剪贴板里就是那条直链（死地址，逐字节相同）', Clip = Net10DownloadDirectUrl) and Ok;
  Ok := CK('B7b ★ 生产走的是「clip.exe」那条路（⛔ 不再是坏的 GlobalLock 写法）',
    ClipCmdSeen and (Pos('clip <', ClipCmd) > 0)) and Ok;
  PLog('  info 写剪贴板命令=[' + ClipCmd + ']');
  PLog('  info 剪贴板=[' + Clip + ']');
  Ok := CK('B8 ★ 五档链没被调用（选的是「稍后」）', OpenChainCalls = 0) and Ok;

  // ---- C. 用户点「重试下载」=> 再走一遍（这次仍失败，再选稍后）----
  ResetCase('C 用户点「重试下载」-> 再走一遍（2+2 次）', 'ffff', 'never', 'RL');
  PromptNet10Missing();
  Ok := CK('C1 ★ 下载共 4 次（两轮各 2 次）', DlCalls = 4) and Ok;
  Ok := CK('C2 ★ 弹框 2 次（第一轮失败 + 重试后仍失败）', AskCount = 2) and Ok;
  Ok := CK('C3 ★ 两轮都是 主 -> 备用',
    DlUrls = '|' + Net10DownloadDirectUrl + '|' + Net10DownloadFallbackUrl
      + '|' + Net10DownloadDirectUrl + '|' + Net10DownloadFallbackUrl) and Ok;

  // ---- D. 下载 + 安装都成功 => ⛔ 不弹框 ----
  ResetCase('D 下载成功 + 安装 rc=0 => 不弹框', 's', 'ok', '');
  PromptNet10Missing();
  Ok := CK('D1 ★ 只下了一次（成功即停，不用备用）', DlCalls = 1) and Ok;
  Ok := CK('D2 ★ 没弹框', AskCount = 0) and Ok;
  Ok := CK('D3 ★ 调起了安装器 1 次', ExecInstallCalls = 1) and Ok;
  Ok := CK('D4 ★ 安装参数含 /install /repair /passive /norestart',
    Pos('/install /repair /passive /norestart', ExecArgs) > 0) and Ok;
  Ok := CK('D5 ★ 退出码用 !ERRORLEVEL! 延迟展开写进标记文件',
    (Pos('!ERRORLEVEL!', ExecArgs) > 0) and (Pos(NetDlDoneFileName, ExecArgs) > 0)) and Ok;
  Ok := CK('D6 没 taskkill', TaskkillCalls = 0) and Ok;
  Ok := CK('D7 没走五档链', OpenChainCalls = 0) and Ok;

  // ---- E. 下载成功但安装器返回非 0 => 弹框（不重下）----
  ResetCase('E 下载成功 + 安装 rc=1218 => 弹框', 's', 'rc1218', 'L');
  PromptNet10Missing();
  Ok := CK('E1 ★ 弹框了（安装失败也算失败）', AskCount = 1) and Ok;
  Ok := CK('E2 ★ 只下载 1 次（安装失败不重下同一个包）', DlCalls = 1) and Ok;
  Ok := CK('E3 调起了安装器 1 次', ExecInstallCalls = 1) and Ok;
  Ok := CK('E4 没 taskkill（是退出码非 0，不是超时）', TaskkillCalls = 0) and Ok;

  // ---- F. 安装器不结束 => 到上限就 taskkill（上限缩小到 3 秒）----
  ResetCase('F 安装超时（上限调小到 3 秒）=> taskkill + 弹框', 's', 'never', 'L');
  NetDlMaxWaitSec := 3;
  T0 := WinGetTickCount();
  PromptNet10Missing();
  T1 := WinGetTickCount();
  Ok := CK('F1 ★ 超时后调了 taskkill（按下载文件名杀安装器）', TaskkillCalls = 1) and Ok;
  Ok := CK('F2 ★ taskkill 用的是我们下载的文件名', Pos(NetDlBaseName, TaskkillParams) > 0) and Ok;
  Ok := CK('F3 ★ 超时后仍然弹框（没中止安装）', AskCount = 1) and Ok;
  Ok := CK('F4 ★ 确实等到了上限附近（>= 3 秒）', (T1 - T0) >= 3000) and Ok;
  PLog('  info 超时用例耗时 = ' + IntToStr(T1 - T0) + ' ms（上限 ' + IntToStr(NetDlMaxWaitSec) + 's）');

  // ---- G. 就绪页那一行（UpdateReadyMemo）----
  PLog('');
  PLog('=== G. 就绪页「附加任务」行 ===');
  ProbeRuntimePresent := False;
  Memo := UpdateReadyMemo('    ', #13#10, 'USERINFO', 'DIRINFO', '', '', '', 'TASKSINFO');
  Ok := CK('G1 ★ 缺运行时 => 追加了「附加任务」行', Pos('附加任务', Memo) > 0) and Ok;
  Ok := CK('G2 ★ 行里带 10.0.x（.NET 10，不是 LLT 的 8）', Pos('10.0.x', Memo) > 0) and Ok;
  Ok := CK('G3 ★ 行里带运行时全名 Microsoft Windows Desktop Runtime 10.0', Pos('Microsoft Windows Desktop Runtime 10.0', Memo) > 0) and Ok;
  Ok := CK('G4 ★ 默认内容一段都没丢（用户信息/目录/任务）',
    (Pos('USERINFO', Memo) > 0) and (Pos('DIRINFO', Memo) > 0) and (Pos('TASKSINFO', Memo) > 0)) and Ok;
  ProbeRuntimePresent := True;
  Memo2 := UpdateReadyMemo('    ', #13#10, 'USERINFO', 'DIRINFO', '', '', '', 'TASKSINFO');
  Ok := CK('G5 ★ 运行时在 => 不追加那一行', Pos('附加任务', Memo2) = 0) and Ok;
  Ok := CK('G6 运行时在时默认内容也在', (Pos('DIRINFO', Memo2) > 0) and (Pos('TASKSINFO', Memo2) > 0)) and Ok;
  PLog('  info Memo(缺) = ' + Memo);

  // ---- H. 「用浏览器下载」=> 接线到 r24 五档链 ----
  ResetCase('H 选「用浏览器下载」=> 调用五档链', 'ff', 'never', 'B');
  PromptNet10Missing();
  Ok := CK('H1 ★ 五档链被调用 1 次', OpenChainCalls = 1) and Ok;
  Ok := CK('H2 五档链拿到的是同一条直链', ChainUrls = '|' + Net10DownloadDirectUrl) and Ok;
  Ok := CK('H3 弹框 1 次', AskCount = 1) and Ok;

  // ---- I. HandleNet10MissingAnswer 的答案码映射 ----
  PLog('');
  PLog('=== I. HandleNet10MissingAnswer 答案码映射 ===');
  ResetCase('I 答案码', '', '', '');
  R := HandleNet10MissingAnswer(IDYES, True);
  Ok := CK('I1 IDYES(重试) 不在 Handle 里动五档链', OpenChainCalls = 0) and Ok;
  Ok := CK('I2 IDYES 原样带回 CopiedIn=True', R) and Ok;
  R := HandleNet10MissingAnswer(IDCANCEL, False);
  Ok := CK('I3 IDCANCEL(稍后) 不动五档链且原样带回 False',
    (OpenChainCalls = 0) and (not R)) and Ok;

  PLog('');
  if Ok then PLog('RESULT: ALL-PASS') else PLog('RESULT: HAS-FAILURE');
  PLog('DONE-ALL-OK');
end;
'''


def net10_args_ok_fn():
    """生产里没有这个函数；探针用一个小助手断言安装参数常量（避免在 InitializeSetup 里写死字符串）。"""
    return ('function Net10InstallArgsOk(): Boolean;\n'
            'begin\n'
            "  Result := NetDlInstallArgs = '/install /repair /passive /norestart';\n"
            'end;\n\n')


body = HEAD
# ⚠️ 用 replace 而不是 %-格式化：HEAD 里有 %TEMP% 这种字面量
for _k, _v in (('%(dead_primary)s', DEAD_PRIMARY), ('%(dead_fallback)s', DEAD_FALLBACK),
               ('%(dead_basename)s', DEAD_BASENAME), ('%(args)s', args),
               ('%(maxwait)d', str(maxwait)), ('%(pollms)d', str(pollms)),
               ('%(donefile)s', donefile), ('%(clipfile)s', clipfile), ('%(rt_name)s', rt_name),
               ('%(rt_hint)s', rt_hint), ('@@ready_memo_msg@@', ready_memo_msg)):
    assert _k in body, _k
    body = body.replace(_k, _v)
for _k in ('%(dead_primary)s', '%(args)s', '%(rt_name)s', '%(maxwait)d'):
    assert _k not in body, _k

tail = TAIL.replace('%(maxwait)d', str(maxwait))
assert '%(maxwait)d' not in tail
# 小助手要放在用它的 InitializeSetup 之前、也在被替身/副本之后（它只用常量）
tail = net10_args_ok_fn() + tail

parts = [body]
for name in ['CopyNet10UrlToClipboard', 'NetDlRunInstaller',
             'NetDlTryInstallRuntime', 'HandleNet10MissingAnswer', 'PromptNet10Missing',
             'UpdateReadyMemo']:
    parts.append('// ---- 以下是 installer-src\\install_script.iss 的逐字副本：%s ----\n' % name)
    parts.append(blocks[name])
    parts.append('\n')
parts.append(tail)
out = ''.join(parts)

# ⛔ 探针里所有【字符串字面量】都不许含真实地址（⚠️ 逐字副本的行尾注释会提到 aka.ms/builds.…，
#    注释不参与联网 => 只扫单引号字面量）
lits = re.findall(r"'([^'\r\n]*)'", out)
for host in ('aka.ms', 'builds.dotnet.microsoft.com', 'dotnet.microsoft.com', 'https://'):
    bad = [l for l in lits if host in l]
    assert not bad, '探针字面量里出现了真实地址：%s -> %r' % (host, bad)
url_lits = [l for l in lits if l.startswith('http')]
assert url_lits, '探针里没有任何 http 字面量？'
for l in url_lits:
    assert l.startswith('http://127.0.0.1:9/'), '探针里的 http 字面量不是死地址：' + l
print('探针 http 字面量（全部为死地址）= %d 个：%s' % (len(url_lits), ' , '.join(sorted(set(url_lits)))))

# ⚠️ 统一成 CRLF（HEAD/TAIL 用 \n 写、抽取的块本身是 \r\n => 先归一再多行拼接，
#    否则 newline='\r\n' 会把已有的 \r\n 变成 \r\r\n）
out = out.replace('\r\n', '\n').replace('\n', '\r\n')

with io.open(PROBE, 'w', encoding='utf-8', newline='') as f:
    f.write(out)

back = io.open(PROBE, encoding='utf-8', newline='').read()
for name in FUNCS:
    assert blocks[name] in back, 'COPY MISMATCH: ' + name
    print('  IDENTICAL  %-26s %5d B' % (name, len(blocks[name])))
assert back.count('\r\n') == back.count('\n'), '出现裸 LF'
print('wrote %s  %d B' % (PROBE, os.path.getsize(PROBE)))
print('probe sha256 = %s' % hashlib.sha256(open(PROBE, 'rb').read()).hexdigest().upper())
