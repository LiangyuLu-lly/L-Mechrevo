using System;
using System.Threading.Tasks;
using MechrevoLite.Hardware;
using MechrevoLite.Mode;

namespace MechrevoLite;

/// <summary>
/// 2026-10-22（机主：无界开机且在自定义模式上再启用风扇曲线）：
/// 开机后若当前是**自定义档**，按该档已保存的点位自动启动 Wujie 25Hz EC 控制器。
/// <para>触发条件：无界 APU + 当前 Active 是自定义档 + 该档有 CpuFanDuty/GpuFanDuty + FanCurveOn 未显式关闭。</para>
/// <para>温度轴用 <see cref="WujieFanEcController.DefaultTemperatures"/>（保存的只有占空比点位）。</para>
/// </summary>
internal static class WujieFanCurveAutoApply
{
	private static int _scheduled;

	internal static void Schedule()
	{
		if (Program.hw?.WujieSmuApu != true)
		{
			return;
		}
		if (System.Threading.Interlocked.Exchange(ref _scheduled, 1) != 0)
		{
			return;
		}
		_ = Task.Run(async delegate
		{
			try
			{
				// 等模式引擎/档位回读稳定（开机 SetAutoModes 之后还会重放一次自定义档）
				await Task.Delay(TimeSpan.FromSeconds(4)).ConfigureAwait(continueOnCapturedContext: false);
				ApplyIfCustomMode();
			}
			catch (Exception ex)
			{
				Logger.WriteLine("[fan] 无界开机自动曲线失败（已吞掉）：" + ex.GetType().Name + " " + ex.Message);
			}
		});
	}

	/// <summary>解析保存的温度轴（逗号分隔，最多 16 点，值 0..254）；无效返回 null。</summary>
	private static byte[] ParseTemps(string text)
	{
		if (string.IsNullOrWhiteSpace(text))
		{
			return null;
		}
		string[] parts = text.Split(',');
		if (parts.Length < 2)
		{
			return null;
		}
		byte[] temps = new byte[Math.Min(16, parts.Length)];
		for (int i = 0; i < temps.Length; i++)
		{
			if (!byte.TryParse(parts[i].Trim(), out temps[i]) || temps[i] > 120)
			{
				return null;
			}
		}
		return temps;
	}

	private static void ApplyIfCustomMode()
	{
		PerfModeService perf = PerfModeService.Instance;
		PerfModeDefinition active = perf?.Active;
		if (active == null)
		{
			Logger.WriteLine("[fan] 无界开机自动曲线：当前没有活动档位，跳过");
			return;
		}
		if (!active.IsCustom)
		{
			Logger.WriteLine("[fan] 无界开机自动曲线：当前档位=" + active.Id + "（非自定义），跳过");
			return;
		}
		PerfModeSettings settings = PerfModeStore.LoadSettings(active.Id);
		if (settings.FanCurveOn == false)
		{
			Logger.WriteLine("[fan] 无界开机自动曲线：档位 " + active.Id + " 的曲线下发开关=关，跳过");
			return;
		}
		if (settings.CpuFanDuty == null || settings.CpuFanDuty.Length == 0)
		{
			Logger.WriteLine("[fan] 无界开机自动曲线：档位 " + active.Id + " 没有保存过 CPU 曲线点位，跳过");
			return;
		}
		int[] gpuDuty = settings.GpuFanDuty ?? settings.CpuFanDuty;
		bool independent = AppConfig.Is("wujie_fan_independent");
		string key = string.IsNullOrWhiteSpace(active.Id) ? "active" : active.Id;
		byte[] cpuTemps = ParseTemps(AppConfig.GetString("wujie_fan_cputemps_" + key)) ?? WujieFanEcController.DefaultTemperatures();
		byte[] gpuTemps = ParseTemps(AppConfig.GetString("wujie_fan_gputemps_" + key)) ?? cpuTemps;
		WujieFanEcController controller = new WujieFanEcController();
		bool ok = controller.Start(cpuTemps, settings.CpuFanDuty, independent, gpuTemps, gpuDuty, out string detail);
		Logger.WriteLine("[fan] 无界开机自动曲线（档位=" + active.Id + " · 独立=" + independent + "）⇒ "
			+ (ok ? "已启用 · " : "未启用 · ") + detail);
	}
}
