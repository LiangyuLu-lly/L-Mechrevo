# -*- coding: utf-8 -*-
r"""r24 source patch: fix the bogus explorer.exe path (System32) in BOTH the Inno installer and the
launcher stub, add the "explicit default browser" tier, make every failure visible + honest.

Discipline: read bytes -> assert unique anchors -> splice -> write TEMP -> verify -> os.replace.
Encodings are preserved exactly (iss: no BOM + CRLF ; .cs/.manifest: no BOM + LF).
Usage: python _r24_patch_sources.py [--apply]
"""
import hashlib, io, os, sys

sys.stdout.reconfigure(encoding='utf-8', errors='replace')
APPLY = '--apply' in sys.argv

ISS = r'D:\openLM\installer-src\install_script.iss'
CS = r'D:\openLM\tools\LmLauncherStub\LmLauncherStub.cs'
MF = r'D:\openLM\tools\LmLauncherStub\app.manifest'


def sha(b):
    return hashlib.sha256(b).hexdigest().upper()


def load(p):
    return open(p, 'rb').read()


def save(p, raw, old_raw):
    tmp = p + '.tmp-write'
    open(tmp, 'wb').write(raw)
    chk = open(tmp, 'rb').read()
    assert chk == raw, 'temp verify failed'
    assert raw != old_raw, 'nothing changed'
    if APPLY:
        os.replace(tmp, p)
    else:
        os.remove(tmp)
    fin = load(p) if APPLY else raw
    print('  %-46s %d B -> %d B  sha %s%s' % (os.path.basename(p), len(old_raw), len(fin), sha(fin)[:24],
                                              '' if APPLY else '   (dry-run)'))


def splice(raw, start, end, new, label):
    """Replace the unique slice [start .. end] (inclusive of end) with new."""
    assert raw.count(start.encode('utf-8')) == 1, 'start anchor not unique: ' + label
    assert raw.count(end.encode('utf-8')) == 1, 'end anchor not unique: ' + label
    i = raw.index(start.encode('utf-8'))
    j = raw.index(end.encode('utf-8'), i) + len(end.encode('utf-8'))
    return raw[:i] + new.encode('utf-8') + raw[j:]


def sub1(raw, old, new, label):
    assert raw.count(old.encode('utf-8')) == 1, 'anchor not unique (%d): %s' % (raw.count(old.encode('utf-8')), label)
    return raw.replace(old.encode('utf-8'), new.encode('utf-8'))


def crlf(s):
    """The .iss file is pure CRLF; make the pattern/replacement agree."""
    return s.replace('\r\n', '\n').replace('\n', '\r\n')


# ================================================================================================
# 1) install_script.iss : new multi-tier chain (helpers + OpenNet10DownloadPage)
# ================================================================================================
ISS_START = "// 打开 .NET 10 桌面运行时（x64）的【官方直链】（用户点「下载 / Download」时唯一的动作）：\r\n"
ISS_END = "  Log('LM-NET-BROWSER: 排查提示：本行以上带 LM-NET-BROWSER: 的每一档都有记录；日志文件 = %TEMP%\\Setup Log*.txt（SetupLogging=yes）');\r\nend;\r\n"

ISS_NEW = '''// ================================================================================================
// 打开 .NET 10 桌面运行时（x64）的【官方直链】（用户点「下载 / Download」时唯一的动作）
// ================================================================================================
//   ⚠️⚠️ 2026-10-13（r24）：r23 的第 2 档把 explorer 的路径写成了 {sys}\\explorer.exe。
//      本安装包的 [Setup] 是 ArchitecturesInstallIn64BitMode=x64compatible，实测（installer-src\\_probe\\probe_syspath.iss）：
//        {sys}       = C:\\Windows\\system32          => C:\\Windows\\system32\\explorer.exe   【不存在】
//        {win}       = C:\\Windows                    => C:\\Windows\\explorer.exe           【存在】<- 真身
//        {syswow64}  = C:\\Windows\\SysWOW64          => ...\\SysWOW64\\explorer.exe          【存在】
//      => 第 2 档必然失败，而且 Windows 自己会弹一个「找不到 C:\\Windows\\System32\\explorer.exe」的
//         系统错误框盖在向导上（用户实测截图两次）。r24 起：
//           ① explorer 路径【先解析、再用 FileExists 校验】，候选取第一个真实存在的；
//           ② 候选全不存在 => ⛔ 连调用都不做，只写日志（绝不再把系统错误框弹给用户）；
//           ③ 新增第 3 档：显式读【用户默认浏览器】并启动它（多子档，逐档写日志 + 返回码）。
//   档位（每一档都写日志 + 检查返回值，全部失败才返回 False => 调用方给可见提示）：
//     第1档 ShellExec('open', 直链)                                    ★ 主档：系统 shell 关联直接开
//            ⚠️ 就是启动壳里【实测能拉起浏览器】的那条等价调用（用户实测：「壳倒是可以拉起浏览器诶」）；
//               r23 把这一档排在最后，前面还先弹了系统错误框 => 用户根本看不到浏览器。
//     第2档 ShellExecAsOriginalUser('open', 直链)                      以【原登录用户】身份（最理想）
//     第3档 ShellExec('open', <真实的 explorer.exe>, '"直链"')          explorer 代转降权（路径先校验）
//     第4档 4a ShellExecAsOriginalUser('open', 浏览器 exe, '"直链"')     显式默认浏览器（原用户身份）
//           4b ShellExec('open', 浏览器 exe, '"直链"')                 显式默认浏览器（提权，尽力而为）
//     第5档 剪贴板 + 可见提示（在 HandleNet10MissingAnswer 里：写清逐档结果 + errcode + 原因判断）
//   ⚠️ 一律 ewNoWait：⛔ 不等浏览器、⛔ 不阻塞安装；返回 False 时调用方补一条【可见提示】。
//   ⚠️ 怎么排查（给用户的说明）：日志文件 = %TEMP%\\Setup Log YYYY-MM-DD #NNN.txt（SetupLogging=yes），
//      搜 `LM-NET-BROWSER:`；每一档都会留下 `第N档 [成功/失败/异常]` + errcode/rc。
//      ⛔ 一行都搜不到 => 这个包早于 r23（那段代码还不存在）。
var
  NetBrowserTierReport: String;   // 逐档结果摘要（全失败时原样进 MsgBox）

procedure NetTierNote(const S: String);
begin
  if NetBrowserTierReport <> '' then
    NetBrowserTierReport := NetBrowserTierReport + ' ; ';
  NetBrowserTierReport := NetBrowserTierReport + S;
  Log('LM-NET-BROWSER: ' + S);
end;

// explorer.exe 的【真实】位置：四个候选里挑第一个 FileExists 的；一个都没有 => 返回 ''（跳过该档）。
//   ⚠️ 顺序 = 真身优先（{win} = C:\\Windows）；{sys}/{sysnative} 放在后面只为兜异常环境。
function NetResolveExplorerExe(): String;
var
  Candidates: TArrayOfString;
  I: Integer;
  S: String;
begin
  Result := '';
  SetArrayLength(Candidates, 4);
  Candidates[0] := ExpandConstant('{win}\\explorer.exe');
  Candidates[1] := ExpandConstant('{syswow64}\\explorer.exe');
  Candidates[2] := ExpandConstant('{sys}\\explorer.exe');
  Candidates[3] := ExpandConstant('{sysnative}\\explorer.exe');
  for I := 0 to GetArrayLength(Candidates) - 1 do
  begin
    S := Candidates[I];
    if FileExists(S) then
    begin
      Result := S;
      Log('LM-NET-BROWSER: 第2档 explorer 候选[' + IntToStr(I + 1) + ']=' + S + ' => 存在，采用');
      Exit;
    end;
    Log('LM-NET-BROWSER: 第2档 explorer 候选[' + IntToStr(I + 1) + ']=' + S + ' => 不存在，换下一个');
  end;
  Log('LM-NET-BROWSER: 第2档 explorer 四个候选都不存在 => 跳过这一档（⛔ 不调用，避免系统报错框）');
end;

// 从注册表读【用户默认浏览器】的 exe（第 3 档用）：
//   HKCU\\...\\UrlAssociations\\http\\UserChoice 的 ProgId -> HKCU\\Software\\Classes\\<ProgId>\\shell\\open\\command
//   （读不到就退到 HKCR\\<ProgId>\\...，再退到 http 协议本身的 shell\\open\\command；本机实测命令在 HKCR）
//   ⚠️ 只取命令行里的 exe 路径：带引号取引号内，不带引号取到第一个空格；解析不到/文件不存在 => ''。
function NetResolveDefaultBrowserExe(): String;
var
  ProgId, Cmd, S: String;
  P: Integer;
begin
  Result := '';
  ProgId := '';
  Cmd := '';
  if RegQueryStringValue(HKCU,
      'Software\\Microsoft\\Windows\\Shell\\Associations\\UrlAssociations\\http\\UserChoice', 'ProgId', ProgId) then
  begin
    if ProgId <> '' then
    begin
      if not RegQueryStringValue(HKCU, 'Software\\Classes\\' + ProgId + '\\shell\\open\\command', '', Cmd) then
        RegQueryStringValue(HKCR, ProgId + '\\shell\\open\\command', '', Cmd);
      Log('LM-NET-BROWSER: 第3档 默认浏览器 ProgId=' + ProgId + ' command=' + Cmd);
    end;
  end;
  if Cmd = '' then
  begin
    if not RegQueryStringValue(HKCU, 'Software\\Classes\\http\\shell\\open\\command', '', Cmd) then
      RegQueryStringValue(HKCR, 'http\\shell\\open\\command', '', Cmd);
    if Cmd <> '' then
      Log('LM-NET-BROWSER: 第3档 由 http 协议命令解析：' + Cmd);
  end;
  if Cmd = '' then
  begin
    Log('LM-NET-BROWSER: 第3档 读不到默认浏览器命令（UserChoice / Classes 都没有）=> 该档只用 cmd start 子档');
    Exit;
  end;
  S := Trim(Cmd);
  if (Length(S) > 0) and (S[1] = '"') then
  begin
    Delete(S, 1, 1);
    P := Pos('"', S);
    if P > 0 then
      S := Copy(S, 1, P - 1);
  end
  else
  begin
    P := Pos(' ', S);
    if P > 0 then
      S := Copy(S, 1, P - 1);
  end;
  // 命令里可能用 %ProgramFiles% / %LOCALAPPDATA% 之类的环境变量（少见，但兜住）
  StringChange(S, '%ProgramFiles(x86)%', GetEnv('ProgramFiles(x86)'));
  StringChange(S, '%ProgramFiles%', GetEnv('ProgramFiles'));
  StringChange(S, '%LOCALAPPDATA%', GetEnv('LOCALAPPDATA'));
  StringChange(S, '%APPDATA%', GetEnv('APPDATA'));
  StringChange(S, '%SystemRoot%', GetEnv('SystemRoot'));
  if (S <> '') and FileExists(S) then
    Result := S
  else
    Log('LM-NET-BROWSER: 第3档 解析出的浏览器 exe 为空或不存在（' + S + '）=> 跳过 exe 子档');
end;

function OpenNet10DownloadPage(): Boolean;
var
  ErrCode: Integer;
  ExplorerExe, BrowserExe: String;
begin
  Result := True;
  NetBrowserTierReport := '';
  // ---- 第 1 档【主档】：系统 shell 关联直接打开直链（= 启动壳里实测能用的那条等价调用）----
  Log('LM-NET-BROWSER: 第1档 ShellExec open 直链（主档）url=' + Net10DownloadDirectUrl);
  try
    ErrCode := 0;
    if ShellExec('open', Net10DownloadDirectUrl, '', '', SW_SHOWNORMAL, ewNoWait, ErrCode) then
    begin
      NetTierNote('第1档[成功] ShellExec open 直链（errcode=' + IntToStr(ErrCode) + '）');
      Exit;
    end;
    NetTierNote('第1档[失败] ShellExec open 直链 返回 False（errcode=' + IntToStr(ErrCode) + '）');
  except
    NetTierNote('第1档[异常] ShellExec open 直链 抛异常');
  end;
  // ---- 第 2 档：以【原登录用户】身份用 shell 打开（最理想：完全用用户自己的浏览器关联）----
  Log('LM-NET-BROWSER: 第2档 ShellExecAsOriginalUser 开始 url=' + Net10DownloadDirectUrl);
  try
    ErrCode := 0;
    if ShellExecAsOriginalUser('open', Net10DownloadDirectUrl, '', '', SW_SHOWNORMAL, ewNoWait, ErrCode) then
    begin
      NetTierNote('第2档[成功] ShellExecAsOriginalUser（errcode=' + IntToStr(ErrCode) + '）');
      Exit;
    end;
    NetTierNote('第2档[失败] ShellExecAsOriginalUser 返回 False（errcode=' + IntToStr(ErrCode) + '）');
  except
    // 以「管理员身份运行」启动安装包 / 初始 Setup 进程已退出 => 拿不到原始用户令牌，这一档会抛异常
    NetTierNote('第2档[异常] ShellExecAsOriginalUser 抛异常（原始用户不可用：多为「以管理员身份运行」）');
  end;
  // ---- 第 3 档：explorer.exe "<直链>"（★ r24 修正：路径先解析 + FileExists 校验，缺了就不调用）----
  ExplorerExe := NetResolveExplorerExe();
  if ExplorerExe = '' then
    NetTierNote('第3档[跳过] explorer.exe 候选都不存在（r23 用的 {sys}\\explorer.exe = C:\\Windows\\system32\\explorer.exe 本就不存在）')
  else
  begin
    Log('LM-NET-BROWSER: 第3档 explorer.exe 开始 path=' + ExplorerExe + ' args="' + Net10DownloadDirectUrl + '"');
    try
      ErrCode := 0;
      if ShellExec('open', ExplorerExe, '"' + Net10DownloadDirectUrl + '"', '', SW_SHOWNORMAL, ewNoWait, ErrCode) then
      begin
        NetTierNote('第3档[成功] explorer.exe ' + ExplorerExe + '（errcode=' + IntToStr(ErrCode) + '）');
        Exit;
      end;
      NetTierNote('第3档[失败] explorer.exe ' + ExplorerExe + ' 返回 False（errcode=' + IntToStr(ErrCode) + '）');
    except
      NetTierNote('第3档[异常] explorer.exe 调用抛异常（path=' + ExplorerExe + '）');
    end;
  end;
  // ---- 第 4 档：显式读【用户默认浏览器】并启动它（4a 原用户身份 / 4b 提权兜底）----
  BrowserExe := NetResolveDefaultBrowserExe();
  if BrowserExe = '' then
    NetTierNote('第4档[跳过] 读不到可用的默认浏览器 exe')
  else
  begin
    Log('LM-NET-BROWSER: 第4a档 ShellExecAsOriginalUser 默认浏览器 exe=' + BrowserExe);
    try
      ErrCode := 0;
      if ShellExecAsOriginalUser('open', BrowserExe, '"' + Net10DownloadDirectUrl + '"', '', SW_SHOWNORMAL, ewNoWait, ErrCode) then
      begin
        NetTierNote('第4a档[成功] 原用户身份启动默认浏览器 ' + BrowserExe);
        Exit;
      end;
      NetTierNote('第4a档[失败] 原用户身份启动浏览器返回 False（errcode=' + IntToStr(ErrCode) + '）');
    except
      NetTierNote('第4a档[异常] 原用户身份启动浏览器抛异常');
    end;
    Log('LM-NET-BROWSER: 第4b档 ShellExec（提权身份）默认浏览器 exe=' + BrowserExe);
    try
      ErrCode := 0;
      if ShellExec('open', BrowserExe, '"' + Net10DownloadDirectUrl + '"', '', SW_SHOWNORMAL, ewNoWait, ErrCode) then
      begin
        NetTierNote('第4b档[成功] 提权身份启动默认浏览器 ' + BrowserExe);
        Exit;
      end;
      NetTierNote('第4b档[失败] 提权身份启动浏览器返回 False（errcode=' + IntToStr(ErrCode) + '）');
    except
      NetTierNote('第4b档[异常] 提权身份启动浏览器抛异常');
    end;
  end;
  // ---- 第 5 档（兜底）：直链已在剪贴板；调用方 HandleNet10MissingAnswer 会给【可见提示】----
  Result := False;
  NetTierNote('第5档[兜底] 自动打开全部失败 -> 直链已在剪贴板，改由【可见提示】让用户手动粘贴；安装照常继续');
  Log('LM-NET-BROWSER: 排查提示：以上每一档都有记录；日志文件 = %TEMP%\\Setup Log*.txt（SetupLogging=yes）；'
    + '若一行都搜不到 => 这个包早于 r23（没有这段代码）');
end;
'''

# HandleNet10MissingAnswer: visible failure prompt with per-tier results + errcodes + honest reason
H_OLD = """      if not WizardSilent() then
        MsgBox('自动打开浏览器失败（安装程序以管理员身份运行，浏览器往往接不到这个地址）。' + #13#10
          + '地址【已复制到剪贴板】，请手动粘贴到浏览器地址栏打开（打开即开始下载）：' + #13#10
          + Net10DownloadDirectUrl, mbInformation, MB_OK);
"""
H_NEW = """      if not WizardSilent() then
      begin
        // ⚠️ r24：文案必须【可见 + 可操作】—— 写清逐档结果（含 errcode/rc）+ 原因判断 + 地址。
        //   剪贴板若第一次没写进去，这里再试一次，并如实区分「已复制」/「复制也失败」。
        if not Copied then
          Copied := CopyNet10UrlToClipboard('自动打开失败后重试');
        if Copied then
          MsgBox('自动打开浏览器没有成功（已走完所有档位，逐档结果如下）。' + #13#10 + #13#10
            + '逐档结果：' + NetBrowserTierReport + #13#10 + #13#10
            + '原因判断：所有自动打开方式都没成功（第1档 系统 shell 关联 / 第2档 原用户身份 / '
            + '第3档 explorer 代转 / 第4档 显式默认浏览器）。若是用「以管理员身份运行」启动的安装程序，'
            + '第2档一定失败；其余档位失败通常说明系统里没有可用的浏览器关联（例如浏览器被安全软件拦下）。' + #13#10 + #13#10
            + '地址【已复制到剪贴板】，请手动粘贴到浏览器地址栏打开（打开即开始下载）：' + #13#10
            + Net10DownloadDirectUrl, mbInformation, MB_OK)
        else
          MsgBox('自动打开浏览器没有成功，复制到剪贴板也失败了。' + #13#10 + #13#10
            + '逐档结果：' + NetBrowserTierReport + #13#10 + #13#10
            + '请手动抄下这个地址，在浏览器里打开（打开即开始下载）：' + #13#10
            + Net10DownloadDirectUrl, mbInformation, MB_OK);
      end;
"""

ISS_COMMENTS = [
    ("//   IDYES    = 「下载 / Download」=> 打开官方直链（ShellExecAsOriginalUser -> explorer.exe 降权 -> ShellExec，ewNoWait）；\r\n"
     "//              三档全失败 => 补一条【可见提示】（原来只写日志 = 用户看到的就是\"点了没反应\"）\r\n",
     "//   IDYES    = 「下载 / Download」=> 打开官方直链（第1档 系统 shell 关联（主档）-> 第2档 原用户身份 ->\r\n"
     "//              第3档 explorer.exe 代转（路径先校验）-> 第4档 显式默认浏览器 -> 第5档 剪贴板 + 可见提示，ewNoWait）；\r\n"
     "//              全部档位失败 => 补一条【可见提示】（原来只写日志 = 用户看到的就是\"点了没反应\"）\r\n"),
    ("//     ③ 点「下载 / Download」=> 依次用 ShellExecAsOriginalUser / explorer.exe（降权）/ ShellExec 打开【官方直链】\r\n",
     "//     ③ 点「下载 / Download」=> 依次用 ShellExec（主档）/ ShellExecAsOriginalUser / explorer.exe\r\n"
     "//        （路径先校验存在）/ 显式默认浏览器 exe 打开【官方直链】\r\n"),
    ("//        ⚠️ 2026-10-13（本批）：三档全失败【不再静默】—— 补一条 MsgBox 说明「地址已复制、请手动粘贴」；\r\n",
     "//        ⚠️ 2026-10-13（r24）：全部档位失败【不再静默】—— 补一条 MsgBox：逐档结果 + errcode + 原因判断\r\n"
     "//          + 「地址已复制、请手动粘贴」；\r\n"),
]


def patch_iss():
    raw = load(ISS)
    out = splice(raw, ISS_START, ISS_END, crlf(ISS_NEW), 'OpenNet10DownloadPage')
    out = sub1(out, crlf(H_OLD), crlf(H_NEW), 'HandleNet10MissingAnswer failure prompt')
    for old, new in ISS_COMMENTS:
        out = sub1(out, old, new, 'comment: ' + old[:40])
    assert out.count(b'{sys}\\explorer.exe') >= 1          # still mentioned only in the explanatory comment
    assert b"Candidates[0] := ExpandConstant('{win}\\explorer.exe');" in out
    assert b"Candidates[1] := ExpandConstant('{syswow64}\\explorer.exe');" in out
    assert b'NetResolveExplorerExe' in out and b'NetResolveDefaultBrowserExe' in out
    assert b"ExecAsOriginalUser(CmdExe" not in out and b"'/c start" not in out   # cmd 子档本轮不采用
    assert out.count(b"ShellExec('open', Net10DownloadDirectUrl") == 1      # tier 1 = the main tier
    assert out.count(b"ShellExecAsOriginalUser('open', Net10DownloadDirectUrl") == 1
    assert out.count(b'ShellExecAsOriginalUser(') >= 2
    assert b'\\r\\n' not in out  # sanity: no stray escaped CRLF leaked into the file
    assert out.count(b'\n') == out.count(b'\r\n'), 'mixed line endings after patch'
    assert b'ErrCode, Rc' not in out
    save(ISS, out, raw)


# ================================================================================================
# 2) LmLauncherStub.cs : manifest comment + tier chain + explorer path fix + log path in the dialog
# ================================================================================================
CS_START = "    // ================================================================================================\n    // 四、以【用户身份】打开网址（2026-10-13 新增；修「点下载没拉起浏览器」）\n"
CS_NEW = '''    // ================================================================================================
    // 四、以【用户身份】打开网址（r23 新增；r24 修「explorer 路径写成 System32」+ 加显式默认浏览器档）
    // ================================================================================================
    /// <summary>
    /// 打开 URL 的档位链。⚠️ r24 起本壳的清单是 asInvoker（tools\\LmLauncherStub\\app.manifest）：
    /// 本壳只做「查 .NET 运行时 + 启动主程序」，真正的 L-Mechrevo.exe 自带 requireAdministrator
    /// 清单会自己提权 => 壳不必提权。于是第 1 档（shell 关联）就能以【用户身份】打开浏览器，
    /// 提权/降权那一整摊麻烦随之消失（用户报的「点了没反应」根因之一就是提权进程里让 shell 开 URL）。
    /// 档位（⛔ 不抛异常、⛔ 不弹 Windows 系统错误框、⛔ 不写注册表；每档各自 try/catch + 写日志）：
    ///   ① shell 关联 Process.Start(url, UseShellExecute=true)（首选，asInvoker 下的正路）
    ///   ② 显式默认浏览器：HKCU ...\\UrlAssociations\\http\\UserChoice 的 ProgId
    ///      -> HKCU\\Software\\Classes\\&lt;ProgId&gt;\\shell\\open\\command（缺则 HKCR\\...）里解析出的 exe
    ///   ③ explorer.exe "&lt;url&gt;"：⚠️ 路径必须【解析 + File.Exists 校验】——
    ///      r23 用的是 Environment.SpecialFolder.System = C:\\Windows\\System32\\explorer.exe，
    ///      而该文件在 64 位 Windows 上【根本不存在】（真身 = C:\\Windows\\explorer.exe）
    ///      => r23 第 1 档每次都抛「找不到文件」，还把 Windows 的系统错误框弹到用户脸上（实测截图）。
    ///      现在三候选取第一个真实存在的；一个都没有 => ⛔ 跳过该档，只写日志。
    /// ⚠️ 可测性：真正拉起进程的动作收敛到 <see cref="TestHooks.TryRun"/>（默认 <see cref="RunDetached"/>），
    ///   harness 换掉它就能断言「到底用哪个 exe、传了什么参数」，⛔ 不会真的开浏览器。
    /// </summary>
    internal static class UrlOpener
    {
        /// <summary>尝试顺序（先正路、后兜底）：返回 null 表示都没成，调用方给可见提示。</summary>
        internal static bool TryOpen(string url)
        {
            if (string.IsNullOrEmpty(url))
            {
                Log("URL 为空 -> 不打开（保留提示框里的「复制地址」）");
                return false;
            }

            // ⚠️ 防参数注入 / 防引号截断：URL 里正常不会有引号，真出现就剔掉。
            string safe = url.Replace("\\"", string.Empty);

            // ---- ① shell 关联（当前用户的默认浏览器；asInvoker 下这就是正路）----
            try
            {
                if (TryRun(null, safe))
                {
                    Log("第1档 shell 关联（UseShellExecute）已接受网址：" + safe);
                    return true;
                }
                Log("第1档 shell 关联失败 -> 转第2档 显式默认浏览器");
            }
            catch (Exception ex)
            {
                Log("第1档 shell 关联异常（" + ex.GetType().Name + " / " + ex.Message + "）-> 转第2档");
            }

            // ---- ② 显式默认浏览器（注册表拿 exe，存在才用）----
            try
            {
                string browser = ResolveDefaultBrowserExe();
                if (browser == null)
                {
                    Log("第2档 跳过：注册表里读不到可用的默认浏览器 exe");
                }
                else if (TryRun(browser, "\\"" + safe + "\\""))
                {
                    Log("第2档 默认浏览器已接受网址：" + browser + " \\"" + safe + "\\"");
                    return true;
                }
                else
                {
                    Log("第2档 默认浏览器启动返回 false：" + browser);
                }
            }
            catch (Exception ex)
            {
                Log("第2档 默认浏览器异常（" + ex.GetType().Name + " / " + ex.Message + "）");
            }

            // ---- ③ explorer.exe "<url>"（★ 路径先解析 + File.Exists 校验，缺了就跳过）----
            try
            {
                string explorer = ResolveExplorerExe();
                if (explorer == null)
                {
                    Log("第3档 跳过：explorer.exe 候选路径都不存在（⛔ 不调用，免得弹系统错误框）");
                }
                else if (TryRun(explorer, "\\"" + safe + "\\""))
                {
                    Log("第3档 explorer.exe 已接受网址：" + explorer + " \\"" + safe + "\\"");
                    return true;
                }
                else
                {
                    Log("第3档 explorer.exe 启动返回 false：" + explorer);
                }
            }
            catch (Exception ex)
            {
                Log("第3档 explorer.exe 异常（" + ex.GetType().Name + " / " + ex.Message + "）");
            }

            Log("三档都没能打开 -> 由界面给出【可见提示】（网址可复制，手动粘贴即可）");
            return false;
        }

        /// <summary>日志文件全路径（提示框里也显示，方便用户按这个路径去取日志）。</summary>
        internal static string LogPath()
        {
            return Path.Combine(Path.GetTempPath(), "L-Mechrevo-Launcher.log");
        }

        /// <summary>
        /// explorer.exe 的真实位置：三候选取第一个 File.Exists 的；都不存在 => null（该档跳过）。
        /// ★ r24 修正：真身是 %windir%\\explorer.exe；System32 下【没有】explorer.exe。
        /// </summary>
        internal static string ResolveExplorerExe()
        {
            var candidates = new List<string>();
            string win = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
            if (!string.IsNullOrEmpty(win))
            {
                candidates.Add(Path.Combine(win, "explorer.exe"));
                candidates.Add(Path.Combine(Path.Combine(win, "SysWOW64"), "explorer.exe"));
            }
            string sys = Environment.GetFolderPath(Environment.SpecialFolder.System);
            if (!string.IsNullOrEmpty(sys))
                candidates.Add(Path.Combine(sys, "explorer.exe"));

            foreach (string c in candidates)
            {
                bool exists;
                try { exists = File.Exists(c); }
                catch (Exception) { exists = false; }
                Log("explorer 候选 " + c + " => " + (exists ? "存在（采用）" : "不存在（换下一个）"));
                if (exists)
                    return c;
            }
            return null;
        }

        /// <summary>默认浏览器 exe：HKCU UserChoice 的 ProgId -> open 命令里的 exe；解析不到/不存在 => null。</summary>
        internal static string ResolveDefaultBrowserExe()
        {
            string progId = null;
            try
            {
                using (RegistryKey k = Registry.CurrentUser.OpenSubKey(
                    @"Software\\Microsoft\\Windows\\Shell\\Associations\\UrlAssociations\\http\\UserChoice"))
                {
                    if (k != null)
                        progId = k.GetValue("ProgId") as string;
                }
            }
            catch (Exception)
            {
            }

            string command = null;
            if (!string.IsNullOrEmpty(progId))
            {
                command = ReadOpenCommand(Registry.CurrentUser, @"Software\\Classes\\" + progId)
                       ?? ReadOpenCommand(Registry.ClassesRoot, progId);
            }
            if (string.IsNullOrEmpty(command))
                command = ReadOpenCommand(Registry.CurrentUser, @"Software\\Classes\\http")
                       ?? ReadOpenCommand(Registry.ClassesRoot, "http");
            Log("默认浏览器 ProgId=" + (progId ?? "<null>") + " command=" + (command ?? "<null>"));

            string exe = ExtractExecutable(command);
            if (string.IsNullOrEmpty(exe))
            {
                Log("默认浏览器 open 命令解析不出 exe => 跳过该档");
                return null;
            }
            bool ok;
            try { ok = File.Exists(exe); }
            catch (Exception) { ok = false; }
            if (!ok)
            {
                Log("默认浏览器 exe 不存在：" + exe + " => 跳过该档");
                return null;
            }
            return exe;
        }

        /// <summary>读 &lt;subPath&gt;\\shell\\open\\command 的默认值；⛔ 不 Dispose 传进来的基键。</summary>
        private static string ReadOpenCommand(RegistryKey baseKey, string subPath)
        {
            try
            {
                using (RegistryKey k = baseKey.OpenSubKey(subPath + @"\\shell\\open\\command"))
                {
                    return k == null ? null : (k.GetValue(null) as string);
                }
            }
            catch (Exception)
            {
                return null;
            }
        }

        /// <summary>命令行里取 exe：带引号取引号内；不带引号取到第一个空格。</summary>
        internal static string ExtractExecutable(string command)
        {
            if (string.IsNullOrEmpty(command))
                return null;
            string s = command.Trim();
            if (s.Length == 0)
                return null;
            if (s[0] == '"')
            {
                int end = s.IndexOf('"', 1);
                return end > 1 ? s.Substring(1, end - 1) : null;
            }
            int sp = s.IndexOf(' ');
            return sp > 0 ? s.Substring(0, sp) : s;
        }

        /// <summary>把 URL 写进本壳自己的日志（%TEMP%\\L-Mechrevo-Launcher.log）；⛔ 失败静默。</summary>
        internal static void Log(string message)
        {
            try
            {
                File.AppendAllText(
                    LogPath(),
                    DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + " [open-url] " + message + Environment.NewLine,
                    new UTF8Encoding(false));
            }
            catch (Exception)
            {
            }
        }

        /// <summary>经 TestHooks 走一次"拉进程"（默认实现用 Process.Start(UseShellExecute=true)）。</summary>
        private static bool TryRun(string fileName, string arguments)
        {
            return TestHooks.TryRun(fileName, arguments);
        }

        /// <summary>默认实现：UseShellExecute=true 拉起（fileName == null 时把 arguments 当 URL 直接交给 shell）。</summary>
        internal static bool RunDetached(string fileName, string arguments)
        {
            var psi = fileName == null
                ? new ProcessStartInfo(arguments)
                : new ProcessStartInfo(fileName) { Arguments = arguments };
            // UseShellExecute：explorer 也好、URL 也好，都交给 shell 打开；⛔ 不使用管道（免得挂住子进程）。
            psi.UseShellExecute = true;
            using (Process p = Process.Start(psi))
            {
                // ⚠️ explorer 打开 URL 时【它是转交后立刻退出】的，p 可能为 null 也可能立刻结束：
                //   => 只要能 Start 成功就判"已交给 shell"，⛔ 不在这里等退出。
                return true;
            }
        }
    }

    /// <summary>给无窗 harness 用的替身钩子（⛔ 生产路径不设它 => 一律走真实的 RunDetached）。</summary>
    internal static class TestHooks
    {
        private static readonly Func<string, string, bool> Default = RunDefault;

        private static Func<string, string, bool> current = Default;

        /// <summary>非 null 时【取代】真正的拉进程动作；harness 用它记录并断言参数。</summary>
        internal static Func<string, string, bool> TryRun
        {
            get { return current; }
            set { current = value ?? Default; }
        }

        /// <summary>把钩子还原成真实实现（harness 收尾用）。</summary>
        internal static void Reset()
        {
            current = Default;
        }

        private static bool RunDefault(string fileName, string arguments)
        {
            return UrlOpener.RunDetached(fileName, arguments);
        }
    }
}
'''

CS_DIALOG_OLD = '所以请先按上面的直链把运行时装好。"'
CS_DIALOG_NEW = ('所以请先按上面的直链把运行时装好。"\n'
                 '                     + "\\r\\n排查用日志（若「打开下载」没反应，可把这个文件发给作者）："\n'
                 '                     + "%TEMP%\\\\L-Mechrevo-Launcher.log"')

CS_MANIFEST_OLD = ('     ⚠️ requireAdministrator 是刻意的：壳要拉起真正的 L-Mechrevo.exe，而后者是 requireAdministrator\n'
                   '        （source\\app.manifest:7）。壳自己先提权 => 子进程以【同一权限】启动，不会再弹一次 UAC，\n'
                   '        用户看到的提权次数与「快捷方式直接指 L-Mechrevo.exe」完全一致。 -->\n')
CS_MANIFEST_NEW = ('     ⚠️ 2026-10-13（r24）：这里从 requireAdministrator 改成 asInvoker。\n'
                   '        理由：本壳不做任何需要管理员权限的事 —— 只【只读】检测 .NET 桌面运行时，然后启动同目录的\n'
                   '        L-Mechrevo.exe；而 L-Mechrevo.exe 自己的清单（source\\app.manifest:7）本来就是\n'
                   '        requireAdministrator，它会自己弹一次 UAC 提权 => 用户看到的提权次数【不变】\n'
                   '        （快捷方式 -> 壳[不提权] -> 主程序[提权，弹一次 UAC]）。\n'
                   '        ⚠️ 反面：requireAdministrator 会让「用 shell 关联打开浏览器」在提权上下文里静默失败\n'
                   '        （用户报的「点下载没拉起浏览器」根因之一）；改成 asInvoker 后浏览器以用户身份打开。 -->\n')


def patch_cs():
    raw = load(CS)
    i = raw.index(CS_START.encode('utf-8'))
    out = raw[:i] + CS_NEW.encode('utf-8')
    out = sub1(out, CS_DIALOG_OLD, CS_DIALOG_NEW, 'dialog foot label')
    assert b'SpecialFolder.System' in out and b'SpecialFolder.Windows' in out
    assert out.count(b'ResolveExplorerExe') == 2 and out.count(b'ResolveDefaultBrowserExe') == 2
    save(CS, out, raw)


def patch_manifest():
    raw = load(MF)
    out = sub1(raw, CS_MANIFEST_OLD, CS_MANIFEST_NEW, 'manifest comment')
    old = '        <requestedExecutionLevel level="requireAdministrator" uiAccess="false" />\n'
    new = '        <requestedExecutionLevel level="asInvoker" uiAccess="false" />\n'
    out = sub1(out, old, new, 'requestedExecutionLevel')
    assert b'level="requireAdministrator"' not in out
    assert out.count(b'level="asInvoker"') == 1
    assert out.count(b'asInvoker') == 3, 'asInvoker count=%d' % out.count(b'asInvoker')   # 2x comment + 1x attribute
    save(MF, out, raw)


print('=== r24 source patch %s ===' % ('APPLY' if APPLY else 'DRY-RUN'))
patch_iss()
patch_cs()
patch_manifest()
print('ALL-DONE')
