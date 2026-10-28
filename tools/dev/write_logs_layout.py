# -*- coding: utf-8 -*-
def append(path, text):
    with open(path, 'a', encoding='utf-8') as f:
        f.write(text)

mod = '''【2026-10-03 布局层 HotSwap 死代码删除（用户拍板：动，但不影响诊断）】
- 背景：GpuRowLayout=HotSwap 依赖 Capabilities.GpuHotSwap（= Flag("GpuHotSwapSwitchSupport") && Flag("lgpuHotSwapSwitchStatus")）——抓包证实两类 GCU 均不上报这两个字段 → 恒 false → 布局判定与高亮分支永不执行（死代码）。与核显省电判断不同源（CanOfferGpuHotSwap），独立清理
- 删除范围：
  1. GpuRowLayout.cs：枚举删 HotSwap 值（Hidden/NvPreference/Mux2/Mux3；HasIgpuSegment/UsesRouteReadback 的数值判断经核对不受影响）
  2. GpuRowLayouts.cs：Resolve 删第 3 参 hotSwap 及 if 分支（调用签名 4→3 参）
  3. MechrevoHw.cs：删 SupportsGpuHotSwap（:413）、CanOfferGpuHotSwap（:441）；GpuRowLayout 属性 Resolve 调用改 3 参（:469）
  4. SettingsForm.cs：删 flag = _gpuRowLayout==HotSwap && num==2（:6913）及其引用（:6916 !flag、:6919 三元恒取 GpuRouteIgpuTip、:6961 menuEco.Enabled=!flag → true，菜单项恒启用——与按钮行为一致）
- 保留（诊断/审计基建）：Capabilities.GpuHotSwap 能力位（MechrevoDeviceCapabilities.cs:112,310）+ 诊断包"显卡热切换: 否"行（DiagnosticSystemInfo.cs:327）+ FeatureMatrix 字段定义 + DisplayRouteMatrix 证据表 + UiAuditRunner 审计样本 + HardcodeDispositions 注释（均为非执行路径的审计/证据资产）
- 返工记录：首次删除后 SettingsForm.cs:6961 遗留 menuEco.Enabled=!flag 引用导致 CS0103 编译错误 → 整行正则匹配修复；替换脚本第二次（elevated_replace_hotswap.ps1）在 Move-Item 环节中断（日志止于 backup，.old 不存在、Copy 未执行）→ 改用简化提权脚本（elevated_copy_only.ps1）直接 Copy-Item -Force + 启动完成
- 编译 0 错误；提权替换完成（安装目录 DLL 1896448B @11:12:17，LM PID 24616）；备份 L-Mechrevo_20261003_111248.dll

'''

upd = '''【2026-10-03】〇、显卡切换布局层清理
- 删除永不触发的 GpuRowLayout=HotSwap 布局判定与高亮分支（GCU 不上报热切换能力字段，恒 false 死代码）
- 诊断包"显卡热切换"能力展示保留不变

'''

append(r'D:\openLM\修改日志.txt', mod)
append(r'D:\openLM\更新日志.txt', upd)
print('logs ok')
