# -*- coding: utf-8 -*-
r"""r24 follow-up: drop the now-unused 'Rc' variable from install_script.iss (ISCC hint-free)."""
import hashlib, os

p = r"D:\openLM\installer-src\install_script.iss"
raw = open(p, "rb").read()
old = b"  ErrCode, Rc: Integer;\r\n"
assert raw.count(old) == 1, raw.count(old)
out = raw.replace(old, b"  ErrCode: Integer;\r\n")
assert b"Rc := 0" not in out and b"IntToStr(Rc)" not in out
tmp = p + ".tmp-write"
open(tmp, "wb").write(out)
assert open(tmp, "rb").read() == out
os.replace(tmp, p)
fin = open(p, "rb").read()
print("iss %d -> %d  sha %s" % (len(raw), len(fin), hashlib.sha256(fin).hexdigest().upper()))
print("ALL-DONE")
