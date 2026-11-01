# -*- coding: utf-8 -*-
r"""
repack9 步骤⑨：用 innounp 真解包目录逐项核对。
解包口令：innounp.exe -x -q -d%TEMP%\r9_unpack <setup>
⚠️ innounp 按**源文件名**落盘（DestName 只在目标机生效）：
   {app}\LICENSE,1.txt / LICENSE,2.txt / THIRD_PARTY_NOTICES,1.txt / THIRD_PARTY_NOTICES,2.txt，
   安装后才变成 {app}\LICENSE.txt / THIRD_PARTY_NOTICES.txt。
"""
import sys, os, io, hashlib, xml.etree.ElementTree as ET
sys.stdout.reconfigure(encoding="utf-8", errors="replace")

D = os.path.join(os.environ["TEMP"], "r9_unpack")
APP = os.path.join(D, "{app}")
BUILD = r"D:\openLM\source\bin\Release\net10.0-windows10.0.19041.0"
FILES = r"D:\openLM\installer-src\files"
RESX_EN = r"D:\openLM\source\MechrevoLite.Properties.Strings.resx"
RESX_ZH = r"D:\openLM\source\MechrevoLite.Properties.Strings.zh-CN.resx"

REF = {  # 本轮构建产物（23:19:39）
    "L-Mechrevo.dll": "6A9EE40EC5DFBC5EC7B6DDBE8FBBB8040993AD941F0F0E75DC498CB30006C02E",
    "L-Mechrevo.exe": "4AF6EFCB6986CCC45FB4524E476622E3032EC0347D6376C77BC71160BF7E0380",
    r"zh-CN\L-Mechrevo.resources.dll": "CBF5B3D5D519EFC463837F0955D80367D2A2F00BAC41F471F0CF826BBA0AB30A",
}


def sha(p):
    return hashlib.sha256(open(p, "rb").read()).hexdigest().upper()


def sz(p):
    return os.path.getsize(p)


def resx_keys(path):
    out = {}
    root = ET.parse(path).getroot()
    for d in root.findall("data"):
        v = d.find("value")
        out[d.get("name")] = "" if v is None or v.text is None else v.text
    return out


print("=== 1) 主程序三件（== 当前构建产物 / == 记录哈希） ===")
for rel, ref in REF.items():
    p = os.path.join(APP, rel)
    if not os.path.isfile(p):
        print("   MISSING", rel); continue
    h = sha(p)
    b = os.path.join(BUILD, rel)
    print("   %-34s %s %d B  ==build:%s  ==expected:%s" % (
        rel, h, sz(p), h == sha(b), h == ref))

print("\n=== 2) 本轮 resx 新增/改动文案在包内的 UTF-8 读回 ===")
en = resx_keys(RESX_EN)
zh = resx_keys(RESX_ZH)
CASES = [
    ("CpuTuneFreqNote", "L-Mechrevo.dll", en.get("CpuTuneFreqNote", "")),
    (r"zh-CN CpuTuneFreqNote", r"zh-CN\L-Mechrevo.resources.dll", zh.get("CpuTuneFreqNote", "")),
    (r"zh-CN CpuTuneSummaryPerCoreOff", r"zh-CN\L-Mechrevo.resources.dll", zh.get("CpuTuneSummaryPerCoreOff", "")),
    (r"zh-CN CpuTunePerCoreCodenameOnly", r"zh-CN\L-Mechrevo.resources.dll", zh.get("CpuTunePerCoreCodenameOnly", "")),
    (r"en CpuTuneValueCo", "L-Mechrevo.dll", en.get("CpuTuneValueCo", "")),
    (r"en AboutLicensesUsePawnIOIntelMsr", "L-Mechrevo.dll", en.get("AboutLicensesUsePawnIOIntelMsr", "")),
    (r"zh-CN AboutLicensesUsePawnIOIntelMsr", r"zh-CN\L-Mechrevo.resources.dll",
     zh.get("AboutLicensesUsePawnIOIntelMsr", "")),
]
for label, rel, val in CASES:
    p = os.path.join(APP, rel)
    data = open(p, "rb").read()
    key = label.split()[-1]
    print("   [%s] %s" % (rel, label))
    print("      key(ascii)=%s  key(utf16le)=%s  value utf8=%s  value utf16le=%s（UTF-16 检索仅作对照）" % (
        key.encode("ascii") in data,
        key.encode("utf-16-le") in data,
        val.encode("utf-8") in data,
        val.encode("utf-16-le") in data))
    print("      值原文: %s" % val.replace("\n", " ")[:120])

print("\n=== 3) 逐变体 文件数/总字节（包内 {app}\\GCU\\payload） ===")
pl = os.path.join(APP, "GCU", "payload")
tot_f = tot_b = 0
REFV = {"GCU": (97, 33844501), "30": (163, 27406159), "40": (4, 17491696),
        "40B": (8, 23858360), "50": (7, 21566056)}
for v in sorted(os.listdir(pl)):
    vd = os.path.join(pl, v)
    if not os.path.isdir(vd):
        continue
    fs = [os.path.join(dp, f) for dp, dn, fn in os.walk(vd) for f in fn]
    tb = sum(sz(x) for x in fs)
    tot_f += len(fs); tot_b += tb
    rf, rb = REFV.get(v, (None, None))
    print("   %-5s %4d 文件 / %s B   参考 %s / %s  %s" % (
        v, len(fs), format(tb, ","),
        rf if rf is not None else "-", format(rb, ",") if rb is not None else "-",
        "OK" if (len(fs), tb) == (rf, rb) else "DIFF"))
print("   合计  %4d 文件 / %s B   参考 279 / 124,166,772  %s" % (
    tot_f, format(tot_b, ","), "OK" if (tot_f, tot_b) == (279, 124166772) else "DIFF"))

print("\n=== 4) payload\\50 SQLiteDB / 全包 -shm -wal 扫描 ===")
sq = os.path.join(pl, "50", "AiStoneService", "MyControlCenter", "SQLiteDB")
print("   SQLiteDB 目录内容:", sorted(os.listdir(sq)) if os.path.isdir(sq) else "MISSING")
hits = []
for dp, dn, fn in os.walk(D):
    for f in fn:
        if f.lower().endswith(("-shm", "-wal")) or "-shm" in f.lower() or "-wal" in f.lower():
            hits.append(os.path.join(dp, f))
print("   含 -shm/-wal 的文件: %d 个 %s" % (len(hits), hits if hits else "（无，符合预期）"))

print("\n=== 5) 许可文件（根 LICENSE + LICENSES\\ 三件 + PawnIO 两件 + 清单 .md） ===")
SRCMAP = {
    "LICENSE": r"D:\openLM\LICENSE",
    "LICENSE,1.txt": os.path.join(FILES, "LICENSE,1.txt"),
    "LICENSE,2.txt": os.path.join(FILES, "LICENSE,2.txt"),
    r"LICENSES\GPL-3.0.txt": r"D:\openLM\LICENSES\GPL-3.0.txt",
    r"LICENSES\LGPL-2.1.txt": r"D:\openLM\LICENSES\LGPL-2.1.txt",
    r"LICENSES\README.md": r"D:\openLM\LICENSES\README.md",
    "THIRD-PARTY-NOTICES.md": r"D:\openLM\THIRD-PARTY-NOTICES.md",
    "COPYING.PawnIO.txt": os.path.join(FILES, "COPYING.PawnIO.txt"),
    "Install-PawnIO.ps1": os.path.join(FILES, "Install-PawnIO.ps1"),
}
for rel, src in SRCMAP.items():
    p = os.path.join(APP, rel)
    if not os.path.isfile(p):
        print("   %-28s MISSING" % rel); continue
    same = os.path.isfile(src) and open(p, "rb").read() == open(src, "rb").read()
    print("   %-28s %8d B  ==磁盘源(%s): %s" % (rel, sz(p), os.path.basename(src), same))

print("\n=== 6) 两份 THIRD_PARTY_NOTICES 是否点名 LMechrevo.IntelMSR.bin ===")
for n in ("THIRD_PARTY_NOTICES,1.txt", "THIRD_PARTY_NOTICES,2.txt"):
    p = os.path.join(APP, n)
    raw = open(p, "rb").read()
    t = raw.decode("utf-8", "replace")
    src = os.path.join(FILES, n)
    print("   %-28s %d B  BOM=%s  CRLF=%d/LF=%d" % (
        n, len(raw), raw[:3] == b"\xef\xbb\xbf", raw.count(b"\r\n"), raw.count(b"\n")))
    for k in ("LMechrevo.IntelMSR.bin", "5,324", "D6ED85D65AB17A22F813EF98207D6D537155EE2DED5976A21CB48413C9B92E5F",
              "LGPL-2.1-or-later", "namazso/PawnIO.Modules", "RyzenSMU.bin"):
        print("      %-70s %s" % (k, k in t))
    print("      ==磁盘源同一份: %s" % (open(src, "rb").read() == raw))

print("\n=== 7) GCU 脚本参数 / 磁盘一致 ===")
for rel, keys in ((r"GCU\Install-Gcu.ps1", ("-RemoveVendorConsole", "-KeepVendorConsole")),
                  (r"GCU\Uninstall-Gcu.ps1", ("-LmOwnedOnly",))):
    p = os.path.join(APP, rel)
    t = open(p, "rb").read().decode("utf-8", "replace")
    dp = os.path.join(FILES, rel)
    print("   %-26s %s  %s  ==磁盘:%s" % (
        rel, sz(p), {k: (k in t) for k in keys},
        os.path.isfile(dp) and open(p, "rb").read() == open(dp, "rb").read()))

print("\n=== 8) 包内总条目 ===")
n = sum(len(fn) for dp, dn, fn in os.walk(D))
print("   %d 个文件（含 install_script.iss；{app} 下 %d 个）" % (
    n, sum(len(fn) for dp, dn, fn in os.walk(APP))))
