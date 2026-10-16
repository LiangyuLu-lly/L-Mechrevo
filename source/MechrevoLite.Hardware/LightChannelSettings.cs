namespace MechrevoLite.Hardware;

internal readonly record struct LightChannelSettings(string Effect, int Light, int Speed, int ColorArgb, bool PowerOn, int[]? ManualIndexes = null, int BreathingIndex = 1, int MonochromeIndex = 4, bool UsePresetColor = false)
{
	internal static readonly int[] DefaultManualIndexes = new int[6] { 1, 5, 9, 13, 21, 25 };

	internal static readonly int[] BreathingPalette = new int[4]
	{
		System.Drawing.Color.FromArgb(255, 255, 255).ToArgb(),
		System.Drawing.Color.FromArgb(255, 0, 0).ToArgb(),
		System.Drawing.Color.FromArgb(0, 255, 0).ToArgb(),
		System.Drawing.Color.FromArgb(0, 0, 255).ToArgb()
	};

	internal static readonly int[] ManualPalette = new int[29]
	{
		System.Drawing.Color.FromArgb(211, 6, 7).ToArgb(),
		System.Drawing.Color.FromArgb(207, 44, 5).ToArgb(),
		System.Drawing.Color.FromArgb(220, 77, 7).ToArgb(),
		System.Drawing.Color.FromArgb(119, 51, 4).ToArgb(),
		System.Drawing.Color.FromArgb(213, 91, 10).ToArgb(),
		System.Drawing.Color.FromArgb(206, 105, 3).ToArgb(),
		System.Drawing.Color.FromArgb(206, 147, 3).ToArgb(),
		System.Drawing.Color.FromArgb(122, 105, 4).ToArgb(),
		System.Drawing.Color.FromArgb(212, 169, 6).ToArgb(),
		System.Drawing.Color.FromArgb(165, 184, 4).ToArgb(),
		System.Drawing.Color.FromArgb(100, 210, 6).ToArgb(),
		System.Drawing.Color.FromArgb(70, 107, 16).ToArgb(),
		System.Drawing.Color.FromArgb(5, 211, 8).ToArgb(),
		System.Drawing.Color.FromArgb(3, 207, 68).ToArgb(),
		System.Drawing.Color.FromArgb(2, 207, 147).ToArgb(),
		System.Drawing.Color.FromArgb(50, 103, 111).ToArgb(),
		System.Drawing.Color.FromArgb(4, 210, 211).ToArgb(),
		System.Drawing.Color.FromArgb(5, 151, 213).ToArgb(),
		System.Drawing.Color.FromArgb(9, 74, 215).ToArgb(),
		System.Drawing.Color.FromArgb(1, 42, 96).ToArgb(),
		System.Drawing.Color.FromArgb(7, 9, 214).ToArgb(),
		System.Drawing.Color.FromArgb(67, 4, 209).ToArgb(),
		System.Drawing.Color.FromArgb(147, 4, 208).ToArgb(),
		System.Drawing.Color.FromArgb(91, 39, 84).ToArgb(),
		System.Drawing.Color.FromArgb(211, 8, 214).ToArgb(),
		System.Drawing.Color.FromArgb(211, 6, 150).ToArgb(),
		System.Drawing.Color.FromArgb(215, 14, 80).ToArgb(),
		System.Drawing.Color.FromArgb(149, 8, 4).ToArgb(),
		System.Drawing.Color.FromArgb(6, 8, 10).ToArgb()
	};
}
