# -*- coding: utf-8 -*-
"""2026-10-05 uifix（第二轮微调）：二次确认框里的基线标签写清楚来源。

    "当前基线：{1}" -> "当前基线（进入时实读）：{1}"

原因：`_perCoreBaseline` 记的是"打开窗口时实读到的每核值"（每核区说明行的字面意思就是
"进入时实读"），它不会因为一次「重置」而变成别的 —— 所以标签必须点明来源，
不能只写"当前基线"让人以为是"此刻的 SMU 现状"。

只改这两个键的 <value>，其它键与文件其余部分不动。
运行：py D:\\openLM\\tools\\uifix_resx2.py
"""
import io
import os
import re
import sys

EN = r"D:\openLM\source\MechrevoLite.Properties.Strings.resx"
ZH = r"D:\openLM\source\MechrevoLite.Properties.Strings.zh-CN.resx"

REPLACEMENTS = {
    "CpuTuneAutoApplyConfirm": [
        ("Current baseline: {1}", "Current baseline (read when the window opened): {1}"),
        ("当前基线：{1}", "当前基线（进入时实读）：{1}"),
    ],
    "CpuTuneResetConfirm": [
        ("Current baseline: {1}", "Current baseline (read when the window opened): {1}"),
        ("当前基线：{1}", "当前基线（进入时实读）：{1}"),
    ],
}


def patch(path, index):
    with io.open(path, "r", encoding="utf-8", newline="") as handle:
        text = handle.read()
    changed = 0
    for key, pairs in REPLACEMENTS.items():
        pattern = re.compile(r'<data name="%s" xml:space="preserve">.*?</data>' % re.escape(key), re.S)
        match = pattern.search(text)
        if not match:
            raise SystemExit("FATAL: key not found: " + key)
        block = match.group(0)
        new_block = block
        old, new = pairs[index]
        if old not in new_block:
            if new in new_block:
                print("skip (already patched): " + key)
                continue
            raise SystemExit("FATAL: text not found in %s: %r" % (key, old))
        new_block = new_block.replace(old, new, 1)
        text = text[:match.start()] + new_block + text[match.end():]
        changed += 1
    with io.open(path, "w", encoding="utf-8", newline="") as handle:
        handle.write(text)
    print("patched %s: %d block(s)" % (os.path.basename(path).encode("ascii", "replace").decode(), changed))


if __name__ == "__main__":
    patch(EN, 0)
    patch(ZH, 1)
    sys.exit(0)
