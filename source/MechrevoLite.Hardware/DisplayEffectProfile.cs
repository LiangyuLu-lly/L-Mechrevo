namespace MechrevoLite.Hardware;

/// <summary>显示效果 profile，对应 Display/Status 的 ProfileNameList 与 Display/Control 的写入对象。</summary>
public sealed class DisplayEffectProfile
{
	public string Name { get; set; } = "";

	public int Brightness { get; set; }

	public int ColorTemp { get; set; }

	public float ColorR { get; set; }

	public float ColorG { get; set; }

	public float ColorB { get; set; }

	public float VibrantValue { get; set; }

	public float Constrast { get; set; }

	public float Gamma { get; set; }

	public DisplayEffectProfile Clone()
	{
		return new DisplayEffectProfile
		{
			Name = Name,
			Brightness = Brightness,
			ColorTemp = ColorTemp,
			ColorR = ColorR,
			ColorG = ColorG,
			ColorB = ColorB,
			VibrantValue = VibrantValue,
			Constrast = Constrast,
			Gamma = Gamma
		};
	}
}
