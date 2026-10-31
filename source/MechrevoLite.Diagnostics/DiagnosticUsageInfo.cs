// ⭐⭐ 2026-10-18（机主：遥测里 CPU/GPU 后面的占用率显示不对）：
//   ⭐ 新增诊断节「占用率来源明细」—— ⭐ 只读 ✅
//
// ⭐ 目的：⭐ 让下一份诊断包**一眼定位**：
//   ① CPU 占用率的**实际来源**（性能计数器 % Processor Utility / % Processor Time / GetSystemTimes 差值 / GCU 兜底）；
//   ② GPU 占用率的**实际来源 + engine**（NVML nvmlDeviceGetUtilizationRates().gpu = graphics/core 口径；
//      LHM GPU Load；GCU 兜底）以及 NVML 同快照的 memory 控制器利用率（⚠️ 仅诊断参考，⛔ 不当核心占用）；
//   ③ 新鲜度（数据年龄 / 上次真实采样间隔 / 是否过期）。
//
// ⚠️ 全程**只读**：⛔ 不写硬件/配置/注册表；⛔ 不触发新的 LHM 采样；⭐ NVML 部分只读上次快照（⛔ 不加原生调用）。
// ⚠️ 每项独立 try/catch ⇒ ⛔ 单点失败不影响诊断包其余部分 ✅。
// ⚠️ 行尾：纯 LF（与 DiagnosticCpuTempInfo / DiagnosticBaolongdaInfo 一致）✅。

using System;
using System.Text;
using MechrevoLite.Hardware;

namespace MechrevoLite.Diagnostics;

/// <summary>⭐ 诊断包「占用率来源明细」一节 —— ⭐ **只读** ✅。</summary>
internal static class DiagnosticUsageInfo
{
	internal const string SectionTitle = "[占用率来源明细（只读）]";

	internal static string Build()
	{
		StringBuilder sb = new StringBuilder();
		try
		{
			sb.AppendLine(SectionTitle);
			sb.AppendLine("  ⭐ 用途：CPU/GPU 占用率显示不对时，用本节定位**取源/换算/刷新**问题（⭐ 只读，不写任何硬件）");
			sb.AppendLine("  采集时间: " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
			AppendCpu(sb);
			AppendGpu(sb);
			AppendConclusion(sb);
		}
		catch (Exception ex)
		{
			sb.AppendLine("⚠️ 占用率来源明细采集异常（已吞掉，不影响诊断包其余部分）：" + ex.GetType().Name + " " + ex.Message);
		}
		sb.AppendLine();
		return sb.ToString();
	}

	/// <summary>⭐ CPU 占用：⭐ 实际来源 + 值 + 新鲜度 + 采样间隔 + 兜底状态 ✅。</summary>
	private static void AppendCpu(StringBuilder sb)
	{
		sb.AppendLine("  ── CPU 占用 ──");
		try
		{
			bool fresh = HardwareControl.CpuUsageIsFresh(HardwareControl.UsageFreshAge);
			sb.AppendLine("    当前值: " + DescribeUsage(HardwareControl.cpuUsage, fresh)
				+ " · 新鲜(≤" + HardwareControl.UsageFreshAge.TotalSeconds.ToString("F0") + "s)=" + (fresh ? "是" : "否")
				+ " · 数据年龄=" + DescribeAge(HardwareControl.CpuUsageAgeMs)
				+ " · 上次真实采样间隔=" + DescribeInterval(HardwareControl.CpuUsageIntervalMs));
			sb.AppendLine("    实际来源: " + DescribeSource(HardwareControl.CpuUsageSource));
			sb.AppendLine("    性能计数器兜底: GetSystemTimes 差值 —— 当前"
				+ (HardwareControl.CpuUsageUsingSystemTimesFallback ? "**已启用**（⭐ % Processor Utility/% Processor Time 不可用）" : "未启用（⭐ 性能计数器可用）"));
			sb.AppendLine("    GCU 推送兜底: 仅 GCU 机型、且本地采样失败时才用（⛔ 宝龙达机型没有 GCU 推送）");
		}
		catch (Exception ex)
		{
			sb.AppendLine("    ⚠️ 读取 CPU 占用状态失败（已跳过）：" + ex.Message);
		}
	}

	/// <summary>⭐ GPU 占用：⭐ 实际来源 + engine + 值 + 新鲜度 + NVML 原始细节（⭐ memory 利用率仅参考）✅。</summary>
	private static void AppendGpu(StringBuilder sb)
	{
		sb.AppendLine("  ── GPU 占用 ──");
		try
		{
			bool fresh = HardwareControl.GpuUsageIsFresh(HardwareControl.UsageFreshAge);
			sb.AppendLine("    当前值: " + DescribeUsage(HardwareControl.gpuUsage, fresh)
				+ " · 新鲜(≤" + HardwareControl.UsageFreshAge.TotalSeconds.ToString("F0") + "s)=" + (fresh ? "是" : "否")
				+ " · 数据年龄=" + DescribeAge(HardwareControl.GpuUsageAgeMs)
				+ " · 上次真实采样间隔=" + DescribeInterval(HardwareControl.GpuUsageIntervalMs));
			sb.AppendLine("    实际来源: " + DescribeSource(HardwareControl.GpuUsageSource));
			sb.AppendLine("    engine / 传感器: " + DescribeSource(HardwareControl.GpuUsageEngine)
				+ "（⭐ NVML 的 gpu 字段 = **核心/graphics** 利用率，⛔ 不是显存占用）");
			AppendNvmlDetail(sb);
			AppendLhmDetail(sb);
		}
		catch (Exception ex)
		{
			sb.AppendLine("    ⚠️ 读取 GPU 占用状态失败（已跳过）：" + ex.Message);
		}
	}

	/// <summary>⭐ NVML 原始细节：⭐ 同快照 memory 控制器利用率（⚠️ 仅参考）+ 句柄/返回码/TTL ✅。</summary>
	private static void AppendNvmlDetail(StringBuilder sb)
	{
		try
		{
			int? memoryUtil = NvmlHelper.GetGpuMemoryUtilizationForDiagnostics();
			int? coreUtil = NvmlHelper.GetGpuUtilization();
			sb.AppendLine("    NVML 细节: 核心利用率=" + (coreUtil.HasValue ? coreUtil.Value + "%" : "无")
				+ " · 显存**控制器**利用率=" + (memoryUtil.HasValue ? memoryUtil.Value + "%" : "无")
				+ "（⚠️ 仅诊断参考：这是「显存控制器忙不忙」，⛔ **不是**显存已用 %）");
			sb.AppendLine("      · 上次占用查询返回码=" + NvmlHelper.LastUtilizationError
				+ "（0 = 成功 · -1 = 还没查过）· 设备句柄存活=" + (NvmlHelper.DeviceHandleAlive ? "是" : "否")
				+ " · 快照年龄=" + DescribeAge(NvmlHelper.SnapshotAgeMs)
				+ " · 快照 TTL=" + NvmlHelper.SnapshotTtlMs + "ms"
				+ " · NVIDIA 遥测=" + (NvmlHelper.NotApplicable
					? "不适用（本机无 NVIDIA / nvidia-smi，正常停用）"
					: (NvmlHelper.Unavailable ? "已隔离（降级停用）" : "可用")));
		}
		catch (Exception ex)
		{
			sb.AppendLine("    ⚠️ 读取 NVML 细节失败（已跳过）：" + ex.Message);
		}
	}

	/// <summary>⭐ LHM GPU 负载候选（⭐ 只在非 NVIDIA 机型有意义：NVIDIA 机型 LHM 的 GPU 那支被关）✅。</summary>
	private static void AppendLhmDetail(StringBuilder sb)
	{
		try
		{
			LhmMonitor monitor = HardwareControl.lhm;
			if (monitor == null || monitor.IsDisposed)
			{
				sb.AppendLine("    LHM GPU 负载候选: ⚠️ LHM 未创建/已释放");
				return;
			}
			sb.AppendLine("    LHM GPU 负载候选: "
				+ (monitor.GpuLoadPercent.HasValue
					? (monitor.GpuLoadSensorName + "=" + monitor.GpuLoadPercent.Value + "%")
					: "无（⛔ 正常：NVIDIA 机型的 LHM GPU 分支在启动时就被关闭，GPU 占用由 NVML 提供）"));
		}
		catch (Exception ex)
		{
			sb.AppendLine("    ⚠️ 读取 LHM GPU 负载失败（已跳过）：" + ex.Message);
		}
	}

	/// <summary>⭐ 口径 / 基准 / 取不到时显示「—」的约定 ✅。</summary>
	private static void AppendConclusion(StringBuilder sb)
	{
		sb.AppendLine("  ── 口径 / 对照基准 ──");
		sb.AppendLine("    · CPU: ⭐ 默认 **% Processor Utility**（Processor Information\\_Total）—— ⭐ 与 **Windows 任务管理器**同口径（含睿频折算）；"
			+ "⭐ 不可用时：% Processor Time → GetSystemTimes 差值（时间口径，与任务管理器略有差异）");
		sb.AppendLine("    · GPU: ⭐ **NVML nvmlDeviceGetUtilizationRates().gpu**（0–100，核心/graphics 口径）—— "
			+ "对照 **任务管理器「GPU 3D」** / **GPU-Z「GPU Load」**；⚠️ 与「显存占用」（used/total）不是一回事");
		sb.AppendLine("    · ⚠️ 取不到值 ⇒ 主界面/悬浮窗显示「—」，⛔ **不显示 0% 冒充**（⭐ 0% 只在「确实空闲」时才会出现 ✅）");
	}

	/// <summary>⭐ 占用率值 → 人读文本（⭐ 不新鲜 ⇒ 「—」✅）。</summary>
	private static string DescribeUsage(int? value, bool fresh)
	{
		if (!fresh || !value.HasValue)
		{
			return "—（没有新鲜值）";
		}
		return value.Value + "%";
	}

	/// <summary>⭐ 来源 → 人读文本（⭐ 空 = 还没采到 ✅）。</summary>
	private static string DescribeSource(string source)
	{
		return string.IsNullOrEmpty(source) ? "（还没有来源）" : source;
	}

	/// <summary>⭐ 毫秒 → 「距离现在多久」文本（⭐ -1 = 未知）✅。</summary>
	private static string DescribeAge(long ageMs)
	{
		return (ageMs < 0L) ? "未知" : ((ageMs / 1000.0).ToString("F1") + "s");
	}

	/// <summary>⭐ 采样间隔 → 人读文本 ✅。</summary>
	private static string DescribeInterval(int intervalMs)
	{
		return (intervalMs <= 0) ? "未知" : (intervalMs + "ms");
	}
}
