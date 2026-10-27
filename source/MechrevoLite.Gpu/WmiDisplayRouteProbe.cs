using System;
using System.Collections.Generic;
using System.Linq;
using System.Management;

namespace MechrevoLite.Gpu;

/// <summary>
/// ⭐ 阶段 2 第 4 条：把「活动显示通路是否落在独显输出」读成纯数据
/// （<see cref="DisplayRouteSnapshot" />），判断交给可离线断言的
/// <see cref="GpuScenarioGuard" />。
///
/// <para>⚠️ 判据**逐字**照搬用户自己那套已验证脚本
/// <c>tools\dgpu-silence\Invoke-GpuHotSwitch.ps1:143-157</c> 的 <c>Get-DesktopDrivers</c>：
/// <c>Win32_VideoController</c> 中 <c>CurrentHorizontalResolution &gt; 0</c> 的才算"在驱动桌面"，
/// 是不是 NVIDIA 看 <c>Name</c> / <c>PNPDeviceID</c>(<c>VEN_10DE</c>)。</para>
/// <para>⛔ 只读查询：不发任何 EC/GCU 命令、⛔ 不写注册表、⛔ 不改显示拓扑。
/// ⚠️ WMI 是慢路径（可能几百毫秒）⇒ <see cref="ProbeAsync" /> 把查询放到线程池并带超时。</para>
/// </summary>
internal static class WmiDisplayRouteProbe
{
	/// <summary>默认超时：WMI 超时就当"读不到"（安全门取保守侧：拒绝强制）。</summary>
	internal static readonly TimeSpan DefaultTimeout = TimeSpan.FromMilliseconds(1500L);

	internal static async System.Threading.Tasks.Task<DisplayRouteSnapshot> ProbeAsync(TimeSpan? timeout = null)
	{
		try
		{
			System.Threading.Tasks.Task<DisplayRouteSnapshot> probe = System.Threading.Tasks.Task.Run(Probe);
			System.Threading.Tasks.Task completed = await System.Threading.Tasks.Task.WhenAny(probe, System.Threading.Tasks.Task.Delay(timeout ?? DefaultTimeout)).ConfigureAwait(continueOnCapturedContext: false);
			if (!ReferenceEquals(completed, probe))
			{
				Logger.WriteLineThrottled("display-route-probe", "Display route probe timed out; treating the route as unavailable.", 60000);
				return DisplayRouteSnapshot.Unavailable;
			}
			return await probe.ConfigureAwait(continueOnCapturedContext: false);
		}
		catch (Exception ex)
		{
			Logger.WriteLine("Display route probe failed: " + ex.Message);
			return DisplayRouteSnapshot.Unavailable;
		}
	}

	private static DisplayRouteSnapshot Probe()
	{
		List<DisplayAdapterSnapshot> adapters = new List<DisplayAdapterSnapshot>();
		try
		{
			using ManagementObjectSearcher searcher = new ManagementObjectSearcher("SELECT Name, CurrentHorizontalResolution, PNPDeviceID FROM Win32_VideoController");
			foreach (ManagementObject row in searcher.Get().Cast<ManagementObject>())
			{
				using (row)
				{
					int width = 0;
					try
					{
						object raw = row["CurrentHorizontalResolution"];
						if (raw != null && raw != DBNull.Value)
						{
							width = Convert.ToInt32(raw);
						}
					}
					catch (Exception ex)
					{
						Logger.WriteLine("Display route probe could not read a resolution: " + ex.Message);
						width = 0;
					}
					adapters.Add(new DisplayAdapterSnapshot(row["Name"]?.ToString() ?? "", row["PNPDeviceID"]?.ToString() ?? "", width));
				}
			}
		}
		catch (Exception ex2)
		{
			Logger.WriteLine("Display route probe could not enumerate adapters: " + ex2.Message);
			return DisplayRouteSnapshot.Unavailable;
		}
		return new DisplayRouteSnapshot(adapters);
	}

	/// <summary>给日志用的一句话（⛔ 不含适配器名/设备实例 ID 等可识别信息）。</summary>
	internal static string Describe(DisplayRouteSnapshot snapshot)
	{
		DisplayRouteKind kind = GpuScenarioGuard.ClassifyRoute(snapshot);
		int active = snapshot.Adapters?.Count((DisplayAdapterSnapshot a) => a.HasActiveDisplay) ?? 0;
		return $"{kind}(activeAdapters={active})";
	}
}
