# NVML 崩溃（JIGUANG）防护

诊断包：L-Mechrevo-诊断包-0.293.5.1-20261026-225708-JIGUANG-Series.zip

## 现场

- `2026-10-26 22:43:38.671` Event 1026 / 0xc0000005
- 栈：`NvmlHelper.nvmlDeviceGetPowerUsage` ← `RefreshLocked` ← `GetGpuUtilization` ← `HardwareControl.SampleLocalGpuUsage` ← 采样泵
- 22:43:08 显示器打开 → NVML 暂停 30s；22:43:28 解锁/显示变化 → 再暂停 8s；22:43:37.852 保护期结束；22:43:38.660 NVML Init；11ms 后崩溃
- 同类事件：10/23 17:38、10/24 13:45、10/24 17:18、10/26 22:43，栈相同
- 崩溃的是已安装旧版 0.293.5.1，但 WPF 共享的 `NvmlHelper` 仍包含同一原生调用

## 修复

JIGUANG 机型（BIOS `SystemProductName`/`BaseBoardProduct` 含 JIGUANG）在 WPF 进程内**永久停用 NVML 原生调用**：

- `NvmlHelper.DisableNativeForProcess()`
- 启动早期在 `EnableLocalMonitoring()` 之前执行
- GPU 温度/占用率/频率继续走 GCU
- GPU 功耗/显存频率改由独立 `nvidia-smi` 子进程读取（子进程崩溃不影响主进程）
- 其它机型保持原行为

## 构建

`bin-alt/nvmlfix` 0 错误。
