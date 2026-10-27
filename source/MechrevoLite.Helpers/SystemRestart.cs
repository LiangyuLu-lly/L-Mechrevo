using System;
using System.Diagnostics;
using System.Threading.Tasks;

namespace MechrevoLite.Helpers;

internal static class SystemRestart
{
	internal const string RebootNowArguments = "/r /t 1";

	internal const string RebootAfterFiveSecondsArguments = "/r /t 5";

	private static readonly UserGestureLease Confirmation = new UserGestureLease();

	internal static Action<string, string>? ProcessStartOverride { get; set; }

	internal static bool CaptureUserConfirmation()
	{
		if (!Confirmation.Capture())
		{
			Logger.WriteLine("Restart confirmation not captured: no fresh user input.");
			return false;
		}
		return true;
	}

	internal static void ResetUserConfirmation()
	{
		Confirmation.Reset();
	}

	private static bool HasCapturedConfirmation()
	{
		return Confirmation.IsActive;
	}

	internal static bool RequestRestart(string reason, string arguments = "/r /t 5")
	{
		if (!NativeMethods.HasFreshUserInput() && !HasCapturedConfirmation())
		{
			Logger.WriteLine("Restart ignored (" + reason + "): no fresh user input.");
			return false;
		}
		Confirmation.Consume();
		Action<string, string> overrideStart = ProcessStartOverride;
		Task.Run(() =>
		{
			try
			{
				if (overrideStart != null)
				{
					overrideStart("shutdown", arguments);
					Logger.WriteLine($"Restart requested ({reason}); override captured shutdown {arguments}.");
				}
				else
				{
					Process.Start(new ProcessStartInfo("shutdown", arguments)
					{
						UseShellExecute = true,
						CreateNoWindow = true
					});
					Logger.WriteLine("Restart requested (" + reason + "): shutdown " + arguments);
				}
			}
			catch (Exception ex)
			{
				Logger.WriteLine("Restart failed (" + reason + "): " + ex.Message);
			}
		});
		return true;
	}
}
