namespace W.Dm;

public class Data
{
	public const int LEN_USINT_MAX = 65280;

	public const int LEN_FULL = 65535;

	public const int LEN_NULL = 65534;

	public const int LEN_LOB_CTL = 65531;

	public const int LEN_LOB_EMPTY = 65530;

	public const int LEN_PUT_DATA2 = 65529;

	public long len;

	public byte[] value;

	public static readonly Data EMPTY_LOB = new Data(65530L, new byte[0]);

	public static readonly Data NULL = new Data(65534L, new byte[0]);

	public Data(long len, byte[] value)
	{
		this.len = len;
		this.value = value;
	}
}
