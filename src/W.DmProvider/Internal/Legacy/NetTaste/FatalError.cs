using System;

namespace W.Dm.Internal.Legacy.NetTaste;

public class FatalError : Exception
{
	public FatalError(string m)
		: base(m)
	{
	}
}
