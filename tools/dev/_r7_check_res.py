# -*- coding: utf-8 -*-
import sys, io, os
sys.stdout.reconfigure(encoding="utf-8", errors="replace")
p = os.path.join(os.environ["TEMP"], "lm_r7_unpack", "{app}", "zh-CN", "L-Mechrevo.resources.dll")
b = open(p, "rb").read()
needles = {
    "\u81ea\u52a8\u5173\u95ed\u65f6\u95f4": "auto-close-time",
    "\u53f3\u952e\u6253\u5f00\u81ea\u5b9a\u4e49\u9762\u677f": "rightclick-custom-panel",
    "\u7248\u6743\u6240\u6709 (C) 2026": "copyright2026",
}
print("dll size", len(b))
for s, tag in needles.items():
    u16 = s.encode("utf-16-le")
    u8 = s.encode("utf-8")
    print(tag, repr(s), "utf16=", b.find(u16), "utf8=", b.find(u8))
# sanity: ascii string
print("sanity L-Mechrevo utf16=", b.find("L-Mechrevo".encode("utf-16-le")))
