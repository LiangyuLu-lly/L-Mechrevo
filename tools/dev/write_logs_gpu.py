# -*- coding: utf-8 -*-
"""追加修改日志 + 更新日志"""
import io

mod = r'D:\openLM\修改日志.txt'
upd = r'D:\openLM\更新日志.txt'

mod_txt = '''
【2026-10-03】beta21 显卡切换高亮/布局体系移植（版本号保持 0.290.0，未授权不升）
- SettingsForm.cs：VisualiseGPUMode 换成 beta21 版（ResolveHighlightedGpuMode 路由回读解析：Mux2/3/HotSwap 走 GpuRouteMonitor 快照、NvPreference 走 NvPreferredGpuMonitor、否则 AppConfig/服务回读）；新增 GpuRowStatusText（切换中/重启待应用/未知态状态行）、ApplyGpuRowLayout（按钮文字+tooltip+布局日志）；HotSwap+独显直连时集显按钮置灰；新增字段 _gpuRowLayout/_gpuSwitchInProgress/_lastGpuModeHint
- HasPendingGpuRestart 改为委托 GpuRestartVerifier.HasPendingThisBoot()；MarkGpuRestartPending 加 targetMode 可选参数（写 gpu_restart_target）；ShowGpuRestartPromptAsync 传 target
- RefreshGpuModeUiStateAsync：UsesRouteReadback→GpuRouteMonitor.RefreshAsync()；NvPreference→NvPreferredGpuMonitor.Store
- ApplySnapshot：UsesRouteReadback 时 GpuRouteMonitor.Snapshot() + GpuRowStatusText
- RefreshDeviceCapabilities：_gpuRowLayout = hw?.GpuRowLayout ?? Hidden
- Program.cs：启动延迟 8s 后 GpuRestartVerifier.RunAfterBootAsync（重启后最多 3 次回读判定 Applied/NotApplied，ToastForm.ShowNotice/ShowFailure 提示）
- GPUModeControl.InitGPUMode：GCU 已连接分支加 settings.ApplyGpuRowLayout(Program.hw.GpuRowLayout)
- 新增 15 个 Gpu 文件：GpuRowLayout/GpuRowLayouts/GpuRoute/GpuRouteReadback/GpuRouteInference/GpuRouteMonitor/GpuRouteProbe/PanelAdapterKind/DgpuPresence/GpuRestartVerdict/GpuRestartVerifier/UefiDisplayModeDiagnostics/NvPreferredGpu/NvPreferredGpuMonitor/NvPreferredGpuReader（来自 beta21 decomp）
- DisplayNative.cs：补 DISPLAYCONFIG_ADAPTER_NAME 结构 + DisplayConfigGetDeviceInfo 重载（GpuRouteProbe 依赖）
- ToastForm.cs：新增 ShowNotice/NoticeCallback（GpuRestartVerifier 依赖，群友版原只有 ShowFailure）
- Strings 三件套：新增 16 键（GpuRouteIgpu/Direct/Standard/UnknownTag/Unknown、GpuRestartPendingTag、GpuIgpuNeedsStandard、GpuRouteIgpuTip/StandardTip/DirectTip、GpuPrefHighPerf/Auto/Unknown/Tip、GpuRouteApplied/NotApplied），中英双语同步
- resx 两次损坏及教训：①字符串拼接插在 </root> 后 → 多根 XML（MSB3103）；②minidom 全量重写破坏内嵌 xs:schema → NullReference。修复：文本提取 data 块 + 18.6 原始头重组。今后 resx 只做文本级插入/局部操作，禁止全量 XML 重写
- 编译 0 错误（-p:NoWarn=WFO1000），产物 L-Mechrevo.dll 1913344B / zh-CN resources 53248B
- 注意：NvPreference 布局在群友版暂不可达（无 CanOfferNvPreferredGpu，Resolve 首参写死 false），NvPreferredGpu* 代码为兼容性保留，后续支持 NVIDIA DRS 偏好再启用
'''

upd_txt = '''
〇、显卡切换高亮/布局体系（0.290.0，与作者 beta21 对齐）
1. 显卡切换按钮高亮按硬件实际状态：Mux2/3/HotSwap 机型走显示输出路由实时回读解析，NVIDIA 首选 GPU 机型走驱动偏好，其余按配置/服务回读
2. 切换后重启确认闭环：切换提示"重启以应用"→ 重启后自动回读判定"已应用/未应用"，未应用弹失败提示
3. 独显直连模式下集显按钮置灰（HotSwap 机型），避免无效切换
4. 未知态提示：路由读取失败时显示"显卡状态未知"，悬停有说明
'''

def append(path, text):
    try:
        with open(path, 'a', encoding='utf-8') as f:
            f.write(text)
        print('OK append', path)
    except Exception as e:
        print('ERR', path, e)

append(mod, mod_txt)
append(upd, upd_txt)
