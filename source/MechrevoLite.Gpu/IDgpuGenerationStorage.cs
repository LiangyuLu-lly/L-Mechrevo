namespace MechrevoLite.Gpu;

public interface IDgpuGenerationStorage
{
	string? Get(string key);

	void Set(string key, string value);
}
