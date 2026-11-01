# -*- coding: utf-8 -*-
"""查群友版现有符号：SettingsForm/MechrevoHw/MechrevoService/GPUModeControl/Strings"""
import os, re

root = r'D:\openLM\source'

checks = [
    # SettingsForm
    'RefreshDeviceCapabilities', 'HotSwitchFromUi', 'ApplySnapshot', 'RequestContextMenuRefresh',
    'VisualiseGPUButtons', 'LockGPUModes', 'HideGPUModes', 'ButtonUltimate_Click', 'ButtonStandard_Click',
    'GpuRowLayout', 'CanOfferIgpuMuxTarget', 'CanOfferGpuModeSwitch', 'CanOfferGpuHotSwap', 'CanOfferNvPreferredGpu',
    # MechrevoService
    'CancelPendingGpuRestartAsync', 'SetNvPreferredGpuAsync', 'SwitchAutomaticGpuMode', 'GpuApplyResult',
    'RequestGpuModeRestartOutcomeAsync', 'RefreshGpuModeStatus', 'SwitchGpuMode',
    # 其它
    'GpuRestartPendingTag', 'GpuRouteUnknownTag', 'GPUChanging', 'GpuRouteUnknown', 'GpuRouteIgpu', 'GpuRouteDirect',
    'GpuRouteStandard', 'GpuIgpuNeedsStandard', 'GpuPrefHighPerf', 'GpuPrefAuto', 'GpuRouteApplied', 'GpuRouteNotApplied',
    'VisualiseGPUMode', 'ShowGpuRestartPromptAsync', 'GpuStatusUnavailable', 'RestartToApply',
    'nvRestartPending', 'RunAfterBootAsync',
]

for kw in checks:
    files = []
    for dp, dn, fn in os.walk(root):
        if 'bin' in dp or 'obj' in dp:
            continue
        for f in fn:
            if not f.endswith('.cs') and not f.endswith('.resx'):
                continue
            p = os.path.join(dp, f)
            try:
                s = open(p, encoding='utf-8', errors='replace').read()
            except Exception:
                continue
            if kw in s:
                files.append(os.path.relpath(p, root))
    print(kw, '->', files if files else 'MISSING')
