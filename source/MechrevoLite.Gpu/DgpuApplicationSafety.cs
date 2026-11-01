using System;
using System.Collections.Generic;
using System.Linq;

namespace MechrevoLite.Gpu;

internal static class DgpuApplicationSafety
{
	private static readonly HashSet<string> ProtectedProcessNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
	{
		"system", "idle", "dwm", "csrss", "winlogon", "services", "lsass", "smss", "wininit", "svchost",
		"fontdrvhost", "igfxem", "igfxhk", "igfxext", "nvcontainer", "nvdisplay.container", "nvsettings", "nvspcaps64", "nvsphelper64", "nvwmi64",
		"nvcplui", "atieclxx", "atiesrxx", "explorer", "taskhostw", "sihost", "runtimebroker", "shellexperiencehost", "searchhost", "startmenuexperiencehost",
		"textinputhost", "applicationframehost", "systemsettings", "dllhost", "conhost", "audiodg", "ctfloader", "spoolsv", "wlanext", "msdtc",
		"l-mechrevo", "gcuservice", "gcubridge", "aistoneservice",
		// 2026-10-13（用户批准「连同强制一起做」）⭐ 安全门 (a)：按只读参考
		// tools\dgpu-silence\Clear-DgpuOccupants.ps1:49-58 的名单补齐上面缺的几项
		// （⛔ 只做**加法**，原有名字与 <see cref="IsProtectedProcessName"/> 的
		// controlcenter 前缀规则一字未改 ⇒ 只会更保守，不会多杀一个进程）。
		"registry", "secure system", "memory compression", "logonui", "lockapp", "ctfmon",
		"wmiprvse", "msmpeng", "nissrv", "securityhealthservice", "securityhealthsystray",
		"nvsmartmaxcontainer"
	};

	internal static IReadOnlyList<DgpuApplication> FilterClosable(IEnumerable<DgpuApplication> applications, int currentProcessId, int currentSessionId)
	{
		return applications.Where((DgpuApplication application) => IsSafeCandidate(application, currentProcessId, currentSessionId)).ToArray();
	}

	internal static bool IsSafeCandidate(DgpuApplication application, int currentProcessId, int currentSessionId)
	{
		if (application.ProcessId > 0 && application.ProcessId != currentProcessId && application.SessionId > 0 && application.SessionId == currentSessionId && application.StartedAtUtc.HasValue && !string.IsNullOrWhiteSpace(application.ProcessName))
		{
			return !IsProtectedProcessName(application.ProcessName);
		}
		return false;
	}

	/// <summary>
	/// 2026-10-13（用户批准「连同强制一起做」）⭐ 强制安全门 (a) 第二部分：
	/// **%SystemRoot% 下的任何进程永不结束**（对照只读参考 <c>tools\dgpu-silence\Clear-DgpuOccupants.ps1:70-73</c>
	/// 的 <c>Test-Protected</c>）。⚠️ 与 <see cref="IsSafeCandidate"/> 分开调用：
	/// 那条判据只看名单/会话/PID，**看不到进程映像路径**，而 SystemRoot 之下有大量
	/// 不在名单里的主机进程（驱动自带的助手、系统自带工具等）。
	/// <para>⚠️ 读不到路径时（权限不足 / 进程已退出 / 位数不匹配）返回 <c>null</c> ⇒
	/// ⛔ **不算** SystemRoot、由上层按名单继续判；但上层把"路径未知"记为**不可结束的兜底理由**
	/// 由 <c>DgpuForceKillPlan.TryPlan</c> 决定（当前口径：路径未知 ⇒ 拒绝结束，宁可漏杀不可错杀）。</para>
	/// </summary>
	internal static bool? IsUnderSystemRoot(string imagePath)
	{
		if (string.IsNullOrWhiteSpace(imagePath))
		{
			return null;
		}
		string root = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
		if (string.IsNullOrWhiteSpace(root))
		{
			root = Environment.GetEnvironmentVariable("SystemRoot");
		}
		if (string.IsNullOrWhiteSpace(root))
		{
			return null;
		}
		string normalizedRoot = root.TrimEnd('\\', '/') + "\\";
		return imagePath.StartsWith(normalizedRoot, StringComparison.OrdinalIgnoreCase);
	}

	private static bool IsProtectedProcessName(string processName)
	{
		if (!ProtectedProcessNames.Contains(processName))
		{
			return processName.StartsWith("controlcenter", StringComparison.OrdinalIgnoreCase);
		}
		return true;
	}
}
