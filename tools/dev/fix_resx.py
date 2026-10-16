# -*- coding: utf-8 -*-
"""修复 resx：截断误插内容，用 ElementTree 正确插入新键"""
import re, xml.etree.ElementTree as ET

zh = {
    'GpuRouteIgpu': '集显模式',
    'GpuRouteDirect': '独显直连',
    'GpuRouteStandard': '混合模式',
    'GpuRouteUnknownTag': '显卡状态未知',
    'GpuRouteUnknown': '显卡输出路由未知，切换显卡模式或重启后重试',
    'GpuRestartPendingTag': '重启以应用',
    'GpuIgpuNeedsStandard': '独显直连模式下无法切换到集显，请先切换到混合模式',
    'GpuRouteIgpuTip': '切换到集显模式',
    'GpuRouteStandardTip': '切换到混合模式',
    'GpuRouteDirectTip': '切换到独显直连',
    'GpuPrefHighPerf': '高性能 GPU',
    'GpuPrefAuto': '自动选择',
    'GpuPrefUnknown': '未知',
    'GpuPrefTip': '在 NVIDIA 驱动设置中管理显卡偏好',
    'GpuRouteApplied': '已应用：{0}',
    'GpuRouteNotApplied': '切换未应用，当前为{0}',
}
en = {
    'GpuRouteIgpu': 'iGPU Mode',
    'GpuRouteDirect': 'dGPU Mode',
    'GpuRouteStandard': 'Hybrid Mode',
    'GpuRouteUnknownTag': 'GPU status unknown',
    'GpuRouteUnknown': 'GPU output route unknown; switch modes or restart to recover',
    'GpuRestartPendingTag': 'Restart to apply',
    'GpuIgpuNeedsStandard': 'Unavailable in dGPU mode; switch to Hybrid first',
    'GpuRouteIgpuTip': 'Switch to iGPU mode',
    'GpuRouteStandardTip': 'Switch to Hybrid mode',
    'GpuRouteDirectTip': 'Switch to dGPU mode',
    'GpuPrefHighPerf': 'High performance GPU',
    'GpuPrefAuto': 'Auto select',
    'GpuPrefUnknown': 'Unknown',
    'GpuPrefTip': 'Manage GPU preference in NVIDIA driver settings',
    'GpuRouteApplied': 'Applied: {0}',
    'GpuRouteNotApplied': 'Not applied; current: {0}',
}

def fix(path, values):
    s = open(path, encoding='utf-8', errors='replace').read()
    # 1) 截断到最后一个 </root>
    end = s.rfind('</root>')
    assert end > 0, 'no root in ' + path
    s = s[:end + len('</root>')]
    # 2) 解析
    ET.register_namespace('', 'http://schemas.microsoft.com/winfx/2006/xaml/presentation')
    root = ET.fromstring(s)
    ns = '{http://schemas.microsoft.com/winfx/2006/xaml/presentation}'
    existing = {d.get('name') for d in root.findall(ns + 'data')}
    added = 0
    for k, v in values.items():
        if k in existing:
            continue
        d = ET.SubElement(root, ns + 'data')
        d.set('name', k)
        d.set('xml:space', 'preserve')
        val = ET.SubElement(d, ns + 'value')
        val.text = v
        added += 1
    # 3) 序列化：保持可读缩进
    out = ET.tostring(root, encoding='unicode')
    # 美化：简单缩进
    import xml.dom.minidom as md
    pretty = md.parseString(ET.tostring(root, encoding='utf-8')).toprettyxml(indent='  ', encoding='utf-8').decode('utf-8')
    # 去掉 xml 声明重复问题（保留原声明格式）
    if not pretty.startswith('<?xml'):
        pretty = '<?xml version="1.0" encoding="utf-8"?>\n' + pretty
    open(path, 'w', encoding='utf-8', newline='').write(pretty)
    print('FIXED', path, 'added', added)

fix(r'D:\openLM\source\MechrevoLite.Properties.Strings.resx', en)
fix(r'D:\openLM\source\MechrevoLite.Properties.Strings.zh-CN.resx', zh)
print('DONE')
