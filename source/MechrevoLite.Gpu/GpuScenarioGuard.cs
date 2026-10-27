using System;
using System.Collections.Generic;

namespace MechrevoLite.Gpu;

/// <summary>
/// 活动显示通路落在哪块显卡上。判据与只读参考 <c>tools\dgpu-silence\Invoke-GpuHotSwitch.ps1:143-157</c>
/// 的 <c>Get-DesktopDrivers</c> 完全一致：<c>Win32_VideoController</c> 里
/// <c>CurrentHorizontalResolution &gt; 0</c> 的才是"正在驱动桌面"的适配器，
/// 适配器是不是 NVIDIA 看 <c>Name</c> 或 <c>PNPDeviceID</c>（<c>VEN_10DE</c>）。
/// </summary>
internal enum DisplayRouteKind
{
	/// <summary>读不到显示通路（WMI 不可用 / 没有任何适配器报分辨率）。</summary>
	Unavailable,

	/// <summary>有适配器在驱动桌面，但**没有** NVIDIA 适配器 ⇒ 混合输出、核显在驱动。</summary>
	IgpuOnly,

	/// <summary>只有 NVIDIA 适配器在驱动桌面 ⇒ ⚠️ 黑屏风险，只读参考在这里**硬拒绝**。</summary>
	DgpuExclusive,

	/// <summary>NVIDIA 与其他适配器**同时**在驱动桌面。</summary>
	Hybrid
}

/// <summary>
/// 活动显示通路快照（纯数据）。
/// <para>⚠️ 这里的 <paramref name="HasActiveDisplay"/> 口径是**整个适配器**有没有在驱动桌面，
/// 不是"这块适配器上挂了哪几个显示器"—— 因此它天然覆盖用户脚本的同一条判据，
/// 而且⛔ 不依赖 <c>Screen.AllScreens</c>（诊断时内屏 <c>device=null</c>，
/// 走 WinForms 屏幕表会漏掉用 D3D 全屏独占的那条通路）。</para>
/// </summary>
internal readonly record struct DisplayAdapterSnapshot(string Name, string PnpDeviceId, int CurrentHorizontalResolution)
{
	internal bool HasActiveDisplay => CurrentHorizontalResolution > 0;

	internal bool IsNvidia
	{
		get
		{
			if (Name != null && Name.IndexOf("NVIDIA", StringComparison.OrdinalIgnoreCase) >= 0)
			{
				return true;
			}
			return PnpDeviceId != null && PnpDeviceId.IndexOf("VEN_10DE", StringComparison.OrdinalIgnoreCase) >= 0;
		}
	}
}

/// <summary>活动显示通路快照（纯数据，见 <see cref="DisplayAdapterSnapshot" />）。</summary>
internal readonly record struct DisplayRouteSnapshot(IReadOnlyList<DisplayAdapterSnapshot> Adapters)
{
	internal static DisplayRouteSnapshot Unavailable => new DisplayRouteSnapshot(null);
}

/// <summary>
/// ⭐ 阶段 2/3 的纯决策结果：**能不能做这次核显省电**、以及**要不要先清占用**。
/// <para>⛔ 本类型不含任何硬件调用：占用快照由调用方（<c>SettingsForm</c>）用
/// <c>NvidiaGpuControl.TryGetActiveDgpuApplications</c> 取，显示通路由
/// <c>WmiDisplayRouteProbe</c> 取；本类型只做判断题 ⇒ 可离线断言
/// （<c>tools\dev\igpu-selftest</c>）。</para>
/// </summary>
internal readonly record struct GpuScenarioVerdict(
	DisplayRouteKind RouteKind,
	bool SnapshotUnavailable,
	bool Occupied,
	bool DgpuOwnsOnlyActiveDisplay,
	bool ForceAllowed,
	bool NeedsForceConfirmation,
	string RefusalReason)
{
	internal bool Refused => RefusalReason.Length > 0;
}

/// <summary>
/// ⭐ 纯决策：**安全门 (b) 独显唯一驱动显示 ⇒ 默认拒绝强制** + 占用判定。
/// <para>只读参考的原文口径：<c>Invoke-GpuHotSwitch.ps1:305-312</c> ——
/// 「the desktop is currently driven by the NVIDIA adapter and by nothing else =>
/// switching it away could leave a black screen, so this is refused. Re-run with -Force only if
/// you have a second display or another way back」；<c>-Force</c> 对应的就是本函数的
/// <paramref name="userOverrideDgpuOwnsDisplay" />（⚠️ 只有用户**明确、已充分告知**后才允许为真）。</para>
/// </summary>
internal static class GpuScenarioGuard
{
	/// <summary>「在驱动桌面」的最低横向分辨率（与只读参考的 <c>$w -le 0</c> 等价，取 1 而非 0）。</summary>
	internal const int MinimumActiveWidth = 1;

	internal static GpuScenarioVerdict Evaluate(
		DisplayRouteSnapshot display,
		bool occupancySnapshotAvailable,
		bool occupied,
		bool forceConfirmed,
		bool userOverrideDgpuOwnsDisplay)
	{
		DisplayRouteKind routeKind = ClassifyRoute(display);
		bool snapshotUnavailable = !occupancySnapshotAvailable || routeKind == DisplayRouteKind.Unavailable;
		if (routeKind == DisplayRouteKind.Unavailable)
		{
			// ① 显示通路读不到 ⇒ 不猜。占用快照单独不可用由下面 ③ 处理。
			return new GpuScenarioVerdict(routeKind, SnapshotUnavailable: true, Occupied: occupied, DgpuOwnsOnlyActiveDisplay: false, ForceAllowed: false, NeedsForceConfirmation: false, "display-route-unavailable");
		}
		bool dgpuExclusive = routeKind == DisplayRouteKind.DgpuExclusive;
		// ② 安全门 (b)：独显唯一驱动显示 ⇒ 默认拒绝；只有明确的用户 override 才放行。
		if (dgpuExclusive && !userOverrideDgpuOwnsDisplay)
		{
			return new GpuScenarioVerdict(routeKind, snapshotUnavailable, occupied, DgpuOwnsOnlyActiveDisplay: true, ForceAllowed: false, NeedsForceConfirmation: false, "dgpu-owns-only-active-display");
		}
		// ③ 占用快照读不到 ⇒ 不能保证安全（对应 DgpuSnapshotUnavailable 文案）。
		if (!occupancySnapshotAvailable)
		{
			return new GpuScenarioVerdict(routeKind, SnapshotUnavailable: true, occupied, dgpuExclusive, ForceAllowed: false, NeedsForceConfirmation: false, "occupancy-snapshot-unavailable");
		}
		if (!occupied)
		{
			return new GpuScenarioVerdict(routeKind, SnapshotUnavailable: false, Occupied: false, dgpuExclusive, ForceAllowed: false, NeedsForceConfirmation: false, "");
		}
		// ④ 有占用 ⇒ 走强制路，但**必须**是用户主动的第二次动作（forceConfirmed）。
		return new GpuScenarioVerdict(routeKind, SnapshotUnavailable: false, Occupied: true, dgpuExclusive, ForceAllowed: forceConfirmed, NeedsForceConfirmation: !forceConfirmed, "");
	}

	internal static DisplayRouteKind ClassifyRoute(DisplayRouteSnapshot display)
	{
		IReadOnlyList<DisplayAdapterSnapshot> adapters = display.Adapters;
		if (adapters == null)
		{
			return DisplayRouteKind.Unavailable;
		}
		bool nvidia = false;
		bool other = false;
		for (int i = 0; i < adapters.Count; i++)
		{
			DisplayAdapterSnapshot adapter = adapters[i];
			if (!adapter.HasActiveDisplay || adapter.CurrentHorizontalResolution < MinimumActiveWidth)
			{
				continue;
			}
			if (adapter.IsNvidia)
			{
				nvidia = true;
			}
			else
			{
				other = true;
			}
		}
		if (nvidia && other)
		{
			return DisplayRouteKind.Hybrid;
		}
		if (nvidia)
		{
			return DisplayRouteKind.DgpuExclusive;
		}
		if (other)
		{
			return DisplayRouteKind.IgpuOnly;
		}
		return DisplayRouteKind.Unavailable;
	}
}

/// <summary>
/// ⭐ 纯决策：**强制路要结束哪几个进程**（安全门 (a)(d)）。
/// <para>两道过滤缺一不可：① <see cref="DgpuApplicationSafety.IsSafeCandidate" />（保护名单 /
/// 会话 / PID / 自己的进程树）；② <see cref="DgpuApplicationSafety.IsUnderSystemRoot" /> ——
/// **%SystemRoot% 下的进程一律不结束**；⚠️ 读不到映像路径时按"拒绝结束"处理（宁可漏杀不可错杀）。</para>
/// <para>⭐ 不变量：<see cref="GpuForceKillPlan.SafeToKill" /> 与 <see cref="GpuForceKillPlan.Skipped" />
/// 互斥且并集 = 输入集合；**没有用户确认（<paramref name="forceConfirmed" /> 为假）时
/// <see cref="GpuForceKillPlan.SafeToKill" /> 与 <see cref="GpuForceKillPlan.Skipped" /> 都为空**
/// ⇒ 上层绝不可能在"没弹过名单"的情况下拿到可结束名单（安全门 (d)：绝不静默结束进程）。</para>
/// </summary>
internal readonly record struct GpuForceKillPlan(
	IReadOnlyList<DgpuApplication> SafeToKill,
	IReadOnlyList<DgpuApplication> Skipped,
	string RefusedReason,
	bool Confirmed)
{
	internal bool Refused => RefusedReason is not null;
}

internal static class DgpuForceKillPlanBuilder
{
	internal const string ReasonProtectedProcess = "protected-process";
	internal const string ReasonNotCurrentSession = "not-current-session";
	internal const string ReasonAlreadyExited = "process-already-exited";
	internal const string ReasonIsLocalProcess = "local-process";
	internal const string ReasonImagePathUnavailable = "image-path-unavailable";
	internal const string ReasonUnderSystemRoot = "under-system-root";
	internal const string ReasonNotConfirmed = "force-not-confirmed";

	/// <param name="applications">占用快照（可能为 <c>null</c> = 读不到）。</param>
	/// <param name="currentProcessId">本进程 PID（LM 自己与其子进程永不动）。</param>
	/// <param name="currentSessionId">本会话（只动自己会话里的程序）。</param>
	/// <param name="forceConfirmed">用户是否已完成"主动的第二次动作"。</param>
	/// <param name="readImagePath">读进程映像路径；读不到返回 <c>null</c>。</param>
	internal static GpuForceKillPlan TryPlan(
		IReadOnlyList<DgpuApplication> applications,
		int currentProcessId,
		int currentSessionId,
		bool forceConfirmed,
		Func<DgpuApplication, string> readImagePath)
	{
		List<DgpuApplication> safe = new List<DgpuApplication>();
		List<DgpuApplication> skipped = new List<DgpuApplication>();
		if (!forceConfirmed)
		{
			return new GpuForceKillPlan(safe, skipped, ReasonNotConfirmed, Confirmed: false);
		}
		if (applications is null || applications.Count == 0)
		{
			return new GpuForceKillPlan(safe, skipped, null, Confirmed: true);
		}
		for (int i = 0; i < applications.Count; i++)
		{
			DgpuApplication application = applications[i];
			if (application.ProcessId == currentProcessId)
			{
				skipped.Add(application);
				continue;
			}
			if (!DgpuApplicationSafety.IsSafeCandidate(application, currentProcessId, currentSessionId))
			{
				skipped.Add(application);
				continue;
			}
			// ⚠️ 路径读得到与读不到分成两条独立路径：读不到（异常 / null）⇒ 一律不结束，
			// 这样既没有"可能为 null 的局部变量"，也不会有"读失败被当成可结束"的口子。
			string imagePath;
			try
			{
				imagePath = readImagePath?.Invoke(application);
			}
			catch
			{
				imagePath = null;
			}
			if (DgpuApplicationSafety.IsUnderSystemRoot(imagePath) != false)
			{
				// true = 在 %SystemRoot% 下；null = 路径读不到 ⇒ 同样不结束。
				skipped.Add(application);
				continue;
			}
			safe.Add(application);
		}
		return new GpuForceKillPlan(safe, skipped, null, Confirmed: true);
	}

	/// <summary>把跳过原因翻译成给日志/名单用的一句话（⛔ 不含路径/用户名等隐私信息）。</summary>
	internal static string DescribeSkipReason(DgpuApplication application, int currentProcessId, int currentSessionId, string imagePath)
	{
		if (application.ProcessId == currentProcessId)
		{
			return ReasonIsLocalProcess;
		}
		if (!DgpuApplicationSafety.IsSafeCandidate(application, currentProcessId, currentSessionId))
		{
			if (application.ProcessId <= 0 || application.SessionId <= 0 || !application.StartedAtUtc.HasValue || string.IsNullOrWhiteSpace(application.ProcessName))
			{
				return ReasonAlreadyExited;
			}
			if (application.SessionId != currentSessionId)
			{
				return ReasonNotCurrentSession;
			}
			return ReasonProtectedProcess;
		}
		bool? underSystemRoot = DgpuApplicationSafety.IsUnderSystemRoot(imagePath);
		if (underSystemRoot == true)
		{
			return ReasonUnderSystemRoot;
		}
		return ReasonImagePathUnavailable;
	}
}
