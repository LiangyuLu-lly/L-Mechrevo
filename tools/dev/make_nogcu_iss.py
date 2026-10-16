# -*- coding: utf-8 -*-
# 生成"不带 GCU"版 LM 安装脚本：删 GCU payload/安装/卸载/备份恢复/官方控制台干预逻辑
import io, re

SRC = r'D:\openLM\installer-src\install_script.iss'
DST = r'D:\openLM\installer-src\install_script_nogcu.iss'

lines = io.open(SRC, encoding='utf-8-sig').read().splitlines()
out = []

i = 0
while i < len(lines):
    ln = lines[i]
    stripped = ln.strip()

    # 1) 删 [Files] 的 GCU payload Source 行
    if stripped.startswith('Source: "files\\GCU\\') or stripped.startswith('Source: "files\GCU\\'):
        i += 1
        continue

    # 2) 删 [Run] 的 Install-Gcu.ps1 行
    if stripped.startswith('Filename: "{sys}\\WindowsPowerShell\\v1.0\\powershell.exe";') and 'Install-Gcu.ps1' in ln:
        i += 1
        continue

    # 3) 删 [UninstallRun] 的 Uninstall-Gcu.ps1 行
    if stripped.startswith('Filename: "{sys}\\WindowsPowerShell\\v1.0\\powershell.exe";') and 'Uninstall-Gcu.ps1' in ln:
        i += 1
        continue

    # 4) 删 [UninstallDelete] 的 GCU 目录行
    if stripped.startswith('Type: filesandordirs; Name: "{app}\\GCU\\'):
        i += 1
        continue

    # 5) 删 [CustomMessages] GCU/官方控制台相关
    if re.match(r'^(chinesesimplified|english)\.(GcuStatus|OfficialConsolePrompt|OfficialConsoleUninstallFailed|GcuPreUninstallFailed)=', stripped):
        i += 1
        continue

    # 6) [Code] 段：删除 GCU/官方控制台/升级相关过程
    if ln.strip() == '[Code]':
        out.append(ln); i += 1
        # 重写 [Code] 内容：按函数边界裁剪
        code = lines[i:]
        code_text = '\n'.join(code)
        # 定位各函数/const
        def find_start(name):
            for j, cl in enumerate(code):
                if name in cl and not cl.lstrip().startswith('//'):
                    return j
            return None

        # const: 删 GcuUserRoot 行
        # 删 GetOfficialUninstallString + UninstallOfficialConsole（到 InitializeSetup 前）
        # 删 InitializeSetup 内官方检测块
        # 删 GetLmUninstallString / CopyDirContents / BackupAndUpgrade（到 CurStepChanged 前）
        # CurStepChanged 重写：只留 install.log 复制
        # 删 PrepareToInstall（到 NeedRestart 前）
        # NeedRestart 保留

        # 先按行内容过滤 const 里的 GcuUserRoot
        new_code = []
        for cl in code:
            if 'GcuUserRoot' in cl and '=' in cl and 'const' not in cl:
                continue
            new_code.append(cl)
        code = new_code

        # 找各函数起始行（在当前 code 列表）
        idx = {}
        for name in ('function GetOfficialUninstallString', 'function InitializeSetup',
                     'function GetLmUninstallString', 'procedure CurStepChanged',
                     'function PrepareToInstall', 'function NeedRestart',
                     'procedure InitializeWizard', 'function InitializeUninstall',
                     'procedure CurUninstallStepChanged'):
            for j, cl in enumerate(code):
                if cl.lstrip().startswith(name):
                    idx[name] = j
                    break

        # 删除 GetOfficialUninstallString..InitializeSetup 之间（GetOfficialUninstallString 和 UninstallOfficialConsole 两个函数）
        if 'function GetOfficialUninstallString' in idx and 'function InitializeSetup' in idx:
            del code[idx['function GetOfficialUninstallString']:idx['function InitializeSetup']]
            # 行号已变，重新定位
        def reindex():
            idx2 = {}
            for j, cl in enumerate(code):
                for name in ('function InitializeSetup', 'function GetLmUninstallString',
                             'procedure CurStepChanged', 'function PrepareToInstall',
                             'function NeedRestart'):
                    if cl.lstrip().startswith(name) and name not in idx2:
                        idx2[name] = j
            return idx2
        idx = reindex()

        # InitializeSetup 内删除官方控制台检测块：从 '// Upgrade path:' 到 '  end;'（函数体倒数第二个 end; 之前的块）
        if 'function InitializeSetup' in idx:
            start = idx['function InitializeSetup']
            # 找 InitializeSetup 内的 '  // Upgrade path:' 行
            up_start = None
            for j in range(start, len(code)):
                if '// Upgrade path:' in code[j]:
                    up_start = j
                    break
            if up_start is not None:
                # 找块结束：从 up_start 开始数，'  end;' 出现处（官方检测块结束）
                # 结构：.NET 块 end;（前一个 end;） + Upgrade path 注释 + if OfficialUninst <> '' begin ... end; + 函数 end;
                # 官方块结束 = 在 up_start 之后第一个 '  end;'（缩进两格的 end;）
                up_end = None
                for j in range(up_start, len(code)):
                    if code[j].strip() == 'end;':
                        up_end = j
                        break
                if up_end is not None:
                    # 官方块从 up_start 到 up_end（含）；函数尾部的 'end;' 是另一个
                    del code[up_start:up_end + 1]
            idx = reindex()

        # 删除 GetLmUninstallString..CurStepChanged 之间（GetLmUninstallString + CopyDirContents + BackupAndUpgrade）
        if 'function GetLmUninstallString' in idx and 'procedure CurStepChanged' in idx:
            del code[idx['function GetLmUninstallString']:idx['procedure CurStepChanged']]
            idx = reindex()

        # CurStepChanged 重写为只保留 install.log 复制
        if 'procedure CurStepChanged' in idx:
            start = idx['procedure CurStepChanged']
            # 找 CurStepChanged 的 'end;'（函数结尾）——procedure 体最后一个 'end;'
            # 找其后的 '// Stop the bundled GCU processes' 注释（PrepareToInstall 注释起点）
            pti_comment = None
            for j in range(start, len(code)):
                if '// Stop the bundled GCU processes' in code[j]:
                    pti_comment = j
                    break
            end_pos = pti_comment if pti_comment is not None else start + 1
            # 重写这段为精简版
            new_body = [
                'procedure CurStepChanged(CurStep: TSetupStep);',
                'begin',
                '  if CurStep = ssPostInstall then',
                '  begin',
                '    // Keep the full setup log next to the app for troubleshooting.',
                '    if CopyFile(ExpandConstant(\'{log}\'), ExpandConstant(\'{app}\\install.log\'), False) then',
                '      Log(\'LM-UPDATE: 安装日志已保存到 {app}\\install.log\')',
                '    else',
                '      Log(\'LM-UPDATE: 安装日志复制失败\');',
                '  end;',
                'end;',
            ]
            code[start:end_pos] = new_body
            idx = reindex()

        # 删除 PrepareToInstall..NeedRestart 之间
        if 'function PrepareToInstall' in idx and 'function NeedRestart' in idx:
            del code[idx['function PrepareToInstall']:idx['function NeedRestart']]

        out.extend(code)
        break  # [Code] 是最后一段，处理完结束

    # 7) 命名调整
    out.append(ln)
    i += 1

text = '\n'.join(out)

# 命名：OutputBaseFilename / AppVerName / UninstallDisplayName
text = text.replace('AppVerName=L-Mechrevo 18.3 内测版',
                    'AppVerName=L-Mechrevo 18.3 内测版 (NoGCU)')
text = text.replace('OutputBaseFilename=LM-18.3内测版-setup',
                    'OutputBaseFilename=LM-18.3内测版-NoGCU-setup')
text = text.replace('UninstallDisplayName=L-Mechrevo 18.3 内测版',
                    'UninstallDisplayName=L-Mechrevo 18.3 内测版 (NoGCU)')

io.open(DST, 'w', encoding='utf-8-sig').write(text)
print('written:', DST)

# 校验：NoGCU iss 中不应再出现关键 GCU 引用
leftover = [kw for kw in ('Install-Gcu', 'Uninstall-Gcu', 'Select-GcuPayload',
                          'GcuUserRoot', 'BackupAndUpgrade', 'UninstallOfficialConsole',
                          'OfficialConsolePrompt', 'files\\GCU\\') if kw in text]
print('leftover GCU refs:', leftover if leftover else 'NONE')
print('lines:', len(text.splitlines()), '(original 802)')
