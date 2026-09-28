using A;

namespace Dm;

internal class COMMIT : MSG<int>
{
	public COMMIT(B access)
		: base(access, (short)8)
	{
	}

	protected override void doEncode()
	{
	}

	protected override int doDecode()
	{
		return 0;
	}
}
