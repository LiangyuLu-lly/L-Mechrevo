// ⭐⭐ 2026-10-18（机主：30 系 MRID6 / 6800H「CPU 温度显示卡在 80℃ 不动，调节电后真实温度会降但显示不跟」）：
//   ⭐ 新增诊断节「CPU 温度来源明细」—— ⭐ 只读 ✅
//
// ⭐ 目的：⭐ 让下一份诊断包**一眼定位**"到底是谁没刷新"：
//   ① 厂商功能号 22 到底读到什么（30 系 = 可读但值 0 ⇒ 已知不可用）；
//   ② LHM 的**全部** CPU 温度传感器（Tctl/Tdie · CPU Package · Core #N …）**各自的值 + 上次变化时刻**；
//   ③ ACPI 热区在同一采集时刻的值（⭐ 交叉校验：ACPI 变了而 LHM 没变 ⇒ LHM 没刷新）；
//   ④ 我们**实际选中的来源**是什么、是否已判定「LHM 未刷新 ⇒ 改以 ACPI 主源」。
//
// ⚠️ 全程**只读**：⛔ 不写硬件 / 配置 / 注册表；⛔ 不触发新的 LHM 硬件更新（⭐ 只读内存快照）；
//    ⭐ 功能号 22 与 ACPI 热区都只发 **Get（读）** ✅。
// ⚠️ 每项独立 try/catch ⇒ ⛔ 单点失败只写一行原因，绝不影响诊断包其余部分 ✅。
// ⚠️ 行尾：本文件为纯 LF（与 DiagnosticBaolongdaInfo / DiagnosticConfigInfo 等一致）✅。

using System;
using System.Collections.Generic;
using System.Text;
using MechrevoLite.Hardware;

namespace MechrevoLite.Diagnostics;

/// <summary>
/// ⭐ 诊断包「CPU 温度来源明细」一节 —— ⭐ **只读** ✅。
/// <para>⭐ 采集项：⭐ 厂商功能号 22 · ⭐ LHM 全部 CPU 温度传感器 · ⭐ ACPI 热区 · ⭐ 当前实际取源 ✅。</para>
/// </summary>
internal static class DiagnosticCpuTempInfo
{
	internal const string SectionTitle = "[CPU 温度来源明细（只读）]";

	internal static string Build()
	{
		StringBuilder sb = new StringBuilder();
		try
		{
			sb.AppendLine(SectionTitle);
			sb.AppendLine("  ⭐ 用途：CPU 温度「显示不跟随真实温度」时，用本节定位**是哪个来源没刷新**（⭐ 只读，不写任何硬件）");
			sb.AppendLine("  采集时间: " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
			AppendSummary(sb);
			AppendFunc22(sb);
			AppendLhm(sb);
			AppendAcpi(sb);
			AppendConclusion(sb);
		}
		catch (Exception ex)
		{
			sb.AppendLine("⚠️ CPU 温度来源明细采集异常（已吞掉，不影响诊断包其余部分）：" + ex.GetType().Name + " " + ex.Message);
		}
		sb.AppendLine();
		return sb.ToString();
	}

	/// <summary>⭐ 当前实际显示值 / 来源 / 新鲜度（⭐ 与主界面、悬浮窗同一套时间戳 ✅）。</summary>
	private static void AppendSummary(StringBuilder sb)
	{
		sb.AppendLine("  ── 当前实际取用 ──");
		try
		{
			string source = HardwareControl.CpuTempSourceName;
			sb.AppendLine("    当前来源: " + (string.IsNullOrEmpty(source) ? "（还没成功取到过）" : source));
			sb.AppendLine("    当前值: " + HardwareControl.cpuTemp + "℃");
			long ageMs = HardwareControl.CpuTempAgeMs;
			bool fresh = HardwareControl.CpuTempIsFresh(HardwareControl.CpuTempFreshAge);
			sb.AppendLine("    数据年龄: " + (ageMs < 0L ? "从未更新" : (ageMs / 1000.0).ToString("F1") + "s")
				+ " · 新鲜(≤" + HardwareControl.CpuTempFreshAge.TotalSeconds.ToString("F0") + "s)=" + (fresh ? "是" : "否"));
			sb.AppendLine("    LHM 未刷新判定: " + (HardwareControl.CpuTempLhmSuspect
				? "**是**（⭐ 已按判据改以 ACPI 热区为主源、LHM 为次源 ✅）"
				: "否（⭐ 仍以 LHM/厂商 为主源）"));
			sb.AppendLine("    上次厂商功能号 22 原始值: " + DescribeFunc22Raw(HardwareControl.CpuTempLastFunc22Celsius));
		}
		catch (Exception ex)
		{
			sb.AppendLine("    ⚠️ 读取当前取用状态失败（已跳过）：" + ex.Message);
		}
	}

	/// <summary>⭐ 厂商功能号 22：⭐ 现读一次（只读 Get）+ 最近一次采样记录 ✅。</summary>
	private static void AppendFunc22(StringBuilder sb)
	{
		sb.AppendLine("  ── 厂商功能号 22（宝龙达 WMI · 只读 Get）──");
		try
		{
			MechrevoHw hw = Program.hw;
			if (hw == null)
			{
				sb.AppendLine("    ⚠️ Program.hw 尚未创建 ⇒ 本次无法读功能号 22");
				return;
			}
			if (!hw.UsesBaolongda)
			{
				sb.AppendLine("    不适用：本机不是宝龙达后端（⭐ 非宝龙达机型的 CPU 温度走 GCU 推送/LHM ✅）");
				return;
			}
			if (BaolongdaWmi.VendorTemperatureUnsupported)
			{
				sb.AppendLine("    状态: ⚠️ **30 系已知不可用**（VendorTemperatureUnsupported=true ⇒ 代码整条跳过功能号 22，⛔ 不读）");
			}
			else
			{
				BaolongdaWmi wmi = hw.BaolongdaWmi;
				if (wmi == null)
				{
					sb.AppendLine("    状态: 宝龙达 WMI 对象为 null ⇒ 读不到");
				}
				else if (wmi.TryReadCpuTemperature(out int celsius))
				{
					sb.AppendLine("    本次现读: ✅ 可读 · 值=" + celsius + "℃（读取时间 " + DateTime.Now.ToString("HH:mm:ss") + "）");
				}
				else
				{
					sb.AppendLine("    本次现读: ⚠️ 读不到 / 返回无效值（0 或 255 一律判无效）");
				}
			}
			sb.AppendLine("    最近一次采样记录: " + DescribeFunc22Raw(HardwareControl.CpuTempLastFunc22Celsius));
		}
		catch (Exception ex)
		{
			sb.AppendLine("    ⚠️ 功能号 22 读取失败（已跳过）：" + ex.Message);
		}
	}

	/// <summary>⭐ LHM：⭐ 全部 CPU 温度传感器（⭐ 名称 / 值 / 档位 / 上次变化时刻）+ 刷新状态 ✅。</summary>
	private static void AppendLhm(StringBuilder sb)
	{
		sb.AppendLine("  ── LHM（LibreHardwareMonitor · 只读内存快照）──");
		try
		{
			LhmMonitor monitor = HardwareControl.lhm;
			if (monitor == null)
			{
				sb.AppendLine("    ⚠️ LHM 监控对象尚未创建（⭐ 本地监控未启用）");
				return;
			}
			if (monitor.IsDisposed)
			{
				sb.AppendLine("    ⚠️ LHM 监控对象已释放");
				return;
			}
			sb.AppendLine("    最近一次 Update: " + DescribeAge(monitor.LastUpdateAgeMs)
				+ "（⭐ >2s ⇒ 可能没在刷新 ⚠️）· 被 1s 限频跳过次数=" + monitor.UpdateSkips);
			sb.AppendLine("    CPU 硬件对象数: " + monitor.CpuHardwareCount
				+ "（⭐ 0 ⇒ 温度必然读不到）");
			string selectedName = monitor.CpuTemperatureSensorName;
			if (monitor.CpuTemperature.HasValue)
			{
				sb.AppendLine("    当前选中: sensor=" + selectedName
					+ " · 值=" + monitor.CpuTemperature.Value.ToString("F1") + "℃"
					+ " · 档位=" + DescribeTier(monitor.CpuTemperatureSelectedTier)
					+ " · 值已 " + DescribeAge(monitor.CpuTemperatureUnchangedMs) + " 未变");
			}
			else
			{
				sb.AppendLine("    当前选中: ⚠️ 本轮没有有效 CPU 温度值");
			}
			IReadOnlyList<LhmMonitor.CpuTemperatureSensorReading> sensors = monitor.CpuTemperatureSensors;
			sb.AppendLine("    CPU 温度传感器（共 " + sensors.Count + " 个）:");
			if (sensors.Count == 0)
			{
				sb.AppendLine("      ⚠️ LHM 没有暴露任何 CPU 温度传感器（⭐ 或本轮 Update 失败）");
			}
			foreach (LhmMonitor.CpuTemperatureSensorReading sensor in sensors)
			{
				long unchangedMs = sensor.UnchangedMs;
				sb.AppendLine("      - " + sensor.Name
					+ " · 值=" + (sensor.Value.HasValue ? sensor.Value.Value.ToString("F1") + "℃" : "无")
					+ " · 档位=" + DescribeTier(sensor.Tier)
					+ " · 上次变化=" + ClockOf(sensor.LastChangeTicks)
					+ "（" + (unchangedMs < 0L ? "未知" : (unchangedMs / 1000.0).ToString("F1") + "s 前") + "）");
			}
			sb.AppendLine("    ⭐ 应选源提示: " + DescribeShouldUse(monitor.CpuTemperatureSelectedTier)
				+ "（⭐ 选取顺序 Tctl/Tdie ＞ CPU Package ＞ ACPI 热区 ＞ 其余；⛔ 不做任何数值偏移/补偿）");
		}
		catch (Exception ex)
		{
			sb.AppendLine("    ⚠️ 读取 LHM 快照失败（已跳过）：" + ex.Message);
		}
	}

	/// <summary>⭐ ACPI 热区：⭐ 强制现读一次（只读 WMI）+ 与当前显示值的差值 ✅。</summary>
	private static void AppendAcpi(StringBuilder sb)
	{
		sb.AppendLine("  ── ACPI 热区（root\\WMI:MSAcpi_ThermalZoneTemperature · 只读）──");
		try
		{
			int acpi = HardwareControl.ReadAcpiThermalZoneCelsiusForDiagnostics();
			if (acpi > 0)
			{
				sb.AppendLine("    本次现读: " + acpi + "℃（读取时间 " + DateTime.Now.ToString("HH:mm:ss")
					+ " · 口径：多个热区取最高值）");
				if (HardwareControl.cpuTemp > 0)
				{
					sb.AppendLine("    与当前显示的差值: " + (HardwareControl.cpuTemp - acpi) + "℃（当前显示 "
						+ HardwareControl.cpuTemp + "℃）");
				}
			}
			else
			{
				sb.AppendLine("    本次现读: ⚠️ 读不到（本机可能没有 ACPI 热区 / 被拒绝访问）");
			}
			sb.AppendLine("    ⭐ 说明: ACPI 是**封装/主板热区**口径，与 Tctl 通常有偏差；⭐ 它的用途是**交叉校验谁在刷新**"
				+ "（⭐ ACPI 变了而 LHM 没变 ⇒ LHM 没刷新 ⚠️），⛔ 不是「官方控制台显示口径」");
		}
		catch (Exception ex)
		{
			sb.AppendLine("    ⚠️ ACPI 热区读取失败（已跳过）：" + ex.Message);
		}
	}

	/// <summary>⭐ 结论：⭐ 官方口径是哪个、本机实际取源是否对齐、怎么判「没刷新」 ✅。</summary>
	private static void AppendConclusion(StringBuilder sb)
	{
		sb.AppendLine("  ── 结论 / 排查口径 ──");
		sb.AppendLine("    · 官方控制台口径: ⭐ AMD = **Tctl/Tdie**（真实结温）· ⭐ Intel = **CPU Package**（封装温度）");
		sb.AppendLine("    · 本机应选源: 见上方「应选源提示」——⭐ 有 Tctl/Tdie 时必须选它（= 官方口径）");
		sb.AppendLine("    · 判定「LHM 没刷新」的证据链: ① LHM 值连续 ≥3 分钟完全不变；"
			+ "② 同一窗口内 ACPI 热区变化 ≥2℃（⭐ 证明环境/负载确实在变）⇒ ⭐ 才改以 ACPI 为主源，并记默认级别可见的日志");
		sb.AppendLine("    · 复现方式: 调 Windows「节电 / 最佳能效」→ 等 10~20s → 看主界面/悬浮窗 CPU 温度是否下降；"
			+ "日志应出现值变化行 `[cpu-temp] 来源=… · 值=…℃ · ACPI=…℃ · …` 与 `[cpu-temp] LHM 原始：…`");
	}

	/// <summary>⭐ 毫秒 → 「距离现在多久」文本（⭐ -1 = 未知）✅。</summary>
	private static string DescribeAge(long ageMs)
	{
		return (ageMs < 0L) ? "从未" : ((ageMs / 1000.0).ToString("F1") + "s");
	}

	/// <summary>⭐ TickCount64 → 本地时钟 HH:mm:ss（⭐ 绝对时刻 ⇒ 诊断里能直接和日志行对照 ✅）。</summary>
	private static string ClockOf(long tick)
	{
		if (tick <= 0L)
		{
			return "未知";
		}
		return DateTime.Now.AddMilliseconds(-(Environment.TickCount64 - tick)).ToString("HH:mm:ss");
	}

	/// <summary>⭐ 功能号 22 原始值 → 人读文本（⭐ -1=没试/不适用 · 0=可读但无效 · &gt;0=有效）✅。</summary>
	private static string DescribeFunc22Raw(int raw)
	{
		if (raw < 0)
		{
			return "（还没试过 / 本机不适用）";
		}
		if (raw == 0)
		{
			return "0（⭐ 可读但值无效 ⇒ 30 系已知不可用）";
		}
		return raw + "℃";
	}

	/// <summary>⭐ LHM CPU 温度档位 → 人读文本（⭐ 标出"官方口径"档位）✅。</summary>
	private static string DescribeTier(LhmMonitor.CpuTemperatureTier tier)
	{
		switch (tier)
		{
		case LhmMonitor.CpuTemperatureTier.TctlTdie:
			return "Tctl/Tdie（⭐⭐ AMD 官方口径 / 真实结温）";
		case LhmMonitor.CpuTemperatureTier.Package:
			return "Package（⭐ Intel 官方口径 / 封装温度）";
		case LhmMonitor.CpuTemperatureTier.Other:
			return "其余（⚠️ Core #N 等单核口径，通常偏低）";
		case LhmMonitor.CpuTemperatureTier.Excluded:
			return "不参与选源（⚠️ 语义相反/无关）";
		default:
			return "无";
		}
	}

	/// <summary>⭐ 本机「应选源」提示（⭐ 由 LHM 实际暴露的最高档位 + 已选档位共同决定）✅。</summary>
	private static string DescribeShouldUse(LhmMonitor.CpuTemperatureTier selectedTier)
	{
		switch (selectedTier)
		{
		case LhmMonitor.CpuTemperatureTier.TctlTdie:
			return "LHM Tctl/Tdie ✅ 与 AMD 官方控制台口径一致";
		case LhmMonitor.CpuTemperatureTier.Package:
			return "LHM CPU Package ⚠️ 本机 LHM 没给 Tctl/Tdie ⇒ 用 Intel 口径的封装温度";
		case LhmMonitor.CpuTemperatureTier.Other:
			return "LHM 其余传感器 ⚠️ 没有 Tctl/Tdie / Package ⇒ 只能兜底，与官方口径可能不同";
		default:
			return "（本轮没有有效传感器）";
		}
	}
}
