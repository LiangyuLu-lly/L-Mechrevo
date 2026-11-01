# -*- coding: utf-8 -*-
"""StaticQuick 白名单加 6 个 sleep 键（注册表功能不依赖 GCU 能力）"""
import io

PATH = r'D:\openLM\source\MechrevoLite\SettingsForm.cs'
with io.open(PATH, 'r', encoding='utf-8') as f:
    src = f.read()

old = """			case "monitoroff":
			case "taskbarautohide":
			case "transparency":
			case "darktheme":
			case "startup":
				return true;"""
new = """			case "monitoroff":
			case "taskbarautohide":
			case "transparency":
			case "darktheme":
			case "startup":
			case "sleepnetoff":
			case "sleepaway":
			case "sleepsysreq":
			case "sleeprtcwake":
			case "sleepdeep":
			case "sleephibernate":
				return true;"""
assert src.count(old) == 1, 'expect 1 match, got %d' % src.count(old)
src = src.replace(old, new)

with io.open(PATH, 'w', encoding='utf-8', newline='') as f:
    f.write(src)
print('OK: StaticQuick sleep keys')
