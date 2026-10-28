# -*- coding: utf-8 -*-
"""看清 resx 的“改动”到底是什么：字符级差异 + 行尾/BOM 体检。"""
import sys, re, zipfile, io
sys.stdout.reconfigure(encoding="utf-8", errors="replace")

Z8 = r"D:\openLM\dist\LM-0.291.6-beta22.6-source-20261010-r8.zip"
TOP8 = "LM-0.291.6-beta22.6-source-20261010-r8/"
PAIRS = [
    (TOP8 + "source/MechrevoLite.Properties.Strings.resx",
     r"D:\openLM\source\MechrevoLite.Properties.Strings.resx"),
    (TOP8 + "source/MechrevoLite.Properties.Strings.zh-CN.resx",
     r"D:\openLM\source\MechrevoLite.Properties.Strings.zh-CN.resx"),
]


def keys(t):
    return {m.group(1): m.group(2) for m in
            re.finditer(r'<data name="([^"]+)"[^>]*>\s*<value>(.*?)</value>', t, re.S)}


z = zipfile.ZipFile(Z8)
for zn, disk in PAIRS:
    ab = z.read(zn)
    bb = io.open(disk, "rb").read()
    print("== %s" % zn.split("/")[-1])
    print("   r8  : %d B  CRLF=%d  LF=%d  BOM=%s" % (
        len(ab), ab.count(b"\r\n"), ab.count(b"\n"), ab[:3] == b"\xef\xbb\xbf"))
    print("   disk: %d B  CRLF=%d  LF=%d  BOM=%s" % (
        len(bb), bb.count(b"\r\n"), bb.count(b"\n"), bb[:3] == b"\xef\xbb\xbf"))
    a = ab.decode("utf-8-sig", "replace")
    b = bb.decode("utf-8-sig", "replace")
    ka, kb = keys(a), keys(b)
    chg = [k for k in kb if k in ka and ka[k] != kb[k]]
    for k in chg[:2]:
        print("   ~ %s" % k)
        print("      OLD repr: %s" % repr(ka[k][:120]))
        print("      NEW repr: %s" % repr(kb[k][:120]))
    # 统计：仅“空白/换行”不同 的键数
    ws = 0
    for k in chg:
        na = re.sub(r'\s+', ' ', ka[k]).strip()
        nb = re.sub(r'\s+', ' ', kb[k]).strip()
        if na != nb:
            ws += 1
            print("   ! 语义不同的键 %s" % k)
            print("      OLD: %s" % na[:140])
            print("      NEW: %s" % nb[:140])
    print("   改动键 %d，其中语义不同 %d（其余仅空白/换行差异）" % (len(chg), ws))
z.close()
