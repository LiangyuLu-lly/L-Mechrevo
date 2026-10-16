# -*- coding: utf-8 -*-
r"""
① 清理 installer-src\files 里确认无用的冗余（移到 backup，不直接删）
② 把 rebuild\lm_fix_build 的新构建合并进 installer-src\files
================================================================================
合并策略：**只覆盖 files\ 里已存在的同名文件**（避免引入未被 .iss 引用的新文件，
否则又变成冗余）；卫星程序集 zh-CN\L-Mechrevo.resources.dll 单独处理。
用法：py -3 merge_and_clean.py [--apply]
"""
import io
import os
import sys
import shutil
import datetime

sys.stdout.reconfigure(encoding="utf-8", errors="replace")

ROOT = r'D:\openLM\installer-src'
FILES = os.path.join(ROOT, 'files')
BUILD = r'D:\openLM\rebuild\lm_fix_build'
STAMP = datetime.datetime.now().strftime('%Y%m%d_%H%M%S')
BACKUP = r'D:\openLM\backup\files_clean_' + STAMP

# ① 冗余清理清单（相对 files\）
JUNK = [
    r'GCU\Install-Gcu.ps1.bak',
    r'GCU\payload\30\AiStoneService\GCUBridge.InstallState',
    r'GCU\payload\30\AiStoneService\GCUBridge.InstallLog',
    r'GCU\payload\30\AiStoneService\InstallUtil.InstallLog',
    r'GCU\payload\30\AiStoneService\install.bat',
    r'GCU\payload\30\AiStoneService\uninstall.bat',
    # RESTORED 2026-10-08: r'GCU\payload\30\AiStoneService\MyControlCenter\NAudio.dll',
    # RESTORED 2026-10-08: r'GCU\payload\30\AiStoneService\MyControlCenter\SharpDX.dll',
    # RESTORED 2026-10-08: r'GCU\payload\30\AiStoneService\MyControlCenter\SharpDX.RawInput.dll',
    # 2026-10-07：GamingMonitor 已由用户手动加回 30 系载荷（GCUService.exe 会加载
    # GCUService.GamingMonitor / GamingMonitorEnable），其 Config\configs.json 是必需的配套文件
    # —— 不再列入清理清单，否则每次 --apply 都会把它移走，30GCU 包将缺少该文件。
    # （原条目：GCU\payload\30\AiStoneService\MyControlCenter\GamingMonitor\Config\configs.json）
    # RESTORED 2026-10-08: r'GCU\payload\30\AiStoneService\MyControlCenter\AppSettings\AppProfileBindingPowerSaving.json',
    # RESTORED 2026-10-08: r'GCU\payload\30\AiStoneService\MyControlCenter\AppSettings\GamingMonitor.json',
]

# ② 需要合并的构建产物（相对 publish 输出）
WANT = ['L-Mechrevo.dll', 'L-Mechrevo.exe', 'L-Mechrevo.deps.json',
        'L-Mechrevo.runtimeconfig.json', 'L-Mechrevo.dll.config']
SATELLITES = [r'zh-CN\L-Mechrevo.resources.dll']


def find_ci(root, rel):
    """大小写不敏感地找文件，返回真实路径或 None"""
    cur = root
    for part in rel.split('\\'):
        if not os.path.isdir(cur):
            return None
        hit = None
        for name in os.listdir(cur):
            if name.lower() == part.lower():
                hit = name
                break
        if hit is None:
            return None
        cur = os.path.join(cur, hit)
    return cur if os.path.isfile(cur) else None


def main():
    apply_ = '--apply' in sys.argv
    print("### ① 冗余清理 ###")
    moved = 0
    for rel in JUNK:
        p = find_ci(FILES, rel)
        if not p:
            print("   （已不存在）" + rel)
            continue
        size = os.path.getsize(p)
        print("   %9.1f KB  %s" % (size / 1024.0, rel))
        if apply_:
            dst = os.path.join(BACKUP, os.path.relpath(p, FILES))
            os.makedirs(os.path.dirname(dst), exist_ok=True)
            shutil.move(p, dst)
            moved += 1
    print("   合计 %d 个文件" % len(JUNK))
    if apply_:
        # 清空目录
        for dp, dn, fn in os.walk(FILES, topdown=False):
            if not dn and not fn:
                try:
                    os.rmdir(dp)
                    print("   删除空目录 " + os.path.relpath(dp, FILES))
                except OSError:
                    pass
        print("   已移到 " + BACKUP)

    print("\n### ② 合并新构建 ###")
    merged, same, missing = [], [], []
    for rel in WANT + SATELLITES:
        src = os.path.join(BUILD, rel)
        if not os.path.isfile(src):
            missing.append(('publish 里没有', rel))
            continue
        dst = find_ci(FILES, rel)
        if dst is None:
            missing.append(('files 里没有（不自动新增）', rel))
            continue
        a, b = os.path.getsize(src), os.path.getsize(dst)
        same_content = False
        if a == b:
            same_content = io.open(src, 'rb').read() == io.open(dst, 'rb').read()
        if same_content:
            same.append(rel)
            print("   = 无变化      %-42s %9.1f KB" % (rel, a / 1024.0))
            continue
        print("   → 更新        %-42s %9.1f KB -> %9.1f KB" % (rel, b / 1024.0, a / 1024.0))
        merged.append(rel)
        if apply_:
            shutil.copy2(src, dst)

    if missing:
        print("   跳过：")
        for why, rel in missing:
            print("     %-28s %s" % (why, rel))
    if not apply_:
        print("\n（dry-run，未改动。加 --apply 执行）")
    else:
        print("\n合并完成：更新 %d 个，未变 %d 个" % (len(merged), len(same)))
    return 0


if __name__ == '__main__':
    sys.exit(main())
