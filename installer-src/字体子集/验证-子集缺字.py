#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""
验证 HarmonyOS Sans SC 子集字体：UI 缺字 = 0，字重正确。
用法（在仓库根 D:\\openLM 下执行）：
    py -3 installer-src\\字体子集\\验证-子集缺字.py
    py -3 installer-src\\字体子集\\验证-子集缺字.py --regular <路径> --bold <路径>
可选：--export-ui  把抽取到的 UI 字符集导出为 UI-CHARS.txt（便于复用）
本脚本只读源码与字体，不改任何文件（除 --export-ui 显式导出时）。
"""
import argparse
import os
import re
import sys
import xml.etree.ElementTree as ET

try:
    sys.stdout.reconfigure(encoding="utf-8", errors="replace")
except Exception:
    pass

REPO = os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
DEFAULT_REGULAR = os.path.join(REPO, "installer-src", "files", "fonts",
                               "HarmonyOS_Sans_SC_Regular.ttf")
DEFAULT_BOLD = os.path.join(REPO, "installer-src", "files", "fonts",
                            "HarmonyOS_Sans_SC_Bold.ttf")
SOURCE_DIR = os.path.join(REPO, "source")

SKIP = "\r\n\t"


# --------------------------------------------------------------------------
# 1) 从 .cs / .resx 抽取 UI 字符（自包含，不依赖上一轮结果）
# --------------------------------------------------------------------------
def strip_comments(s):
    """剔除 // 与 /* */ 注释，保留字符串字面量内容。"""
    out = []
    i, n, mode = 0, len(s), 0  # 0=code 1=// 2=/* */ 3="str" 5=@"verbatim" 6='char'
    while i < n:
        c = s[i]
        if mode == 0:
            if c == "/" and i + 1 < n and s[i + 1] == "/":
                mode = 1; i += 2; continue
            if c == "/" and i + 1 < n and s[i + 1] == "*":
                mode = 2; i += 2; continue
            if c == "@" and i + 1 < n and s[i + 1] == '"':
                out.append('"'); mode = 5; i += 2; continue
            if c == '"':
                out.append(c); mode = 3; i += 1; continue
            if c == "'":
                out.append(c); mode = 6; i += 1; continue
            out.append(c); i += 1; continue
        if mode == 1:
            if c == "\n":
                mode = 0; out.append("\n")
            i += 1; continue
        if mode == 2:
            if c == "*" and i + 1 < n and s[i + 1] == "/":
                mode = 0; i += 2; continue
            i += 1; continue
        if mode == 3:
            if c == "\\":
                out.append(s[i:i + 2]); i += 2; continue
            if c == '"':
                mode = 0
            out.append(c); i += 1; continue
        if mode == 5:
            if c == '"' and i + 1 < n and s[i + 1] == '"':
                out.append('""'); i += 2; continue
            if c == '"':
                mode = 0
            out.append(c); i += 1; continue
        if mode == 6:
            if c == "\\":
                out.append(s[i:i + 2]); i += 2; continue
            if c == "'":
                mode = 0
            out.append(c); i += 1; continue
    return "".join(out)


LIT_RE = re.compile(r'"((?:[^"\\\n]|\\.)*)"' + r"|'((?:[^'\\\n]|\\.)*)'")


def extract_ui_chars(root=SOURCE_DIR):
    chars, n_cs, n_lit, n_resx = {}, 0, 0, 0
    for dp, _, fns in os.walk(root):
        for fn in fns:
            p = os.path.join(dp, fn)
            if fn.endswith(".cs"):
                n_cs += 1
                try:
                    s = open(p, encoding="utf-8-sig", errors="replace").read()
                except OSError:
                    continue
                for m in LIT_RE.finditer(strip_comments(s)):
                    t = m.group(1) if m.group(1) is not None else m.group(2)
                    if t is None:
                        continue
                    n_lit += 1
                    for ch in t:
                        if ch not in SKIP:
                            chars[ch] = chars.get(ch, 0) + 1
            elif fn.endswith(".resx"):
                n_resx += 1
                try:
                    tree = ET.fromstring(open(p, encoding="utf-8-sig",
                                               errors="replace").read())
                except Exception:
                    continue
                for d in tree.iter("data"):
                    v = d.find("value")
                    if v is None or not v.text:
                        continue
                    for ch in v.text:
                        if ch not in SKIP:
                            chars[ch] = chars.get(ch, 0) + 1
    # 源码中出现在字面量之外的少量真实字符（如码位区间边界 '⺀' / '鿿'）
    for ch in "\u2e80\u9fff\uff3b\uff3d":
        chars.setdefault(ch, 0)
    return chars, n_cs, n_lit, n_resx


# --------------------------------------------------------------------------
# 2) 读 ttf 的 cmap / 字重
# --------------------------------------------------------------------------
def read_font(path):
    from fontTools.ttLib import TTFont
    f = TTFont(path, fontNumber=0, lazy=True)
    cmap = set()
    for t in f["cmap"].tables:
        cmap |= set(t.cmap.keys())
    weight = None
    try:
        weight = f["OS/2"].usWeightClass
    except Exception:
        pass
    fam = None
    try:
        for rec in f["name"].names:
            if rec.nameID == 1:
                fam = rec.toUnicode()
                break
    except Exception:
        pass
    n_glyphs = f["maxp"].numGlyphs
    f.close()
    return cmap, weight, fam, n_glyphs


# --------------------------------------------------------------------------
def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--regular", default=DEFAULT_REGULAR)
    ap.add_argument("--bold", default=DEFAULT_BOLD)
    ap.add_argument("--export-ui", action="store_true",
                    help="把 UI 字符集导出为 UI-CHARS.txt（默认关闭，不落盘）")
    a = ap.parse_args()

    print("=" * 68)
    print("UI 字符抽取：", SOURCE_DIR)
    ui, n_cs, n_lit, n_resx = extract_ui_chars()
    han = sorted(c for c in ui if "\u4e00" <= c <= "\u9fff")
    print("  .cs = %d 个，字面量 %d 条；.resx = %d 个" % (n_cs, n_lit, n_resx))
    print("  UI 字符 = %d 个（汉字 %d + 非汉字 %d）"
          % (len(ui), len(han), len(ui) - len(han)))

    if a.export_ui:
        dst = os.path.join(REPO, "installer-src", "字体子集", "UI-CHARS.txt")
        with open(dst, "wb") as fh:
            fh.write("".join(sorted(ui, key=ord)).encode("utf-8"))
        print("  已导出 UI 字符集 ->", dst)

    ok = True
    for label, path, want_w in (("Regular", a.regular, 400),
                                ("Bold", a.bold, 700)):
        print("-" * 68)
        print("[%s] %s" % (label, path))
        if not os.path.exists(path):
            print("  !! 文件不存在 -> FAIL")
            ok = False
            continue
        try:
            cmap, weight, fam, n_glyphs = read_font(path)
        except ImportError:
            print("  !! 未安装 fontTools。请先执行： pip install fonttools")
            sys.exit(2)
        except Exception as e:
            print("  !! 无法解析字体：%r -> FAIL" % (e,))
            ok = False
            continue

        missing = sorted(c for c in ui if ord(c) not in cmap)
        covered = len(ui) - len(missing)
        print("  子集 numGlyphs = %d" % n_glyphs)
        print("  覆盖 %d/%d 字符，缺 %d 个" % (covered, len(ui), len(missing)))
        print("  族名(name ID 1) = %r" % fam)
        print("  usWeightClass   = %s（期望 %d）" % (weight, want_w))

        if missing:
            ok = False
            print("  [FAIL] 缺失字符：")
            for c in missing[:80]:
                print("      U+%04X %s" % (ord(c), c if ord(c) > 0x20 else "(空白)"))
            if len(missing) > 80:
                print("      ... 共 %d 个" % len(missing))
        else:
            print("  [OK] 缺字 = 0")

        if weight != want_w:
            ok = False
            print("  [FAIL] 字重不对：usWeightClass=%s，期望 %d" % (weight, want_w))
        else:
            print("  [OK] 字重正确")
        if not fam or "HarmonyOS Sans SC" not in (fam or ""):
            ok = False
            print("  [FAIL] 族名不含 'HarmonyOS Sans SC'（UiVisualStyle.cs:87 会找不到字体）")
        else:
            print("  [OK] 族名正确")

    print("=" * 68)
    print("总判定：%s" % ("PASS ✅ 全部通过" if ok else "FAIL ❌ 见上面标记"))
    sys.exit(0 if ok else 1)


if __name__ == "__main__":
    main()
