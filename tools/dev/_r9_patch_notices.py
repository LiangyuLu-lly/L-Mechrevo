# -*- coding: utf-8 -*-
r"""
repack9 步骤①：给 installer-src\files\THIRD_PARTY_NOTICES,1.txt / ,2.txt 补上
内嵌模块 LMechrevo.IntelMSR.bin 的点名（名称 / 大小 / SHA-256 / 许可 / 上游）。

- 保真：两文件实测为 **UTF-8 无 BOM + LF 行尾**，本脚本按此写回（不引入 CRLF/BOM）。
- 口径来源（不自创）：
  * THIRD-PARTY-NOTICES.md §1.2 B4 / §1.3
  * source\MechrevoLite\AboutLicensesForm.cs 的 `PawnIO.Modules IntelMSR.bin` 行
  * Strings.AboutLicensesUsePawnIOIntelMsr（en / zh-CN 两份 resx）
用法：py -3 _r9_patch_notices.py [--apply]
"""
import io
import os
import sys

sys.stdout.reconfigure(encoding="utf-8", errors="replace")

FILES = r"D:\openLM\installer-src\files"

EN_ANCHOR = ("Corresponding module source and release:\n"
             "https://github.com/namazso/PawnIO.Modules\n")

EN_INSERT = (
    "The IntelMSR module (LMechrevo.IntelMSR.bin) is embedded unmodified in L-Mechrevo.dll\n"
    "and provides the kernel-mediated MSR 0x150 OC-mailbox access used for Intel CPU tuning.\n"
    "Module file: 5,324 B, SHA-256\n"
    "D6ED85D65AB17A22F813EF98207D6D537155EE2DED5976A21CB48413C9B92E5F.\n"
    "Same upstream (namazso/PawnIO.Modules) and the same LGPL-2.1-or-later licence text as\n"
    "RyzenSMU.bin above: COPYING.PawnIO.txt (or LICENSES\\LGPL-2.1.txt).\n"
)

ZH_ANCHOR = "模块对应源码与发布：https://github.com/namazso/PawnIO.Modules\n"

ZH_INSERT = (
    "IntelMSR 模块（LMechrevo.IntelMSR.bin）以**未经修改**的形式内嵌在 L-Mechrevo.dll 中，\n"
    "为 Intel CPU 调节提供经内核转发的 MSR 0x150 超频邮箱访问。\n"
    "模块文件：5,324 字节，SHA-256\n"
    "D6ED85D65AB17A22F813EF98207D6D537155EE2DED5976A21CB48413C9B92E5F。\n"
    "与上面的 RyzenSMU.bin 同一上游（namazso/PawnIO.Modules）、同一份 LGPL-2.1-or-later\n"
    "许可文本：COPYING.PawnIO.txt（或 LICENSES\\LGPL-2.1.txt）。\n"
)

JOBS = [
    ("THIRD_PARTY_NOTICES,1.txt", EN_ANCHOR, EN_INSERT),
    ("THIRD_PARTY_NOTICES,2.txt", ZH_ANCHOR, ZH_INSERT),
]


def probe(p):
    b = io.open(p, "rb").read()
    bom = b[:3] == b"\xef\xbb\xbf"
    crlf = b.count(b"\r\n")
    lf = b.count(b"\n")
    return len(b), bom, crlf, lf, b


def main():
    apply_ = "--apply" in sys.argv
    for name, anchor, ins in JOBS:
        p = os.path.join(FILES, name)
        n0, bom, crlf, lf, raw = probe(p)
        if bom or crlf:
            print("!! %s 预期 UTF-8 无 BOM / 纯 LF，实测 bom=%s crlf=%d -> 中止" % (name, bom, crlf))
            return 1
        t = raw.decode("utf-8")
        if "LMechrevo.IntelMSR.bin" in t:
            print("== %s 已含 LMechrevo.IntelMSR.bin，跳过" % name)
            continue
        c = t.count(anchor)
        print("== %s 锚点命中 %d 次 (len=%d, lf=%d)" % (name, c, n0, lf))
        if c != 1:
            print("!! 锚点不唯一，中止")
            return 1
        t2 = t.replace(anchor, anchor + ins, 1)
        data = t2.encode("utf-8")
        print("   新长度 %d B (+%d)，行数 %d -> %d" % (
            len(data), len(data) - n0, lf, data.count(b"\n")))
        if apply_:
            with io.open(p, "wb") as f:
                f.write(data)
            n1, bom1, crlf1, lf1, _ = probe(p)
            print("   写回: %d B  bom=%s  crlf=%d  lf=%d  IntelMSR.bin present=%s" % (
                n1, bom1, crlf1, lf1,
                b"LMechrevo.IntelMSR.bin" in io.open(p, "rb").read()))
    if not apply_:
        print("\n（dry-run；加 --apply 执行）")
    return 0


if __name__ == "__main__":
    sys.exit(main())
