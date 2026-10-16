# -*- coding: utf-8 -*-
r"""
repack14 (0.291.7.3 / beta22.7.3)：给两份 更新日志.txt 追加「二十七、0.291.7.3」一节。

纪律（今日事故后约定）：
  读字节 -> 定点拼接（旧字节 + 新节字节，不重算任何偏移）-> 先写 TEMP -> 校验 -> 才替换。
  ⛔ 不用 PowerShell 文本 cmdlet 改含中文文件；保持无 BOM / CRLF（与原文一致）。

两份目标：
  D:\openLM\更新日志.txt                      （仓库根那份）
  D:\openLM\installer-src\files\更新日志.txt  （随包那份 -> iss:97 映射到 {app}\Docs\更新日志.txt）
"""
import hashlib
import io
import os
import sys

sys.stdout.reconfigure(encoding="utf-8", errors="replace")

ROOT_LOG = r"D:\openLM\更新日志.txt"
PKG_LOG = r"D:\openLM\installer-src\files\更新日志.txt"
TMP = os.path.join(os.environ["TEMP"], "_r14_newchangelog.txt")

# 写入前两份必须是这个状态（多代理并发 => 被改过就停下报告）
EXPECT_LEN = 97673
EXPECT_SHA = "6A0D0F400A3ED67714D7"  # 前缀（前 20 hex）

SEP = "\u2500" * 44
SECTION = "\r\n".join([
    "",
    "二十七、0.291.7.3 beta22.7.3（2026-10-13）",
    SEP,
    "一、界面",
    "1. 六个工具 / 设置窗口——**自定义模式、设置、CPU 调校、风扇曲线、RGB、灯光**——标题栏现在**只剩一个关闭按钮**，"
    "不再有最小化与最大化按钮（以前误点最小化会让窗口「消失」，只能在任务栏里找回）。",
    "二、安装目录更整洁",
    "1. 许可与合规文本统一收进 **`LICENSES\\`**：`LICENSE`、`THIRD_PARTY_NOTICES.txt`、`THIRD-PARTY-NOTICES.md`、"
    "`COPYING.PawnIO.txt`、`GPL-3.0.txt`、`LGPL-2.1.txt`、`README.md`；程序根目录不再散落这些文本。",
    "2. 随包文档统一收进 **`Docs\\`**：`更新日志.txt`、`用前必看.txt`；开始菜单里的"
    "「使用前必看 / 更新日志 / 开源许可」快捷方式已指向新位置。",
    "3. 升级安装会顺手清掉旧版本留在程序根目录的那批许可 / 文档副本，不会出现「新位置一份 + 根目录旧一份」并存。",
    "4. 程序目录里**不再有 `LICENSE.txt`**（它和 `LICENSES\\LICENSE` 本来是同一份 GPL 正文的重复件），升级时也会被清掉。",
    "三、卸载",
    "1. 修复「卸载之后 `L-Mechrevo` 安装文件夹还在」：卸载会把安装日志副本一并删除，"
    "安装文件夹现在能真正被删干净（用户数据仍按卸载时的询问决定保留还是删除）。",
    "四、驱动",
    "1. **ACPI 驱动（UWACPIDriver）改为随程序自带**：不论安装时命中哪个 GCU 变体、GCU 步骤是否成功，"
    "程序目录下的 `UWACPIDriver\\` 都会带上 `UWACPIDriver.inf` / `uwacpidriver.cat` / `UWACPIDriver.sys` 三件，"
    "不再依赖 GCU 载荷是否落盘。",
    "五、版本号",
    "1. 程序集版本 / 文件版本 0.291.7.2 → **0.291.7.3**（产品版本串 `0.291.7.3-beta22.7.3`）；"
    "安装包文件名 `LM-0.291.7.3-beta22.7.3-setup.exe`。",
    "",
]).encode("utf-8")  # 结尾自带一个 CRLF（join 尾部空串）=> 文件仍以 CRLF 收尾


def sha(b):
    return hashlib.sha256(b).hexdigest().upper()


def main():
    old_root = open(ROOT_LOG, "rb").read()
    old_pkg = open(PKG_LOG, "rb").read()

    print("before: root  %d B  %s" % (len(old_root), sha(old_root)[:24]))
    print("before: pkg   %d B  %s" % (len(old_pkg), sha(old_pkg)[:24]))
    if old_root != old_pkg:
        print("!! 两份不同源，停止"); return 2
    if len(old_root) != EXPECT_LEN:
        print("!! 长度与预期不符（%d != %d），停止" % (len(old_root), EXPECT_LEN)); return 2
    if sha(old_root)[:20] != EXPECT_SHA:
        print("!! SHA256 前缀与预期不符（%s != %s），停止" % (sha(old_root)[:20], EXPECT_SHA)); return 2
    if b"0.291.7.3" in old_root:
        print("!! 已含 0.291.7.3，停止（避免重复追加）"); return 2
    if not old_root.endswith(b"\r\n"):
        print("!! 原文不是以 CRLF 收尾，停止"); return 2
    if old_root.count(b"\x00"):
        print("!! 原文含 NUL，停止"); return 2

    new = old_root + SECTION

    # 先写 TEMP 并校验
    with open(TMP, "wb") as f:
        f.write(new)
    chk = open(TMP, "rb").read()
    assert chk == new, "TEMP 写回不一致"
    assert chk[:len(old_root)] == old_root, "前缀被改动"
    assert chk[len(old_root):] == SECTION, "追加段不一致"
    assert len(chk) == len(old_root) + len(SECTION)
    assert chk[-2:] == b"\r\n" and b"\x00" not in chk
    assert chk[:3] != b"\xef\xbb\xbf", "不应有 BOM"
    print("temp ok: %d B  %s  (+%d B)" % (len(chk), sha(chk)[:24], len(SECTION)))

    # TEMP 校验通过后才替换（用 os.replace 做原子替换，目标先备份到 TEMP）
    for tgt in (ROOT_LOG, PKG_LOG):
        cur = open(tgt, "rb").read()
        if cur != old_root:
            print("!! 替换前 %s 已被改动，停止" % tgt); return 2
        bak = os.path.join(os.environ["TEMP"], "_r14_bak_" + os.path.basename(os.path.dirname(tgt)) + "_changelog.txt")
        open(bak, "wb").write(cur)
        with open(tgt, "wb") as f:
            f.write(chk)
        back = open(tgt, "rb").read()
        assert back == chk, tgt
        print("written: %s  %d B  %s  (bak %s)" % (tgt, len(back), sha(back)[:24], bak))

    print("\n--- 追加正文（crlf 行）---")
    print(SECTION.decode("utf-8").replace("\r\n", "\n"))
    return 0


if __name__ == "__main__":
    sys.exit(main())
