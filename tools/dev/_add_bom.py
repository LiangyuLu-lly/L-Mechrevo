# -*- coding: utf-8 -*-
"""Re-save a PowerShell script as UTF-8 **with BOM** (Windows PowerShell 5.1 reads BOM-less files
as ANSI, which mangles Chinese comments and can break parsing)."""
import io
import sys

for path in sys.argv[1:]:
    with io.open(path, "r", encoding="utf-8-sig", newline="") as handle:
        text = handle.read()
    with io.open(path, "w", encoding="utf-8-sig", newline="") as handle:
        handle.write(text)
    print("BOM added: " + path)
