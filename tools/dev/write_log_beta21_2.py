# -*- coding: utf-8 -*-
import io
log_path = r'D:\openLM\修改日志.txt'
entry = '''
2026-10-03 beta21.2 (0.290.2) 修复
1. 安装器升级流程：删除显式调用 Uninstall-Gcu.ps1 的步骤。
   依据：卸载旧版 L-Mechrevo 时，unins000 的 [UninstallRun] 会自行执行
   Uninstall-Gcu.ps1 卸载 GCU 服务，升级流程里再调一次属重复（也是此前
   "powershell 卡一会"的来源）。保留 GCU 进程 taskkill 以解除文件占用。
2. 40 系屏幕校色无记忆修复：初始化/轮询状态 ResolveColorCalibrationMode
   在 40 系 GCU 无档位回读时，优先读 LM 自身记忆（AppConfig calib_mode，
   设置校色时已写入），从未用 LM 设置过才回退读官方控制台注册表。
   效果：在 LM 内关闭校色后重启 LM，开关保持"关闭"，不再回跳"打开"。
3. 托盘右键菜单核显模式项误显示修复：显示条件由
   CanOfferIgpuOnly || (CanOfferGpuModeSwitch && SupportsDgpuDirect)
   收紧为仅 CanOfferIgpuOnly。原先的兜底分支会把蛟龙 16s 这类
   "双显双模（混合+独显直连、无核显模式）"机型误判为支持核显模式。
   主界面布局（GpuRowLayout）一直正确，仅托盘受影响。
'''
with io.open(log_path, 'a', encoding='utf-8', newline='\n') as f:
    f.write(entry)
t = io.open(log_path, encoding='utf-8').read()
print('写入成功, 尾部60字:', t[-60:])
