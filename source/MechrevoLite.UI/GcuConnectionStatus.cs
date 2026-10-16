using System.Drawing;
using MechrevoLite.Properties;
namespace MechrevoLite.UI;

internal static class GcuConnectionStatus
{
	internal const long FirstConnectGiveUpMs = 20000L;

	internal static GcuConnectionState Resolve(bool hardwarePresent, bool connected, bool reconnecting, bool everConnected, long reconnectAgeMs = 0L)
	{
		if (!hardwarePresent)
		{
			return GcuConnectionState.Unknown;
		}
		if (connected)
		{
			return GcuConnectionState.Connected;
		}
		if (reconnecting && !everConnected)
		{
			if (reconnectAgeMs < 20000)
			{
				return GcuConnectionState.Connecting;
			}
			return GcuConnectionState.Disconnected;
		}
		return GcuConnectionState.Disconnected;
	}

	internal static (Color Fore, string Text, string Tooltip) Describe(GcuConnectionState state)
	{
		return state switch
		{
			GcuConnectionState.Connected => (Fore: UiVisualStyle.Ok, Text: Strings.GcuConnectedText, Tooltip: Strings.GcuConnectedTooltip), 
			GcuConnectionState.Connecting => (Fore: UiVisualStyle.Warn, Text: Strings.GcuConnectingText, Tooltip: Strings.GcuConnectingTooltip), 
			GcuConnectionState.Disconnected => (Fore: UiVisualStyle.Danger, Text: Strings.GcuDisconnectedText, Tooltip: Strings.GcuDisconnectedTooltip), 
			_ => (Fore: UiVisualStyle.Muted, Text: Strings.GcuUnknownText, Tooltip: Strings.GcuUnknownTooltip), 
		};
	}
}
