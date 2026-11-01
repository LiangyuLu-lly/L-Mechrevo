# -*- coding: utf-8 -*-
"""Check that tools\\add_percore_resx.py has balanced string literals (no stray ASCII quotes)."""
import ast
import io

P = r"D:\openLM\tools\add_percore_resx.py"
src = io.open(P, encoding="utf-8").read()
tree = ast.parse(src)
print("AST OK, top-level statements =", len(tree.body))
for node in tree.body:
    if isinstance(node, ast.Assign) and getattr(node.targets[0], "id", "") in ("NEW_EN", "NEW_ZH"):
        print(node.targets[0].id, "entries =", len(node.value.elts))
