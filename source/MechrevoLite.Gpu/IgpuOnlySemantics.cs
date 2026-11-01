namespace MechrevoLite.Gpu;

public static class IgpuOnlySemantics
{
	public const int PollIntervalMilliseconds = 2000;

	public const int PollLimit = 61;

	public const int RetryEveryPolls = 4;

	public const int SuccessOn = 2;

	public const int SuccessOff = 1;

	public static bool ShouldResend(int poll)
	{
		if (poll >= 0)
		{
			return poll % 4 == 0;
		}
		return false;
	}

	public static bool IsSuccess(int reported, bool turningOn)
	{
		return reported == ((!turningOn) ? 1 : 2);
	}

	public static int AutomaticRuntime(bool plugged)
	{
		if (!plugged)
		{
			return 2;
		}
		return 1;
	}

	public static int RollbackStatus(int modeBeforeSwitch)
	{
		return modeBeforeSwitch switch
		{
			3 => 2, 
			0 => 1, 
			_ => 0, 
		};
	}
}
