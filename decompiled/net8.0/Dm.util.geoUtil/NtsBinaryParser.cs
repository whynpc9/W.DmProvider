using System;
using System.IO;
using System.Linq;
using Dm.net.buffer;

namespace Dm.util.geoUtil;

public class NtsBinaryParser
{
	public const int POINT_TYPE = 1;

	public const int LINE_TYPE = 2;

	public const int POLYGON_TYPE = 3;

	public const int MULTIPOINT_TYPE = 4;

	public const int MULTILINE_TYPE = 5;

	public const int MULTIPOLYGON_TYPE = 6;

	public const int COLLECTION_TYPE = 7;

	public const int CIRCSTRING_TYPE = 8;

	public const int COMPOUND_TYPE = 9;

	public const int CURVEPOLY_TYPE = 10;

	public const int MULTICURVE_TYPE = 11;

	public const int MULTISURFACE_TYPE = 12;

	public const int POLYHEDRALSURFACE_TYPE = 13;

	public const int TRIANGLE_TYPE = 14;

	public const int TIN_TYPE = 15;

	public const int WKB_POINT_TYPE = 1;

	public const int WKB_LINE_TYPE = 2;

	public const int WKB_POLYGON_TYPE = 3;

	public const int WKB_MULTIPOINT_TYPE = 4;

	public const int WKB_MULTILINE_TYPE = 5;

	public const int WKB_MULTIPOLYGON_TYPE = 6;

	public const int WKB_COLLECTION_TYPE = 7;

	public const int WKB_CIRCSTRING_TYPE = 8;

	public const int WKB_COMPOUND_TYPE = 9;

	public const int WKB_CURVEPOLY_TYPE = 10;

	public const int WKB_MULTICURVE_TYPE = 11;

	public const int WKB_MULTISURFACE_TYPE = 12;

	public const int WKB_POLYHEDRALSURFACE_TYPE = 15;

	public const int WKB_TIN_TYPE = 16;

	public const int WKB_TRIANGLE_TYPE = 17;

	private const int WKB_BYTE_SIZE = 1;

	private const int WKB_INT_SIZE = 4;

	private const int WKB_DOUBLE_SIZE = 8;

	private const int EWKBZOFFSET = int.MinValue;

	private const int EWKBMOFFSET = 1073741824;

	private const int EWKBSRIDFLAG = 536870912;

	private const double EPSILON_SQLMM = 1E-08;

	private const double FLT_MAX = 3.4028234663852886E+38;

	private const double M_PI = Math.PI;

	private const int GEOGRAPHY_SRID_DEFAULT = 4326;

	private const double FP_TOLERANCE = 1E-12;

	private static readonly byte[] NAN = new byte[8] { 0, 0, 0, 0, 0, 0, 248, 255 };

	private void PtarrayToWriter(ByteReader reader, ByteWriter writer, int dimension, int points)
	{
		for (int i = 0; i < points * dimension; i++)
		{
			byte[] buf = reader.ReadPart(8);
			writer.WritePart(buf);
		}
	}

	public int SridFromGserialized(byte[] gserialized)
	{
		ByteReader byteReader = new ByteReader(gserialized);
		byteReader.skip(4);
		return byteReader.ReadSrid();
	}

	internal byte[] GserToWKb(byte[] gserialized, string endian, string variant)
	{
		if (!string.IsNullOrEmpty(endian) && !endian.Equals("NDR", StringComparison.OrdinalIgnoreCase) && !endian.Equals("XDR", StringComparison.OrdinalIgnoreCase))
		{
			throw new ArgumentException("Unknown Geometry Endian!");
		}
		ByteReader byteReader = new ByteReader(gserialized);
		ByteWriter byteWriter = new ByteWriter();
		if (endian.Equals("NDR", StringComparison.OrdinalIgnoreCase))
		{
			byteWriter.SetByteOrder(1);
			byteWriter.Write(1);
			byteReader.SetOutByteOrder(1);
		}
		else
		{
			byteWriter.SetByteOrder(0);
			byteWriter.Write(0);
			byteReader.SetOutByteOrder(0);
		}
		byteReader.skip(4);
		int value = byteReader.ReadSrid();
		bool flag = true;
		if (variant.Equals("WKB", StringComparison.OrdinalIgnoreCase))
		{
			flag = false;
		}
		int num = byteReader.Read();
		bool flag2 = (num & 1) != 0;
		bool flag3 = (num & 2) != 0;
		bool flag4 = (num & 4) != 0;
		bool flag5 = (num & 8) != 0;
		bool num2 = (num & 0x10) != 0;
		int num3 = 2;
		num3 += (flag2 ? 1 : 0);
		num3 += (flag3 ? 1 : 0);
		if (num2)
		{
			byteReader.skip(8);
		}
		if (flag4)
		{
			if (flag5)
			{
				byteReader.skip(24);
			}
			else
			{
				byteReader.skip(8 * num3);
			}
		}
		int num4 = byteReader.readInt();
		byteWriter.WriteInt(GeometryWkbType(num4, flag2, flag3, flag, variant));
		if (flag)
		{
			byteWriter.WriteInt(value);
		}
		switch (num4)
		{
		case 1:
			ParsePointWkb(byteReader, byteWriter, num3);
			break;
		case 2:
		case 8:
			ParseLineStringWkb(byteReader, byteWriter, num3);
			break;
		case 3:
			ParsePolygonWkb(byteReader, byteWriter, num3);
			break;
		case 14:
			ParseTriangleWkb(byteReader, byteWriter, num3);
			break;
		case 4:
		case 5:
		case 6:
		case 7:
		case 9:
		case 10:
		case 11:
		case 12:
		case 13:
		case 15:
			ParseCollectionWkb(byteReader, byteWriter, flag2, flag3, num3, variant);
			break;
		default:
			throw new ArgumentException("Unknown Geometry Type!");
		}
		return byteWriter.ToByteArray();
	}

	private int GeometryWkbType(int type, bool hasZ, bool hasM, bool hasS, string variant)
	{
		int num = 0;
		num = type switch
		{
			1 => 1, 
			2 => 2, 
			3 => 3, 
			4 => 4, 
			5 => 5, 
			6 => 6, 
			7 => 7, 
			8 => 8, 
			9 => 9, 
			10 => 10, 
			11 => 11, 
			12 => 12, 
			13 => 15, 
			15 => 16, 
			14 => 17, 
			_ => throw new IOException("Unsupported geometry type"), 
		};
		if (variant.Equals("EWKB", StringComparison.OrdinalIgnoreCase))
		{
			if (hasZ)
			{
				num |= int.MinValue;
			}
			if (hasM)
			{
				num |= 0x40000000;
			}
			if (hasS)
			{
				num |= 0x20000000;
			}
		}
		if (variant.Equals("WKB", StringComparison.OrdinalIgnoreCase))
		{
			if (hasZ)
			{
				num += 1000;
			}
			if (hasM)
			{
				num += 2000;
			}
		}
		return num;
	}

	private void ParsePointWkb(ByteReader gserReader, ByteWriter wkbWriter, int dimension)
	{
		gserReader.mark();
		if (gserReader.readInt() == 0)
		{
			for (int i = 0; i < dimension; i++)
			{
				wkbWriter.WritePart(NAN, 1);
			}
		}
		else
		{
			gserReader.reset();
			gserReader.skip(4);
			PtarrayToWriter(gserReader, wkbWriter, dimension, 1);
		}
	}

	private void ParseLineStringWkb(ByteReader gserReader, ByteWriter wkbWriter, int dimension)
	{
		int num = gserReader.readInt();
		wkbWriter.WriteInt(num);
		PtarrayToWriter(gserReader, wkbWriter, dimension, num);
	}

	private void ParsePolygonWkb(ByteReader gserReader, ByteWriter wkbWriter, int dimension)
	{
		int num = gserReader.readInt();
		wkbWriter.WriteInt(num);
		if (num != 0)
		{
			if (num % 2 != 0)
			{
				num++;
			}
			int[] array = new int[num];
			for (int i = 0; i < num; i++)
			{
				array[i] = gserReader.readInt();
			}
			if (array[num - 1] == 0)
			{
				num--;
			}
			for (int j = 0; j < num; j++)
			{
				wkbWriter.WriteInt(array[j]);
				PtarrayToWriter(gserReader, wkbWriter, dimension, array[j]);
			}
		}
	}

	private void ParseCollectionWkb(ByteReader gserReader, ByteWriter wkbWriter, bool hasZ, bool hasM, int dimension, string variant)
	{
		int num = gserReader.readInt();
		wkbWriter.WriteInt(num);
		for (int i = 0; i < num; i++)
		{
			wkbWriter.Write(wkbWriter.GetByteOrder());
			int num2 = gserReader.readInt();
			wkbWriter.WriteInt(GeometryWkbType(num2, hasZ, hasM, hasS: false, variant));
			switch (num2)
			{
			case 1:
				ParsePointWkb(gserReader, wkbWriter, dimension);
				break;
			case 2:
			case 8:
				ParseLineStringWkb(gserReader, wkbWriter, dimension);
				break;
			case 3:
				ParsePolygonWkb(gserReader, wkbWriter, dimension);
				break;
			case 14:
				ParseTriangleWkb(gserReader, wkbWriter, dimension);
				break;
			case 4:
			case 5:
			case 6:
			case 7:
			case 9:
			case 10:
			case 11:
			case 12:
			case 13:
			case 15:
				ParseCollectionWkb(gserReader, wkbWriter, hasZ, hasM, dimension, variant);
				break;
			}
		}
	}

	private void ParseTriangleWkb(ByteReader gserReader, ByteWriter wkbWriter, int dimension)
	{
		wkbWriter.WriteInt(1);
		ParseLineStringWkb(gserReader, wkbWriter, dimension);
	}

	private bool GserIsneedBox(ByteReader wkbReader, int type, int dimension)
	{
		switch (type)
		{
		case 1:
			return false;
		case 2:
			if (wkbReader.readInt() <= 2)
			{
				return false;
			}
			return true;
		case 4:
			if (wkbReader.readInt() == 1)
			{
				return false;
			}
			return true;
		case 5:
		{
			int num = wkbReader.readInt();
			int num2 = 0;
			for (int i = 0; i < num; i++)
			{
				wkbReader.skip(5);
				int num3 = wkbReader.readInt();
				num2 += num3;
			}
			if (num == 1 && num2 <= 2)
			{
				return false;
			}
			return true;
		}
		case 7:
			return IsCollectionNeedBox(wkbReader, dimension);
		default:
			return true;
		}
	}

	protected bool wkbIsEmpty(ByteReader wkbReader, int type, int dimension)
	{
		switch (type)
		{
		case 1:
		{
			int num3 = dimension;
			for (int j = 1; j <= dimension; j++)
			{
				if (wkbReader.readPart(8, 1).Equals(NAN))
				{
					num3--;
				}
			}
			if (num3 == 0)
			{
				return true;
			}
			break;
		}
		case 2:
		case 3:
		case 8:
		case 17:
			if (wkbReader.readInt() == 0)
			{
				return true;
			}
			break;
		case 4:
		case 5:
		case 6:
		case 7:
		case 9:
		case 10:
		case 11:
		case 12:
		case 15:
		case 16:
		{
			int num = wkbReader.readInt();
			if (num == 0)
			{
				return true;
			}
			for (int i = 0; i < num; i++)
			{
				wkbReader.skip(1);
				int num2 = 0;
				if (wkbReader.getReaderByteOrder() == 1)
				{
					num2 = wkbReader.getRepPartValue(2);
					wkbReader.skip(2);
				}
				else
				{
					wkbReader.skip(2);
					num2 = wkbReader.getRepPartValue(2);
				}
				if (num2 > 2000)
				{
					num2 -= 2000;
				}
				if (num2 > 1000)
				{
					num2 -= 1000;
				}
				if (!wkbIsEmpty(wkbReader, num2, dimension))
				{
					return false;
				}
			}
			return true;
		}
		}
		return false;
	}

	private bool IsCollectionNeedBox(ByteReader wkbReader, int dimension)
	{
		bool result = false;
		int num = wkbReader.readInt();
		for (int i = 0; i < num; i++)
		{
			wkbReader.skip(1);
			int num2;
			if (wkbReader.getReaderByteOrder() == 1)
			{
				num2 = wkbReader.getRepPartValue(2);
				wkbReader.skip(2);
			}
			else
			{
				wkbReader.skip(2);
				num2 = wkbReader.getRepPartValue(2);
			}
			if (num2 > 2000)
			{
				num2 -= 2000;
			}
			if (num2 > 1000)
			{
				num2 -= 1000;
			}
			num2 = num2 switch
			{
				1 => 1, 
				2 => 2, 
				3 => 3, 
				4 => 4, 
				5 => 5, 
				6 => 6, 
				7 => 7, 
				8 => 8, 
				9 => 9, 
				10 => 10, 
				11 => 11, 
				12 => 12, 
				15 => 13, 
				16 => 15, 
				17 => 14, 
				_ => throw new IOException("Unsupported geometry type"), 
			};
			bool flag = false;
			switch (num2)
			{
			case 1:
				flag = IsEmptyPoint(wkbReader, dimension);
				break;
			case 2:
			case 8:
				flag = IsEmptyLineString(wkbReader, dimension);
				break;
			case 3:
				flag = IsEmptyPolygon(wkbReader, dimension);
				break;
			case 14:
				flag = IsEmptyTriangle(wkbReader, dimension);
				break;
			case 4:
			case 5:
			case 6:
			case 7:
			case 9:
			case 10:
			case 11:
			case 12:
			case 13:
			case 15:
				flag = IsEmptyCollection(wkbReader, dimension);
				break;
			}
			if (!flag)
			{
				result = true;
				break;
			}
		}
		return result;
	}

	private bool IsEmptyPoint(ByteReader wkbReader, int dimension)
	{
		bool result = true;
		for (int i = 0; i < dimension; i++)
		{
			if (!Enumerable.SequenceEqual(wkbReader.readPart(8, 1), NAN))
			{
				result = false;
				break;
			}
		}
		return result;
	}

	private bool IsEmptyLineString(ByteReader wkbReader, int dimension)
	{
		return wkbReader.readInt() == 0;
	}

	private bool IsEmptyPolygon(ByteReader wkbReader, int dimension)
	{
		return wkbReader.readInt() == 0;
	}

	private bool IsEmptyTriangle(ByteReader wkbReader, int dimension)
	{
		throw new NotImplementedException();
	}

	private bool IsEmptyCollection(ByteReader wkbReader, int dimension)
	{
		return wkbReader.readInt() == 0;
	}

	private void CalculateBoxGeodetic(ByteReader wkbReader, int type, bool hasZ, bool hasM, double[] box)
	{
		switch (type)
		{
		case 1:
		{
			int pointNumber = 1;
			CalculateGboxGeodetic(wkbReader, hasZ, hasM, pointNumber, box);
			break;
		}
		case 2:
		{
			int pointNumber = wkbReader.readInt();
			CalculateGboxGeodetic(wkbReader, hasZ, hasM, pointNumber, box);
			break;
		}
		case 3:
			PolygonCalculateGboxGeodetic(wkbReader, hasZ, hasM, box);
			break;
		case 17:
		{
			wkbReader.skip(4);
			int pointNumber = wkbReader.readInt();
			CalculateGboxGeodetic(wkbReader, hasZ, hasM, pointNumber, box);
			break;
		}
		case 4:
		case 5:
		case 6:
		case 7:
		case 15:
		case 16:
			CollectionCalculateGboxGeodetic(wkbReader, hasZ, hasM, box);
			break;
		case 8:
		case 9:
		case 10:
		case 11:
		case 12:
		case 13:
		case 14:
			break;
		}
	}

	private void CalculateGboxGeodetic(ByteReader wkbReader, bool hasZ, bool hasM, int pointNumber, double[] box)
	{
		bool flag = true;
		double[] array = new double[8];
		double[] array2 = new double[8];
		array2[0] = (array2[2] = (array2[4] = (array2[6] = 3.4028234663852886E+38)));
		array2[1] = (array2[3] = (array2[5] = (array2[7] = -3.4028234663852886E+38)));
		Point4d point4d = new Point4d();
		Point4d point4d2 = new Point4d();
		Point4d point4d3 = new Point4d();
		GetPoint4d(wkbReader, hasZ, hasM, point4d);
		Ll2Cart(point4d, point4d2);
		if (pointNumber == 1)
		{
			box[0] = (box[1] = point4d2.x);
			box[2] = (box[3] = point4d2.y);
			box[4] = (box[5] = point4d2.z);
			return;
		}
		for (int i = 1; i < pointNumber; i++)
		{
			GetPoint4d(wkbReader, hasZ, hasM, point4d);
			Ll2Cart(point4d, point4d3);
			EdgeCalculateGbox(point4d2, point4d3, array2);
			if (flag)
			{
				array = Arrays.CopyOfRange(array2, 0, array2.Length);
				flag = false;
			}
			else
			{
				gbox_merge(array2, array, hasZ: true, hasM);
			}
			point4d2.x = point4d3.x;
			point4d2.y = point4d3.y;
			point4d2.z = point4d3.z;
		}
		for (int j = 0; j < 8; j++)
		{
			box[j] = array[j];
		}
	}

	private void Ll2Cart(Point4d g, Point4d p)
	{
		double num = Math.PI * g.x / 180.0;
		double num2 = Math.PI * g.y / 180.0;
		double num3 = Math.Cos(num2);
		p.x = num3 * Math.Cos(num);
		p.y = num3 * Math.Sin(num);
		p.z = Math.Sin(num2);
	}

	private void EdgeCalculateGbox(Point4d A1, Point4d A2, double[] box)
	{
		Point4d point4d = new Point4d();
		Point4d point4d2 = new Point4d();
		Point4d point4d3 = new Point4d();
		Point4d point4d4 = new Point4d();
		Point4d point4d5 = new Point4d();
		Point4d point4d6 = new Point4d();
		box[0] = (box[1] = A1.x);
		box[2] = (box[3] = A1.y);
		box[4] = (box[5] = A1.z);
		GboxMergePoint3d(A2, box);
		if ((A1.x == A2.x && A1.y == A2.y && A1.z == A2.z) || (A1.x == -1.0 * A2.x && A1.y == -1.0 * A2.y && A1.z == -1.0 * A2.z))
		{
			return;
		}
		UnitNormal(A1, A2, point4d5);
		UnitNormal(point4d5, A1, point4d6);
		point4d.x = 1.0;
		point4d.y = 0.0;
		point4d2.x = DotProduct(A2, A1);
		point4d2.y = DotProduct(A2, point4d6);
		Point4d[] array = new Point4d[6];
		for (int i = 0; i < 6; i++)
		{
			array[i] = new Point4d();
		}
		array[0].x = (array[2].y = (array[4].z = 1.0));
		array[1].x = (array[3].y = (array[5].z = -1.0));
		point4d4.x = (point4d4.y = 0.0);
		int num = SegmentSide(point4d, point4d2, point4d4);
		for (int j = 0; j < 6; j++)
		{
			point4d3.x = DotProduct(array[j], A1);
			point4d3.y = DotProduct(array[j], point4d6);
			Normalize2d(point4d3);
			if (SegmentSide(point4d, point4d2, point4d3) != num)
			{
				Point4d point4d7 = new Point4d();
				point4d7.x = point4d3.x * A1.x + point4d3.y * point4d6.x;
				point4d7.y = point4d3.x * A1.y + point4d3.y * point4d6.y;
				point4d7.z = point4d3.x * A1.z + point4d3.y * point4d6.z;
				MergePoint3d(point4d7, box);
			}
		}
	}

	private void GboxMergePoint3d(Point4d p, double[] box)
	{
		box[0] = Math.Min(box[0], p.x);
		box[1] = Math.Max(box[1], p.x);
		box[2] = Math.Min(box[2], p.y);
		box[3] = Math.Max(box[3], p.y);
		box[4] = Math.Min(box[4], p.z);
		box[5] = Math.Max(box[5], p.z);
	}

	private void UnitNormal(Point4d P1, Point4d P2, Point4d normal)
	{
		double num = DotProduct(P1, P2);
		Point4d point4d = new Point4d();
		if (num < 0.0)
		{
			point4d.x = P1.x + P2.x;
			point4d.y = P1.y + P2.y;
			point4d.z = P1.z + P2.z;
			Normalize(point4d);
		}
		else if (num > 0.95)
		{
			point4d.x = P2.x - P1.x;
			point4d.y = P2.y - P1.y;
			point4d.z = P2.z - P1.z;
			Normalize(point4d);
		}
		else
		{
			point4d.x = P2.x;
			point4d.y = P2.y;
			point4d.z = P2.z;
		}
		normal.x = P1.y * point4d.z - P1.z * point4d.y;
		normal.y = P1.z * point4d.x - P1.x * point4d.z;
		normal.z = P1.x * point4d.y - P1.y * point4d.x;
		Normalize(normal);
	}

	private double DotProduct(Point4d p1, Point4d p2)
	{
		return p1.x * p2.x + p1.y * p2.y + p1.z * p2.z;
	}

	private void Normalize(Point4d P3)
	{
		double num = Math.Sqrt(P3.x * P3.x + P3.y * P3.y + P3.z * P3.z);
		if (Math.Abs(num) < 1E-12)
		{
			P3.x = (P3.y = (P3.z = 0.0));
			return;
		}
		P3.x /= num;
		P3.y /= num;
		P3.z /= num;
	}

	private void Normalize2d(Point4d P3)
	{
		double num = Math.Sqrt(P3.x * P3.x + P3.y * P3.y);
		if (Math.Abs(num) < 1E-12)
		{
			P3.x = (P3.y = 0.0);
			return;
		}
		P3.x /= num;
		P3.y /= num;
	}

	private void MergePoint3d(Point4d p, double[] gbox)
	{
		gbox[0] = Math.Min(gbox[0], p.x);
		gbox[1] = Math.Max(gbox[1], p.x);
		gbox[2] = Math.Min(gbox[2], p.y);
		gbox[3] = Math.Max(gbox[3], p.y);
		gbox[4] = Math.Min(gbox[4], p.z);
		gbox[5] = Math.Max(gbox[5], p.z);
	}

	private void PolygonCalculateGboxGeodetic(ByteReader wkbReader, bool hasZ, bool hasM, double[] box)
	{
		double[] array = new double[8];
		double[] array2 = new double[8];
		bool flag = true;
		int num = wkbReader.readInt();
		array[0] = (array[2] = (array[4] = (array[6] = 3.4028234663852886E+38)));
		array[1] = (array[3] = (array[5] = (array[7] = -3.4028234663852886E+38)));
		for (int i = 0; i < num; i++)
		{
			int pointNumber = wkbReader.readInt();
			CalculateGboxGeodetic(wkbReader, hasZ, hasM, pointNumber, array);
			if (flag)
			{
				array2 = Arrays.CopyOfRange(array, 0, array.Length);
				flag = false;
			}
			else
			{
				gbox_merge(array, array2, hasZ: true, hasM);
			}
		}
		GboxCheckPoles(array2);
		for (int j = 0; j < 8; j++)
		{
			box[j] = array2[j];
		}
	}

	private void GboxCheckPoles(double[] gbox)
	{
		if (gbox[0] < 0.0 && gbox[1] > 0.0 && gbox[2] < 0.0 && gbox[3] > 0.0)
		{
			if (gbox[4] > 0.0 && gbox[5] > 0.0)
			{
				gbox[5] = 1.0;
			}
			else if (gbox[4] < 0.0 && gbox[5] < 0.0)
			{
				gbox[4] = -1.0;
			}
			else
			{
				gbox[4] = -1.0;
				gbox[5] = 1.0;
			}
		}
		if (gbox[0] < 0.0 && gbox[1] > 0.0 && gbox[4] < 0.0 && gbox[5] > 0.0)
		{
			if (gbox[2] > 0.0 && gbox[3] > 0.0)
			{
				gbox[3] = 1.0;
			}
			else if (gbox[2] < 0.0 && gbox[3] < 0.0)
			{
				gbox[2] = -1.0;
			}
			else
			{
				gbox[3] = 1.0;
				gbox[2] = -1.0;
			}
		}
		if (gbox[2] < 0.0 && gbox[3] > 0.0 && gbox[4] < 0.0 && gbox[5] > 0.0)
		{
			if (gbox[0] > 0.0 && gbox[1] > 0.0)
			{
				gbox[1] = 1.0;
				return;
			}
			if (gbox[0] < 0.0 && gbox[1] < 0.0)
			{
				gbox[0] = -1.0;
				return;
			}
			gbox[1] = 1.0;
			gbox[0] = -1.0;
		}
	}

	private void CollectionCalculateGboxGeodetic(ByteReader wkbReader, bool hasZ, bool hasM, double[] box)
	{
		double[] array = new double[8];
		double[] array2 = new double[8];
		bool flag = true;
		int num = wkbReader.readInt();
		array[0] = (array[2] = (array[4] = (array[6] = 3.4028234663852886E+38)));
		array[1] = (array[3] = (array[5] = (array[7] = -3.4028234663852886E+38)));
		for (int i = 0; i < num; i++)
		{
			wkbReader.skip(1);
			int num2 = 0;
			if (wkbReader.getReaderByteOrder() == 1)
			{
				num2 = wkbReader.getRepPartValue(2);
				wkbReader.skip(2);
			}
			else
			{
				wkbReader.skip(2);
				num2 = wkbReader.getRepPartValue(2);
			}
			if (num2 > 2000)
			{
				num2 -= 2000;
			}
			if (num2 > 1000)
			{
				num2 -= 1000;
			}
			CalculateBoxGeodetic(wkbReader, num2, hasZ, hasM, array);
			if (flag)
			{
				array2 = Arrays.CopyOfRange(array, 0, array.Length);
				flag = false;
			}
			else
			{
				gbox_merge(array, array2, hasZ: true, hasM);
			}
		}
		for (int j = 0; j < 8; j++)
		{
			box[j] = array2[j];
		}
	}

	private void CalculateBoxCartesian(ByteReader wkbReader, int type, bool hasZ, bool hasM, double[] box)
	{
		switch (type)
		{
		case 1:
		{
			int pointNumber = 1;
			CalculateGboxCartesian(wkbReader, hasZ, hasM, pointNumber, box);
			break;
		}
		case 2:
		{
			int pointNumber = wkbReader.readInt();
			CalculateGboxCartesian(wkbReader, hasZ, hasM, pointNumber, box);
			break;
		}
		case 8:
		{
			int pointNumber = wkbReader.readInt();
			CircstringCalculateGboxCartesian(wkbReader, hasZ, hasM, pointNumber, box);
			break;
		}
		case 3:
		case 17:
		{
			wkbReader.skip(4);
			int pointNumber = wkbReader.readInt();
			CalculateGboxCartesian(wkbReader, hasZ, hasM, pointNumber, box);
			break;
		}
		case 4:
		case 5:
		case 6:
		case 7:
		case 9:
		case 10:
		case 11:
		case 12:
		case 15:
		case 16:
			CollectionCalculateGboxCartesian(wkbReader, hasZ, hasM, box);
			break;
		case 13:
		case 14:
			break;
		}
	}

	private void CircstringCalculateGboxCartesian(ByteReader wkbReader, bool hasZ, bool hasM, int pointNumber, double[] box)
	{
		_ = 2 + (hasZ ? 1 : 0);
		double[] array = new double[8];
		Point4d[] array2 = new Point4d[pointNumber];
		for (int i = 0; i < pointNumber; i++)
		{
			array2[i] = new Point4d();
			GetPoint4d(wkbReader, hasZ, hasM, array2[i]);
		}
		for (int j = 1; j < pointNumber; j += 2)
		{
			Point4d p = array2[j - 1];
			Point4d p2 = array2[j];
			Point4d p3 = array2[j + 1];
			if (ArcCalculateGboxCartesian(p, p2, p3, array, hasZ, hasM) != 0)
			{
				gbox_merge(array, box, hasZ, hasM);
			}
		}
	}

	private void GetPoint4d(ByteReader wkbReader, bool hasZ, bool hasM, Point4d p)
	{
		p.x = wkbReader.ReadDouble();
		p.y = wkbReader.ReadDouble();
		if (hasZ)
		{
			p.z = wkbReader.ReadDouble();
		}
		if (hasM)
		{
			p.m = wkbReader.ReadDouble();
		}
	}

	private int ArcCalculateGboxCartesian(Point4d p1, Point4d p2, Point4d p3, double[] gbox, bool hasZ, bool hasM)
	{
		Point4d point4d = new Point4d();
		Point4d point4d2 = new Point4d();
		Point4d point4d3 = new Point4d();
		Point4d point4d4 = new Point4d();
		Point4d point4d5 = new Point4d();
		double num = ArcCenter(p1, p2, p3, point4d5, hasZ, hasM);
		if (num < 0.0)
		{
			gbox[0] = Math.Min(p1.x, p3.x);
			gbox[1] = Math.Max(p1.x, p3.x);
			gbox[2] = Math.Min(p1.y, p3.y);
			gbox[3] = Math.Max(p1.y, p3.y);
			return 1;
		}
		if (p1.x == p3.x && p1.y == p3.y)
		{
			gbox[0] = point4d5.x - num;
			gbox[1] = point4d5.x + num;
			gbox[2] = point4d5.y - num;
			gbox[3] = point4d5.y + num;
			return 1;
		}
		gbox[0] = Math.Min(p1.x, p3.x);
		gbox[1] = Math.Max(p1.x, p3.x);
		gbox[2] = Math.Min(p1.y, p3.y);
		gbox[3] = Math.Max(p1.y, p3.y);
		point4d.x = point4d5.x - num;
		point4d.y = point4d5.y;
		point4d3.x = point4d5.x;
		point4d3.y = point4d5.y - num;
		point4d2.x = point4d5.x + num;
		point4d2.y = point4d5.y;
		point4d4.x = point4d5.x;
		point4d4.y = point4d5.y + num;
		int num2 = SegmentSide(p1, p3, p2);
		if (num2 == SegmentSide(p1, p3, point4d))
		{
			gbox[0] = point4d.x;
		}
		if (num2 == SegmentSide(p1, p3, point4d2))
		{
			gbox[1] = point4d2.x;
		}
		if (num2 == SegmentSide(p1, p3, point4d3))
		{
			gbox[2] = point4d3.y;
		}
		if (num2 == SegmentSide(p1, p3, point4d4))
		{
			gbox[3] = point4d4.y;
		}
		gbox[4] = Math.Min(p1.z, p3.z);
		gbox[5] = Math.Max(p1.z, p3.z);
		gbox[6] = Math.Min(p1.m, p3.m);
		gbox[7] = Math.Max(p1.m, p3.m);
		return 1;
	}

	private int SegmentSide(Point4d p1, Point4d p2, Point4d q)
	{
		double num = (q.x - p1.x) * (p2.y - p1.y) - (p2.x - p1.x) * (q.y - p1.y);
		if (num > 0.0)
		{
			return 1;
		}
		if (num < 0.0)
		{
			return -1;
		}
		return 0;
	}

	private double ArcCenter(Point4d p1, Point4d p2, Point4d p3, Point4d result, bool hasZ, bool hasM)
	{
		double num;
		double num2;
		if (Math.Abs(p1.x - p3.x) < 1E-08 && Math.Abs(p1.y - p3.y) < 1E-08)
		{
			num = p1.x + (p2.x - p1.x) / 2.0;
			num2 = p1.y + (p2.y - p1.y) / 2.0;
			result.x = num;
			result.y = num2;
			return Math.Sqrt(Math.Pow(num - p1.x, 2.0) + Math.Pow(num2 - p1.y, 2.0));
		}
		double num3 = p2.x - p1.x;
		double num4 = p2.y - p1.y;
		double num5 = p3.x - p1.x;
		double num6 = p3.y - p1.y;
		double num7 = Math.Pow(num3, 2.0) + Math.Pow(num4, 2.0);
		double num8 = Math.Pow(num5, 2.0) + Math.Pow(num6, 2.0);
		double num9 = 2.0 * (num3 * num6 - num5 * num4);
		if (Math.Abs(num9) < 1E-08)
		{
			return -1.0;
		}
		num = p1.x + (num7 * num6 - num8 * num4) / num9;
		num2 = p1.y - (num7 * num5 - num8 * num3) / num9;
		result.x = num;
		result.y = num2;
		return Math.Sqrt(Math.Pow(num - p1.x, 2.0) + Math.Pow(num2 - p1.y, 2.0));
	}

	private void CalculateGboxCartesian(ByteReader wkbReader, bool hasZ, bool hasM, int pointNumber, double[] box)
	{
		int num = 2;
		num += (hasZ ? 1 : 0);
		switch (num + (hasM ? 1 : 0))
		{
		case 2:
		{
			double num8 = wkbReader.ReadDouble();
			double num9 = num8;
			double num10 = wkbReader.ReadDouble();
			double num11 = num10;
			for (int j = 1; j < pointNumber; j++)
			{
				double val4 = wkbReader.ReadDouble();
				double val5 = wkbReader.ReadDouble();
				num8 = Math.Min(val4, num8);
				num9 = Math.Max(val4, num9);
				num10 = Math.Min(val5, num10);
				num11 = Math.Max(val5, num11);
			}
			box[0] = num8;
			box[1] = num9;
			box[2] = num10;
			box[3] = num11;
			return;
		}
		case 3:
		{
			double num2 = wkbReader.ReadDouble();
			double num3 = num2;
			double num4 = wkbReader.ReadDouble();
			double num5 = num4;
			double num6 = wkbReader.ReadDouble();
			double num7 = num6;
			for (int i = 1; i < pointNumber; i++)
			{
				double val = wkbReader.ReadDouble();
				double val2 = wkbReader.ReadDouble();
				double val3 = wkbReader.ReadDouble();
				num2 = Math.Min(val, num2);
				num3 = Math.Max(val, num3);
				num4 = Math.Min(val2, num4);
				num5 = Math.Max(val2, num5);
				num6 = Math.Min(val3, num6);
				num7 = Math.Max(val3, num7);
			}
			box[0] = num2;
			box[1] = num3;
			box[2] = num4;
			box[3] = num5;
			box[4] = num6;
			box[5] = num7;
			return;
		}
		}
		double num12 = wkbReader.ReadDouble();
		double num13 = num12;
		double num14 = wkbReader.ReadDouble();
		double num15 = num14;
		double num16 = wkbReader.ReadDouble();
		double num17 = num16;
		double num18 = wkbReader.ReadDouble();
		double num19 = num18;
		for (int k = 1; k < pointNumber; k++)
		{
			double val6 = wkbReader.ReadDouble();
			double val7 = wkbReader.ReadDouble();
			double val8 = wkbReader.ReadDouble();
			double val9 = wkbReader.ReadDouble();
			num12 = Math.Min(val6, num12);
			num13 = Math.Max(val6, num13);
			num14 = Math.Min(val7, num14);
			num15 = Math.Max(val7, num15);
			num16 = Math.Min(val8, num16);
			num17 = Math.Max(val8, num17);
			num18 = Math.Min(val9, num18);
			num19 = Math.Max(val9, num19);
		}
		box[0] = num12;
		box[1] = num13;
		box[2] = num14;
		box[3] = num15;
		box[4] = num16;
		box[5] = num17;
		box[6] = num18;
		box[7] = num19;
	}

	private void CollectionCalculateGboxCartesian(ByteReader wkbReader, bool hasZ, bool hasM, double[] box)
	{
		int num = wkbReader.readInt();
		bool flag = true;
		double[] array = new double[8];
		double[] array2 = new double[8];
		array2[0] = (array2[2] = (array2[4] = (array2[6] = 3.4028234663852886E+38)));
		array2[1] = (array2[3] = (array2[5] = (array2[7] = -3.4028234663852886E+38)));
		for (int i = 0; i < num; i++)
		{
			wkbReader.skip(1);
			int num2 = 0;
			if (wkbReader.getReaderByteOrder() == 1)
			{
				num2 = wkbReader.getRepPartValue(2);
				wkbReader.skip(2);
			}
			else
			{
				wkbReader.skip(2);
				num2 = wkbReader.getRepPartValue(2);
			}
			if (num2 > 2000)
			{
				num2 -= 2000;
			}
			if (num2 > 1000)
			{
				num2 -= 1000;
			}
			CalculateBoxCartesian(wkbReader, num2, hasZ, hasM, array2);
			if (!array2.Any(double.IsNaN))
			{
				if (flag)
				{
					array = Arrays.CopyOfRange(array2, 0, array2.Length);
					flag = false;
				}
				else
				{
					gbox_merge(array2, array, hasZ, hasM);
				}
			}
		}
		for (int j = 0; j < 8; j++)
		{
			box[j] = array[j];
		}
	}

	private void gbox_merge(double[] newBox, double[] mergeBox, bool hasZ, bool hasM)
	{
		int num = 2;
		num += (hasZ ? 1 : 0);
		num += (hasM ? 1 : 0);
		for (int i = 0; i < 2 * num; i += 2)
		{
			mergeBox[i] = Math.Min(newBox[i], mergeBox[i]);
			mergeBox[i + 1] = Math.Max(newBox[i + 1], mergeBox[i + 1]);
		}
	}

	private byte[] GserAddSize(ByteWriter gserWriter)
	{
		int num = 4 * (gserWriter.Size() + 4);
		int num2 = gserWriter.Size() + 4;
		byte[] array = gserWriter.ToByteArray();
		byte[] array2 = new byte[num2];
		if (gserWriter.GetByteOrder() == 1)
		{
			array2[0] = (byte)(num & 0xFF);
			array2[1] = (byte)((num >> 8) & 0xFF);
			array2[2] = (byte)((num >> 16) & 0xFF);
			array2[3] = (byte)((num >> 24) & 0xFF);
		}
		else
		{
			array2[0] = (byte)((num >> 24) & 0xFF);
			array2[1] = (byte)((num >> 16) & 0xFF);
			array2[2] = (byte)((num >> 8) & 0xFF);
			array2[3] = (byte)(num & 0xFF);
		}
		for (int i = 4; i < num2; i++)
		{
			array2[i] = array[i - 4];
		}
		return array2;
	}

	internal byte[] WkbTogser(byte[] wkb, int srid, string variant, bool isGeog)
	{
		if (wkb == null)
		{
			return null;
		}
		ByteReader byteReader = new ByteReader(wkb);
		ByteWriter byteWriter = new ByteWriter(1);
		int num = byteReader.Read();
		if (num != 0 && num != 1)
		{
			throw new IOException("Unsupported byteorder type");
		}
		byteReader.SetReaderByteOrder(num);
		byteReader.SetOutByteOrder(1);
		bool hasM = false;
		bool hasZ = false;
		bool flag = false;
		bool flag2 = false;
		int num2 = 2;
		int num3 = 64;
		int num4 = 0;
		int num5 = 0;
		int num6 = 0;
		if (variant.Equals("WKB", StringComparison.OrdinalIgnoreCase))
		{
			num6 = byteReader.readInt();
			if (num6 > 2000)
			{
				hasM = true;
				num3 |= 2;
				num2++;
				num6 -= 2000;
			}
			if (num6 > 1000)
			{
				hasZ = true;
				num3 |= 1;
				num2++;
				num6 -= 1000;
			}
			if (isGeog)
			{
				num3 |= 8;
			}
			num4 = num6;
		}
		else
		{
			num5 = byteReader.readInt();
			if ((num5 & int.MinValue) != 0)
			{
				hasZ = true;
				num3 |= 1;
				num2++;
				num5 &= 0x7FFFFFFF;
			}
			if ((num5 & 0x40000000) != 0)
			{
				hasM = true;
				num3 |= 2;
				num2++;
				num5 &= -1073741825;
			}
			if ((num5 & 0x20000000) != 0)
			{
				flag = true;
				num5 &= -536870913;
			}
			if (isGeog)
			{
				num3 |= 8;
			}
			num4 = num5;
		}
		if (flag)
		{
			srid = byteReader.readInt();
		}
		else if (isGeog && srid == -1)
		{
			srid = 4326;
		}
		else if (srid == -1)
		{
			srid = 0;
		}
		byteWriter.WriteSrid(srid);
		byteReader.mark();
		bool num7 = wkbIsEmpty(byteReader, num4, num2);
		byteReader.reset();
		byteReader.mark();
		if (!num7 && GserIsneedBox(byteReader, num4, num2))
		{
			flag2 = true;
			num3 |= 4;
		}
		byteReader.reset();
		byteWriter.Write(num3);
		byteReader.mark();
		if (flag2)
		{
			double[] array = new double[8];
			array[0] = (array[2] = (array[4] = (array[6] = 3.4028234663852886E+38)));
			array[1] = (array[3] = (array[5] = (array[7] = -3.4028234663852886E+38)));
			if (!isGeog)
			{
				CalculateBoxCartesian(byteReader, num4, hasZ, hasM, array);
				for (int i = 0; i < 2 * num2; i++)
				{
					float num8 = 0f;
					double num9 = array[i];
					float num10 = (float)array[i];
					num8 = ((i % 2 != 0) ? ((!((double)num10 >= num9)) ? NextAfter(num10, 3.4028234663852886E+38) : num10) : ((!((double)num10 <= num9)) ? NextAfter(num10, -3.4028234663852886E+38) : num10));
					int value = BitConverter.ToInt32(BitConverter.GetBytes(num8), 0);
					byteWriter.WriteInt(value);
				}
			}
			else
			{
				CalculateBoxGeodetic(byteReader, num4, hasZ, hasM, array);
				for (int j = 0; j < 6; j++)
				{
					float num11 = 0f;
					double num12 = array[j];
					float num13 = (float)array[j];
					num11 = ((j % 2 != 0) ? ((!((double)num13 >= num12)) ? NextAfter(num13, 3.4028234663852886E+38) : num13) : ((!((double)num13 <= num12)) ? NextAfter(num13, -3.4028234663852886E+38) : num13));
					int value2 = BitConverter.ToInt32(BitConverter.GetBytes(num11), 0);
					byteWriter.WriteInt(value2);
				}
			}
		}
		byteReader.reset();
		num4 = num4 switch
		{
			1 => 1, 
			2 => 2, 
			3 => 3, 
			4 => 4, 
			5 => 5, 
			6 => 6, 
			7 => 7, 
			8 => 8, 
			9 => 9, 
			10 => 10, 
			11 => 11, 
			12 => 12, 
			15 => 13, 
			16 => 15, 
			17 => 14, 
			_ => throw new IOException("Unsupported geometry type"), 
		};
		byteWriter.WriteInt(num4);
		switch (num4)
		{
		case 1:
			ParsePointGser(byteReader, byteWriter, num2);
			break;
		case 2:
		case 8:
			ParseLineStringGser(byteReader, byteWriter, num2);
			break;
		case 3:
			ParsePolygonGser(byteReader, byteWriter, num2);
			break;
		case 14:
			ParseTriangleGser(byteReader, byteWriter, num2);
			break;
		case 4:
		case 5:
		case 6:
		case 7:
		case 9:
		case 10:
		case 11:
		case 12:
		case 13:
		case 15:
			ParseCollectionGser(byteReader, byteWriter, num2);
			break;
		default:
			throw new ArgumentException("Unknown Geometry Type!");
		}
		return GserAddSize(byteWriter);
	}

	private static float NextAfter(float start, double direction)
	{
		if (float.IsNaN(start) || double.IsNaN(direction))
		{
			return start + (float)direction;
		}
		if ((double)start == direction)
		{
			return (float)direction;
		}
		int num = BitConverter.ToInt32(BitConverter.GetBytes(start), 0);
		num = ((direction > (double)start) ? (num + ((num >= 0) ? 1 : (-1))) : ((num > 0) ? (num - 1) : ((num >= 0) ? (-2147483647) : (num + 1))));
		return BitConverter.ToSingle(BitConverter.GetBytes(num), 0);
	}

	private void ParsePointGser(ByteReader wkbReader, ByteWriter gserWriter, int dimension)
	{
		bool flag = true;
		wkbReader.mark();
		for (int i = 0; i < dimension; i++)
		{
			if (!Enumerable.SequenceEqual(wkbReader.readPart(8, 1), NAN))
			{
				flag = false;
			}
		}
		if (flag)
		{
			gserWriter.WriteInt(0);
			return;
		}
		wkbReader.reset();
		gserWriter.WriteInt(1);
		PtarrayToWriter(wkbReader, gserWriter, dimension, 1);
	}

	private void ParseLineStringGser(ByteReader wkbReader, ByteWriter gserWriter, int dimension)
	{
		int num = wkbReader.readInt();
		gserWriter.WriteInt(num);
		PtarrayToWriter(wkbReader, gserWriter, dimension, num);
	}

	private static void ParsePolygonGser(ByteReader wkbReader, ByteWriter gserWriter, int dimension)
	{
		int num = wkbReader.readInt();
		gserWriter.WriteInt(num);
		int num2 = 0;
		wkbReader.mark();
		for (int i = 0; i < num; i++)
		{
			num2 = wkbReader.readInt();
			gserWriter.WriteInt(num2);
			wkbReader.skip(num2 * dimension * 8);
		}
		wkbReader.reset();
		if (num % 2 != 0)
		{
			gserWriter.WriteInt(0);
		}
		for (int j = 0; j < num; j++)
		{
			num2 = wkbReader.readInt();
			for (int k = 0; k < num2 * dimension; k++)
			{
				byte[] buf = wkbReader.ReadPart(8);
				gserWriter.WritePart(buf);
			}
		}
	}

	private void ParseTriangleGser(ByteReader wkbReader, ByteWriter gserWriter, int dimension)
	{
		wkbReader.skip(4);
		ParseLineStringGser(wkbReader, gserWriter, dimension);
	}

	private void ParseCollectionGser(ByteReader wkbReader, ByteWriter gserWriter, int dimension)
	{
		int num = wkbReader.readInt();
		gserWriter.WriteInt(num);
		for (int i = 0; i < num; i++)
		{
			wkbReader.skip(1);
			int num2 = 0;
			if (wkbReader.getReaderByteOrder() == 1)
			{
				num2 = wkbReader.getRepPartValue(2);
				wkbReader.skip(2);
			}
			else
			{
				wkbReader.skip(2);
				num2 = wkbReader.getRepPartValue(2);
			}
			if (num2 > 2000)
			{
				num2 -= 2000;
			}
			if (num2 > 1000)
			{
				num2 -= 1000;
			}
			num2 = num2 switch
			{
				1 => 1, 
				2 => 2, 
				3 => 3, 
				4 => 4, 
				5 => 5, 
				6 => 6, 
				7 => 7, 
				8 => 8, 
				9 => 9, 
				10 => 10, 
				11 => 11, 
				12 => 12, 
				15 => 13, 
				16 => 15, 
				17 => 14, 
				_ => throw new IOException("Unsupported geometry type"), 
			};
			gserWriter.WriteInt(num2);
			switch (num2)
			{
			case 1:
				ParsePointGser(wkbReader, gserWriter, dimension);
				break;
			case 2:
			case 8:
				ParseLineStringGser(wkbReader, gserWriter, dimension);
				break;
			case 3:
				ParsePolygonGser(wkbReader, gserWriter, dimension);
				break;
			case 14:
				ParseTriangleGser(wkbReader, gserWriter, dimension);
				break;
			case 4:
			case 5:
			case 6:
			case 7:
			case 9:
			case 10:
			case 11:
			case 12:
			case 13:
			case 15:
				ParseCollectionGser(wkbReader, gserWriter, dimension);
				break;
			}
		}
	}
}
