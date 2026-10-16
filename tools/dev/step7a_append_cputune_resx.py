# -*- coding: utf-8 -*-
"""Step 7A: append the upstream CpuTune* resource keys to our Strings resx files.

Add-only: existing keys are never touched or reordered.
"""
import io
import os
import re
import sys

UP_EN = r"D:\openLM\_upstream\0.290.6\decomp\MechrevoLite.Properties.Strings.resx"
UP_ZH = r"D:\openLM\_upstream\0.290.6\_zhcn_decomp\MechrevoLite.Properties.Strings.zh-CN.resx"
OUR_EN = r"D:\openLM\source\MechrevoLite.Properties.Strings.resx"
OUR_ZH = r"D:\openLM\source\MechrevoLite.Properties.Strings.zh-CN.resx"

DATA_RE = re.compile(
    r'<data name="(?P<name>[^"]+)"[^>]*>\s*<value>(?P<value>.*?)</value>\s*</data>',
    re.S,
)

# Our own two debug-switch keys (not present upstream).
EXTRA = {
    "CpuTuneForcedMark": {
        "en": " (forced display)",
        "zh": "（强制显示）",
    },
    "CpuTuneForcedNotice": {
        "en": ("Debug switch LMECHREVO_FORCE_CPU_TUNING is on: this window is shown by force, "
               "not by capability detection. Apply / Restore still follow the detected "
               "capability and stay disabled when no writable interface is found."),
        "zh": ("调试开关 LMECHREVO_FORCE_CPU_TUNING 已启用：本窗口为强制显示，并非能力检测结果。"
               "应用/恢复按钮仍完全按检测结果决定，未检测到可写接口时保持禁用。"),
    },
}


def read(path):
    with io.open(path, "r", encoding="utf-8-sig") as handle:
        return handle.read()


def parse(path):
    text = read(path)
    pairs = {}
    for match in DATA_RE.finditer(text):
        pairs[match.group("name")] = match.group("value")
    return pairs


def render(name, value):
    return '<data name="%s" xml:space="preserve">\n    <value>%s</value>\n  </data>\n' % (name, value)


def append(path, entries):
    text = read(path)
    existing = parse(path)
    added = []
    block = []
    for name, value in entries:
        if name in existing:
            continue
        block.append(render(name, value))
        added.append(name)
    if not block:
        print("no-op (all keys present): %s" % path)
        return added
    idx = text.rfind("</root>")
    if idx < 0:
        raise SystemExit("no </root> in %s" % path)
    text = text[:idx] + "".join(block) + text[idx:]
    with io.open(path, "w", encoding="utf-8-sig", newline="") as handle:
        handle.write(text)
    print("appended %d keys to %s" % (len(added), path))
    return added


def main():
    up_en = parse(UP_EN)
    up_zh = parse(UP_ZH)
    names = sorted(n for n in up_en if n.startswith("CpuTune"))
    print("upstream CpuTune keys: %d" % len(names))
    missing_zh = [n for n in names if n not in up_zh]
    if missing_zh:
        print("WARNING: zh-CN missing %s" % missing_zh)

    en_entries = [(n, up_en[n]) for n in names]
    zh_entries = [(n, up_zh.get(n, up_en[n])) for n in names]
    for name, texts in EXTRA.items():
        en_entries.append((name, texts["en"]))
        zh_entries.append((name, texts["zh"]))

    added_en = append(OUR_EN, en_entries)
    added_zh = append(OUR_ZH, zh_entries)
    print("EN added: %s" % added_en)
    print("ZH added: %s" % added_zh)

    # sanity: reload and verify
    en_after = parse(OUR_EN)
    zh_after = parse(OUR_ZH)
    bad = [n for n, _ in en_entries if n not in en_after]
    bad += [n for n, _ in zh_entries if n not in zh_after]
    print("verify missing after append: %s" % (bad or "none"))
    print("key counts: en=%d zh=%d" % (len(en_after), len(zh_after)))


if __name__ == "__main__":
    sys.exit(main())
