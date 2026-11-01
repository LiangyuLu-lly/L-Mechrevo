# -*- coding: utf-8 -*-
import os, glob
for root, dirs, files in os.walk(r'D:\openLM'):
    if '.sessions' in root or 'node_modules' in root or '.git' in root:
        continue
    for f in files:
        if f.endswith('.resx') and 'Strings' in f:
            print(os.path.join(root, f))
