# -*- coding: utf-8 -*-
r"""把 2026-10-05 当天改动的源码 + 修改日志 打包成可复原存档"""
import io
import os
import re
import shutil
import zipfile
import datetime

SRC = r'D:\openLM\source'
LOG = r'D:\openLM\修改日志.txt'
OUTDIR = r'D:\openLM\archive\checkpoints\_checkpoints\20261005'
STAGE = os.path.join(OUTDIR, 'source')
LOGCOPY = os.path.join(OUTDIR, '修改日志-截至20261005.txt')
ZIP = r'D:\openLM\archive\checkpoints\_checkpoints\LM-20261005-改动存档.zip'

DAY = datetime.datetime(2026, 10, 5)


def main():
    if os.path.isdir(OUTDIR):
        shutil.rmtree(OUTDIR, ignore_errors=True)
    os.makedirs(STAGE, exist_ok=True)

    files = []
    for dp, dns, fns in os.walk(SRC):
        dns[:] = [d for d in dns if d not in ('obj', 'bin')]
        for fn in fns:
            if not (fn.endswith('.cs') or fn.endswith('.resx')):
                continue
            p = os.path.join(dp, fn)
            try:
                mt = datetime.datetime.fromtimestamp(os.path.getmtime(p))
            except OSError:
                continue
            if mt >= DAY:
                rel = os.path.relpath(p, SRC)
                dst = os.path.join(STAGE, rel)
                os.makedirs(os.path.dirname(dst), exist_ok=True)
                shutil.copy2(p, dst)
                files.append((rel, os.path.getsize(p), mt))

    files.sort()
    shutil.copy2(LOG, LOGCOPY)

    # 从修改日志里抽取当天的条目标题
    titles = []
    try:
        for line in io.open(LOG, encoding='utf-8', errors='replace'):
            m = re.match(r'\[(2026-10-05[^\]]*)\]\s*(.*)', line.strip())
            if m:
                titles.append((m.group(1), m.group(2)[:110]))
    except OSError:
        pass

    man = os.path.join(OUTDIR, 'MANIFEST.md')
    with io.open(man, 'w', encoding='utf-8-sig', newline='\r\n') as f:
        w = f.write
        w('# L-Mechrevo 改动存档 —— 2026-10-05\n\n')
        w('> 用途：本日改动**未打包**。此存档用于在未来并入上游新版本（如 0.290.6）时，\n')
        w('> 能逐文件复原本日成果，避免丢失。\n\n')
        w('## 归档内容\n\n')
        w('- `source/` —— 本日（2026-10-05 00:00 起）所有被改动的 `.cs` / `.resx`，保持原相对路径\n')
        w('- `修改日志-截至20261005.txt` —— 修改日志快照（含每条改动的详细说明与踩坑记录）\n')
        w('- 本文件 —— 清单与要点\n\n')
        w('## 改动文件清单（%d 个）\n\n' % len(files))
        w('| 文件（相对 source/） | 字节 | 改动时间 |\n|---|---|---|\n')
        for rel, size, mt in files:
            w('| `%s` | %d | %s |\n' % (rel.replace('\\', '/'), size, mt.strftime('%m-%d %H:%M')))
        w('\n## 本日修改日志条目标题（%d 条）\n\n' % len(titles))
        for ts, t in titles:
            w('- `%s` %s\n' % (ts, t))
        w('\n## 重要备注\n\n')
        w('- **未打包**（用户要求）；源码改动已部署到本机安装目录用于验证。\n')
        w('- `installer-src/install_script.iss` 与 `install_script_nogcu.iss` 也已改动\n')
        w('  （卸载全删用户数据 / 升级保留 / 全新安装清残留使首次引导重现），\n')
        w('  **不在本存档的 source/ 内**，另有备份：\n')
        w('  `backup/install_script.iss_20261005_pre_userdata.iss` 等。\n')
        w('- 各次改动的**逐条备份**在 `D:\\openLM\\backup\\`（多数以 `_20261005_` 命名）。\n')
        w('- 未实现项：⑦「读官方控制台配置作佐证」（见 `核显模式显示策略-方案存档.md` §3⑦）。\n')

    if os.path.isfile(ZIP):
        os.remove(ZIP)
    with zipfile.ZipFile(ZIP, 'w', zipfile.ZIP_DEFLATED) as z:
        for rel, _s, _m in files:
            z.write(os.path.join(STAGE, rel), os.path.join('source', rel))
        z.write(LOGCOPY, os.path.basename(LOGCOPY))
        z.write(man, 'MANIFEST.md')

    print('文件数 : %d' % len(files))
    print('日志条 : %d' % len(titles))
    print('目录   : %s' % OUTDIR)
    print('ZIP    : %s  (%.1f MB)' % (ZIP, os.path.getsize(ZIP) / 1048576.0))


if __name__ == '__main__':
    main()
