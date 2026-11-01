using Microsoft.Win32;

namespace MechrevoLite.Helpers;

internal static class GcuCoexistence
{
	internal const string VendorServiceName = "GCUBridge";

	internal static GcuCoexistenceKind Classify(bool foreignService, bool foreignPortOwner)
	{
		if (foreignService)
		{
			if (foreignPortOwner)
			{
				return GcuCoexistenceKind.Both;
			}
			return GcuCoexistenceKind.ForeignService;
		}
		if (foreignPortOwner)
		{
			return GcuCoexistenceKind.PortOwner;
		}
		return GcuCoexistenceKind.None;
	}

	internal static string? ReadServiceImagePath(string serviceName)
	{
		using RegistryKey registryKey = Registry.LocalMachine.OpenSubKey("SYSTEM\\CurrentControlSet\\Services\\" + serviceName);
		return registryKey?.GetValue("ImagePath")?.ToString();
	}

	internal static bool RequiresConsoleRemovalPrompt(GcuCoexistenceKind kind)
	{
		return kind != GcuCoexistenceKind.None;
	}

	internal static string BuildConsoleRemovalPrompt(GcuCoexistenceKind kind)
	{
		return "检测到机器上仍有厂商的 GCU 环境（" + kind.ToString() + "）。\r\nL-Mechrevo 不会替你静默删除厂商的软件。请手动卸载「官方控制台」应用（设置 → 应用 → 已安装的应用），然后重新启动 L-Mechrevo。\r\nGCU 服务的接管（先卸后装 + 回滚护栏）由安装器负责。";
	}
}
