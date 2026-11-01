# -*- coding: utf-8 -*-
"""2026-10-05 实现「核显模式显示策略」①~⑥：为两个 resx 追加新增文案。
两者均为 UTF-8 带 BOM + LF，逐条 <data> 块的空白排版必须与原文件一致。
"""
import io
import sys

BLOCK = '<data name="{name}" xml:space="preserve">\n    \n    \n    <value>{value}</value>\n    \n  \n  </data>\n'

EN = [
    ("IgpuOnlyGuideIntro", "One quick question — you can change the answer any time in Settings."),
    ("IgpuOnlyGuideTitle", "iGPU-only mode display"),
    ("IgpuOnlyShowToggle", "Show iGPU-only mode"),
    ("IgpuOnlyShowToggleTip", "Show the \"iGPU-only mode\" button and tray menu item. Turn it off if your PC has no \"iGPU only / integrated-graphics only\" option in BIOS or the official console."),
    ("FirstRunStep4Body", "Look in BIOS or the official console: is there an \"iGPU only / integrated-graphics only\" option? \"Yes\" shows the iGPU-only mode button, \"No\" hides it; \"Not sure\" keeps it visible, and you can change this any time in Settings."),
    ("FirstRunStep4No", "No"),
    ("FirstRunStep4Title", "Does your PC have an \"iGPU-only\" option?"),
    ("FirstRunStep4Unknown", "Not sure"),
    ("FirstRunStep4Yes", "Yes"),
]

ZH = [
    ("IgpuOnlyGuideIntro", "只需回答一个问题，回答后还可以在设置里随时修改。"),
    ("IgpuOnlyGuideTitle", "核显模式显示设置"),
    ("IgpuOnlyShowToggle", "显示核显模式"),
    ("IgpuOnlyShowToggleTip", "在「系统」页显示「核显模式」按钮与托盘菜单项。若你的电脑在 BIOS 或官方控制台里没有「集显模式 / 核显模式 / iGPU Only」这一档，可以关掉它。"),
    ("FirstRunStep4Body", "请在 BIOS 或官方控制台里找一下：有没有「集显模式 / 核显模式 / iGPU Only」这一档？选「有」会显示「核显模式」按钮，选「没有」会隐藏它；选「不确定」保持显示，之后可在设置里随时修改。"),
    ("FirstRunStep4No", "没有"),
    ("FirstRunStep4Title", "你的电脑有「集显模式」这一档吗？"),
    ("FirstRunStep4Unknown", "不确定"),
    ("FirstRunStep4Yes", "有"),
]

TARGETS = [
    (r"D:\openLM\source\MechrevoLite.Properties.Strings.resx", EN, "English"),
    (r"D:\openLM\source\MechrevoLite.Properties.Strings.zh-CN.resx", ZH, "Chinese"),
]

GROUPS = [
    (("IgpuOnlyGuideIntro", "IgpuOnlyGuideTitle", "IgpuOnlyShowToggle", "IgpuOnlyShowToggleTip"),
     '<data name="IgpuShort" xml:space="preserve">'),
    (("FirstRunStep4Body", "FirstRunStep4No", "FirstRunStep4Title", "FirstRunStep4Unknown", "FirstRunStep4Yes"),
     '<data name="FirstRunWelcome" xml:space="preserve">'),
]


def run():
    for path, entries, label in TARGETS:
        raw = open(path, "rb").read()
        if not raw.startswith(b"\xef\xbb\xbf"):
            print("ERROR: %s has no BOM" % path)
            return 1
        text = raw.decode("utf-8-sig")
        values = dict(entries)
        for names, anchor in GROUPS:
            if anchor not in text:
                print("ERROR: anchor not found in %s: %s" % (path, anchor))
                return 1
            for name in names:
                if ('<data name="%s"' % name) in text:
                    print("ERROR: %s already present in %s" % (name, path))
                    return 1
            block = "".join(BLOCK.format(name=n, value=values[n]) for n in names)
            text = text.replace(anchor, block + anchor, 1)
        out = text.encode("utf-8")
        with open(path, "wb") as fh:
            fh.write(b"\xef\xbb\xbf" + out)
        print("%s: %d entries added" % (label, len(entries)))
    return 0


if __name__ == "__main__":
    sys.exit(run())
