using System;

namespace MechrevoLite.Hardware;

public sealed class ModelRegistryDataException : Exception
{
	public ModelRegistryDataException(string message)
		: base(message)
	{
	}
}
