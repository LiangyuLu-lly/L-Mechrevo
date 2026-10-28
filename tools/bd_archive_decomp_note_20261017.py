# -*- coding: utf-8 -*-
r"""2026-10-17（用户口径）：抗编译原文要保留 ⇒ 统一放 `_upstream\decompiled-originals\`。

本次只改文档（清单/交接），并把「专用位置」写进开源边界；不动源码。
"""
import os
import sys

sys.stdout.reconfigure(encoding="utf-8", errors="replace")

# ── 1) NOT-OPEN-SOURCE.md ──────────────────────────────────────────
P = r"D:\openLM\NOT-OPEN-SOURCE.md"
with open(P, "rb") as f:
    t = f.read().decode("utf-8")
assert "\r\n" not in t

# 重新编号后面的三节（从后往前替换，避免撞车）
t = t.replace("## 五、结论（给下一个接手的人）", "## 六、结论（给下一个接手的人）")
t = t.replace("## 四、发布前怎么查（自动检查）", "## 五、发布前怎么查（自动检查）")
t = t.replace("## 三、本来就不在 git 里的材料（✅ 天然不进开源版）",
              "## 四、本来就不在 git 里的材料（✅ 天然不进开源版）")

NEW = """## 三、反编译原文的专用位置（✅ 2026-10-17 已统一）

> 用户口径：「**反编译原文要保留的**，你以后专门放一个位置就行」。

🆕 所有反编译产物统一放：

```
D:\\openLM\\_upstream\\decompiled-originals\\
  official-40-v0.3.15\\        蛟龙游戏控制中心.decompiled.cs     （原厂 40 系 · 144,711 B）
  official-30-v0.2.35\\        蛟龙游戏控制中心.decompiled.cs     （原厂 30 系 · 119,791 B）
  thirdparty-JiaoLong7.3\\     第三方蛟龙游戏控制中心.decompiled.cs（第三方 · 272,836 B）
  gcu-service\\                GCUService.decompiled.cs           （同方 GCU · 2,282,338 B）
  MANIFEST.md                  ⭐ 来源/大小/sha256/复现命令/使用规则
```

- ✅ 这个目录在 `_upstream\\` 下 ⇒ `.gitignore:67` 已忽略 ⇒ **不进 git、不进安装包、不进开源版**
- ✅ 原文**保留**（不删、不改写）；查证时直接在这里 grep 即可
- ⛔ 仍然**不许**把这里的片段复制进 `source\\`（含注释），也不许复制进 `docs\\` 之外的分发位置
- ⚠️ `docs\\notes\\` 里**已经**贴了原文的那 6 份文档保持"⛔ 不可开源"标记不变；
  发布开源版时按本清单剔除即可 —— **原文不受影响，它本来就不在 git 里**
- ⚠️ 这是**本机存档**（`_upstream` 不进 git ⇒ 换机器需重新解包反编译，命令见 `MANIFEST.md`）

"""
anchor = "## 四、本来就不在 git 里的材料（✅ 天然不进开源版）"
assert anchor in t
t = t.replace(anchor, NEW + anchor)

with open(P, "wb") as f:
    f.write(t.encode("utf-8"))
print("[OK] NOT-OPEN-SOURCE.md -> 新增「反编译原文专用位置」")

# ── 2) 交接文档 §7.7 ──────────────────────────────────────────────
H = r"D:\openLM\docs\notes\交接-宝龙达-0.291.9.7-20261017.md"
with open(H, "rb") as f:
    h = f.read().decode("utf-8")
assert "\r\n" not in h

OLD = "- ✅ 取证原文只留在 `docs\\notes\\`（内部记录）；**记录时也尽量只写事实**（取值、功能号、常量、文件/行号），别整段贴代码"
NEW2 = ("- ✅ 取证原文统一放 `_upstream\\decompiled-originals\\`（⭐ 用户 2026-10-17："
        "「反编译原文要保留的，专门放一个位置」）——该目录在 `_upstream\\` 下 ⇒ **不进 git**；"
        "清单与复现命令见其中的 `MANIFEST.md`\n"
        "- ✅ `docs\\notes\\` 里如需摘录（仅限内部记录）：抬头必须标「⛔ 不可开源」，发布前按 `NOT-OPEN-SOURCE.md` 剔除；"
        "**记录时尽量只写事实**（取值、功能号、常量、文件/行号）")
assert OLD in h, "交接 §7.7 锚点不匹配"
h = h.replace(OLD, NEW2)
with open(H, "wb") as f:
    f.write(h.encode("utf-8"))
print("[OK] 交接文档 §7.7 -> 写明专用位置")

print("DONE")
