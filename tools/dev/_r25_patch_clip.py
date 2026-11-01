# -*- coding: utf-8 -*-
r"""
r25 补丁 2：把 r24 的「写剪贴板」实现换成能用的那条（clip.exe）。

为什么要换（r25 探针实测，全部有读数）：
  · r24 的 GlobalAlloc+GlobalLock 写法是【坏的】—— GlobalLock 返回的 PAnsiChar 转成 AnsiString 是
    【拷贝】=> 逐字符写（L[I] := S[I]）/ lstrcpyA 都只写进拷贝、没写进那块 GlobalMem：
      ① 块里垃圾串比直链短时抛 'Out of string range'（实测复现，异常会让 PrepareToInstall 的 try 吞掉
         => 我这一轮把「写剪贴板」挪到了弹框【之前】=> 会变成「弹框都不出来」=> 必须修）；
      ② 就算不抛，也是把垃圾塞进用户剪贴板（实测：lstrcpyA 版与逐字符版读回都是乱码）。
  · StrPCopy 不存在（编译期实测 Unknown identifier）；PAnsiChar 不能下标赋值（r24 实测）。
  · ✅ Windows 自带 clip.exe 可用：写一个【不带换行】的临时文件 + `cmd /c clip < 文件` =>
    实测 rc=0，读回与直链【逐字节相同】。

纪律：读 -> 断言 SHA + 锚点唯一 -> 写 TEMP -> 校验 TEMP -> 替换正式文件。
"""
import hashlib
import io
import os
import re
import shutil
import sys

sys.stdout.reconfigure(encoding='utf-8', errors='replace')

ISS = r'D:\openLM\installer-src\install_script.iss'
EXPECT_SHA = '958FB6BFB1FC353A2CCA5CB7787935BCACC36B3896C6AE50E77DC599348B79E1'
CRLF = '\r\n'

CLIP_START = '// 把官方【直链】写进剪贴板。返回 True = 真的写进去了（Win32 全链路成功）；任何一步失败 => False。'
CLIP_FUNC = 'function CopyNet10UrlToClipboard(const Where: String): Boolean;'

NEW = CRLF.join([
    '// 把官方【直链】写进剪贴板。返回 True = 真的写进去了；任何一步失败 => False。',
    '//   ⚠️⚠️ 2026-10-13（r25）：r24 用 GlobalAlloc + GlobalLock + SetClipboardData 自己拼——【实测是坏的】：',
    '//      · Inno 6.7.1 的 Pascal Script 里 GlobalLock 返回的 PAnsiChar 转成 AnsiString 是【拷贝】',
    '//        => 逐字符写（P[I] := S[I]）和 lstrcpyA 都只写进拷贝、没写进那块 GlobalMem；实测后果：',
    '//        ① 块里残留的垃圾串比直链短时抛 Inno 内部 Runtime error「Out of string range」（探针实测复现）；',
    '//        ② 就算不抛，也是把【垃圾】塞进用户剪贴板（实测：lstrcpyA 版 / 逐字符版读回都是乱码）。',
    '//      · StrPCopy 不存在（编译期实测 Unknown identifier）；PAnsiChar 不能下标赋值（r24 实测）。',
    '//      ⚠️ 这个异常很致命：本轮把「写剪贴板」放在了弹框【之前】=> 一旦抛出，连选择框都弹不出来。',
    '//   ✅ r25 改用 Windows 自带的 clip.exe：直链写进【不带换行】的临时文件，再 `cmd /c clip < 文件`。',
    '//      实测：rc=0，读回与直链【逐字节相同】（无多余空格/换行）。⛔ 不再 import 任何剪贴板 API。',
    '//   ⚠️ 一律 SW_HIDE + ewWaitUntilTerminated（clip.exe 毫秒级结束；⛔ 不留窗口、⛔ 不阻塞安装）；',
    '//   ⚠️ 整段 try/except：⛔ 任何异常都只记日志 + 返回 False，⛔ 绝不影响安装、⛔ 绝不中止。',
    '//   ⚠️ 剪贴板可能被别的进程临时占用 => 失败【只记日志】，由调用方的可见提示兜底。',
    'function CopyNet10UrlToClipboard(const Where: String): Boolean;',
    'var',
    '  Rc: Integer;',
    '  ClipFile, Cmd: String;',
    'begin',
    '  Result := False;',
    "  if Net10DownloadDirectUrl = '' then",
    '  begin',
    "    Log('LM-NET-CLIP: 直链为空 -> 不写剪贴板（' + Where + '）');",
    '    Exit;',
    '  end;',
    '  // clip.exe 读的是【文件】：写文件时⛔ 不加换行（加了会被一起粘进地址栏）',
    "  ClipFile := ExpandConstant('{tmp}\\' + NetDlClipFileName);",
    '  try',
    '    if not SaveStringToFile(ClipFile, Net10DownloadDirectUrl, False) then',
    '    begin',
    "      Log('LM-NET-CLIP: 写临时文件失败 -> 放弃本次复制（' + Where + '）：' + ClipFile);",
    '      Exit;',
    '    end;',
    '    // ⚠️ 直链里没有 cmd 特殊字符（无 & | > < ^）；路径加引号兜住空格（{tmp} 可能带空格）',
    "    Cmd := '/c clip < \"' + ClipFile + '\"';",
    "    if Exec(ExpandConstant('{cmd}'), Cmd, '', SW_HIDE, ewWaitUntilTerminated, Rc) and (Rc = 0) then",
    '    begin',
    '      Result := True;',
    "      Log('LM-NET-CLIP: 已把官方直链写入剪贴板（' + Where + '）url=' + Net10DownloadDirectUrl",
    "        + ' len=' + IntToStr(Length(Net10DownloadDirectUrl)) + '（clip.exe rc=0）');",
    '    end',
    '    else',
    "      Log('LM-NET-CLIP: clip.exe 失败 rc=' + IntToStr(Rc) + ' -> 放弃本次复制（' + Where + '）');",
    '  except',
    '    Result := False;',
    "    Log('LM-NET-CLIP: 写剪贴板异常（已吞掉，不影响安装）：' + GetExceptionMessage + '（' + Where + '）');",
    '  end;',
    'end;',
    ''])


def sha256_of(path):
    with open(path, 'rb') as f:
        return hashlib.sha256(f.read()).hexdigest().upper()


def main():
    pre = sha256_of(ISS)
    print('pre  sha256 = %s  size=%d' % (pre, os.path.getsize(ISS)))
    assert pre == EXPECT_SHA, '前置 SHA 不符（文件已被改过？）: %s' % pre
    src = io.open(ISS, encoding='utf-8', newline='').read()

    # 1) 定位并整体替换「注释头 + 9 个 external + 旧函数体」
    i = src.index(CLIP_START)
    assert src.count(CLIP_START) == 1
    m = re.search(r'^end;\r?\n', src[src.index(CLIP_FUNC):], re.M)
    assert m, 'CopyNet10UrlToClipboard 没有结束的 end;'
    j = src.index(CLIP_FUNC) + m.end()
    old = src[i:j]
    assert 'WinGlobalLock' in old and 'WinSetClipboardData' in old
    assert 'WinOpenClipboard' in old and 'external' in old
    assert old.count('function ') == 9, '旧块里 external 声明数不对：%d' % old.count('function ')
    print('旧块 %d B（注释 + 9 个 external + 函数体）-> 将被替换' % len(old.encode('utf-8')))
    src = src[:i] + NEW + src[j:]

    # 2) 新常量 NetDlClipFileName
    anchor = "  NetDlDoneFileName = 'lm-net10-install-rc.txt';"
    assert src.count(anchor) == 1
    src = src.replace(
        anchor,
        anchor + CRLF
        + '  // 写剪贴板用的临时文件（⛔ 不加换行；clip.exe 读它）' + CRLF
        + "  NetDlClipFileName = 'lm-net10-clip-url.txt';", 1)

    # 3) 结构哨兵
    assert 'WinGlobalLock' not in src, '还有旧的 GlobalLock 剪贴板实现残留'
    assert 'WinOpenClipboard' not in src, '还有旧的 OpenClipboard 声明残留'
    assert src.count('function CopyNet10UrlToClipboard(const Where: String): Boolean;') == 1
    assert "'/c clip < \"' + ClipFile + '\"'" in src
    assert 'NetDlClipFileName' in src
    assert 'Out of string range' in src  # 说明里留了实测证据
    assert src.count('SaveStringToFile(ClipFile, Net10DownloadDirectUrl, False)') == 1

    # 4) 写 TEMP -> 校验 -> 替换
    tmp = os.path.join(os.environ['TEMP'], 'lm_r25_install_script_clip.iss')
    with io.open(tmp, 'w', encoding='utf-8', newline='') as f:
        f.write(src)
    back = io.open(tmp, encoding='utf-8', newline='').read()
    assert back == src, 'TEMP 回读不一致'
    assert back.count('\n') == back.count(CRLF), '出现裸 LF'
    print('temp verified: %d B / %d lines' % (len(back.encode('utf-8')), back.count(CRLF) + 1))
    shutil.copyfile(tmp, ISS)
    print('post sha256 = %s  size=%d' % (sha256_of(ISS), os.path.getsize(ISS)))
    print('OK')
    return 0


if __name__ == '__main__':
    sys.exit(main())
