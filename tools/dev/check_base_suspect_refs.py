# -*- coding: utf-8 -*-
r"""
在 GCUService.exe / GCUBridge.exe 里搜"可疑大文件"的引用，判断能否删除
================================================================================
对象：底座 payload\GCU\AiStoneService 里的可疑文件
  oled_care.scr 12.35MB / rtknrai.exe 2.48MB / DisplayInfo.dll 2.43MB /
  SQLite.Interop.dll 1.91MB / GCUUtil.exe 0.10MB / DeviceBase64.dll 0.27MB /
  GCUServicePlugin.dll 0.06MB / RTK* / *oled* / *screen* 等
方法：ASCII + UTF-16LE 双编码搜文件名（不含扩展名）+ 搜关键目录名
"""
import io
import os
import sys

sys.stdout.reconfigure(encoding="utf-8", errors="replace")

BASE = r'D:\openLM\installer-src\files\GCU\payload\GCU\AiStoneService'
N30 = r'D:\openLM\installer-src\files\GCU\payload\30\AiStoneService'

# 要验证的"可疑文件"（底座里）
SUSPECTS = [
    'oled_care.scr', 'rtknrai.exe', 'DisplayInfo.dll', 'SQLite.Interop.dll',
    'GCUUtil.exe', 'DeviceBase64.dll', 'GCUServicePlugin.dll',
    'EntityFramework.dll', 'System.Management.Automation.dll',
    'IntelOverclockingSDK.dll', 'UWDiskInfo.dll', 'UWAirplane.dll',
    'ACPIDriverDll.dll', 'OutputDiskInfo.exe', 'RegistryUtils.dll',
    'Gma.UserActivityMonitor.dll', 'Colourful.dll', 'M2Mqtt.Net.dll',
]


def load_strings(path):
    raw = io.open(path, 'rb').read()
    a = raw.decode('ascii', 'ignore')
    try:
        u = raw.decode('utf-16-le', 'ignore')
    except Exception:
        u = ''
    return a, u


def main():
    # 扫描器（优先 30 补丁里的，没有就用底座的）
    scanners = []
    for root, label in ((N30, '30补丁'), (BASE, '底座')):
        for name in ('GCUService.exe', 'GCUBridge.exe', 'GCUServicePlugin.dll'):
            p = None
            for dp, _d, fs in os.walk(root):
                if name in fs:
                    p = os.path.join(dp, name)
                    break
            if p:
                scanners.append((label, name, p))

    print("扫描器：")
    for label, name, p in scanners:
        print("  [%s] %-24s %8.1f KB" % (label, name, os.path.getsize(p) / 1024.0))
    print()

    # 各可疑文件的实际大小与位置
    print("可疑文件（底座内）：")
    sizes = {}
    for dp, _d, fs in os.walk(BASE):
        for f in fs:
            if f in SUSPECTS:
                full = os.path.join(dp, f)
                sizes[f] = os.path.getsize(full)
                print("  %9.1f KB  %s" % (os.path.getsize(full) / 1024.0, os.path.relpath(full, BASE)))

    print("\n引用检测（文件名去掉扩展名后搜 ascii + utf16）：")
    for label, name, path in scanners:
        a, u = load_strings(path)
        print("--- %s / %s ---" % (label, name))
        for f in sorted(sizes, key=lambda k: -sizes[k]):
            stem = os.path.splitext(f)[0]
            ha = stem.lower() in a.lower()
            hu = stem.lower() in u.lower()
            if ha or hu:
                print("   命中 %-30s ascii=%-5s utf16=%-5s  (%.1f KB)"
                      % (f, ha, hu, sizes[f] / 1024.0))
        # 额外搜一些关键词
        for kw in ('ota', 'OTA', 'firmware', 'Firmware', 'oled', 'OLED'):
            if kw in a or kw in u:
                print("   关键词命中: %s" % kw)
    return 0


if __name__ == '__main__':
    sys.exit(main())
