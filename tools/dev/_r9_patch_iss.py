# -*- coding: utf-8 -*-
r"""
repack9 步骤②：把仓库根的 THIRD-PARTY-NOTICES.md 纳入安装包（{app}\THIRD-PARTY-NOTICES.md）。

理由（写入报告）：
  * 随包的两份文档都指向它：`{app}\LICENSE`（根 LICENSE 横幅）与
    `{app}\LICENSES\README.md:4`（"...See `..\THIRD-PARTY-NOTICES.md` for the full inventory"）；
    不纳入则这两处引用在目标机上是悬空的。
  * 它是权威清单（§1.2 B4 已点名 LMechrevo.IntelMSR.bin 的 5,324 B / SHA-256 / LGPL-2.1-or-later），
    比两份 .txt 的粒度更细；38 KB 成本可忽略。
  * 不在 [Files] 的只有它自己（.md），ATTRIBUTION-PATCH-PLAN.md 属内部工作文档，**不纳入**。
只加 [Files] 一条；不动 [Icons]/[CustomMessages]/任何行为逻辑。
保真：文件实测 UTF-8（无 BOM）+ CRLF，本脚本按此写回。
用法：py -3 _r9_patch_iss.py [--apply]
"""
import io
import os
import sys

sys.stdout.reconfigure(encoding="utf-8", errors="replace")

ISS = r"D:\openLM\installer-src\install_script.iss"
MD = r"D:\openLM\THIRD-PARTY-NOTICES.md"

OLD = (b'Source: "..\\LICENSES\\README.md"; DestDir: "{app}\\LICENSES"; '
       b'Flags: ignoreversion\r\n')

NEW_TEXT = (
    "; 2026-10-10 (repack9)：随包的两份文档都指向它（根 LICENSE 的横幅与\r\n"
    ";   LICENSES\\README.md 的 \"..\\THIRD-PARTY-NOTICES.md\"）——不纳入则目标机上这两处引用悬空；\r\n"
    ";   它也是包内内嵌模块 IntelMSR.bin 的权威清单（§1.2 B4）。ATTRIBUTION-PATCH-PLAN.md 属内部文档，不纳入。\r\n"
    "Source: \"..\\THIRD-PARTY-NOTICES.md\"; DestDir: \"{app}\"; Flags: ignoreversion\r\n"
)
NEW = NEW_TEXT.encode("utf-8")


def main():
    apply_ = "--apply" in sys.argv
    b = io.open(ISS, "rb").read()
    if b[:3] == b"\xef\xbb\xbf":
        print("!! iss 含 BOM，与预期不符 -> 中止")
        return 1
    print("iss: %d B, CRLF=%d, LF=%d" % (len(b), b.count(b"\r\n"), b.count(b"\n")))
    if b'Source: "..\\THIRD-PARTY-NOTICES.md"' in b:
        print("== iss 已引用 THIRD-PARTY-NOTICES.md，跳过")
        return 0
    c = b.count(OLD)
    print("锚点命中 %d 次" % c)
    if c != 1:
        print("!! 锚点不唯一 -> 中止")
        return 1
    md_size = os.path.getsize(MD)
    nb = b.replace(OLD, OLD + NEW, 1)
    print("新长度 %d B (+%d)；源 %s = %d B" % (len(nb), len(nb) - len(b), MD, md_size))
    if apply_:
        with io.open(ISS, "wb") as f:
            f.write(nb)
        bb = io.open(ISS, "rb").read()
        print("写回: %d B, CRLF=%d, LF=%d, BOM=%s, 引用=%s" % (
            len(bb), bb.count(b"\r\n"), bb.count(b"\n"), bb[:3] == b"\xef\xbb\xbf",
            b"THIRD-PARTY-NOTICES.md".decode() in bb.decode("utf-8", "replace")))
        try:
            bb.decode("utf-8")
            print("UTF-8 解码 OK")
        except Exception as e:
            print("!! UTF-8 解码失败:", e)
            return 1
    else:
        print("（dry-run；加 --apply 执行）")
    return 0


if __name__ == "__main__":
    sys.exit(main())
