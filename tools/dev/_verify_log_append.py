# -*- coding: utf-8 -*-
"""Verify the two appended log files: title, user quote, history intact, valid UTF-8."""
import io

LOG = r"D:\openLM\修改日志.txt"
CHG = r"D:\openLM\更新日志.txt"

TITLE = "[2026-10-05 27:00] \u4ea7\u54c1\u5316\u6bcf\u6838 CO\uff1a\u6bcf\u6838\u8bfb\u5199 + \u91cd\u7f6e(\u6bcf\u6838\u51990) + \u4fdd\u5b58 + \u5f00\u673a\u81ea\u52a8\u5e94\u7528\u5f00\u5173(\u9ed8\u8ba4\u5173)"
QUOTE = "\u53ef\u4ee5\u5199\u8fdb\u4ea7\u54c1\u4e86 \u70b9\u91cd\u7f6e\u662f\u6bcf\u4e2a\u6838\u5fc3\u51990 \u7136\u540e\u5141\u8bb8\u5f00\u673a\u81ea\u52a8\u5e94\u7528\u548c\u4fdd\u5b58\u8d85\u9891\u8bbe\u7f6e"
PREV_TAIL = "13,145,497"          # ASCII marker from an older entry
PREV_TAIL2 = "\u672c\u6b21\u4ecd\u672a\u6253\u5305\u3001\u672a\u6253 NoGCU\u3001\u672a\u6539\u4ea7\u54c1\u6e90\u7801"

out = []
log = io.open(LOG, encoding="utf-8").read()
chg = io.open(CHG, encoding="utf-8").read()

out.append("修改日志.txt chars = %d" % len(log))
out.append("  title present          : %s" % (TITLE in log))
out.append("  user quote present     : %s" % (QUOTE in log))
out.append("  history intact marker  : %s" % (PREV_TAIL2 in log))
out.append("  title at tail          : %s" % (TITLE.split("]")[1].strip()[:12] in log[-20000:]))
out.append("更新日志.txt chars = %d" % len(chg))
out.append("  per-core section       : %s" % ("\u6bcf\u6838 Curve Optimizer" in chg[-3000:]))
out.append("  auto-apply default off : %s" % ("\u9ed8\u8ba4\u5173" in chg[-3000:]))

text = "\n".join(out)
io.open(r"D:\openLM\diagnostics\percore-20261005-2700\_log_verify.txt", "w", encoding="utf-8").write(text + "\n")
print(text.encode("ascii", "backslashreplace").decode())
