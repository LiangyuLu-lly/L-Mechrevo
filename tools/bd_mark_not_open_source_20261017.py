# -*- coding: utf-8 -*-
r"""给"不可开源"的存档文档加抬头标记（配合根目录 NOT-OPEN-SOURCE.md）。

标记只加在**含反编译代码片段**的文件上；纯协议事实的文档不加（避免标记贬值）。
"""
import os
import sys

sys.stdout.reconfigure(encoding="utf-8", errors="replace")
BASE = r"D:\openLM\docs\notes"

L1 = """> ⛔ **不可开源（发布前必须移除或改写）** —— 本文含**原厂控制台反编译**的代码片段（厂商专有）。
> 边界与处置见根目录 [`NOT-OPEN-SOURCE.md`](../../NOT-OPEN-SOURCE.md)。
> 对外发布开源版时：**删掉本文件**，或把代码片段改写成"事实"描述（功能号 / 常量 / 顺序 / 时序），⛔ 不要保留原文。
"""

L2 = """> ⛔ **不可开源（发布前必须移除或改写）** —— 本文含**第三方修改版控制台 / 无许可项目**的反编译片段。
> 边界与处置见根目录 [`NOT-OPEN-SOURCE.md`](../../NOT-OPEN-SOURCE.md)。
> 对外发布开源版时：**删掉本文件**，或改写成"事实"描述。
"""

TARGETS = {
    "原厂控制台-蛟龙游戏控制中心-分析-20261017.md": L1,
    "30系官方控制台-分析-20261017.md": L1,
    "官方控制台-输入开关实现-20261017.md": L1,
    "第三方JiaoLong7.3-灯光指令-20261017.md": L2,
    "第三方JiaoLong7.3-四个开关实现-20261017.md": L2,
    "充电阈值基线-静态挖掘-20261016.md": L2,
}


def must(c, m):
    if not c:
        raise SystemExit("FAIL: " + m)


for name, banner in TARGETS.items():
    p = os.path.join(BASE, name)
    with open(p, "rb") as f:
        raw = f.read()
    must(not raw.startswith(b"\xef\xbb\xbf"), "有 BOM: " + name)
    text = raw.decode("utf-8")
    must("\r\n" not in text, "不是纯 LF: " + name)
    if "不可开源（发布前必须移除或改写）" in text:
        print("  = 已有标记，跳过：" + name)
        continue
    lines = text.split("\n")
    # 插在 H1 之后（没有 H1 就放最前）
    if lines and lines[0].startswith("# "):
        new = lines[0] + "\n\n" + banner + "\n" + "\n".join(lines[1:])
    else:
        new = banner + "\n" + text
    with open(p, "wb") as f:
        f.write(new.encode("utf-8"))
    print("  ✅ 已加标记：" + name)

print("DONE")
