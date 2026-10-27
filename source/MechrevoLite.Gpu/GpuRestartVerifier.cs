using System;
using System.Globalization;
using System.Threading.Tasks;
using MechrevoLite.Helpers;
using MechrevoLite.Properties;

namespace MechrevoLite.Gpu;

internal static class GpuRestartVerifier
{
	internal const string PendingKey = "gpu_restart_pending";

	internal const string TickKey = "gpu_restart_tick";

	internal const string TargetKey = "gpu_restart_target";

	/// <summary>
	/// 2026-10-05：当 NVRAM 的待生效显示路由**等于**当前实际路由时，说明没有真正待生效的变更
	/// （典型场景：切过去又切回来、或已生效但配置里的 pending 标记尚未清），
	/// 此时不应再显示「重启以应用」与棕色高亮。
	/// 与 PendingTarget 里「不同则以 NVRAM 为准」互为补充。
	/// </summary>
	internal static bool PendingAlreadySatisfied()
	{
		if (!AppConfig.Is("gpu_restart_pending"))
		{
			return false;
		}
		if (!UefiDisplayModeDiagnostics.TryGetPendingRoute(Program.hw?.IsAmdPlatformForLimits ?? false, out GpuRoute? pending) || !pending.HasValue)
		{
			return false;
		}
		GpuRoute actual = GpuRouteMonitor.Snapshot().Route;
		return actual != GpuRoute.Unknown && pending.Value == actual;
	}

	internal static int PendingTarget
	{
		get
		{
			if (!AppConfig.Is("gpu_restart_pending"))
			{
				return -1;
			}
			// 2026-10-05：NVRAM 待生效路由 == 实际路由 -> 没有真正待生效的变更
			if (PendingAlreadySatisfied())
			{
				return -1;
			}
			// 2026-10-05：外部命令（不经 LM）也会写 NVRAM 的待生效显示路由。
			// 若 NVRAM 的待生效路由与当前实际路由不同，则以 NVRAM 为准 ——
			// 否则界面会把「重启以应用」的目标显示错（实测 target 残留 2=独显，
			// 而 NVRAM 实际待生效是 2(IgpuOnly)=核显模式）。
			if (UefiDisplayModeDiagnostics.TryGetPendingRoute(Program.hw?.IsAmdPlatformForLimits ?? false, out GpuRoute? pending)
				&& pending.HasValue)
			{
				GpuRoute actual = GpuRouteMonitor.Snapshot().Route;
				if (actual != GpuRoute.Unknown && pending.Value != actual)
				{
					int fromNvram = GpuRouteInference.ToGpuMode(pending.Value);
					if (fromNvram >= 0)
					{
						return fromNvram;
					}
				}
			}
			return AppConfig.Get("gpu_restart_target");
		}
	}

	internal static Action<GpuRestartVerdict, string>? NotifyOverride { get; set; }

	internal static TimeSpan ReadbackRetryDelay { get; set; } = TimeSpan.FromSeconds(5L);

	internal static void MarkPending(int targetMode)
	{
		AppConfig.Set("gpu_restart_pending", 1);
		AppConfig.Set("gpu_restart_tick", Environment.TickCount64.ToString(CultureInfo.InvariantCulture));
		AppConfig.Set("gpu_restart_target", targetMode);
	}

	internal static void Clear()
	{
		AppConfig.Set("gpu_restart_pending", 0);
		AppConfig.Remove("gpu_restart_tick");
		AppConfig.Remove("gpu_restart_target");
	}

	internal static bool RebootHappened(long markedTick, long nowTick)
	{
		return nowTick + 10000 < markedTick;
	}

	internal static bool HasPendingThisBoot()
	{
		if (!AppConfig.Is("gpu_restart_pending"))
		{
			return false;
		}
		// 2026-10-05：NVRAM 待生效路由 == 实际路由 -> 视为没有待生效（修掉"切过去又切回来"的残留）
		if (PendingAlreadySatisfied())
		{
			return false;
		}
		if (long.TryParse(AppConfig.GetString("gpu_restart_tick"), out var result))
		{
			return !RebootHappened(result, Environment.TickCount64);
		}
		return true;
	}

	internal static GpuRestartVerdict Evaluate(int targetMode, GpuRoute actual)
	{
		if ((targetMode < 0 || targetMode > 2) ? true : false)
		{
			return GpuRestartVerdict.Unconfirmed;
		}
		if (actual == GpuRoute.Unknown)
		{
			return GpuRestartVerdict.Unconfirmed;
		}
		if (GpuRouteInference.ToGpuMode(actual) != targetMode)
		{
			return GpuRestartVerdict.NotApplied;
		}
		return GpuRestartVerdict.Applied;
	}

	internal static bool TryTakeRebootTarget(out int targetMode)
	{
		targetMode = -1;
		if (!AppConfig.Is("gpu_restart_pending"))
		{
			return false;
		}
		if (!long.TryParse(AppConfig.GetString("gpu_restart_tick"), out var result) || !RebootHappened(result, Environment.TickCount64))
		{
			return false;
		}
		targetMode = AppConfig.Get("gpu_restart_target");
		Clear();
		return true;
	}

	internal static async Task<GpuRestartVerdict> RunAfterBootAsync(TimeSpan delay)
	{
		_ = 1;
		try
		{
			if (delay > TimeSpan.Zero)
			{
				await Task.Delay(delay).ConfigureAwait(continueOnCapturedContext: false);
			}
			if (!TryTakeRebootTarget(out var target))
			{
				GpuRouteReadback value = GpuRouteMonitor.Refresh();
				Logger.WriteInfo($"GPU route at startup: {value}; {UefiDisplayModeDiagnostics.Describe(Program.hw?.IsAmdPlatformForLimits ?? false)}");
				return GpuRestartVerdict.NotPending;
			}
			GpuRouteReadback readback = GpuRouteReadback.Unavailable;
			for (int attempt = 0; attempt < 3; attempt++)
			{
				readback = GpuRouteMonitor.Refresh();
				if (readback.Route != GpuRoute.Unknown || attempt == 2)
				{
					break;
				}
				await Task.Delay(ReadbackRetryDelay).ConfigureAwait(continueOnCapturedContext: false);
			}
			GpuRestartVerdict gpuRestartVerdict = Evaluate(target, readback.Route);
			string logMessage = $"GPU route verify: target={target} actual={readback.Route} ({readback}) verdict={gpuRestartVerdict}; {UefiDisplayModeDiagnostics.Describe(Program.hw?.IsAmdPlatformForLimits ?? false)}";
			if (gpuRestartVerdict == GpuRestartVerdict.NotApplied)
			{
				Logger.WriteError(logMessage);
			}
			else
			{
				Logger.WriteInfo(logMessage);
			}
			// 2026-10-06 D-1「试过就记住」：如果这次待重启是用户点「核显模式」发起的，
			// 就把**这次实际路由回读**的结论记进 config（成功=以后一直显示 / 失败=不再猜、隐藏）。
			// 只在有 igpu_attempt_pending 标记时生效（见 Gpu\IgpuOnlyAttempt.cs），本行不改变任何既有判定。
			IgpuOnlyAttempt.RecordBootVerdict(target, gpuRestartVerdict);
			string text = gpuRestartVerdict switch
			{
				GpuRestartVerdict.Applied => string.Format(Strings.GpuRouteApplied, ModeName(target)), 
				GpuRestartVerdict.NotApplied => string.Format(Strings.GpuRouteNotApplied, ModeName(GpuRouteInference.ToGpuMode(readback.Route))), 
				_ => "", 
			};
			if (text.Length > 0)
			{
				Action<GpuRestartVerdict, string> notifyOverride = NotifyOverride;
				if (notifyOverride != null)
				{
					notifyOverride(gpuRestartVerdict, text);
				}
				else if (gpuRestartVerdict == GpuRestartVerdict.Applied)
				{
					ToastForm.ShowNotice(text);
				}
				else
				{
					ToastForm.ShowFailure(text);
				}
			}
			return gpuRestartVerdict;
		}
		catch (Exception ex)
		{
			Logger.WriteLine("GPU route verify failed: " + ex.Message);
			return GpuRestartVerdict.Unconfirmed;
		}
	}

	internal static string ModeName(int mode)
	{
		return mode switch
		{
			0 => Strings.GpuRouteIgpu, 
			2 => Strings.GpuRouteDirect, 
			_ => Strings.GpuRouteStandard, 
		};
	}
}
