# -*- coding: utf-8 -*-
"""2026-10-06 D-1：把两条新文案同步加进两个 resx + Strings.cs。

新增键（中英一致，风格对齐同文件既有条目）：
  IgpuOnlyUnverifiedHint —— 主界面显卡行右侧那句提示（短，必须能在 400px 标签里放下）
  IgpuOnlyUnverifiedTip  —— 「核显模式」按钮的 tooltip 追加说明（长）

用法：python d1_add_igpu_strings.py [--check]
  --check 只检查不写（用于复查）。
编码：两个 resx 与 Strings.cs 都是 UTF-8 **无 BOM**、LF 换行 —— 原样保持（不引入 BOM / CRLF）。
"""
import io
import os
import sys

ROOT = r"D:\openLM\source"
RESX_EN = os.path.join(ROOT, "MechrevoLite.Properties.Strings.resx")
RESX_ZH = os.path.join(ROOT, "MechrevoLite.Properties.Strings.zh-CN.resx")
STRINGS_CS = os.path.join(ROOT, "MechrevoLite.Properties", "Strings.cs")

EN = {
    "IgpuOnlyUnverifiedHint":
        "This PC may not support iGPU-only mode; try once and the result is remembered",
    "IgpuOnlyUnverifiedTip":
        "There is no reliable way to tell whether this PC supports iGPU-only mode, "
        "so this button is shown by rule of thumb. You can try it once: after the reboot, "
        "if the switch really worked I will remember it and keep the button; if it did not, "
        "I will hide the button (you can still turn it back on in Settings).",
}
ZH = {
    "IgpuOnlyUnverifiedHint":
        "本机可能不支持核显模式；可试一次，试过会记住结果",
    "IgpuOnlyUnverifiedTip":
        "本机没有可靠的办法判断是否支持核显模式，这个按钮是按经验规则显示出来的。"
        "可以点一次试试：重启后如果真切到了核显模式，我会记住并一直显示它；"
        "如果没切过去，我会把它收起来（之后仍可在设置里重新打开）。",
}

# 插入锚点：两个 resx 里 <data name="IgpuShort" ...> 之前（IgpuOnly* 组之后，字母序一致）
ANCHOR = '<data name="IgpuShort" xml:space="preserve">'


def read(path):
    with io.open(path, "r", encoding="utf-8", newline="") as fh:
        return fh.read()


def write(path, text):
    # 与既有文件一致：UTF-8 无 BOM；newline='' 保证 LF 原样
    with io.open(path, "w", encoding="utf-8", newline="") as fh:
        fh.write(text)


def parse_resx_accounting(text):
    """返回 (以 <data 开头的条目数, 以 </data> 结尾的条目数, 全部 name 列表)"""
    starts = [ln for ln in text.split("\n") if ln.startswith("<data ")]
    ends = [ln for ln in text.split("\n") if ln.strip() == "</data>"]
    names = []
    for ln in starts:
        i = ln.find('name="')
        j = ln.find('"', i + 6)
        names.append(ln[i + 6:j])
    return len(starts), len(ends), names


def entry(name, value):
    return ('<data name="%s" xml:space="preserve">\n'
            '    <value>%s</value>\n'
            '  </data>\n') % (name, value)


def add_to_resx(path, table, check):
    text = read(path)
    missing = [k for k in table if ('name="%s"' % k) not in text]
    if not missing:
        print("  [skip] %s : already present" % os.path.basename(path))
        return
    if ANCHOR not in text:
        raise SystemExit("anchor not found in " + path)
    if text.count(ANCHOR) != 1:
        raise SystemExit("anchor not unique in " + path)
    assert text.count("\r\n") == 0, "unexpected CRLF in " + path
    block = "".join(entry(k, table[k]) for k in sorted(table))
    new = text.replace(ANCHOR, block + ANCHOR, 1)
    s0, e0, n0 = parse_resx_accounting(text)
    s1, e1, n1 = parse_resx_accounting(new)
    print("  %s : data %d -> %d ; </data> %d -> %d ; added=%s"
          % (os.path.basename(path), s0, s1, e0, e1, ",".join(sorted(table))))
    assert s1 == s0 + len(table) and e1 == e0 + len(table)
    assert len(n1) == len(set(n1)), "duplicate resource name"
    if not check:
        write(path, new)


def add_to_strings_cs(table, check):
    text = read(STRINGS_CS)
    missing = [k for k in table if ("GetString(\"%s\"" % k) not in text]
    if not missing:
        print("  [skip] Strings.cs : already present")
        return
    anchor = '\tinternal static string IgpuShort => ResourceManager.GetString("IgpuShort", resourceCulture);\n'
    if anchor not in text:
        raise SystemExit("Strings.cs anchor not found")
    block = "".join(
        '\tinternal static string %s => ResourceManager.GetString("%s", resourceCulture);\n\n'
        % (k, k) for k in sorted(table))
    new = text.replace(anchor, block + anchor, 1)
    print("  Strings.cs : +%d accessor(s): %s" % (len(table), ",".join(sorted(table))))
    if not check:
        write(STRINGS_CS, new)


def main():
    check = "--check" in sys.argv
    print("D-1 iGPU-only strings (%s)" % ("check only" if check else "write"))
    add_to_resx(RESX_EN, EN, check)
    add_to_resx(RESX_ZH, ZH, check)
    add_to_strings_cs(dict(list(EN.items()) + list(ZH.items())), check)
    print("done")


if __name__ == "__main__":
    main()
