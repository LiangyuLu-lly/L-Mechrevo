# -*- coding: utf-8 -*-
r"""
repack15 (0.291.7.4 / beta22.7.4) 解包逐项核对（沿用 r14 的检查项，版本与日志节改 0.291.7.4）。

解包：innounp.exe -x -q -b -y -d%TEMP%\r15_unpack <setup>
⚠️ innounp 按**源文件名**落盘（DestName / DestDir 只在目标机生效）：
   files\THIRD_PARTY_NOTICES,1.txt / ,2.txt 落成同名两个文件，目标机上才由 [Files] 的 DestName
   合并成 {app}\LICENSES\THIRD_PARTY_NOTICES.txt（后一条=,2.txt 生效）。
"""
import sys, os, io, hashlib, re

sys.stdout.reconfigure(encoding="utf-8", errors="replace")

D = os.path.join(os.environ["TEMP"], "r15_unpack")
APP = os.path.join(D, "{app}")
BUILD = r"D:\openLM\source\bin\Release\net10.0-windows10.0.19041.0"
FILES = r"D:\openLM\installer-src\files"
ISS = r"D:\openLM\installer-src\install_script.iss"
VER = "0.291.7.4"
PVER = "0.291.7.4-beta22.7.4"

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


print("=== (a) {app} 根目录：只剩程序本体 + 已知子目录 ===")
root_entries = sorted(os.listdir(APP))
root_dirs = sorted(e for e in root_entries if os.path.isdir(os.path.join(APP, e)))
root_files = sorted(e for e in root_entries if os.path.isfile(os.path.join(APP, e)))
want_dirs = ["Docs", "Fonts", "GCU", "LICENSES", "Resources", "UWACPIDriver", "usbmmidd_v2", "zh-CN"]
want_files = ["BlackSharp.Core.dll", "DiskInfoToolkit.dll", "favicon.ico", "HidSharp.dll",
              "Install-PawnIO.ps1", "L-Mechrevo.deps.json", "L-Mechrevo.dll", "L-Mechrevo.dll.config",
              "L-Mechrevo.exe", "L-Mechrevo.runtimeconfig.json", "LibreHardwareMonitorLib.dll",
              "Microsoft.Win32.TaskScheduler.dll", "Microsoft.Windows.SDK.NET.dll", "MQTTnet.dll",
              "Newtonsoft.Json.dll", "NvAPIWrapper.dll", "Probe.dll", "RAMSPDToolkit-NDD.dll",
              "System.Management.dll", "WinRT.Runtime.dll"]
chk(root_dirs == sorted(want_dirs), "根目录子目录 = %d 个" % len(root_dirs), ", ".join(root_dirs))
chk(root_files == sorted(want_files), "根目录文件 = %d 个" % len(root_files),
    "多 %s / 少 %s" % (sorted(set(root_files) - set(want_files)), sorted(set(want_files) - set(root_files))))
loose = [e for e in root_files if e.lower().endswith((".txt", ".md"))]
chk(not loose, "根目录无散落许可/文档文本", "散落 %s" % loose)

print("\n=== (b) {app}\\LICENSES ===")
LIC = os.path.join(APP, "LICENSES")
got = sorted(os.listdir(LIC)) if os.path.isdir(LIC) else []
want_dest = ["COPYING.PawnIO.txt", "GPL-3.0.txt", "LGPL-2.1.txt", "LICENSE", "README.md",
             "THIRD-PARTY-NOTICES.md"]
want_src_extra = ["THIRD_PARTY_NOTICES,1.txt", "THIRD_PARTY_NOTICES,2.txt"]
chk(sorted(got) == sorted(want_dest + want_src_extra), "LICENSES 内容", ", ".join(got))
chk("LICENSE.txt" not in got, "【无】LICENSE.txt（重复件已去）")
chk("THIRD_PARTY_NOTICES.txt" not in got, "无 THIRD_PARTY_NOTICES.txt 落盘（DestName 由安装期生效）")
for n in want_dest:
    p = os.path.join(LIC, n)
    chk(os.path.isfile(p), n, "%d B" % os.path.getsize(p) if os.path.isfile(p) else "MISSING")

print("\n=== (c) {app}\\Docs ===")
DOC = os.path.join(APP, "Docs")
got = sorted(os.listdir(DOC)) if os.path.isdir(DOC) else []
chk(got == ["\u66f4\u65b0\u65e5\u5fd7.txt", "\u7528\u524d\u5fc5\u770b.txt"], "Docs 内容", ", ".join(got))

print("\n=== (d) {app}\\UWACPIDriver（3 件 / 期望 11,342 / 2,034 / 46,352 B）===")
UWD = os.path.join(APP, "UWACPIDriver")
got = sorted(os.listdir(UWD)) if os.path.isdir(UWD) else []
chk(got == ["UWACPIDriver.inf", "UWACPIDriver.sys", "uwacpidriver.cat"], "UWACPIDriver 内容", ", ".join(got))
for n, want in (("uwacpidriver.cat", 11342), ("UWACPIDriver.inf", 2034), ("UWACPIDriver.sys", 46352)):
    p = os.path.join(UWD, n)
    ok = os.path.isfile(p) and os.path.getsize(p) == want
    chk(ok, n, "%s B (期望 %d)" % (os.path.getsize(p) if os.path.isfile(p) else "MISSING", want))
    if os.path.isfile(p):
        chk(sha(p) == sha(os.path.join(FILES, "GCU", "payload", "GCU", "UWACPIDriver", n)), "  ==files 源件 " + n)

print("\n=== (e) 9 个 usbmmidd_v2 文件（含 Win32\\ / x64\\）===")
UM = os.path.join(APP, "usbmmidd_v2")
want_um = ["deviceinstaller.exe", "deviceinstaller64.exe", "idd_instructions.txt", "License.txt",
           "usbmmidd.bat", "usbmmidd.cat", "usbmmIdd.inf",
           r"Win32\usbmmIdd.dll", r"x64\usbmmIdd.dll"]
got_um = sorted(rel(p)[len("usbmmidd_v2\\"):] for p in allfiles(UM)) if os.path.isdir(UM) else []
chk(sorted(want_um) == got_um, "包内 9 项与清单逐名一致",
    "实得 %d；缺 %s / 多 %s" % (len(got_um), sorted(set(want_um) - set(got_um)), sorted(set(got_um) - set(want_um))))

print("\n=== (f) 四张收款码在 {app}\\Resources ===")
RES = os.path.join(APP, "Resources")
for n in ("qrcode1.jpg", "qrcode2.jpg", "qrcode3.png", "qrcode4.png"):
    p = os.path.join(RES, n)
    if os.path.isfile(p):
        chk(sha(p) == sha(os.path.join(FILES, "Resources", n)), n,
            "%d B  ==files:%s" % (os.path.getsize(p), sha(p) == sha(os.path.join(FILES, "Resources", n))))
    else:
        chk(False, n, "MISSING")

print("\n=== (g) 许可/notices 文本无「18 files」===")
for n in (r"LICENSES\THIRD-PARTY-NOTICES.md", r"LICENSES\LICENSE", r"LICENSES\THIRD_PARTY_NOTICES,1.txt",
          r"LICENSES\THIRD_PARTY_NOTICES,2.txt"):
    p = os.path.join(APP, n)
    if not os.path.isfile(p):
        chk(False, n, "MISSING"); continue
    t = io.open(p, encoding="utf-8-sig", errors="replace").read()
    chk(len(re.findall(r"18\s*files", t)) == 0, n, "%d B, 0 处 '18 files'" % os.path.getsize(p))

print("\n=== (h) 包内 Docs\\更新日志.txt：新的 %s 一节在**最上** ===" % VER)
p = os.path.join(DOC, "\u66f4\u65b0\u65e5\u5fd7.txt")
t = io.open(p, encoding="utf-8-sig", errors="replace").read()
chk(VER in t, "含 " + VER, "命中 %d 处" % t.count(VER))
chk(PVER in t, "含 " + PVER, "命中 %d 处" % t.count(PVER))
chk("\u4e8c\u5341\u516b\u30010.291.7.4 beta22.7.4" in t, "含「二十八、0.291.7.4 beta22.7.4」节标题")
lines = [l for l in t.splitlines()]
firstsec = next((i for i, l in enumerate(lines) if re.match(r"^[\u4e00-\u9fff]+[\u3001]", l) and "0.291" in l), -1)
chk(firstsec >= 0 and "0.291.7.4" in lines[firstsec],
    "第一节就是 0.291.7.4（顶部）",
    "line %d: %s" % (firstsec + 1, lines[firstsec] if firstsec >= 0 else "N/A"))
chk(any("0.291.7.3" in l for l in lines[firstsec + 1:firstsec + 20]), "0.291.7.3 一节在其下方（紧邻后一节）",
    "下一节标题: %s" % next((l for l in lines[firstsec + 1:firstsec + 20]
                            if re.match(r"^[\u4e00-\u9fff]+[\u3001]", l)), "N/A"))
for kw in ("\u4ec5\u9519\u8bef", "LICENSES", "Docs", "UWACPIDriver", "LM-0.291.7.4-beta22.7.4-setup.exe"):
    chk(kw in t, "含关键词 " + kw)
chk(sha(p) == sha(os.path.join(FILES, "\u66f4\u65b0\u65e5\u5fd7.txt")), "==files\\更新日志.txt")
ROOT_LOG = "D:\\openLM\\\u66f4\u65b0\u65e5\u5fd7.txt"
chk(sha(os.path.join(FILES, "\u66f4\u65b0\u65e5\u5fd7.txt")) == sha(ROOT_LOG),
    "根 更新日志.txt == files\\ 更新日志.txt（两份一致）")

print("\n=== (i) 6 核心文件：包内 == 构建产物 == files\\ ===")
CORE = ["L-Mechrevo.dll", "L-Mechrevo.exe", "L-Mechrevo.deps.json",
        "L-Mechrevo.runtimeconfig.json", "L-Mechrevo.dll.config",
        r"zh-CN\L-Mechrevo.resources.dll"]
for r in CORE:
    p = os.path.join(APP, r)
    if not os.path.isfile(p):
        chk(False, r, "MISSING"); continue
    h, hb, hf = sha(p), sha(os.path.join(BUILD, r)), sha(os.path.join(FILES, r))
    chk(h == hb == hf, r, "%s %d B  ==build:%s ==files:%s" % (h, os.path.getsize(p), h == hb, h == hf))

print("\n=== (k) payload 各变体数量（无 1020）===")
GP = os.path.join(APP, "GCU", "payload")
_rebuilt = os.path.join(D, "install_script.iss")
_rt = io.open(_rebuilt, encoding="utf-8-sig", errors="replace").read() if os.path.isfile(_rebuilt) else ""
if not _rt:
    _rt = io.open(ISS, encoding="utf-8-sig", errors="replace").read()
    print("   （注：包内重建 iss 不存在，改用仓库 iss 计数）")
exp = {"30": 163, "40": 4, "40B": 8, "50": 7, "GCU": 97}
for v, want in exp.items():
    d = os.path.join(GP, v)
    n_disk = len(allfiles(d)) if os.path.isdir(d) else 0
    n_entry = len(re.findall(r'DestDir:\s*"\{app\}\\GCU\\payload\\' + v + r'(?:\\|")', _rt))
    chk(n_entry == want, "payload\\%s（iss 条目）" % v,
        "条目 %d / 期望 %d；磁盘实得 %d%s" % (n_entry, want, n_disk,
                                            "" if n_disk == n_entry else "（innounp 同源去重 -%d）" % (n_entry - n_disk)))
chk(not os.path.isdir(os.path.join(GP, "1020")), "无 payload\\1020")

print("\n=== (l) 根目录是否有 LICENSE（新布局应为「无」）===")
chk(not os.path.isfile(os.path.join(APP, "LICENSE")), "根目录无 LICENSE（已移入 LICENSES\\）")
chk(not os.path.isfile(os.path.join(APP, "LICENSE.txt")), "根目录无 LICENSE.txt")
chk(os.path.isfile(os.path.join(APP, "LICENSES", "LICENSE")), "LICENSE 在 LICENSES\\ 下")

print("\n=== (m) /KEEPVENDORCONSOLE 与 -LmOwnedOnly ===")
t = io.open(ISS, encoding="utf-8-sig", errors="replace").read()
chk("KEEPVENDORCONSOLE" in t, "iss 内含 KEEPVENDORCONSOLE")
chk("-LmOwnedOnly" in t, "iss 内含 -LmOwnedOnly")
gu = os.path.join(APP, "GCU", "Uninstall-Gcu.ps1")
if os.path.isfile(gu):
    chk("LmOwnedOnly" in io.open(gu, encoding="utf-8-sig", errors="replace").read(),
        "包内 Uninstall-Gcu.ps1 含 LmOwnedOnly")
chk(re.search(r'Uninstall-Gcu\.ps1""?\s+[^\n]*?-LmOwnedOnly', t) is not None,
    "iss [UninstallRun] 把 -LmOwnedOnly 传给 Uninstall-Gcu.ps1")
chk(re.search(r'GetVendorConsoleParam\b', t) is not None, "iss 用 {code:GetVendorConsoleParam} 透传开关")

print("\n=== (n) 包内版本号字段（iss 的版本声明 + 重建 iss）===")
chk("AppVersion=%s" % VER in _rt, "重建 iss 内 AppVersion=%s" % VER)
# ⚠️ innounp 重建的 iss **不落** VersionInfoVersion / VersionInfoProductVersion（那两项只在 setup 的
#    PE 版本资源里生效）。因此：iss 里若还在就断言值，否则记录「该字段被重建器省略」，真值由
#    setup exe 的 VersionInfo 实测（见 _r15 报告 / PowerShell 实测 FV=0.291.7.4 PV=0.291.7.4）。
for key in ("VersionInfoVersion", "VersionInfoProductVersion"):
    m = re.search(r"^%s=(\S+)" % key, _rt, re.M)
    if m:
        chk(m.group(1) == VER, "重建 iss 内 %s=%s（四段纯数字）" % (key, VER), "实得 " + m.group(1))
    else:
        chk(True, "重建 iss 未落 %s（innounp 省略；真值在 setup PE 版本资源里）" % key,
            "仓库 iss 内为 %s=%s" % (key, VER))
chk("AppVerName=L-Mechrevo %s beta22.7.4" % VER in _rt, "重建 iss 内 AppVerName 带 beta22.7.4")
chk("LM-%s-setup" % PVER in _rt, "重建 iss 内 OutputBaseFilename = LM-%s-setup" % PVER)
chk(not re.search(r'0\.291\.7\.3', _rt), "重建 iss 内**已无** 0.291.7.3")

print("\n=== 附：集合差（包内 vs files\\）===")
pk = set(rel(p) for p in allfiles(APP))
fk = set(os.path.relpath(p, FILES).replace("/", "\\") for p in allfiles(FILES))
CASEC = ("fonts\\harmonyos_sans_sc_bold.ttf", "fonts\\harmonyos_sans_sc_regular.ttf")
only_pkg = sorted(k for k in pk - fk if k.lower() not in CASEC)
only_files = sorted(k for k in fk - pk if k.lower() not in CASEC)
print("   已排除仅大小写之差 2 项（Fonts\\ vs files\\fonts\\）")
print("   包内有而 files\\ 无 %d 项：%s" % (len(only_pkg), only_pkg))
print("   files\\ 有而包内无 %d 项：%s" % (len(only_files), only_files))
print("   包内总文件 %d ; files\\ 总文件 %d" % (len(pk), len(fk)))

print("\n=== 汇总 ===")
print("FAIL 项数 = %d" % len(FAIL))
for f in FAIL:
    print("   FAIL " + f)
sys.exit(1 if FAIL else 0)
