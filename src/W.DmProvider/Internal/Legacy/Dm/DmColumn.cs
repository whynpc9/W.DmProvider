namespace W.Dm;

internal class DmColumn : DmField
{
	internal DmColumn(DmConnInstance conn)
		: base(conn)
	{
	}

	public static int getMaxTupleLen(DmColumn[] cols, int maxRowSize)
	{
		int num = 0;
		for (short num2 = 0; num2 < cols.Length; num2++)
		{
			int num3 = cols[num2].type;
			if (num3 == 12 || num3 == 19 || DmSqlType.isComplexType(num3, cols[num2].scale))
			{
				num = maxRowSize;
				break;
			}
			num += cols[num2].prec;
		}
		if (num > maxRowSize)
		{
			num = maxRowSize;
		}
		return num + (10 + 2 * cols.Length + 8);
	}
}
