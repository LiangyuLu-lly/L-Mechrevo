# -*- coding: utf-8 -*-
"""批4：Program.cs RunAfterBootAsync + GPUModeControl.InitGPUMode + Strings 三件套"""
import re, io

# ============ 1. Program.cs ============
p = r'D:\openLM\source\MechrevoLite\Program.cs'
s = open(p, encoding='utf-8', errors='replace').read()
old = '''			if (AppConfig.IsOverlay())
			{
				hardwareOverlay?.StartOverlay();
			}
		};
		Application.Idle += deferredInitialization;'''
new = '''			if (AppConfig.IsOverlay())
			{
				hardwareOverlay?.StartOverlay();
			}
			GpuRestartVerifier.RunAfterBootAsync(TimeSpan.FromSeconds(8L));
		};
		Application.Idle += deferredInitialization;'''
assert old in s and s.count(old) == 1, 'Program.cs anchor'
s = s.replace(old, new, 1)
open(p, 'w', encoding='utf-8', newline='').write(s)
print('OK: Program.cs RunAfterBootAsync')

# ============ 2. GPUModeControl.cs InitGPUMode ============
g = r'D:\openLM\source\MechrevoLite.Gpu\GPUModeControl.cs'
t = open(g, encoding='utf-8', errors='replace').read()
old = '''			bool canOfferGpuModeSwitch = Program.hw.CanOfferGpuModeSwitch;
			bool flag = canOfferGpuModeSwitch && Program.hw.CanOfferIgpuOnly;
			bool flag2 = canOfferGpuModeSwitch && Program.hw.SupportsDgpuDirect;
			settings.VisualiseGPUButtons(flag | flag2, flag2, flag);
			settings.RefreshDeviceCapabilities();'''
new = '''			bool canOfferGpuModeSwitch = Program.hw.CanOfferGpuModeSwitch;
			bool flag = canOfferGpuModeSwitch && Program.hw.CanOfferIgpuOnly;
			bool flag2 = canOfferGpuModeSwitch && Program.hw.SupportsDgpuDirect;
			settings.ApplyGpuRowLayout(Program.hw.GpuRowLayout);
			settings.VisualiseGPUButtons(flag | flag2, flag2, flag);
			settings.RefreshDeviceCapabilities();'''
assert old in t and t.count(old) == 1, 'GPUModeControl anchor'
t = t.replace(old, new, 1)
open(g, 'w', encoding='utf-8', newline='').write(t)
print('OK: GPUModeControl InitGPUMode')

# ============ 3. Strings 三件套 ============
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

def add_resx(path, values):
    r = open(path, encoding='utf-8', errors='replace').read()
    # 去重
    existing = set(re.findall(r'<data name="([^"]+)"', r))
    add_keys = [k for k in values if k not in existing]
    for k in add_keys:
        block = '  <data name="%s" xml:space="preserve">\n    <value>%s</value>\n  </data>\n' % (k, values[k])
        # 插到 </resheader> 之后第一个 <data 之前，保持格式
        anchor = '</resheader>\n'
        idx = r.find(anchor)
        if idx >= 0:
            ins = idx + len(anchor)
            r = r[:ins] + block + r[ins:]
        else:
            r = r.rstrip() + '\n' + block
    open(path, 'w', encoding='utf-8', newline='').write(r)
    print('OK resx:', path, 'added', len(add_keys))

add_resx(r'D:\openLM\source\MechrevoLite.Properties.Strings.resx', en)
add_resx(r'D:\openLM\source\MechrevoLite.Properties.Strings.zh-CN.resx', zh)

# Strings.cs 强类型属性
sc = r'D:\openLM\source\MechrevoLite.Properties\Strings.cs'
t = open(sc, encoding='utf-8', errors='replace').read()
props = []
for k, v in en.items():
    prop = '\tinternal static string %s => ResourceManager.GetString("%s", resourceCulture);\n' % (k, k)
    props.append(prop)
# 插到类尾部（最后一个 '}' 前）
idx = t.rstrip().rfind('\n}')
if idx < 0:
    idx = t.rfind('}')
t = t[:idx] + ''.join(props) + t[idx:]
open(sc, 'w', encoding='utf-8', newline='').write(t)
print('OK: Strings.cs props added', len(props))
print('DONE 批4')
