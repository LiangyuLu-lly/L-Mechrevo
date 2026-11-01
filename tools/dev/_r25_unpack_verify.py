# -*- coding: utf-8 -*-
r"""r25（0.291.7.6 / beta22.7.6 同名重打）解包逐项核对。

与 r23 版的差异（本轮新口径）：
  ⭐ 启动壳已删除 => {app} 根【不该再有】L-Mechrevo-Launcher.exe，(o) 由「断言存在」反转为「断言不存在」；
  ⭐ 并断言仓库 iss 与包内重建 iss 的 [Files] / [Run] / [Icons] 三段里**不再出现**壳的文件名；
  ⭐ 包内 Docs\更新日志.txt 顶部第一节 = 0.291.7.6，且措辞 == 改后的 files\ / 根 版本（三处逐字节一致）；
  ⭐ 第一节内⛔ 不得出现「启动壳 / 中文提示 / 探针」等说法。

解包：innounp.exe -x -q -b -y -d<dir> <setup>   （⚠️ innounp 按【源文件名】落盘）
用法: py -3 tools\dev\_r25_unpack_verify.py <unpack_dir>
"""
import sys, os, io, hashlib, re

sys.stdout.reconfigure(encoding="utf-8", errors="replace")

D = sys.argv[1] if len(sys.argv) > 1 else r"D:\openLM\work\r25_unpack"
APP = os.path.join(D, "{app}")
BUILD = r"D:\openLM\source\bin\Release\net10.0-windows10.0.19041.0"
FILES = r"D:\openLM\installer-src\files"
ISS = r"D:\openLM\installer-src\install_script.iss"
ROOT_LOG = "D:\\openLM\\\u66f4\u65b0\u65e5\u5fd7.txt"
FILES_LOG = os.path.join(FILES, "\u66f4\u65b0\u65e5\u5fd7.txt")
VER = "0.291.7.6"
PVER = "0.291.7.6-beta22.7.6"
PREV = "0.291.7.5"
STUB = "L-Mechrevo-Launcher.exe"
OLD_PKG_SHA = "00CB6BAEC694F092780FA818F7C104A3717361F61C4E673FA945B2B77BC65AFC"
NEW_PKG_SHA = "3562386D1844A4378570F62C02E19426FA8732D4CA6964E0F762F4C59494AB6A"

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


def sections(text):
    """把 iss 切成 {段名小写: 段正文}。"""
    out, cur, buf = {}, None, []
    for line in text.splitlines():
        m = re.match(r"^\[(\w+)\]\s*$", line.strip())
        if m:
            if cur:
                out[cur] = "\n".join(buf)
            cur, buf = m.group(1).lower(), []
        elif cur is not None:
            buf.append(line)
    if cur:
        out[cur] = "\n".join(buf)
    return out


print("=== (a) {app} 根目录：只剩程序本体 + 已知子目录（⛔ 无启动壳）===")
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
chk(STUB not in root_files, "⭐ {app} 根【无】" + STUB)
loose = [e for e in root_files if e.lower().endswith((".txt", ".md"))]
chk(not loose, "根目录无散落 txt/md", "散落 %s" % loose)
chk(not os.path.isfile(os.path.join(APP, "LICENSE")), "根目录无 LICENSE")
chk(not os.path.isfile(os.path.join(APP, "LICENSE.txt")), "根目录无 LICENSE.txt")

print("\n=== (b) {app}\\LICENSES ===")
LIC = os.path.join(APP, "LICENSES")
got = sorted(os.listdir(LIC)) if os.path.isdir(LIC) else []
want_dest = ["COPYING.PawnIO.txt", "GPL-3.0.txt", "LGPL-2.1.txt", "LICENSE", "README.md",
             "THIRD-PARTY-NOTICES.md"]
want_src_extra = ["THIRD_PARTY_NOTICES,1.txt", "THIRD_PARTY_NOTICES,2.txt"]
chk(sorted(got) == sorted(want_dest + want_src_extra), "LICENSES 内容", ", ".join(got))
chk("LICENSE.txt" not in got, "【无】LICENSE.txt")
for n in want_dest:
    p = os.path.join(LIC, n)
    chk(os.path.isfile(p), n, "%d B" % os.path.getsize(p) if os.path.isfile(p) else "MISSING")

print("\n=== (c) {app}\\Docs ===")
DOC = os.path.join(APP, "Docs")
got = sorted(os.listdir(DOC)) if os.path.isdir(DOC) else []
chk(got == ["\u66f4\u65b0\u65e5\u5fd7.txt", "\u7528\u524d\u5fc5\u770b.txt"], "Docs 内容", ", ".join(got))

print("\n=== (d) {app}\\UWACPIDriver（3 件 / 11,342 / 2,034 / 46,352 B）===")
UWD = os.path.join(APP, "UWACPIDriver")
got = sorted(os.listdir(UWD)) if os.path.isdir(UWD) else []
chk(got == ["UWACPIDriver.inf", "UWACPIDriver.sys", "uwacpidriver.cat"], "UWACPIDriver 内容", ", ".join(got))
for n, want in (("uwacpidriver.cat", 11342), ("UWACPIDriver.inf", 2034), ("UWACPIDriver.sys", 46352)):
    p = os.path.join(UWD, n)
    ok = os.path.isfile(p) and os.path.getsize(p) == want
    chk(ok, n, "%s B (期望 %d)" % (os.path.getsize(p) if os.path.isfile(p) else "MISSING", want))
    if os.path.isfile(p):
        chk(sha(p) == sha(os.path.join(FILES, "GCU", "payload", "GCU", "UWACPIDriver", n)), "  == files 源件 " + n)

print("\n=== (e) 9 个 usbmmidd_v2 文件 ===")
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

print("\n=== (g) notices 文本 0 处「18 files」===")
for n in (r"LICENSES\THIRD-PARTY-NOTICES.md", r"LICENSES\LICENSE",
          r"LICENSES\THIRD_PARTY_NOTICES,1.txt", r"LICENSES\THIRD_PARTY_NOTICES,2.txt"):
    p = os.path.join(APP, n)
    if not os.path.isfile(p):
        chk(False, n, "MISSING"); continue
    t = io.open(p, encoding="utf-8-sig", errors="replace").read()
    chk(len(re.findall(r"18\s*files", t)) == 0, n, "%d B, 0 处 '18 files'" % os.path.getsize(p))

print("\n=== (h) 包内 Docs\\更新日志.txt：顶部第一节 = %s 且措辞 == 改后版本 ===" % VER)
p = os.path.join(DOC, "\u66f4\u65b0\u65e5\u5fd7.txt")
t = io.open(p, encoding="utf-8-sig", errors="replace").read()
chk(sha(p) == sha(FILES_LOG), "包内 == files\\更新日志.txt", sha(p))
chk(sha(FILES_LOG) == sha(ROOT_LOG), "files\\ == 根 更新日志.txt（两份逐字节一致）")
lines = t.splitlines()
firstsec = next((i for i, l in enumerate(lines) if re.match(r"^[\u4e00-\u9fff]+[\u3001]", l) and "0.291" in l), -1)
chk(firstsec == 0 and VER in lines[firstsec], "第一节就是 %s（第 1 行）" % VER,
    "line %d: %s" % (firstsec + 1, lines[firstsec] if firstsec >= 0 else "N/A"))
sec1 = "\n".join(lines[:next((i for i, l in enumerate(lines[1:], 1)
                             if re.match(r"^[\u4e00-\u9fff]+[\u3001]", l) and "0.291" in l), len(lines))])
for kw in ("自动下载", "静默安装", "就绪页", "重试下载 / Retry", "用浏览器下载 / Open in browser",
           "稍后 / Later", "剪贴板", "校色开关", "MiniLED", "核显省电", "不再需要单独的「启动器」"):
    chk(kw in sec1, "第一节含「%s」" % kw)
for bad in ("启动壳", "\u4e2d\u6587\u63d0\u793a", "\u63a2\u9488", ".iss \u635f\u574f"):
    chk(bad not in sec1, "第一节⛔ 不含「%s」" % bad)
chk(any(PREV in l for l in lines[firstsec + 1:firstsec + 40]), "%s 一节在其下方" % PREV)

print("\n=== (i) 6 核心文件：包内 == 构建产物 == files\\ ===")
CORE = ["L-Mechrevo.dll", "L-Mechrevo.exe", "L-Mechrevo.deps.json",
        "L-Mechrevo.runtimeconfig.json", "L-Mechrevo.dll.config",
        r"zh-CN\L-Mechrevo.resources.dll"]
for r in CORE:
    q = os.path.join(APP, r)
    if not os.path.isfile(q):
        chk(False, r, "MISSING"); continue
    h, hb, hf = sha(q), sha(os.path.join(BUILD, r)), sha(os.path.join(FILES, r))
    chk(h == hb == hf, r, "%d B  ==build:%s ==files:%s" % (os.path.getsize(q), h == hb, h == hf))

print("\n=== (k) payload 各变体数量（无 1020）===")
GP = os.path.join(APP, "GCU", "payload")
_rt = io.open(os.path.join(D, "install_script.iss"), encoding="utf-8-sig", errors="replace").read() \
    if os.path.isfile(os.path.join(D, "install_script.iss")) else ""
exp = {"30": 163, "40": 4, "40B": 8, "50": 7, "GCU": 97}
for v, want in exp.items():
    d = os.path.join(GP, v)
    n_disk = len(allfiles(d)) if os.path.isdir(d) else 0
    n_entry = len(re.findall(r'DestDir:\s*"\{app\}\\GCU\\payload\\' + v + r'(?:\\|")', _rt))
    chk(n_entry == want, "payload\\%s（iss 条目）" % v,
        "条目 %d / 期望 %d；磁盘实得 %d%s" % (n_entry, want, n_disk,
                                            "" if n_disk == n_entry else "（innounp 同源去重 -%d）" % (n_entry - n_disk)))
chk(not os.path.isdir(os.path.join(GP, "1020")), "无 payload\\1020")

print("\n=== (m) /KEEPVENDORCONSOLE 与 -LmOwnedOnly ===")
t_iss = io.open(ISS, encoding="utf-8-sig", errors="replace").read()
chk("KEEPVENDORCONSOLE" in t_iss, "仓库 iss 内含 KEEPVENDORCONSOLE")
chk("-LmOwnedOnly" in t_iss, "仓库 iss 内含 -LmOwnedOnly")
gu = os.path.join(APP, "GCU", "Uninstall-Gcu.ps1")
if os.path.isfile(gu):
    chk("LmOwnedOnly" in io.open(gu, encoding="utf-8-sig", errors="replace").read(),
        "包内 Uninstall-Gcu.ps1 含 LmOwnedOnly")
chk(re.search(r'Uninstall-Gcu\.ps1""?\s+[^\n]*?-LmOwnedOnly', t_iss) is not None,
    "iss [UninstallRun] 把 -LmOwnedOnly 传给 Uninstall-Gcu.ps1")
chk(re.search(r'GetVendorConsoleParam\b', t_iss) is not None, "iss 用 {code:GetVendorConsoleParam} 透传开关")

print("\n=== (o) ⭐ 启动壳彻底不在（包内 / files\\ / iss 三段 / 重建 iss）===")
chk(not os.path.exists(os.path.join(APP, STUB)), "包内无 " + STUB)
chk(not os.path.exists(os.path.join(FILES, STUB)), "files\\ 无 " + STUB)
chk(not [q for q in allfiles(APP) if os.path.basename(q).lower() == STUB.lower()], "包内任何层级都无壳")
for src_name, text in (("仓库 iss", t_iss), ("包内重建 iss", _rt)):
    sec = sections(text)
    for s in ("files", "run", "icons"):
        body = sec.get(s, "")
        # ⚠️ 只算【条目行】：';' 开头的注释行是 r25 留下的「为什么删壳 + 备份在哪」说明，
        #    提到文件名是刻意的（iss 的 SHA 已被本轮钉死，不改 iss）。
        entries = "\n".join(l for l in body.splitlines() if l.strip() and not l.lstrip().startswith(";"))
        n = len(re.findall(r"L-Mechrevo-Launcher", entries, re.I))
        chk(n == 0, "%s [%s] 段内【条目行】壳引用 = 0" % (src_name, s),
            "" if n == 0 else "\n".join(l for l in entries.splitlines() if "Launcher" in l))
chk('Filename: "{app}\\L-Mechrevo.exe"' in t_iss, "[Run] 指向 {app}\\L-Mechrevo.exe")
for lab in ('Name: "{group}\\L-Mechrevo"', 'Name: "{autodesktop}\\L-Mechrevo"'):
    m = re.search(re.escape(lab) + r"[^\n]*", t_iss)
    chk(m is not None and 'Filename: "{app}\\L-Mechrevo.exe"' in m.group(0),
        lab + " -> L-Mechrevo.exe", m.group(0).strip() if m else "NOT FOUND")

print("\n=== (p) GCU\\Install-Gcu.ps1 == AABE3771... ===")
WANT_GCU = "AABE37714B71E2EF0AE97321185817904E3D78FD03D56DF2A6E705017FDA0F41"
gp = os.path.join(APP, "GCU", "Install-Gcu.ps1")
if os.path.isfile(gp):
    chk(sha(gp) == WANT_GCU, "包内 Install-Gcu.ps1 SHA256", "%s  %d B" % (sha(gp), os.path.getsize(gp)))
    chk(sha(gp) == sha(os.path.join(FILES, "GCU", "Install-Gcu.ps1")), "== files\\ 源件")
else:
    chk(False, "包内 GCU\\Install-Gcu.ps1", "MISSING")

print("\n=== (r) 包内重建 iss 的版本声明 ===")
chk("AppVersion=%s" % VER in _rt, "AppVersion=%s" % VER)
chk("AppVerName=L-Mechrevo %s beta22.7.6" % VER in _rt, "AppVerName 带 beta22.7.6")
chk("LM-%s-setup" % PVER in _rt, "OutputBaseFilename = LM-%s-setup" % PVER)
chk(not re.search(r"0\.291\.7\.5", _rt), "重建 iss 内已无 0.291.7.5")

print("\n=== 附：集合差（包内 vs files\\）===")
pk = set(rel(q) for q in allfiles(APP))
fk = set(os.path.relpath(q, FILES).replace("/", "\\") for q in allfiles(FILES))
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
