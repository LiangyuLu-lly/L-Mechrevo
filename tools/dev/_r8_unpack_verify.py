# -*- coding: utf-8 -*-
"""r8 安装包 innounp 解包逐项核对"""
import sys, os, hashlib, collections
sys.stdout.reconfigure(encoding="utf-8", errors="replace")

D = os.path.join(os.environ["TEMP"], "r8_unpack")
APP = os.path.join(D, "{app}")

BUILD = r"D:\openLM\source\bin\Release\net10.0-windows10.0.19041.0"


def sha(p):
    return hashlib.sha256(open(p, "rb").read()).hexdigest().upper()


def sz(p):
    return os.path.getsize(p)


print("=== 1) 主程序三件 ===")
for rel, ref in (("L-Mechrevo.dll", "771EF2A4941A097D87ECA9E5646068C8F284AC4AE8B59B92FE147F7085C2DE27"),
                 ("L-Mechrevo.exe", "4AF6EFCB6986CCC45FB4524E476622E3032EC0347D6376C77BC71160BF7E0380"),
                 (r"zh-CN\L-Mechrevo.resources.dll", "2D2CF5817E8F80BCEDF659CC4C3937FA92481EF6902C5CBA2E24D3FA7F33C0E4")):
    p = os.path.join(APP, rel)
    if not os.path.isfile(p):
        print("  MISSING", rel); continue
    h = sha(p)
    b = os.path.join(BUILD, rel)
    print("  %-34s %s  %d B  ==build:%s  ==ref:%s" % (rel, h, sz(p), h == sha(b), h == ref))

print("\n=== 2) exe FileVersion ===")
print("  ", open(os.path.join(APP, "L-Mechrevo.exe"), "rb").read()[:0] or "见下（用 pwsh 读 VersionInfo）")

print("\n=== 3) 新 resx 文案（UTF-8 字节检索） ===")
en = "The IntelMSR module (LMechrevo.IntelMSR.bin) is embedded unmodified in L-Mechrevo.dll and provides the kernel-mediated MSR 0x150 OC-mailbox access used for Intel CPU tuning."
zh = "IntelMSR 模块（LMechrevo.IntelMSR.bin）以**未经修改**的形式内嵌在 L-Mechrevo.dll 中，为 Intel CPU 调节提供经内核转发的 MSR 0x150 超频邮箱访问。"
key = "AboutLicensesUsePawnIOIntelMsr"
for rel in ("L-Mechrevo.dll", r"zh-CN\L-Mechrevo.resources.dll"):
    p = os.path.join(APP, rel)
    data = open(p, "rb").read()
    print("  %-34s key=%s  en_utf8=%s  zh_utf8=%s  en_utf16=%s" % (
        rel, key.encode("ascii") in data, en.encode("utf-8") in data,
        zh.encode("utf-8") in data, en.encode("utf-16-le") in data))

print("\n=== 4) 逐变体文件数/总字节（{app}\\GCU\\payload） ===")
pl = os.path.join(APP, "GCU", "payload")
tot_f = tot_b = 0
for v in sorted(os.listdir(pl)):
    vd = os.path.join(pl, v)
    if not os.path.isdir(vd):
        continue
    fs = []
    for dp, dn, fn in os.walk(vd):
        for f in fn:
            fs.append(os.path.join(dp, f))
    tb = sum(sz(x) for x in fs)
    tot_f += len(fs); tot_b += tb
    print("  %-5s %4d 文件 / %,d B".replace(",d", "d") % (v, len(fs), tb) if False else
          "  %-5s %4d 文件 / %s B" % (v, len(fs), format(tb, ",")))
print("  合计  %4d 文件 / %s B" % (tot_f, format(tot_b, ",")))

print("\n=== 5) payload\\50 SQLiteDB ===")
sq = os.path.join(pl, "50", "AiStoneService", "MyControlCenter", "SQLiteDB")
if os.path.isdir(sq):
    for f in sorted(os.listdir(sq)):
        print("   present: %s  %d B" % (f, sz(os.path.join(sq, f))))
else:
    print("   MISSING dir", sq)

print("\n=== 6) 许可文件 ===")
for rel in ("LICENSE", "LICENSE.txt", r"LICENSES\GPL-3.0.txt", r"LICENSES\LGPL-2.1.txt", r"LICENSES\README.md",
            "COPYING.PawnIO.txt", "Install-PawnIO.ps1"):
    p = os.path.join(APP, rel)
    print("   %-28s %s" % (rel, ("%d B" % sz(p)) if os.path.isfile(p) else "MISSING"))

print("\n=== 7) 两份 THIRD_PARTY_NOTICES（解包后同 DestName，只落一份） ===")
p = os.path.join(APP, "THIRD_PARTY_NOTICES.txt")
t = open(p, encoding="utf-8", errors="replace").read()
for k in ("SMUDebugTool", "intel-undervolt", "RyzenSMU.bin", "IntelMSR.bin", "namazso", "LGPL-2.1"):
    print("   %-16s %s" % (k, k in t))
src = r"D:\openLM\installer-src\files"
for n in ("THIRD_PARTY_NOTICES,1.txt", "THIRD_PARTY_NOTICES,2.txt"):
    s = open(os.path.join(src, n), encoding="utf-8", errors="replace").read()
    print("   源 %s 含 SMUDebugTool=%s ; 落盘副本==源%s : %s" % (
        n, "SMUDebugTool" in s, n[-5], s == t))

print("\n=== 8) GCU 脚本参数 ===")
for rel, keys in ((r"GCU\Install-Gcu.ps1", ("-RemoveVendorConsole", "-KeepVendorConsole")),
                  (r"GCU\Uninstall-Gcu.ps1", ("-LmOwnedOnly",))):
    p = os.path.join(APP, rel)
    t = open(p, encoding="utf-8", errors="replace").read() if os.path.isfile(p) else ""
    print("   %-26s %d B  %s" % (rel, len(t.encode("utf-8")), {k: (k in t) for k in keys}))
    dp = os.path.join(r"D:\openLM\installer-src\files", rel)
    print("        磁盘一致=%s" % (os.path.isfile(p) and open(p, "rb").read() == open(dp, "rb").read()))

print("\n=== 9) 解包总条目 ===")
n = 0
for dp, dn, fn in os.walk(D):
    n += len(fn)
print("   %d 个文件（含 install_script.iss）" % n)

print("\n=== 10) 参考口径比对 ===")
REF = {"GCU": (97, 33844501), "30": (163, 27406159), "40": (4, 17491696),
       "40B": (8, 23858360), "50": (7, 21566056)}
for v, (rf, rb) in REF.items():
    vd = os.path.join(pl, v)
    fs = [os.path.join(dp, f) for dp, dn, fn in os.walk(vd) for f in fn] if os.path.isdir(vd) else []
    tb = sum(sz(x) for x in fs)
    print("   %-5s 实测 %4d / %s  参考 %4d / %s  %s" % (
        v, len(fs), format(tb, ","), rf, format(rb, ","), "OK" if (len(fs), tb) == (rf, rb) else "DIFF"))
print("   合计 实测 %d / %s   参考 279 / 124,166,772  %s" % (
    tot_f, format(tot_b, ","), "OK" if (tot_f, tot_b) == (279, 124166772) else "DIFF"))
