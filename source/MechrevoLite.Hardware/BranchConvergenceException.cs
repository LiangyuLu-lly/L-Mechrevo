using System;

namespace MechrevoLite.Hardware;

public sealed class BranchConvergenceException : Exception
{
	public BranchConvergenceException(string message)
		: base(message)
	{
	}
}
