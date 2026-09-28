using System;

namespace Dm.util.geoUtil;

public class DmGeo2Util
{
	public const string NDR = "NDR";

	public const string XDR = "XDR";

	public static int SridFromGser(byte[] gserialized)
	{
		return new NtsBinaryParser().SridFromGserialized(gserialized);
	}

	public static byte[] WkbFromGser(byte[] gserialized, string endian)
	{
		return new NtsBinaryParser().GserToWKb(gserialized, endian, "WKB");
	}

	public static byte[] EWkbFromGser(byte[] gserialized, string endian)
	{
		return new NtsBinaryParser().GserToWKb(gserialized, endian, "EWKB");
	}

	public static byte[] WkbToGserGeom(byte[] wkb)
	{
		return new NtsBinaryParser().WkbTogser(wkb, -1, "WKB", isGeog: false);
	}

	public static byte[] WkbToGserGeom(byte[] wkb, int srid)
	{
		if (srid < 0)
		{
			throw new ArgumentException("Illegal Srid Value!");
		}
		return new NtsBinaryParser().WkbTogser(wkb, srid, "WKB", isGeog: false);
	}

	public static byte[] WkbToGserGeog(byte[] wkb)
	{
		return new NtsBinaryParser().WkbTogser(wkb, -1, "WKB", isGeog: true);
	}

	public static byte[] WkbToGserGeog(byte[] wkb, int srid)
	{
		if (srid < 0)
		{
			throw new ArgumentException("Illegal Srid Value!");
		}
		return new NtsBinaryParser().WkbTogser(wkb, srid, "WKB", isGeog: true);
	}

	public static byte[] EWkbToGserGeom(byte[] eWKb)
	{
		return new NtsBinaryParser().WkbTogser(eWKb, 0, "EWKB", isGeog: false);
	}

	public static byte[] EWkbToGserGeog(byte[] eWKb)
	{
		return new NtsBinaryParser().WkbTogser(eWKb, 0, "EWKB", isGeog: true);
	}
}
