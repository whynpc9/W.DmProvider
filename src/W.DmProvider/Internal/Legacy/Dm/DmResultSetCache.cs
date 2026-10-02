using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using W.Dm.Internal.Legacy.A;
using W.Dm.Internal.Protocol;

namespace W.Dm;

internal class DmResultSetCache
{
	public global::W.Dm.Internal.Legacy.A.A statement;

	public DmConnInstance connInstance;

	public int colNum;

	public byte[][][] datas;

	public int datasOffset = -1;

	public long datasStartPos;

	public long currentPos = -1L;

	public long totalRowCount = long.MaxValue;

	public bool isRsBdta;

	public short rsBdtaRowidCol;

	public int[] ids;

	public long[] tss;

	public DateTime lastCheckDt;

	public int BytesCount;

	public DmResultSetCache(global::W.Dm.Internal.Legacy.A.A stmt, int colNum, long totalRowCount)
	{
		statement = stmt;
		connInstance = stmt.G();
		this.colNum = colNum;
		this.totalRowCount = totalRowCount;
	}

	public void SetCols(int colNum)
	{
		Reset();
		this.colNum = colNum;
	}

	public bool do_isBeforeFirstNoCheck()
	{
		if (totalRowCount != 0L)
		{
			return currentPos <= -1;
		}
		return false;
	}

	public bool do_isAfterLastNoCheck()
	{
		return currentPos >= totalRowCount;
	}

	protected void checkColumnIndex(int columnIndex)
	{
		if (columnIndex < 0 || columnIndex > colNum - 1)
		{
			DmError.ThrowDmException(DmErrorDefinition.ECNET_INVALID_SEQUENCE_NUMBER);
		}
	}

	protected void checkCurrentRow()
	{
		if (totalRowCount == 0L || do_isBeforeFirstNoCheck() || do_isAfterLastNoCheck())
		{
			DmError.ThrowDmException(DmErrorDefinition.ECNET_READ_NO_DATA);
		}
	}

	public void GetBytes(short columnIndex, ref byte[] data)
	{
		checkCurrentRow();
		checkColumnIndex(columnIndex);
		data = datas[datasOffset][columnIndex + 1];
	}

	public void Reset()
	{
		datas = null;
		colNum = 0;
		datasOffset = -1;
		datasStartPos = 0L;
		currentPos = -1L;
		totalRowCount = long.MaxValue;
	}

	public bool fetchData(long startPos)
	{
		return connInstance.GetCsi().A(statement, this, 0, startPos, long.MaxValue);
	}

	public void checkClosed()
	{
	}

	internal bool do_next()
	{
		checkClosed();
		if (totalRowCount == 0L)
		{
			currentPos++;
			return false;
		}
		if (currentPos >= totalRowCount)
		{
			return false;
		}
		if (currentPos == totalRowCount - 1)
		{
			currentPos++;
			datasOffset++;
			return false;
		}
		if (currentPos + 1 < datasStartPos || currentPos + 1 >= datasStartPos + datas.Length)
		{
			if (fetchData(currentPos + 1))
			{
				currentPos++;
				return true;
			}
			currentPos++;
			datasOffset++;
			return false;
		}
		datasOffset++;
		currentPos++;
		return true;
	}

	internal bool do_previous()
	{
		checkClosed();
		if (currentPos <= -1)
		{
			return false;
		}
		if (currentPos == 0L)
		{
			currentPos--;
			datasOffset--;
			return false;
		}
		if (currentPos - 1 < datasStartPos || currentPos - 1 >= datasStartPos + datas.Length)
		{
			int maxTupleLen = DmColumn.getMaxTupleLen(statement.F().GetColumnsInfo(), connInstance.ConnProperty.MaxRowSize);
			int num = 32640 / maxTupleLen;
			long num2 = currentPos - num / 2;
			if (num2 < 0)
			{
				num2 = 0L;
			}
			fetchData(num2);
			datasOffset = (int)(currentPos - num2) - 1;
			currentPos--;
			return true;
		}
		datasOffset--;
		currentPos--;
		return true;
	}

	internal bool do_absolute(int row)
	{
		checkClosed();
		if (row == 0)
		{
			DmError.ThrowDmException(DmErrorDefinition.ECNET_INVALUID_ROW_NUMBER);
		}
		long num = row;
		if (row < 0)
		{
			if (totalRowCount == long.MaxValue)
			{
				totalRowCount = connInstance.GetCsi().a(this);
			}
			num = totalRowCount + row;
		}
		else
		{
			num--;
		}
		if (num >= datasStartPos && num < datasStartPos + datas.Length)
		{
			currentPos = num;
			datasOffset = (int)(currentPos - datasStartPos);
			return true;
		}
		if (num >= 0 && num < totalRowCount)
		{
			currentPos = num;
			if (fetchData(num))
			{
				datasOffset = 0;
				return true;
			}
			return false;
		}
		if (row > 0)
		{
			currentPos = totalRowCount;
			datasOffset = datas.Length;
		}
		else
		{
			currentPos = -1L;
			datasOffset = -1;
		}
		return false;
	}

	public void do_beforeFirst()
	{
		checkClosed();
		currentPos = -1L;
		datasOffset = -1;
	}

	public void do_afterLast()
	{
		checkClosed();
		if (totalRowCount == long.MaxValue)
		{
			totalRowCount = connInstance.GetCsi().a(this);
		}
		currentPos = totalRowCount;
		datasOffset = ((datas != null) ? datas.Length : 0);
	}

	internal bool do_relative(int rows)
	{
		checkClosed();
		int num = (int)(currentPos + rows + 1);
		if (num < 1)
		{
			do_beforeFirst();
			return false;
		}
		if (num > totalRowCount)
		{
			do_afterLast();
			return false;
		}
		return do_absolute(num);
	}

	internal bool do_last()
	{
		checkClosed();
		if (totalRowCount == long.MaxValue)
		{
			totalRowCount = connInstance.GetCsi().a(this);
		}
		if (totalRowCount != 0L)
		{
			return do_absolute((int)totalRowCount);
		}
		return false;
	}

	internal bool do_first()
	{
		checkClosed();
		return do_absolute(1);
	}

	public void FillRows(long rowPos, int fetchedRows, b msg, bool isRsBdta, short rsBdtaRowidCol)
	{
		if (fetchedRows < 0 || colNum < 0) throw new InvalidDataException("Negative result dimensions.");
		datasStartPos = rowPos;
		this.isRsBdta = isRsBdta;
		this.rsBdtaRowidCol = rsBdtaRowidCol;
		if (fetchedRows <= 0)
		{
			return;
		}
		long num = msg.A(false);
		long num2 = msg.g();
		BytesCount = (int)(num2 - num);
		DmFrameReader.ValidateRows(fetchedRows, colNum, msg.a(false));
		if (!isRsBdta)
		{
			int minimumRow = checked(2 + ((connInstance.ConnProperty.msgVersion < 9) ? 8 : 12) + checked(4 * colNum));
			DmFrameReader.ValidateCount(fetchedRows, minimumRow, msg.a(false));
		}
		datas = new byte[fetchedRows][][];
		for (int i = 0; i < fetchedRows; i++)
		{
			datas[i] = new byte[colNum + 1][];
		}
		if (this.isRsBdta)
		{
			int num3 = 0;
			long num4 = num;
			long decodedValueBytes = 0;
			while (num4 < num2)
			{
				int num5 = (int)msg.E();
				int nflds = msg.D();
				int num6 = (int)msg.E();
				if (num5 <= 0 || num5 > fetchedRows - num3 || num6 <= 0 || num6 > num2 - num4)
					throw new InvalidDataException("Invalid BDTA package dimensions.");
				Bdta.decode(datas, colNum, num3, num5, nflds, msg, rsBdtaRowidCol, ref decodedValueBytes);
				num4 += num6;
				num3 += num5;
			}
			if (num3 != fetchedRows) throw new InvalidDataException("BDTA row count mismatch.");
			return;
		}
		for (int j = 0; j < fetchedRows; j++)
		{
			msg.A(2, false, true);
			datas[j][0] = msg.F((connInstance.ConnProperty.msgVersion < 9) ? 8 : 12);
			msg.A(2 * colNum, false, true);
			for (int k = 1; k < colNum + 1; k++)
			{
				int num7 = msg.D();
				switch (num7)
				{
				case 65533:
				case 65534:
					datas[j][k] = null;
					break;
				default:
					datas[j][k] = msg.F(num7);
					break;
				case 65535:
					datas[j][k] = msg.G();
					break;
				}
			}
		}
	}

	internal long CursorUpdateRow()
	{
		return currentPos;
	}
	internal async Task<bool> do_nextAsync(CancellationToken cancellationToken = default)
	{
		cancellationToken.ThrowIfCancellationRequested();
		checkClosed();
		if (totalRowCount == 0L)
		{
			currentPos++;
			return false;
		}
		if (currentPos >= totalRowCount)
		{
			return false;
		}
		if (currentPos == totalRowCount - 1)
		{
			currentPos++;
			datasOffset++;
			return false;
		}
		if (currentPos + 1 < datasStartPos || datas == null || currentPos + 1 >= datasStartPos + datas.Length)
		{
			if (await connInstance.GetCsi().AAsync(statement, this, 0, currentPos + 1, long.MaxValue, cancellationToken).ConfigureAwait(false))
			{
				currentPos++;
				return true;
			}
			currentPos++;
			datasOffset++;
			return false;
		}
		datasOffset++;
		currentPos++;
		return true;
	}
}
