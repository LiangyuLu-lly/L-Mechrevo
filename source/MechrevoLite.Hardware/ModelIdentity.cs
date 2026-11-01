namespace MechrevoLite.Hardware;

public sealed record ModelIdentity(string ProjectId, int RawProjectByte, string BiosProjectId, ModelSource Source)
{
	public bool IsParsed => Source == ModelSource.Ec;

	public const string UnknownName = "Unknown";

	public static readonly ModelIdentity Unknown = new ModelIdentity("Unknown", -1, "Unknown", ModelSource.Unknown);
}
