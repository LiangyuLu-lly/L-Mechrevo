# -*- coding: utf-8 -*-
r"""GCU payload/变体表一致性静态校验（防回归，只读）。

背景（owner 原话，2026-10-14）：
    「检查一下 1020 30 系的 GCU 它们文件夹名字是 uniwill 服务，这个到时候别出现回归问题」

也就是：1020.7z / 30.7z 内层是老的 `UniwillService\`，而 40/50/WJXY 内层是 `AiStoneService\`。
安装期 Install-Gcu.ps1 会把 1020/30 的 `UniwillService` **改名**统一落地成
`{app}\GCU\AiStoneService\`。这个改名一旦没生效 / 目录错位 / 四件缺失，要**在出包前**就暴露，
而不是等用户装了 1020/30 系机器才发现。

本脚本是【A：出包前静态校验】。它只读：
  · 5 个 7z 的**条目清单**（`7za l`，⛔ 不真解压、不落盘），
  · `Select-GcuPayload.ps1` / `Install-Gcu.ps1` / `gcu-variants.json` 里的变体表（正则抽取）。

它同时校验【变体表的 4 份拷贝是否互相一致】—— 同一张表散落在 4 个文件里，
正是「改了 A 忘了改 B」这类回归的温床。

用法:
    py -3 tools\dev\check-gcu-payload-layout.py            # 校验仓库默认路径
    py -3 tools\dev\check-gcu-payload-layout.py --repo D:\openLM
退出码: 0 = 全 PASS；1 = 有 FAIL；2 = 环境问题（缺文件/缺 7za）。
"""
import argparse
import json
import os
import re
import subprocess
import sys

sys.stdout.reconfigure(encoding="utf-8", errors="replace")

# ---------------------------------------------------------------------------
# 期望的变体表 —— 这是【唯一权威期望值】，来自 2026-10-14 owner 纠偏口径。
# 任何一份变体表拷贝与它不符 => FAIL。
# ---------------------------------------------------------------------------
EXPECTED = {
    # key   : (7z 文件名,     7z 内层顶层目录, 7z 内服务目录名 )
    "1020": ("1020.7z", "20",   "UniwillService"),
    "30":   ("30.7z",   "30",   "UniwillService"),
    "40":   ("40.7z",   "40",   "AiStoneService"),
    "50":   ("50.7z",   "50",   "AiStoneService"),
    "WJXY": ("WJXY.7z", "WJXY", "AiStoneService"),
}
# 落地后统一名（Get-GcuServiceLeafName 的返回值，必须与 3 个脚本一致）。
LANDED_AS = "AiStoneService"
# 1020/30 必须保留老命名的事实（有人"顺手统一"把表改成 AiStoneService 就是回归）。
LEGACY_KEYS = ("1020", "30")

# 【必须存在】的文件（相对 <InnerRoot>\<ServiceDir>）——全变体一致，缺即 FAIL。
REQUIRED_REL = (
    r"MyControlCenter\GCUService.exe",
    r"GCUBridge.exe",
)
# 【信息行 / 不做 FAIL 判据】的文件：厂商各代载荷本身就不一样。
#   GCUUtil.exe 只有 50/WJXY 有；1020/30/40 根本没有（已逐 7z 实测 2026-10-14）。
#   OSDTpDetect.exe 全变体都有，但保留为 informational 以免未来厂商换代时误报。
OPTIONAL_REL = (
    r"MyControlCenter\GCUUtil.exe",
    r"MyControlCenter\OSDTpDetect.exe",
)

# 服务目录**之外**允许出现在内层根下的同级条目（不参与"服务目录 == ServiceDir"判定）。
#   50.7z 实测带 logo\ / UWACPIDriver\ / README.txt / NOTICE.txt / install.bat / uninstall.bat
#   / 清理官方控制台.bat —— 这些都不落地（见 gcu-variants.json 的 archiveExtraDirsNotLaidDown）。
ALLOWED_ROOT_SIBLINGS = {
    "logo", "UWACPIDriver", "README.txt", "NOTICE.txt", "LICENSE.txt",
    "install.bat", "uninstall.bat", "uninstallReg.bat", "清理官方控制台.bat",
}

# 变体表的 4 份拷贝（同一张表散落 4 处 = 回归温床）。
#   ⚠️ 权威顺序：Select-GcuPayload.ps1 是【运行时真源】（安装期真正读它），
#      其余 3 份是【镜像】，必须与真源一致。
AUTHORITATIVE = "Select-GcuPayload.ps1"

RESULTS = []   # (ok, tag, message)


def ok(tag, msg):
    RESULTS.append((True, tag, msg))


def bad(tag, msg):
    RESULTS.append((False, tag, msg))


def info(tag, msg):
    RESULTS.append((None, tag, msg))


# ---------------------------------------------------------------------------
# 7z 条目清单（只读；不落盘）
# ---------------------------------------------------------------------------
def seven_zip_entries(sevenzip, archive):
    """返回 (entries, error)。entries = 归档内的相对路径列表（去尾 \\）。"""
    if not os.path.isfile(sevenzip):
        return None, "7za.exe not found: %s" % sevenzip
    try:
        proc = subprocess.run(
            [sevenzip, "l", "-slt", archive],
            stdout=subprocess.PIPE, stderr=subprocess.STDOUT,
            creationflags=0x08000000 if os.name == "nt" else 0,
        )
    except OSError as exc:
        return None, "cannot run 7za: %s" % exc
    if proc.returncode != 0:
        return None, "7za l failed (exit %d) on %s" % (proc.returncode, archive)
    # -slt 是 "Path = xxx" 的键值格式，比解析表格稳。
    text = proc.stdout.decode("utf-8", "replace")
    entries = []
    for line in text.splitlines():
        if line.startswith("Path = "):
            entries.append(line[7:].strip().rstrip("\\"))
    # 第一条 Path = 是归档自身路径，去掉。
    if entries:
        entries = entries[1:]
    return entries, None


# ---------------------------------------------------------------------------
# 从 PowerShell / JSON 抽变体表
# ---------------------------------------------------------------------------
def parse_ps_tables(text):
    """抽 Select-GcuPayload.ps1 / Install-Gcu.ps1 里的变体表行。

    两种写法都能吃：
        "1020" = [pscustomobject]@{ ... InnerRoot = "20";    ServiceDir = "UniwillService"; ... }
        '1020' = @{ FileName = '1020.7z'; InnerRoot = '20';    ServiceDir = 'UniwillService' }
    """
    out = {}
    for line in text.splitlines():
        m = re.match(
            r"""^\s*['"](1020|30|40|50|WJXY)['"]\s*=\s*(?:\[pscustomobject\]@\{|@\{)""",
            line,
        )
        if not m:
            continue
        key = m.group(1)
        rec = {}
        for field in ("FileName", "InnerRoot", "ServiceDir"):
            fm = re.search(
                r"""\b%s\s*=\s*['"]([^'"]*)['"]""" % field, line
            )
            if fm:
                rec[field] = fm.group(1)
        if rec:
            out[key] = rec
    return out


def parse_json_table(path):
    """抽 gcu-variants.json 的 variantTable。"""
    if not os.path.isfile(path):
        return None, "not found: %s" % path
    try:
        with open(path, "r", encoding="utf-8-sig") as fh:
            data = json.load(fh)
    except (OSError, ValueError) as exc:
        return None, "cannot read/parse: %s" % exc
    out = {}
    for row in data.get("variantTable", []) or []:
        vid = row.get("id")
        if not vid:
            continue
        out[vid] = {
            "FileName": row.get("archive"),
            "InnerRoot": row.get("innerRoot"),
            "ServiceDir": row.get("serviceDir"),
            "LandedAs": row.get("landedAs"),
        }
    return out, None


def cmp_table(label, table, expect_landed=False):
    """把某一份变体表拷贝与 EXPECTED 逐条比对。"""
    if table is None:
        bad("catalog", "%s: 读不到" % label)
        return
    if set(table) != set(EXPECTED):
        bad("catalog", "%s: 变体键不一致 expected=%s got=%s"
            % (label, sorted(EXPECTED), sorted(table)))
    for key in sorted(EXPECTED):
        exp_file, exp_root, exp_svc = EXPECTED[key]
        got = table.get(key)
        if got is None:
            continue
        pairs = [
            ("FileName", exp_file, got.get("FileName")),
            ("InnerRoot", exp_root, got.get("InnerRoot")),
            ("ServiceDir", exp_svc, got.get("ServiceDir")),
        ]
        diffs = ["%s: expected %r got %r" % (f, e, g) for f, e, g in pairs if e != g]
        if diffs:
            bad("catalog", "%s [%s] %s" % (label, key, "; ".join(diffs)))
        else:
            ok("catalog", "%s [%s] %s / %s / %s" % (label, key, exp_file, exp_root, exp_svc))
    if expect_landed:
        for key in sorted(EXPECTED):
            got = table.get(key) or {}
            landed = got.get("LandedAs")
            if landed is None:
                continue
            if landed != LANDED_AS:
                bad("catalog", "%s [%s] landedAs: expected %r got %r"
                    % (label, key, LANDED_AS, landed))
            else:
                ok("catalog", "%s [%s] landedAs=%s" % (label, key, LANDED_AS))


# ---------------------------------------------------------------------------
def main():
    ap = argparse.ArgumentParser(description="GCU payload/variant-table static check (read-only)")
    ap.add_argument("--repo", default=r"D:\openLM", help="repository root")
    args = ap.parse_args()

    repo = os.path.abspath(args.repo)
    gcu = os.path.join(repo, "installer-src", "files", "GCU")
    payload = os.path.join(gcu, "payload")
    sevenzip = os.path.join(gcu, "7za.exe")
    select_ps1 = os.path.join(gcu, "Select-GcuPayload.ps1")
    install_ps1 = os.path.join(gcu, "Install-Gcu.ps1")
    uninstall_ps1 = os.path.join(gcu, "Uninstall-Gcu.ps1")
    variants_json = os.path.join(gcu, "gcu-variants.json")

    for path in (gcu, payload, sevenzip, select_ps1, install_ps1, uninstall_ps1):
        if not os.path.exists(path):
            print("FATAL: required path missing: %s" % path)
            return 2

    print("=" * 78)
    print("GCU payload layout / variant table static check")
    print("GCU dir : %s" % gcu)
    print("7za     : %s" % sevenzip)
    print("=" * 78)

    # ---- [1/6] 5 个 7z 是否存在，且与变体表一一对应（不多不少）----
    print("\n[1/6] payload\\*.7z vs variant table (exact one-to-one)")
    on_disk = sorted(
        f for f in os.listdir(payload) if f.lower().endswith(".7z")
    )
    want = sorted(v[0] for v in EXPECTED.values())
    if on_disk == want:
        ok("1", "payload\\ 有且仅有 5 个 7z: %s" % ", ".join(on_disk))
    else:
        missing = sorted(set(want) - set(on_disk))
        extra = sorted(set(on_disk) - set(want))
        detail = []
        if missing:
            detail.append("missing=%s" % missing)
        if extra:
            detail.append("unexpected=%s" % extra)
        bad("1", "payload\\ 与变体表不是一一对应: on_disk=%s expected=%s (%s)"
            % (on_disk, want, "; ".join(detail)))

    # ---- [2/6] 每个 7z 解出来的顶层目录名 == 变体表承诺的 InnerRoot ----
    print("\n[2/6] archive inner root == catalog InnerRoot  (read-only: 7za l)")
    archives = {}
    for key in ("1020", "30", "40", "50", "WJXY"):
        fname, exp_root, exp_svc = EXPECTED[key]
        archive = os.path.join(payload, fname)
        entries, err = seven_zip_entries(sevenzip, archive)
        if err:
            bad("2", "[%s] %s" % (key, err))
            archives[key] = None
            continue
        archives[key] = entries
        roots = sorted({
            e.split("\\")[0] for e in entries if e and "\\" in e
        } | {e for e in entries if e and "\\" not in e})
        if roots == [exp_root]:
            ok("2", "[%s] %s top-level == %r" % (key, fname, exp_root))
        else:
            bad("2", "[%s] %s top-level: expected exactly %r, got %s"
                % (key, fname, exp_root, roots))

    # ---- [3/6] 服务目录名：1020/30 必须 UniwillService；40/50/WJXY 必须 AiStoneService ----
    print("\n[3/6] ServiceDir inside the archive (legacy naming must NOT be 'fixed')")
    for key in ("1020", "30", "40", "50", "WJXY"):
        fname, exp_root, exp_svc = EXPECTED[key]
        entries = archives.get(key)
        if entries is None:
            bad("3", "[%s] 无条目清单，跳过 ServiceDir 判定" % key)
            continue
        prefix = exp_root + "\\"
        all_first = sorted({
            e[len(prefix):].split("\\")[0]
            for e in entries
            if e.startswith(prefix) and len(e) > len(prefix)
        })
        # 只有"目录"才算候选服务目录：用是否还有更深一层来判定（<root>\<x>\... 存在）。
        dirs = sorted({
            e[len(prefix):].split("\\")[0]
            for e in entries
            if e.startswith(prefix + "") and e[len(prefix):].count("\\") >= 1
        })
        siblings = sorted(set(all_first) - set(dirs))
        unexpected = [d for d in dirs if d not in (exp_svc,) and d not in ALLOWED_ROOT_SIBLINGS]
        if exp_svc in dirs and not unexpected:
            note = ""
            if len(dirs) > 1:
                extra = [d for d in dirs if d != exp_svc]
                note = "（同级还有 %s，按约定不落地）" % ", ".join(extra)
            ok("3", "[%s] ServiceDir == %r%s" % (key, exp_svc, note))
        else:
            bad("3", "[%s] ServiceDir: expected %r present and no unexpected sibling dir, got %s"
                % (key, exp_svc, dirs))
        if siblings:
            unknown = [s for s in siblings if s not in ALLOWED_ROOT_SIBLINGS]
            if unknown:
                info("3", "[%s] 内层根下的未知同级文件: %s" % (key, unknown))
        # 显式回归哨兵：老命名不许被"顺手统一"
        if key in LEGACY_KEYS and exp_svc != "UniwillService":
            bad("3", "[%s] 回归哨兵：1020/30 的 ServiceDir 必须是 'UniwillService'，"
                     "现在是 %r —— 有人误改成 AiStoneService 会导致安装期改名逻辑错位"
                % (key, exp_svc))

    # ---- [4/6] 关键文件在 <InnerRoot>\<ServiceDir> 下必须存在 ----
    print("\n[4/6] required files under <InnerRoot>\\<ServiceDir>")
    for key in ("1020", "30", "40", "50", "WJXY"):
        fname, exp_root, exp_svc = EXPECTED[key]
        entries = archives.get(key)
        if entries is None:
            bad("4", "[%s] 无条目清单，跳过" % key)
            continue
        present = {e.lower() for e in entries}
        for rel in REQUIRED_REL:
            full = ("%s\\%s\\%s" % (exp_root, exp_svc, rel)).lower()
            if full in present:
                ok("4", "[%s] %s" % (key, rel))
            else:
                bad("4", "[%s] %s 缺失（期望 %s\\%s\\%s）"
                    % (key, rel, exp_root, exp_svc, rel))
        for rel in OPTIONAL_REL:
            full = ("%s\\%s\\%s" % (exp_root, exp_svc, rel)).lower()
            mark = "有" if full in present else "无(该代载荷本来就没有)"
            info("4", "[%s] %s: %s" % (key, rel, mark))

    # ---- [5/6] 变体表的 4 份拷贝必须互相一致 ----
    print("\n[5/6] variant table copies agree ({Select-GcuPayload.ps1, Install-Gcu.ps1, gcu-variants.json})")
    with open(select_ps1, "r", encoding="utf-8", errors="replace") as fh:
        select_text = fh.read()
    with open(install_ps1, "r", encoding="utf-8", errors="replace") as fh:
        install_text = fh.read()
    cmp_table("Select-GcuPayload.ps1", parse_ps_tables(select_text))
    cmp_table("Install-Gcu.ps1", parse_ps_tables(install_text))
    jtable, jerr = parse_json_table(variants_json)
    if jerr:
        bad("5", "gcu-variants.json: %s" % jerr)
    else:
        cmp_table("gcu-variants.json", jtable, expect_landed=True)

    # 落地名常量：只有 Install-Gcu.ps1 定义 GcuServiceLeafName（Select 只在注释里提到）。
    if re.search(r"""\$script:GcuServiceLeafName\s*=\s*['"]%s['"]""" % LANDED_AS, install_text):
        ok("5", "Install-Gcu.ps1: $script:GcuServiceLeafName == %r" % LANDED_AS)
    elif "GcuServiceLeafName" in install_text:
        bad("5", "Install-Gcu.ps1: $script:GcuServiceLeafName 存在但不是 %r" % LANDED_AS)
    else:
        bad("5", "Install-Gcu.ps1: 找不到 $script:GcuServiceLeafName 常量（落地名丢了）")
    # Get-GcuServiceLeafName 必须确实 return AiStoneService
    if re.search(r"""function\s+Get-GcuServiceLeafName\b[\s\S]{0,4000}?return\s+['"]%s['"]"""
                 % LANDED_AS, install_text):
        ok("5", "Install-Gcu.ps1: Get-GcuServiceLeafName returns %r" % LANDED_AS)
    else:
        bad("5", "Install-Gcu.ps1: Get-GcuServiceLeafName 没有 return %r（改名逻辑被改坏/删除）"
            % LANDED_AS)

    # ---- [6/6] 退役项：40B 不该回到落地判据里；UniwillService 只该作为"归档内名"出现 ----
    print("\n[6/6] retired 40B / legacy UniwillService invariants")
    with open(uninstall_ps1, "r", encoding="utf-8", errors="replace") as fh:
        uninstall_text = fh.read()
    # 卸载器不再把 UniwillService 当成"当前落地名"（只作为旧包残留清理）
    if re.search(r"""\{app\}\\GCU\\UniwillService""", uninstall_text) or \
       re.search(r"""\{app\}\\GCU\\UniwillService""", install_text):
        info("6", "UniwillService 仍出现在 [UninstallDelete]/清理条目里（旧包残留清理，正确）")
    else:
        info("6", "UniwillService 未出现在 {app}\\GCU\\ 清理条目里（若旧包残留需清理则应收录）")
    # Select-GcuPayload 的 VariantKeys 里不该有 40B
    m = re.search(r"""\$script:VariantKeys\s*=\s*@\(([^)]*)\)""", select_text)
    if m:
        keys = re.findall(r"""['"]([^'"]+)['"]""", m.group(1))
        if "40B" in keys:
            bad("6", "Select-GcuPayload.ps1: VariantKeys 仍含退役的 '40B' => %s" % keys)
        else:
            ok("6", "Select-GcuPayload.ps1: VariantKeys 不含 40B => %s" % keys)
    else:
        bad("6", "Select-GcuPayload.ps1: 找不到 $script:VariantKeys 变体键表")
    # install_script.iss 里的 GcuStagingFiles 必须正好是 5 个 7z
    iss = os.path.join(repo, "installer-src", "install_script.iss")
    if os.path.isfile(iss):
        with open(iss, "r", encoding="utf-8", errors="replace") as fh:
            iss_text = fh.read()
        staged = re.findall(r"""GcuStagingFiles\[\d+\]\s*:=\s*'([^']+)'""", iss_text)
        staged_archives = [s for s in staged if s.lower().endswith(".7z")]
        want_staged = [EXPECTED[k][0]
                       for k in ("1020", "30", "40", "50", "WJXY")]
        if sorted(staged_archives) == sorted(want_staged):
            ok("6", "install_script.iss: 5 个 7z 的 staging 条目 == 变体表（裸文件名）")
        else:
            bad("6", "install_script.iss: 7z staging 条目与变体表不一致 expected=%s got=%s"
                % (want_staged, staged_archives))
        # ---- r29 新增 [6b]: [Files] dontcopy 条目 与 ExtractTemporaryFile staging 名单 交叉核对 ----
        #   背景：Inno 的 ExtractTemporaryFile() 只接受【裸文件名】（[Files] Source 的文件名部分
        #   才是包内名字，DestDir 不构成包内子路径）。r28 的 install_script.iss 把带子路径的
        #   'payload\1020.7z' 传给了 ExtractTemporaryFile => 真机 5 件全解出失败、安装中止。
        #   本组检查：文本级交叉核对（强度=中：抓形态回归，抓不住 Inno 运行时行为；真机装一次
        #   仍不可省）。依旧只读，不改 .iss。
        dontcopy = [src for src, ln in re.findall(
            r'Source:\s*"([^"]+)";\s*DestDir:\s*"\{tmp\}"([^\n]*)', iss_text)
            if "dontcopy" in ln]
        dontcopy_bare = sorted({os.path.basename(s.replace("/", "\\")) for s in dontcopy})
        want7 = sorted([EXPECTED[k][0] for k in ("1020", "30", "40", "50", "WJXY")] + ["7za.exe"])
        if dontcopy_bare == want7:
            ok("6b", "[Files] {tmp}+dontcopy 条目裸名 == 5 个 7z + 7za.exe (%d 条)" % len(dontcopy))
        else:
            bad("6b", "[Files] {tmp}+dontcopy 裸名与预期不符 expected=%s got=%s"
                % (want7, dontcopy_bare))
        sub = [s for s in staged if ("\\" in s) or ("/" in s)]
        if sub:
            bad("6b", "staging 名单含子路径（r28 事故形态，ExtractTemporaryFile 必失败）: %s" % sub)
        else:
            ok("6b", "staging 名单全部为裸文件名（无 \\ 或 /）")
        missing_in_files = [s for s in staged if s not in dontcopy_bare]
        if missing_in_files:
            bad("6b", "staging 名单在 [Files] dontcopy 条目里找不到 Source: %s" % missing_in_files)
        else:
            ok("6b", "staging 名单每个名字都有对应 [Files] dontcopy Source:")
        # [6c]: Install-Gcu.ps1 的 -StagingRoot 语义 == .iss 摆位（{tmp} 根有 7za.exe + payload\*.7z）
        #   依据（文本级，强度=中）：ps1:558 主路径 Join-Path StagingRoot 'payload\<name>.7z'、
        #   ps1:564 flat fallback、ps1:573 7za.exe 固定在 StagingRoot 根、ps1:1550 archiveRelPath
        #   固定 'payload\<name>'。.iss 侧摆位 = 解到 {tmp} 根后重建 payload\ 子目录、7za.exe 留根。
        layout_ok = (
            re.search(r"""Join-Path\s+\$StagingRoot\s+'7za\.exe'""", install_text) is not None
            and re.search(r"""Join-Path\s+'payload'\s+\$archiveInfo\.FileName""", install_text) is not None)
        if layout_ok:
            ok("6c", "Install-Gcu.ps1: 期望 <StagingRoot>\payload\<name>.7z + <StagingRoot>\7za.exe，与 .iss {tmp} 摆位一致")
        else:
            bad("6c", "Install-Gcu.ps1 的 StagingRoot 摆位锚点变了，需人工复核与 .iss 摆位是否仍一致")        # 7za.exe 也必须是 dontcopy 临时文件（它不该被装进 {app}）
        other = [s for s in staged if not s.lower().endswith(".7z")]
        if other == ["7za.exe"]:
            ok("6", "install_script.iss: staging 的非 7z 条目只有 7za.exe（安装期临时工具）")
        else:
            bad("6", "install_script.iss: staging 的非 7z 条目异常: %s" % other)
    else:
        info("6", "install_script.iss 不存在，跳过")

    # ---- [5b/6] 第 4 份拷贝：GCU变体目录说明.md（文档，做【结构化关键字】检查）----
    #   ⚠️ 强度说明：.md 是文档，格式自由，无法像 ps1/json 那样逐字段解析。
    #   这里做的是【每变体一行的结构化匹配】：在同一行/同一表格行里必须同时出现
    #   (变体 id) + (InnerRoot) + (ServiceDir)，且 ServiceDir 归属正确。
    #   强度 = 中（能抓住"把 1020/30 改成 AiStoneService"这类核心回归，
    #   但抓不住纯格式/措辞漂移）。真正权威仍是 Select-GcuPayload.ps1。
    md_path = os.path.join(gcu, "GCU变体目录说明.md")
    if not os.path.isfile(md_path):
        info("5b", "GCU变体目录说明.md 不存在，跳过第 4 份拷贝检查")
    else:
        with open(md_path, "r", encoding="utf-8", errors="replace") as fh:
            md_text = fh.read()
        for key in ("1020", "30", "40", "50", "WJXY"):
            fname, exp_root, exp_svc = EXPECTED[key]
            # 找提到这个 7z 的那些行，要求其中至少一行的"行内上下文"含 InnerRoot 与 ServiceDir。
            hit_lines = [ln for ln in md_text.splitlines() if fname in ln]
            good = [
                ln for ln in hit_lines
                if (exp_root in ln) and (exp_svc in ln)
            ]
            if good:
                ok("5b", ".md [%s] %s 行内同时含 InnerRoot=%r 与 ServiceDir=%r"
                    % (key, fname, exp_root, exp_svc))
            elif hit_lines:
                bad("5b", ".md [%s] %s 提到该 7z 但没有一行同时含 InnerRoot=%r 与 "
                         "ServiceDir=%r；实际行=%s"
                    % (key, fname, exp_root, exp_svc,
                       [ln.strip()[:110] for ln in hit_lines][:3]))
            else:
                bad("5b", ".md [%s] 完全没提到 %s" % (key, fname))
        # 反向哨兵：文档里不该把 1020/30 说成 AiStoneService
        for key in LEGACY_KEYS:
            fname = EXPECTED[key][0]
            for ln in md_text.splitlines():
                if fname in ln and "AiStoneService" in ln and "UniwillService" not in ln:
                    bad("5b", ".md [%s] 该行把 %s 的服务目录写成 AiStoneService 却未提 "
                             "UniwillService（疑似回归）: %s"
                        % (key, fname, ln.strip()[:110]))

    # ---- [5c/6] 4 份拷贝交叉一致性汇总（哪份是权威、哪些不一致）----
    print("\n[5c/6] cross-copy authority summary")
    copies = {
        "Select-GcuPayload.ps1": parse_ps_tables(select_text),
        "Install-Gcu.ps1": parse_ps_tables(install_text),
        "gcu-variants.json": (jtable or {}),
    }
    info("5c", "权威 = %s（安装期运行时真正读的变体表，模型在 .iss 的 "
               "GetGcuVariantParam / [Run] 链路里）" % AUTHORITATIVE)
    for name, tbl in copies.items():
        if name == AUTHORITATIVE:
            info("5c", "%s = 权威" % name)
            continue
        diffs = []
        for key in sorted(EXPECTED):
            a = copies[AUTHORITATIVE].get(key) or {}
            b = tbl.get(key) or {}
            for field in ("FileName", "InnerRoot", "ServiceDir"):
                if a.get(field) != b.get(field):
                    diffs.append("[%s].%s: 权威=%r 本份=%r"
                                 % (key, field, a.get(field), b.get(field)))
        if diffs:
            bad("5c", "%s 与权威 %s 不一致: %s"
                % (name, AUTHORITATIVE, "; ".join(diffs)))
        else:
            ok("5c", "%s 与权威 %s 完全一致" % (name, AUTHORITATIVE))
    info("5c", "GCU变体目录说明.md = 文档镜像（强度中，结构化关键字检查见 [5b]）")

    # ---- 汇总 ----
    fails = [r for r in RESULTS if r[0] is False]
    passes = [r for r in RESULTS if r[0] is True]
    print("\n" + "=" * 78)
    for is_ok, tag, msg in RESULTS:
        mark = "PASS" if is_ok else ("FAIL" if is_ok is False else "INFO")
        if is_ok is None:
            print("  [INFO] %s" % msg)
        else:
            print("  [%s] %s" % (mark, msg))
    print("=" * 78)
    print("SUMMARY: %d PASS / %d FAIL / %d INFO"
          % (len(passes), len(fails), len(RESULTS) - len(passes) - len(fails)))
    if fails:
        print("RESULT: FAIL")
        for _, tag, msg in fails:
            print("  !! %s" % msg)
        return 1
    print("RESULT: PASS (no regression in GCU payload layout / variant table)")
    return 0


if __name__ == "__main__":
    sys.exit(main())
