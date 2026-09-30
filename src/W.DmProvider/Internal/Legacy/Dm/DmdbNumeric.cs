using System;
using W.Dm.Internal.Types;

namespace W.Dm;

/// <summary>Legacy facade over the exact DECIMAL base-100 codec.</summary>
public class DmdbNumeric
{
	public const decimal MAX_VALUE = decimal.MaxValue;
	public const decimal MIN_VALUE = decimal.MinValue;
	public static readonly decimal SERVER_NEG_MIN_VALUE = 0.0000000000000000000000000001m;
	public static readonly decimal SERVER_POSITIVE_MIN_VALUE = -0.0000000000000000000000000001m;
	// Retained for source compatibility. T09 never rounds or truncates by this switch.
	public static bool ROUND_HALF_SWITCH = false;
	public const int XDEC_MAX_PREC = 40;
	public const int FLAG_POSITIVE = 193;
	public const int FLAG_NEGTIVE = 62;

	private readonly int precision;
	private readonly int scale;
	private readonly bool hasScale;
	private DmDecimal value;
	private byte[] wire;

	private DmdbNumeric(int precision, int scale)
	{
		this.precision = precision;
		this.scale = scale;
		hasScale = scale >= 0 && (precision > 0 || scale > 0);
	}

	public DmdbNumeric(byte[] values, int prec, int scale) : this(prec, scale) => decode(values);

	public static DmdbNumeric valueOf(decimal dec, int prec, int scale)
	{
		var result = new DmdbNumeric(prec, scale);
		result.SetValue(DmDecimal.FromDecimal(dec));
		return result;
	}

	public static DmdbNumeric valueOf(string str, int prec, int scale)
	{
		var result = new DmdbNumeric(prec, scale);
		result.SetValue(DmDecimal.Parse(str));
		return result;
	}

	public static DmdbNumeric valueOf(long val, int prec, int scale) =>
		valueOf(new decimal(val), prec, scale);

	private void SetValue(DmDecimal input)
	{
		wire = DmNumericCodec.EncodeDecimal(input, precision, hasScale ? scale : -1);
		value = DmNumericCodec.DecodeDecimal(wire, hasScale ? scale : null);
	}

	public void decode(byte[] values)
	{
		ArgumentNullException.ThrowIfNull(values);
		wire = (byte[])values.Clone();
		value = DmNumericCodec.DecodeDecimal(wire, hasScale ? scale : null);
	}

	public decimal toDecimal(bool compatibleOracle) => value.ToDecimalExact();
	public string toString(bool compatibleOracle) => value.ToString();
	public string toString() => toString(compatibleOracle: false);
	public bool isZero() => value.Coefficient.IsZero;

	public byte[] encode(bool allowOverflow)
	{
		if (wire == null) throw new InvalidOperationException("DECIMAL value has not been initialized.");
		// allowOverflow never authorizes loss of digits in the T09 exact path.
		return (byte[])wire.Clone();
	}
}
