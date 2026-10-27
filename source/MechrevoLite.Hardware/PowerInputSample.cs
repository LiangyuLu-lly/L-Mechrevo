namespace MechrevoLite.Hardware;

public sealed record PowerInputSample(PowerInputKind Kind, int? AdapterWatts, int ComplexStatusRaw, int BiosInfo3Raw, long TakenTick, bool DecodeEnabled)
{
	internal bool EcReadable => DecodeEnabled && ComplexStatusRaw >= 0;
}
