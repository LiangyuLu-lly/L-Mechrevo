namespace MechrevoLite.UI;

public static class ScreenPanelVisibility
{
	public static bool PanelVisible(int maxFrequency)
	{
		return maxFrequency > 0;
	}

	public static bool RefreshTableVisible(int maxFrequency, int minRate)
	{
		return maxFrequency > minRate;
	}
}
