# -*- coding: utf-8 -*-
"""2026-10-05 收紧「核显模式显示设置」单问对话框：文案同步。

1) 改写 IgpuOnlyGuideIntro（该键只被单问模式 igpuOnlyStepOnly 使用，
   完整引导用的是 FirstRunIntro，故完整模式文案不受影响）-> 一行短句。
2) 新增 FirstRunStep4BodyShort：单问模式专用的短正文（保留
   「BIOS 或官方控制台」与「集显模式 / 核显模式 / iGPU Only」关键词）；
   完整引导继续用原 FirstRunStep4Body，一字不改。
3) 同步 Strings.cs 的强类型属性。

两个 resx 均为 UTF-8 带 BOM + LF，逐条 <data> 空白排版与原文件一致；
Strings.cs 为无 BOM + LF。
"""
import re
import sys

RESX_EN = r"D:\openLM\source\MechrevoLite.Properties.Strings.resx"
RESX_ZH = r"D:\openLM\source\MechrevoLite.Properties.Strings.zh-CN.resx"
STRINGS_CS = r"D:\openLM\source\MechrevoLite.Properties\Strings.cs"

BLOCK = '<data name="{name}" xml:space="preserve">\n    \n    \n    <value>{value}</value>\n    \n  \n  </data>\n'
ANCHOR = '<data name="FirstRunStep4No" xml:space="preserve">'

TARGETS = [
    (
        RESX_EN,
        "One quick question \u2014 you can change the answer any time in Settings.",
        "You can change the answer any time in Settings.",
        "Look in BIOS or the official console for an \"iGPU only / integrated-graphics only\" option.",
        "English",
    ),
    (
        RESX_ZH,
        "\u53ea\u9700\u56de\u7b54\u4e00\u4e2a\u95ee\u9898\uff0c\u56de\u7b54\u540e\u8fd8\u53ef\u4ee5\u5728\u8bbe\u7f6e\u91cc\u968f\u65f6\u4fee\u6539\u3002",
        "\u56de\u7b54\u540e\u4ecd\u53ef\u5728\u8bbe\u7f6e\u91cc\u968f\u65f6\u4fee\u6539\u3002",
        "\u5728 BIOS \u6216\u5b98\u65b9\u63a7\u5236\u53f0\u91cc\u627e\u300c\u96c6\u663e\u6a21\u5f0f / \u6838\u663e\u6a21\u5f0f / iGPU Only\u300d\u8fd9\u4e00\u6863\u3002",
        "Chinese",
    ),
]

NEW_KEY = "FirstRunStep4BodyShort"


def patch_resx(path, old_intro, new_intro, short_body, label):
    raw = open(path, "rb").read()
    if not raw.startswith(b"\xef\xbb\xbf"):
        print("ERROR: %s has no BOM" % path)
        return 1
    text = raw.decode("utf-8-sig")

    if ('<data name="%s"' % NEW_KEY) in text:
        print("ERROR: %s already present in %s" % (NEW_KEY, path))
        return 1
    if old_intro not in text:
        print("ERROR: old IgpuOnlyGuideIntro value not found in %s" % path)
        return 1
    if ANCHOR not in text:
        print("ERROR: anchor not found in %s" % path)
        return 1

    text = text.replace(old_intro, new_intro, 1)
    text = text.replace(ANCHOR, BLOCK.format(name=NEW_KEY, value=short_body) + ANCHOR, 1)

    with open(path, "wb") as fh:
        fh.write(b"\xef\xbb\xbf" + text.encode("utf-8"))
    print("%s: intro shortened + %s added" % (label, NEW_KEY))
    return 0


def patch_strings_cs(path):
    raw = open(path, "rb").read()
    if raw.startswith(b"\xef\xbb\xbf"):
        print("ERROR: %s unexpectedly has BOM" % path)
        return 1
    text = raw.decode("utf-8")

    if ('"%s"' % NEW_KEY) in text:
        print("ERROR: %s already present in %s" % (NEW_KEY, path))
        return 1

    anchor = '\tinternal static string FirstRunStep4Body => ResourceManager.GetString("FirstRunStep4Body", resourceCulture);\n'
    if anchor not in text:
        print("ERROR: Strings.cs anchor not found")
        return 1
    prop = '\tinternal static string %s => ResourceManager.GetString("%s", resourceCulture);\n' % (NEW_KEY, NEW_KEY)
    text = text.replace(anchor, anchor + "\n" + prop, 1)

    with open(path, "wb") as fh:
        fh.write(text.encode("utf-8"))
    print("Strings.cs: property %s added" % NEW_KEY)
    return 0


def count_keys(path, is_cs):
    """键数校验：resx 数 <data name=；Strings.cs 只数「属性 = GetString("键")」形式
    （文件里还有 DeepSleepResult(bool) 这类非属性 GetString，不能一起数）。"""
    raw = open(path, "rb").read()
    text = raw.decode("utf-8-sig" if raw.startswith(b"\xef\xbb\xbf") else "utf-8")
    if is_cs:
        return len(re.findall(r'^\tinternal static string [A-Za-z0-9_]+ => ResourceManager\.GetString\("[A-Za-z0-9_]+", resourceCulture\);$', text, re.M))
    return text.count("<data name=")


def main():
    for path, old_intro, new_intro, short_body, label in TARGETS:
        rc = patch_resx(path, old_intro, new_intro, short_body, label)
        if rc != 0:
            return rc
    rc = patch_strings_cs(STRINGS_CS)
    if rc != 0:
        return rc

    counts = [
        ("Strings.resx", count_keys(RESX_EN, False)),
        ("Strings.zh-CN.resx", count_keys(RESX_ZH, False)),
        ("Strings.cs", count_keys(STRINGS_CS, True)),
    ]
    for name, n in counts:
        print("%-18s keys = %d" % (name, n))
    if len(set(n for _, n in counts)) != 1:
        print("ERROR: key counts differ")
        return 1
    print("OK: key counts consistent")
    return 0


if __name__ == "__main__":
    sys.exit(main())
