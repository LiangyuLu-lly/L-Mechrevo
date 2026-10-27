namespace MechrevoLite.Gpu;

public sealed class AppConfigDgpuGenerationStorage : IDgpuGenerationStorage
{
	public string? Get(string key)
	{
		return AppConfig.GetString(key);
	}

	public void Set(string key, string value)
	{
		AppConfig.Set(key, value);
	}
}
