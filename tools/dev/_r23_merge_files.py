# -*- coding: utf-8 -*-
r"""r23（0.291.7.6）把 publish 产物并入 installer-src\files：逐文件 SHA-256 比对，只覆盖真不同的。
   ⛔ 不新增文件、⛔ 不删除文件、⛔ 不碰 GCU/字体/usbmmidd/Resources/启动壳/更新日志。
   沿用 tools\merge_and_clean.py 的「只覆盖 files\ 里已存在的同名文件」策略（含 zh-CN 卫星）。
用法: py -3 tools\dev\_r23_merge_files.py [--apply]"""
import io, os, sys, shutil, hashlib

sys.stdout.reconfigure(encoding='utf-8', errors='replace')

ROOT = r'D:\openLM\installer-src'
FILES = os.path.join(ROOT, 'files')
BUILD = r'D:\openLM\rebuild\lm_r23_build'
WANT = ['L-Mechrevo.dll', 'L-Mechrevo.exe', 'L-Mechrevo.deps.json',
        'L-Mechrevo.runtimeconfig.json', 'L-Mechrevo.dll.config']
SATELLITES = [r'zh-CN\L-Mechrevo.resources.dll']
# ⛔ 显式排除：启动壳由 LmLauncherStub.csproj 自己落，更新日志由本轮单独改。
NEVER = {'L-Mechrevo-Launcher.exe', '更新日志.txt'}


def find_ci(root, rel):
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


def sha(p):
    h = hashlib.sha256()
    with open(p, 'rb') as f:
        for blk in iter(lambda: f.read(1 << 20), b''):
            h.update(blk)
    return h.hexdigest().upper()


def main():
    apply_ = '--apply' in sys.argv
    n_before = sum(len(f) for _, _, f in os.walk(FILES))
    print('files\\ 改前文件数 = %d' % n_before)
    updated, same, skipped = [], [], []
    for rel in WANT + SATELLITES:
        base = os.path.basename(rel)
        if base in NEVER:
            skipped.append(rel)
            continue
        src = os.path.join(BUILD, rel)
        if not os.path.isfile(src):
            print('   ! publish 里没有 %s' % rel)
            continue
        dst = find_ci(FILES, rel)
        if dst is None:
            print('   ! files 里没有（⛔ 不自动新增）%s' % rel)
            continue
        a, b = os.path.getsize(src), os.path.getsize(dst)
        if a == b and sha(src) == sha(dst):
            same.append(rel)
            print('   = SAME   %-40s %9d B  %s' % (rel, a, sha(src)[:16]))
            continue
        print('   -> UPDATE %-38s %9d B %s -> %9d B %s' % (rel, b, sha(dst)[:16], a, sha(src)[:16]))
        updated.append(rel)
        if apply_:
            # 写临时文件 -> 校验 -> 原子替换（⛔ 不直接 copy 覆盖，防半截文件）
            tmp = dst + '.tmp-copy'
            shutil.copyfile(src, tmp)
            assert sha(tmp) == sha(src), 'TEMP copy verify failed: ' + rel
            os.replace(tmp, dst)
            assert sha(dst) == sha(src)
    for rel in skipped:
        print('   = SKIP   %-40s（本轮由别的步骤负责）' % rel)
    n_after = sum(len(f) for _, _, f in os.walk(FILES))
    print('files\\ 改后文件数 = %d（%s）' % (n_after, '未变' if n_after == n_before else '★变了'))
    assert n_after == n_before, '文件数变了 —— 必须为 0 新增 / 0 删除'
    # 确认没有任何 .tmp-copy 残留
    leftovers = [os.path.join(dp, f) for dp, _, fs in os.walk(FILES) for f in fs if f.endswith('.tmp-copy')]
    assert not leftovers, leftovers
    print('更新 %d 个 / 未变 %d 个 / 跳过 %d 个%s' % (len(updated), len(same), len(skipped),
                                                '' if apply_ else '   （dry-run，未改动；加 --apply）'))
    return 0


if __name__ == '__main__':
    sys.exit(main())
