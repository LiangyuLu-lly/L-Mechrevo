# -*- coding: utf-8 -*-
r"""
版本号更新 0.290.3-beta21.3 -> 0.290.7-beta21.7（仅改版本号，不打包）
沿用 tools\bump_version_2903.py 的既有做法（同一套文件、同一套顺序、同一套编码）。

改前先备份到 backup\：
  AssemblyInfo_20261005_pre_02907.cs
  install_script.iss.bak-20261005_1800
  install_script_nogcu.iss.bak-20261005_1800

不动：
  source\obj\**                     （编译中间产物，会重新生成）
  installer-src\files\更新日志.txt   （第 507/539 行是历史版本记录）
  修改日志.txt / 交接文档.md / 待办.txt（历史记录）
  installer-src\files\GCU\**        （我们的 GCU payload 分流，绝对不动）
"""
import io
import os
import shutil
import sys

sys.stdout.reconfigure(encoding="utf-8", errors="replace")

OLD_VER = '0.290.3'
NEW_VER = '0.290.7'
OLD_LABEL = 'beta21.3'
NEW_LABEL = 'beta21.7'

STAMP = '20261005_1800'

# (路径, 备份名, 编码)
TARGETS = [
    (r'D:\openLM\source\Properties\AssemblyInfo.cs',
     r'D:\openLM\backup\AssemblyInfo_20261005_pre_02907.cs', 'utf-8'),
    (r'D:\openLM\installer-src\install_script.iss',
     r'D:\openLM\backup\install_script.iss.bak-' + STAMP, 'utf-8-sig'),
    (r'D:\openLM\installer-src\install_script_nogcu.iss',
     r'D:\openLM\backup\install_script_nogcu.iss.bak-' + STAMP, 'utf-8-sig'),
]


def main():
    # ① 备份
    print('### ① 备份 ###')
    for path, bak, enc in TARGETS:
        shutil.copy2(path, bak)
        print('   %-52s -> %s' % (os.path.basename(path), bak))
    print('')

    # ② 替换（先标签后数字，避免交叉替换）
    print('### ② 替换 %s/%s -> %s/%s ###' % (OLD_VER, OLD_LABEL, NEW_VER, NEW_LABEL))
    total = 0
    for path, bak, enc in TARGETS:
        text = io.open(path, encoding=enc, newline='').read()
        n_lab = text.count(OLD_LABEL)
        n_ver = text.count(OLD_VER)
        assert n_lab >= 1 and n_ver >= 1, 'no match in ' + path
        text = text.replace(OLD_LABEL, NEW_LABEL).replace(OLD_VER, NEW_VER)
        # 核验：旧串已清零、新串数量一致（用 newline='' 原样写回，CRLF/BOM 保持不变）
        assert OLD_LABEL not in text and OLD_VER not in text, 'leftover old version in ' + path
        io.open(path, 'w', encoding=enc, newline='').write(text)
        # 复读核验
        back = io.open(path, encoding=enc, newline='').read()
        c_lab = back.count(NEW_LABEL)
        c_ver = back.count(NEW_VER)
        assert c_lab == n_lab and c_ver == n_ver, 'verify failed: ' + path
        total += n_lab + n_ver
        print('   %-52s label %d / ver %d  (改后 label %d / ver %d)'
              % (os.path.basename(path), n_lab, n_ver, c_lab, c_ver))
    print('\n合计替换 %d 处' % total)
    return 0


if __name__ == '__main__':
    sys.exit(main())
