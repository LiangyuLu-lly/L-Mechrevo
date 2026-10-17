# -*- coding: utf-8 -*-
"""
开源范围检查（配合根目录 `NOT-OPEN-SOURCE.md` 与 `tools/open_source_scope.json`）。

用途：发布开源版之前跑一遍，确认
  ① 清单里 ⛔ 的文件**还在 git 里**（在 ⇒ 必须剔除/改写）；
  ② 全仓（git 跟踪的文本文件里）**没有**未登记的"反编译代码特征词"；
  ③ 清单本身没过期（列了但文件已不存在 ⇒ 提示清理清单）。

用法：
    py -3 tools\\check_open_source_scope.py            # 只报告
    py -3 tools\\check_open_source_scope.py --strict   # 有违规 ⇒ 退出码 1（可挂发布流程）

注意：本脚本**不改任何文件**，只读。
"""
import json
import os
import subprocess
import sys

sys.stdout.reconfigure(encoding="utf-8", errors="replace")
ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
SCOPE = os.path.join(ROOT, "tools", "open_source_scope.json")

TEXT_EXT = {".md", ".cs", ".py", ".ps1", ".iss", ".txt", ".json", ".xml", ".resx",
            ".cfg", ".ini", ".yml", ".yaml", ".js", ".ts", ".bat", ".cmd", ".sh"}


def git_tracked():
    out = subprocess.run(["git", "-C", ROOT, "ls-files", "-z"],
                         capture_output=True, check=True).stdout
    return [p for p in out.decode("utf-8", "replace").split("\0") if p]


def norm(p):
    return p.replace("\\", "/")


def is_text(rel):
    if os.path.splitext(rel)[1].lower() not in TEXT_EXT:
        return False
    full = os.path.join(ROOT, rel)
    try:
        with open(full, "rb") as f:
            head = f.read(4096)
        return b"\0" not in head
    except OSError:
        return False


def main():
    strict = "--strict" in sys.argv
    with open(SCOPE, "r", encoding="utf-8") as f:
        scope = json.load(f)

    blocked = {norm(x["path"]): x for x in scope.get("blocked", [])}
    allowed = {norm(x["path"]): x for x in scope.get("allowed_open", [])}
    markers = scope.get("markers", [])

    tracked = git_tracked()
    tracked_set = set(tracked)
    print(f"# git 跟踪文件 {len(tracked)} 个；清单 blocked={len(blocked)} allowed_open={len(allowed)}")

    # ① 清单里的文件是否仍被跟踪
    still = []
    stale = []
    for path, meta in blocked.items():
        (still if path in tracked_set else stale).append(path)
    print("\n## ① ⛔ 不可开源但仍在 git 里的文件（发布前必须剔除）")
    if still:
        for p in sorted(still):
            print(f"  ⛔ [{blocked[p]['level']}] {p}  —— {blocked[p]['reason']}")
    else:
        print("  （无）")
    if stale:
        print("\n  ℹ️ 清单里这些路径已不在 git（清理清单或恢复文件）：")
        for p in sorted(stale):
            print(f"     - {p}")

    # ② 扫描特征词
    print("\n## ② 全仓扫描：反编译代码特征词")
    hits = {}
    for rel in tracked:
        if not is_text(rel):
            continue
        full = os.path.join(ROOT, rel)
        try:
            with open(full, "r", encoding="utf-8", errors="replace") as f:
                text = f.read()
        except OSError:
            continue
        found = [m for m in markers if m in text]
        if found:
            hits[rel] = found
    if not hits:
        print("  ✅ 0 命中")
    else:
        for rel in sorted(hits):
            tag = "⛔ 已标记" if rel in blocked else ("✅ 许可允许" if rel in allowed else "⚠️ 新增未标记")
            print(f"  {tag}: {rel}  ← {', '.join(hits[rel])}")

    # ③ 汇总
    unmarked = [r for r in hits if r not in blocked and r not in allowed]
    print("\n## ③ 结论")
    print(f"  ⛔ 需剔除（清单内且仍被跟踪）：{len(still)}")
    print(f"  ⚠️ 新增未标记：{len(unmarked)}")
    if unmarked:
        print("  ⇒ 这些文件含反编译特征词但不在清单里：请要么删除/改写，要么登记进")
        print("     tools\\open_source_scope.json 的 blocked / allowed_open（并同步 NOT-OPEN-SOURCE.md）。")

    bad = bool(still) or bool(unmarked)
    if strict and bad:
        return 1
    return 0


if __name__ == "__main__":
    sys.exit(main())
