# -*- coding: utf-8 -*-
r"""
repack12 (0.291.7.2 / beta22.7.2) 解包逐项核对。
解包：innounp.exe -x -q -d%TEMP%\r12_unpack <setup>
⚠️ innounp 按**源文件名**落盘（DestName 只在目标机生效）：
   LICENSE,1.txt / LICENSE,2.txt / THIRD_PARTY_NOTICES,1.txt / THIRD_PARTY_NOTICES,2.txt，
   安装后才变成 {app}\LICENSE.txt / THIRD_PARTY_NOTICES.txt。
"""
import sys, os, io, hashlib, re

sys.stdout.reconfigure(encoding="utf-8", errors="replace")

D = os.path.join(os.environ["TEMP"], "r12_unpack")
APP = os.path.join(D, "{app}")
BUILD = r"D:\openLM\source\bin\Release\net10.0-windows10.0.19041.0"
FILES = r"D:\openLM\installer-src\files"
ISS = r"D:\openLM\installer-src\install_script.iss"

FAIL = []


def sha(p):
    h = hashlib.sha256()
    with open(p, "rb") as f:
        for b in iter(lambda: f.read(1 << 20), b""):
            h.update(b)
    return h.hexdigest().upper()


def rel(p):
    return os.path.relpath(p, APP).replace("/", "\\")


def allfiles(root):
    out = []
    for dp, _d, fs in os.walk(root):
        for f in fs:
            out.append(os.path.join(dp, f))
    return out


def chk(ok, label, detail=""):
    print("   [%s] %s%s" % ("OK " if ok else "FAIL", label, ("  " + detail) if detail else ""))
    if not ok:
        FAIL.append(label)


print("=== ① 9 个 usbmmidd_v2 文件（含 Win32\\ / x64\\）===")
UM = os.path.join(APP, "usbmmidd_v2")
want_um = [
    "deviceinstaller.exe", "deviceinstaller64.exe", "idd_instructions.txt", "License.txt",
    "usbmmidd.bat", "usbmmidd.cat", "usbmmIdd.inf",
    r"Win32\usbmmIdd.dll", r"x64\usbmmIdd.dll",
]
got_um = sorted(rel(p)[len("usbmmidd_v2\\"):] for p in allfiles(UM)) if os.path.isdir(UM) else []
chk(len(want_um) == 9, "期望清单 9 项", "实得 %d" % len(got_um))
chk(sorted(want_um) == sorted(got_um), "包内 9 项与清单逐名一致",
    "缺 %s / 多 %s" % (sorted(set(want_um) - set(got_um)), sorted(set(got_um) - set(want_um))))
for n in (r"Win32\usbmmIdd.dll", r"x64\usbmmIdd.dll"):
    p = os.path.join(UM, n)
    chk(os.path.isfile(p), n, "%d B" % os.path.getsize(p) if os.path.isfile(p) else "MISSING")

print("\n=== ② 四张收款码在 {app}\\Resources ===")
RES = os.path.join(APP, "Resources")
for n in ("qrcode1.jpg", "qrcode2.jpg", "qrcode3.png", "qrcode4.png"):
    p = os.path.join(RES, n)
    chk(os.path.isfile(p), n, "%d B  ==files:%s" % (
        os.path.getsize(p), sha(p) == sha(os.path.join(FILES, "Resources", n))) if os.path.isfile(p) else "MISSING")

print("\n=== ③ 包内 THIRD-PARTY-NOTICES.md / .txt 无「18 files」 ===")
for n in ("THIRD-PARTY-NOTICES.md", "THIRD_PARTY_NOTICES,1.txt", "THIRD_PARTY_NOTICES,2.txt"):
    p = os.path.join(APP, n)
    if not os.path.isfile(p):
        chk(False, n, "MISSING"); continue
    t = io.open(p, encoding="utf-8-sig", errors="replace").read()
    chk(len(re.findall(r"18\s*files", t)) == 0, n, "%d B, 0 处 '18 files'" % os.path.getsize(p))

print("\n=== ④ 随包 更新日志.txt 含到 0.291.7 的条目 ===")
p = os.path.join(APP, "更新日志.txt")
t = io.open(p, encoding="utf-8-sig", errors="replace").read()
chk("0.291.7" in t, "含 0.291.7", "命中 %d 处" % t.count("0.291.7"))
chk("0.291.6" in t, "含 0.291.6（历史留档）")
chk(sha(p) == sha(os.path.join(FILES, "更新日志.txt")), "== files\\更新日志.txt")

print("\n=== ⑤ 6 核心文件：包内 == 构建产物 == files\\ ===")
CORE = ["L-Mechrevo.dll", "L-Mechrevo.exe", "L-Mechrevo.deps.json",
        "L-Mechrevo.runtimeconfig.json", "L-Mechrevo.dll.config",
        r"zh-CN\L-Mechrevo.resources.dll"]
for r in CORE:
    p = os.path.join(APP, r)
    if not os.path.isfile(p):
        chk(False, r, "MISSING"); continue
    h = sha(p); hb = sha(os.path.join(BUILD, r)); hf = sha(os.path.join(FILES, r))
    chk(h == hb == hf, r, "%s %d B  ==build:%s ==files:%s" % (h, os.path.getsize(p), h == hb, h == hf))

print("\n=== ⑥ payload 各变体数量（无 1020）===")
GP = os.path.join(APP, "GCU", "payload")
exp = {"30": 163, "40": 4, "40B": 8, "50": 7, "GCU": 97}
for v, want in exp.items():
    d = os.path.join(GP, v)
    n = len(allfiles(d)) if os.path.isdir(d) else 0
    chk(n == want, "payload\\%s" % v, "%d / 期望 %d" % (n, want))
chk(not os.path.isdir(os.path.join(GP, "1020")), "无 payload\\1020")

print("\n=== ⑦ 许可与 notices ===")
for n in ("LICENSE", r"LICENSES\GPL-3.0.txt", r"LICENSES\LGPL-2.1.txt", r"LICENSES\README.md"):
    p = os.path.join(APP, n)
    chk(os.path.isfile(p), n, "%d B" % os.path.getsize(p) if os.path.isfile(p) else "MISSING")
chk(os.path.isfile(os.path.join(APP, "LICENSE,1.txt")), "LICENSE,1.txt (=GPL 正文, DestName LICENSE.txt)")
chk(os.path.isfile(os.path.join(APP, "LICENSE,2.txt")), "LICENSE,2.txt")
chk(os.path.isfile(os.path.join(APP, "THIRD_PARTY_NOTICES,1.txt")), "THIRD_PARTY_NOTICES,1.txt")
chk(os.path.isfile(os.path.join(APP, "THIRD_PARTY_NOTICES,2.txt")), "THIRD_PARTY_NOTICES,2.txt")
chk(os.path.isfile(os.path.join(APP, "THIRD-PARTY-NOTICES.md")), "THIRD-PARTY-NOTICES.md")

print("\n=== ⑧ 开关：/KEEPVENDORCONSOLE 与 -LmOwnedOnly ===")
t = io.open(ISS, encoding="utf-8-sig", errors="replace").read()
chk("KEEPVENDORCONSOLE" in t, "iss 内含 KEEPVENDORCONSOLE")
chk("-LmOwnedOnly" in t, "iss 内含 -LmOwnedOnly")
gi = os.path.join(APP, "GCU", "Install-Gcu.ps1")
gu = os.path.join(APP, "GCU", "Uninstall-Gcu.ps1")
# ⚠️ -LmOwnedOnly 按设计**只属于 Uninstall-Gcu.ps1**（iss:449 [UninstallRun] 传参）；
#    Install-Gcu.ps1 里本就没有这个串（源码里也是 0 处）⇒ 不应拿它去查 Install-Gcu.ps1。
if os.path.isfile(gu):
    s = io.open(gu, encoding="utf-8-sig", errors="replace").read()
    chk("LmOwnedOnly" in s, "包内 Uninstall-Gcu.ps1 含 LmOwnedOnly")
want_pass = re.search(r'Uninstall-Gcu\.ps1""?\s+[^\n]*?-LmOwnedOnly', t) is not None
chk(want_pass, "iss [UninstallRun] 把 -LmOwnedOnly 传给 Uninstall-Gcu.ps1")
# ⚠️ 包内嵌 install_script.iss 是 innounp 的**重建版**：它把 Source: "files\X" 改写成 Source: "{app}\X"，
#    并补/删 [Setup] 键（WizardImageFile、VersionInfoVersion 等）⇒ 字节相等**本就不该期待**。
#    这里只核对语义等价：AppVersion / OutputBaseFilename / [Files] 的 DestDir 集合。
pis = os.path.join(D, "install_script.iss")
if os.path.isfile(pis):
    pt = io.open(pis, encoding="utf-8-sig", errors="replace").read()
    for key in ("AppVersion=0.291.7.2", "OutputBaseFilename=LM-0.291.7.2-beta22.7.2-setup"):
        chk(key in pt, "包内嵌 iss 含 " + key)
    dd = lambda s: set(re.findall(r'DestDir:\s*"([^"]+)"', s))
    # 包内嵌 iss 比源多出恰好 2 个 DestDir —— 那是 innounp 把源里
    #   Source: "files\usbmmidd_v2\*" (recursesubdirs createallsubdirs) 展开成逐文件后的
    #   {app}\usbmmidd_v2\Win32 / x64 ⇒ 正是"9 个驱动文件含两个子目录都真在包里"的正面证据。
    WILD = {r"{app}\usbmmidd_v2\Win32", r"{app}\usbmmidd_v2\x64"}
    a, b = dd(t), dd(pt)
    chk(b >= a and (b - a) == WILD, "包内嵌 iss 的 DestDir ⊇ 源（额外仅为 usbmmidd 通配符展开）",
        "源 %d / 包 %d，额外 %s" % (len(a), len(b), sorted(b - a)))

print("\n=== ⑨ 集合差（包内 vs files\\）===")
pk = set(rel(p) for p in allfiles(APP))
fk = set()
for p in allfiles(FILES):
    fk.add(os.path.relpath(p, FILES).replace("/", "\\"))
# ⚠️ Windows 不区分大小写：[Files] 写 "files\Fonts\..."，而磁盘目录是 files\fonts\ ⇒ 仅大小写之差，非真差。
CASEC = ("fonts\\harmonyos_sans_sc_bold.ttf", "fonts\\harmonyos_sans_sc_regular.ttf")
only_pkg = sorted(k for k in pk - fk if k.lower() not in CASEC)
only_files = sorted(k for k in fk - pk if k.lower() not in CASEC)
print("   （已排除仅大小写之差 2 项：Fonts\\ vs files\\fonts\\）")
print("   包内有而 files\\ 无 %d 项：" % len(only_pkg))
for k in only_pkg:
    print("      " + k)
print("   files\\ 有而包内无 %d 项：" % len(only_files))
for k in only_files:
    print("      " + k)
print("   包内总文件 %d ; files\\ 总文件 %d" % (len(pk), len(fk)))

print("\n=== 汇总 ===")
print("FAIL 项数 = %d" % len(FAIL))
for f in FAIL:
    print("   FAIL " + f)
sys.exit(1 if FAIL else 0)
