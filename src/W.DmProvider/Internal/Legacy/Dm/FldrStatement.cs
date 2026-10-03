using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using W.Dm.Internal.Legacy.A;
using W.Dm.filter.fldr;
using W.Dm.util;
using W.Dm.util.Atomic;

namespace W.Dm;

public class FldrStatement
{
	public class Callback<T>
	{
		public delegate void deletgateRun(T siteObject);

		public deletgateRun run;
	}

	public class Callback2<T>
	{
		public delegate void deletgateRun(T siteObject, int colIndex);

		public deletgateRun run;
	}

	public DmConnection connection;

	public string schemaName;

	public string tableName;

	public string schemaTable;

	private volatile bool firstFlag = true;

	private byte lockFlag;

	private const string SQL_SAMPLE = "SELECT 1 FROM DUAL";

	private TableInfo tableInfo;

	internal Fldr primaryFldr;

	private bool clusterFlag;

	private SetEnvInfo setEnvInfo;

	private int rows;

	private Dictionary<short, string> setIdMap = new Dictionary<short, string>();

	private FldrClusterInfo clusterInfo;

	private CancellationTokenSource clusterExecutorCts = new CancellationTokenSource();

	private CancellationTokenSource bindExecutorCts = new CancellationTokenSource();

	private ConcurrentDictionary<int, Fldr> fldrsMap;

	private CopyOnWriteArrayList<DmConnection> connections;

	private HashSet<Fldr> usedFldr = new HashSet<Fldr>();

	private ConcurrentDictionary<int, Dictionary<short, string>> lastBpIdTabName = new ConcurrentDictionary<int, Dictionary<short, string>>();

	public Dictionary<int, int[]> columnIdScale = new Dictionary<int, int[]>();

	private const string DECIMAL_PRECISION_SCALE_SQL = "SELECT * FROM \"{0}\".\"{1}\" LIMIT 1";

	private const string INVALID_PARAM_FORMAT = "the {0} column {1} row data cannot match to Type {{{2}}}";

	private const string CAST_ERROR_FORMAT = "could not cast to target type, targetSqlType: {0}, serverSqlType: {1}";

	private const string EXCEED_MAX_ROWS_FORMAT = "Binding parameter exceeds maxRows: {{maxRows: {0}, currentRow: {1}, data: {2}}}";

	private const string TIMESTAMP_FORMAT = "{0}-{1}-{2} {3}:{4}:{5}.{6}";

	private volatile bool insertFlag;

	private bool usedAsyncPrep;

	private AtomicBoolean usedAsyncInsert = new AtomicBoolean(initialValue: false);

	private bool stopFlag;

	private int realSeqNo = 1;

	private volatile bool cancelFlag;

	private Dictionary<int, FldrBuffer> batchMap = new Dictionary<int, FldrBuffer>();

	private volatile bool asyncStopFlag;

	private volatile CacheQueue<FldrBuffer> batchQueue = new CacheQueue<FldrBuffer>(60, enableLRU: false);

	private int maxSeqNo = int.MinValue;

	public int maxError = 1;

	private volatile int committedRows;

	public AtomicInteger curErrorNum = new AtomicInteger(0);

	public string logDir;

	private int reconnectTimes = 3;

	private long connInterval = 3000L;

	private AtomicInteger count = new AtomicInteger(1);

	private HashSet<int> defaultColumns = new HashSet<int>();

	private bool firstTimeWithColIndex = true;

	private FldrErrorWriter fldrErrorWriter;

	public object objLock = new object();

	public int irow;

	public int paramCount;

	public object[] curRowDatas;

	public List<object[]> multiRowDatas = new List<object[]>();

	public int getCommittedRows()
	{
		return committedRows;
	}

	public FldrStatement(DmConnection connection, FldrConfig config)
	{
		throw new NotSupportedException("Native bulk loading is unsupported.");
		this.connection = connection;
		setFldrTableInfo(config.schemaName, config.tableName, config.parallelFlag ? 1 : 0, config.indexOption);
		setFldrProperties(config);
		schemaTable = "[DMFLDR]\"" + schemaName + "\".\"" + tableName + "\"";
	}

	public DmDataReader do_executeQuery()
	{
		DmError.ThrowDmException(DmErrorDefinition.ECNET_UNSUPPORED_INTERFACE);
		return null;
	}

	public int do_executeUpdate()
	{
		DmError.ThrowDmException(DmErrorDefinition.ECNET_UNSUPPORED_INTERFACE);
		return -1;
	}

	public void setReconnectTimes(int times)
	{
		reconnectTimes = times;
	}

	public void setConnInterval(long interval)
	{
		if (interval > 0)
		{
			connInterval = interval;
		}
	}

	public int getReconnectTimes()
	{
		return reconnectTimes;
	}

	public void setNull(int parameterIndex, int sqlType)
	{
		try
		{
			setObject(parameterIndex, null);
		}
		catch (Exception ex)
		{
			shutdownExecutor();
			DmError.ThrowDmException(DmErrorDefinition.ECNET_COMMUNITION_ERROR, ex.ToString());
		}
	}

	private void shutdownExecutor()
	{
		clusterExecutorCts.Cancel();
		bindExecutorCts.Cancel();
		if (fldrErrorWriter.isAlive())
		{
			fldrErrorWriter.setStopFlag(stopFlag: true);
		}
	}

	public bool isBlocked()
	{
		if (cancelFlag)
		{
			return false;
		}
		if (tableInfo.containLob && realSeqNo >= count.get() + 1)
		{
			return true;
		}
		if (realSeqNo > count.get() + batchQueue.maxSize)
		{
			return true;
		}
		return batchQueue.maxSize == batchQueue.size();
	}

	public void setBatchQueue(int size)
	{
		batchQueue = new CacheQueue<FldrBuffer>(size, enableLRU: false);
	}

	public void setBoolean(int parameterIndex, bool x)
	{
		try
		{
			int num = (x ? 1 : 0);
			setObject(parameterIndex, num);
		}
		catch (Exception ex)
		{
			shutdownExecutor();
			DmError.ThrowDmException(DmErrorDefinition.ECNET_COMMUNITION_ERROR, ex.ToString());
		}
	}

	public void setByte(int parameterIndex, byte x)
	{
		try
		{
			if (parameterIndex < 1 || parameterIndex > paramCount)
			{
				DmError.ThrowDmException(DmErrorDefinition.ECNET_INVALID_SEQUENCE);
			}
			if (tableInfo.getColumnInfos()[parameterIndex - 1].getColumnType() != 5)
			{
				setObject(parameterIndex, x);
			}
			else
			{
				curRowDatas[parameterIndex - 1] = x;
			}
		}
		catch (Exception ex)
		{
			shutdownExecutor();
			DmError.ThrowDmException(DmErrorDefinition.ECNET_COMMUNITION_ERROR, ex.ToString());
		}
	}

	public void setShort(int parameterIndex, short x)
	{
		try
		{
			if (parameterIndex < 1 || parameterIndex > paramCount)
			{
				DmError.ThrowDmException(DmErrorDefinition.ECNET_INVALID_SEQUENCE);
			}
			ColumnInfo columnInfo = tableInfo.getColumnInfos()[parameterIndex - 1];
			if (columnInfo.getColumnType() != 6 && columnInfo.getColumnType() != 5)
			{
				setObject(parameterIndex, x);
			}
			else
			{
				curRowDatas[parameterIndex - 1] = x;
			}
		}
		catch (Exception ex)
		{
			shutdownExecutor();
			throw ex;
		}
	}

	public void setInt(int parameterIndex, int x)
	{
		try
		{
			if (parameterIndex < 1 || parameterIndex > paramCount)
			{
				DmError.ThrowDmException(DmErrorDefinition.ECNET_INVALID_SEQUENCE);
			}
			if (tableInfo.getColumnInfos()[parameterIndex - 1].getColumnType() != 7)
			{
				setObject(parameterIndex, x);
			}
			else
			{
				curRowDatas[parameterIndex - 1] = x;
			}
		}
		catch (Exception ex)
		{
			shutdownExecutor();
			throw ex;
		}
	}

	public void setINTERVALDT(int parameterIndex, DmIntervalDT x)
	{
		try
		{
			if (parameterIndex < 1 || parameterIndex > paramCount)
			{
				DmError.ThrowDmException(DmErrorDefinition.ECNET_INVALID_SEQUENCE);
			}
			ColumnInfo columnInfo = tableInfo.getColumnInfos()[parameterIndex - 1];
			if (columnInfo.getColumnType() != 21)
			{
				DmError.ThrowDmException(DmErrorDefinition.ECNET_INVALID_PARAMETER_VALUE, $"the {parameterIndex} column {irow} row data cannot match to Type {{{columnInfo.getColumnType()}}}");
			}
			curRowDatas[parameterIndex - 1] = x;
		}
		catch (Exception ex)
		{
			shutdownExecutor();
			DmError.ThrowDmException(DmErrorDefinition.ECNET_COMMUNITION_ERROR, ex.ToString());
		}
	}

	public void setINTERVALYM(int parameterIndex, DmIntervalYM x)
	{
		try
		{
			if (parameterIndex < 1 || parameterIndex > paramCount)
			{
				DmError.ThrowDmException(DmErrorDefinition.ECNET_INVALID_SEQUENCE);
			}
			ColumnInfo columnInfo = tableInfo.getColumnInfos()[parameterIndex - 1];
			if (columnInfo.getColumnType() != 20)
			{
				DmError.ThrowDmException(DmErrorDefinition.ECNET_INVALID_PARAMETER_VALUE, $"the {parameterIndex} column {irow} row data cannot match to Type {{{columnInfo.getColumnType()}}}");
			}
			curRowDatas[parameterIndex - 1] = x;
		}
		catch (Exception ex)
		{
			shutdownExecutor();
			DmError.ThrowDmException(DmErrorDefinition.ECNET_COMMUNITION_ERROR, ex.ToString());
		}
	}

	public void setLong(int parameterIndex, long x)
	{
		try
		{
			if (parameterIndex < 1 || parameterIndex > paramCount)
			{
				DmError.ThrowDmException(DmErrorDefinition.ECNET_INVALID_SEQUENCE);
			}
			if (tableInfo.getColumnInfos()[parameterIndex - 1].getColumnType() != 8)
			{
				setObject(parameterIndex, x);
			}
			else
			{
				curRowDatas[parameterIndex - 1] = x;
			}
		}
		catch (Exception ex)
		{
			shutdownExecutor();
			DmError.ThrowDmException(DmErrorDefinition.ECNET_COMMUNITION_ERROR, ex.ToString());
		}
	}

	public void setFloat(int parameterIndex, float x)
	{
		try
		{
			if (parameterIndex < 1 || parameterIndex > paramCount)
			{
				DmError.ThrowDmException(DmErrorDefinition.ECNET_INVALID_SEQUENCE);
			}
			if (tableInfo.getColumnInfos()[parameterIndex - 1].getColumnType() != 10)
			{
				setObject(parameterIndex, x);
			}
			else
			{
				curRowDatas[parameterIndex - 1] = x;
			}
		}
		catch (Exception ex)
		{
			shutdownExecutor();
			DmError.ThrowDmException(DmErrorDefinition.ECNET_COMMUNITION_ERROR, ex.ToString());
		}
	}

	public void setDouble(int parameterIndex, double x)
	{
		try
		{
			if (parameterIndex < 1 || parameterIndex > paramCount)
			{
				DmError.ThrowDmException(DmErrorDefinition.ECNET_INVALID_SEQUENCE);
			}
			if (tableInfo.getColumnInfos()[parameterIndex - 1].getColumnType() != 11)
			{
				setObject(parameterIndex, x);
			}
			else
			{
				curRowDatas[parameterIndex - 1] = x;
			}
		}
		catch (Exception ex)
		{
			shutdownExecutor();
			DmError.ThrowDmException(DmErrorDefinition.ECNET_COMMUNITION_ERROR, ex.ToString());
		}
	}

	public void setBigDecimal(int parameterIndex, decimal x)
	{
		try
		{
			if (parameterIndex < 1 || parameterIndex > paramCount)
			{
				DmError.ThrowDmException(DmErrorDefinition.ECNET_INVALID_SEQUENCE);
			}
			if (tableInfo.getColumnInfos()[parameterIndex - 1].getColumnType() != 9)
			{
				setObject(parameterIndex, x);
				return;
			}
			int[] array = columnIdScale[parameterIndex - 1];
			if (array == null)
			{
				DmError.ThrowDmException(DmErrorDefinition.ECNET_INVALID_SCALE, "Server could not provide valid precision and scale");
			}
			curRowDatas[parameterIndex - 1] = roundHalfUp(x, array[0], array[1]);
		}
		catch (Exception ex)
		{
			shutdownExecutor();
			DmError.ThrowDmException(DmErrorDefinition.ECNET_COMMUNITION_ERROR, ex.ToString());
		}
	}

	public static decimal roundHalfUp(decimal dec, int prec, int scale)
	{
		if (dec.CompareTo(0m) == 0)
		{
			return 0m;
		}
		if (scale == 0 && prec == 0 && (dec.precision() == 0 || dec.scale() != 0))
		{
			if (dec.precision() + dec.scale() < 38)
			{
				return dec;
			}
			int num = dec.precision() - dec.scale();
			num = ((num % 2 == 0) ? num : (num + 1));
			scale = ((38 - num > 0) ? (38 - num) : scale);
			decimal.Round(dec, scale, MidpointRounding.AwayFromZero);
			dec = dec.setScale(scale);
		}
		if (scale > 0 && dec.scale() > scale)
		{
			dec = dec.setScale(scale);
		}
		return dec;
	}

	public void setString(int parameterIndex, string x)
	{
		try
		{
			if (parameterIndex < 1 || parameterIndex > paramCount)
			{
				DmError.ThrowDmException(DmErrorDefinition.ECNET_INVALID_SEQUENCE);
			}
			ColumnInfo columnInfo = tableInfo.getColumnInfos()[parameterIndex - 1];
			if (columnInfo.getColumnType() == 2 || columnInfo.getColumnType() == 1 || columnInfo.getColumnType() == 0)
			{
				curRowDatas[parameterIndex - 1] = x;
			}
			else if (columnInfo.getColumnType() == 6 || columnInfo.getColumnType() == 5)
			{
				curRowDatas[parameterIndex - 1] = Convert.ToInt16(x);
			}
			else if (columnInfo.getColumnType() == 11)
			{
				curRowDatas[parameterIndex - 1] = Convert.ToDouble(x);
			}
			else if (columnInfo.getColumnType() == 10)
			{
				curRowDatas[parameterIndex - 1] = Convert.ToSingle(x);
			}
			else if (columnInfo.getColumnType() == 9)
			{
				curRowDatas[parameterIndex - 1] = decimal.Parse(x);
			}
			else if (columnInfo.getColumnType() == 14 || columnInfo.getColumnType() == 15 || columnInfo.getColumnType() == 22 || columnInfo.getColumnType() == 16 || columnInfo.getColumnType() == 26 || columnInfo.getColumnType() == 23 || columnInfo.getColumnType() == 27)
			{
				curRowDatas[parameterIndex - 1] = DmDateTime.valueOf(x);
			}
			else if (columnInfo.getColumnType() == 19)
			{
				curRowDatas[parameterIndex - 1] = DmClob.newInstance(x, connection);
			}
			else if (columnInfo.getColumnType() == 12)
			{
				curRowDatas[parameterIndex - 1] = DmBlob.newInstanceOfLocal(ByteUtil.fromString(x, connection.GetConnInstance().ConnProperty.ServerEncoding), connection);
			}
			else if (columnInfo.getColumnType() == 20)
			{
				curRowDatas[parameterIndex - 1] = new DmIntervalYM(x);
			}
			else if (columnInfo.getColumnType() == 21)
			{
				curRowDatas[parameterIndex - 1] = new DmIntervalDT(x);
			}
			else if (columnInfo.getColumnType() == 3)
			{
				curRowDatas[parameterIndex - 1] = Convert.ToInt32(x);
			}
			else if (columnInfo.getColumnType() == 7 || columnInfo.getColumnType() == 8)
			{
				curRowDatas[parameterIndex - 1] = long.Parse(x);
			}
			else if (columnInfo.getColumnType() == 28)
			{
				curRowDatas[parameterIndex - 1] = DmRowId.valueOf(x);
			}
			else
			{
				DmError.ThrowDmException(DmErrorDefinition.ECNET_INVALID_PARAMETER_VALUE, $"the {parameterIndex} column {irow} row data cannot match to Type {{{columnInfo.getColumnType()}}}");
			}
		}
		catch (Exception)
		{
			shutdownExecutor();
			DmError.ThrowDmException(DmErrorDefinition.ECNET_DATA_CONVERTION_ERROR);
		}
	}

	public void setBytes(int parameterIndex, byte[] x)
	{
		try
		{
			if (parameterIndex < 1 || parameterIndex > paramCount)
			{
				DmError.ThrowDmException(DmErrorDefinition.ECNET_INVALID_SEQUENCE);
			}
			ColumnInfo columnInfo = tableInfo.getColumnInfos()[parameterIndex - 1];
			if (columnInfo.getColumnType() != 18 && columnInfo.getColumnType() != 17)
			{
				setObject(parameterIndex, x);
			}
			else
			{
				curRowDatas[parameterIndex - 1] = x;
			}
		}
		catch (Exception ex)
		{
			shutdownExecutor();
			DmError.ThrowDmException(DmErrorDefinition.ECNET_COMMUNITION_ERROR, ex.ToString());
		}
	}

	private void setFldrDate(int parameterIndex, DateTime x)
	{
		try
		{
			if (parameterIndex < 1 || parameterIndex > paramCount)
			{
				DmError.ThrowDmException(DmErrorDefinition.ECNET_INVALID_SEQUENCE);
			}
			curRowDatas[parameterIndex - 1] = x;
		}
		catch (Exception ex)
		{
			shutdownExecutor();
			DmError.ThrowDmException(DmErrorDefinition.ECNET_COMMUNITION_ERROR, ex.ToString());
		}
	}

	public void setDate(int parameterIndex, DateTime x)
	{
		setFldrDate(parameterIndex, x);
	}

	public void setTime(int parameterIndex, DateTime x)
	{
		setFldrDate(parameterIndex, x);
	}

	public void setTimestamp(int parameterIndex, DateTime x)
	{
		setFldrDate(parameterIndex, x);
	}

	public void setTIMESTAMP(int parameterIndex, DateTime x)
	{
		setFldrDate(parameterIndex, x);
	}

	public void setObject(int parameterIndex, object x, int targetSqlType)
	{
		if (parameterIndex < 1 || parameterIndex > paramCount)
		{
			DmError.ThrowDmException(DmErrorDefinition.ECNET_INVALID_SEQUENCE);
		}
		setObject(parameterIndex, x);
	}

	public void setObject(int parameterIndex, object x)
	{
		if (parameterIndex < 1 || parameterIndex > paramCount)
		{
			DmError.ThrowDmException(DmErrorDefinition.ECNET_INVALID_SEQUENCE);
		}
		if (x is DBNull)
		{
			curRowDatas[parameterIndex - 1] = null;
		}
		else if (x is string)
		{
			setString(parameterIndex, (string)x);
		}
		else if (9 == tableInfo.getColumnInfos()[parameterIndex - 1].getColumnType())
		{
			ObjectToDecimal(parameterIndex, x);
		}
		else if (12 == tableInfo.getColumnInfos()[parameterIndex - 1].getColumnType())
		{
			ObjectToBlob(parameterIndex, x);
		}
		else if (19 == tableInfo.getColumnInfos()[parameterIndex - 1].getColumnType())
		{
			if (x is char[] value)
			{
				curRowDatas[parameterIndex - 1] = DmClob.newInstance(new string(value), connection);
			}
			else if (x is StringBuilder stringBuilder)
			{
				curRowDatas[parameterIndex - 1] = DmClob.newInstance(stringBuilder.ToString(), connection);
			}
			else if (x is DmClob clob)
			{
				setClob(parameterIndex, clob);
			}
			else
			{
				DmError.ThrowDmException(DmErrorDefinition.ECNET_DATA_CONVERTION_ERROR, x.ToString());
			}
		}
		else if (20 == tableInfo.getColumnInfos()[parameterIndex - 1].getColumnType())
		{
			if (x is DmIntervalYM)
			{
				curRowDatas[parameterIndex - 1] = x;
			}
			else
			{
				curRowDatas[parameterIndex - 1] = new DmIntervalYM(Convert.ToString(x));
			}
		}
		else if (21 == tableInfo.getColumnInfos()[parameterIndex - 1].getColumnType())
		{
			if (x is DmIntervalDT)
			{
				curRowDatas[parameterIndex - 1] = x;
			}
			else
			{
				curRowDatas[parameterIndex - 1] = new DmIntervalDT(Convert.ToString(x));
			}
		}
		else
		{
			curRowDatas[parameterIndex - 1] = x;
		}
	}

	private void checkColumnIndex(int[] columnIndex)
	{
		for (int i = 0; i < columnIndex.Length; i++)
		{
			if (columnIndex[i] < 0)
			{
				DmError.ThrowDmException(DmErrorDefinition.FLDR_INVALID_COLUMN_INDEX);
			}
		}
	}

	public void setBatchData(object[][] dataArr, int seqNo)
	{
		while (isBlocked())
		{
			if (maxError > 0 && curErrorNum.get() >= maxError)
			{
				shutdownExecutor();
				DmError.ThrowDmException(DmErrorDefinition.FLDR_APPROACH_MAX_ERROR);
			}
			if ((dataArr == null || dataArr.Length == 0) && seqNo != -1)
			{
				stopFlag = true;
				close();
				DmError.ThrowDmException(DmErrorDefinition.ECNET_INVALID_PARAMETER_VALUE);
			}
		}
		if (cancelFlag)
		{
			asyncStopFlag = true;
			return;
		}
		usedAsyncPrep = true;
		if (seqNo == -1)
		{
			asyncColumnDataList(dataArr, seqNo);
			return;
		}
		int num = dataArr.Length / rows + ((dataArr.Length % rows > 0) ? 1 : 0);
		if (num == 1)
		{
			setMaxSeqNo();
			asyncColumnDataList(dataArr, realSeqNo++);
			return;
		}
		for (int i = 0; i < num; i++)
		{
			object[][] array;
			if (i == num - 1)
			{
				array = new object[dataArr.Length - i * rows][];
				for (int j = 0; j < array.Length; j++)
				{
					array[j] = new object[dataArr[0].Length];
				}
				Array.Copy(dataArr, i * rows, array, 0, array.Length);
			}
			else
			{
				array = new object[rows][];
				for (int k = 0; k < array.Length; k++)
				{
					array[k] = new object[dataArr[0].Length];
				}
				Array.Copy(dataArr, i * rows, array, 0, rows);
			}
			setMaxSeqNo();
			asyncColumnDataList(array, realSeqNo++);
		}
	}

	private void setMaxSeqNo()
	{
		maxSeqNo = realSeqNo;
	}

	private void ObjectToBlob(int parameterIndex, object x)
	{
		if (x == null)
		{
			curRowDatas[parameterIndex - 1] = null;
		}
		else if (x is byte[])
		{
			setBlob(parameterIndex, DmBlob.newInstanceOfLocal((byte[])x, connection));
		}
		else if (x is string)
		{
			setBlob(parameterIndex, DmBlob.newInstanceOfLocal(ByteUtil.fromString((string)x, connection.GetConnInstance().ConnProperty.ServerEncoding), connection));
		}
		else if (x is int)
		{
			setBlob(parameterIndex, DmBlob.newInstanceOfLocal(ByteUtil.fromInt((int)x), connection));
		}
		else if (x is long)
		{
			setBlob(parameterIndex, DmBlob.newInstanceOfLocal(ByteUtil.fromLong((long)x), connection));
		}
		else if (x is short)
		{
			setBlob(parameterIndex, DmBlob.newInstanceOfLocal(ByteUtil.fromShort((short)x), connection));
		}
		else if (x is float)
		{
			setBlob(parameterIndex, DmBlob.newInstanceOfLocal(ByteUtil.fromFloat((float)x), connection));
		}
		else if (x is double)
		{
			setBlob(parameterIndex, DmBlob.newInstanceOfLocal(ByteUtil.fromDouble((double)x), connection));
		}
		else if (x is decimal)
		{
			setBlob(parameterIndex, DmBlob.newInstanceOfLocal(((decimal)x).toPlainString().getBytes(connection.GetConnInstance().ConnProperty.ServerEncoding), connection));
		}
		else if (x is DmBlob)
		{
			setBlob(parameterIndex, (DmBlob)x);
		}
		else if (x is DmClob)
		{
			DmClob dmClob = (DmClob)x;
			byte[] bytes = dmClob.MaterializeBytesUnderOwner(connection.GetConnInstance().ConnProperty.ServerEncoding);
			setBlob(parameterIndex, DmBlob.newInstanceOfLocal(bytes, connection));
		}
		else
		{
			setBlob(parameterIndex, DmBlob.newInstanceOfLocal(ByteUtil.fromString(Convert.ToString(x), connection.GetConnInstance().ConnProperty.ServerEncoding), connection));
		}
	}

	private void ObjectToDecimal(int parameterIndex, object x)
	{
		setBigDecimal(parameterIndex, Convert.ToDecimal(x));
	}

	public bool do_execute()
	{
		if (curRowDatas == null || curRowDatas.Length == 0)
		{
			return false;
		}
		List<object[]> list = new List<object[]>();
		list.add(curRowDatas);
		if (tableInfo.gethTableHead() == null)
		{
			tableInfo.hTableNameData.put(tableName, list);
		}
		else
		{
			try
			{
				tableInfo.getHTabPartition(curRowDatas, clusterInfo, connections, fldrsMap, columnIdScale);
			}
			catch (Exception)
			{
				shutdownExecutor();
				DmError.ThrowDmException("Partition mismatched: {currentRow: 1}");
			}
		}
		if (clusterFlag)
		{
			clusterProcess();
		}
		else
		{
			noClusterProcess();
		}
		tableInfo.clearData();
		return true;
	}

	public void addBatch()
	{
		multiRowDatas.Add(curRowDatas);
		irow++;
		if (irow > rows)
		{
			StringBuilder stringBuilder = new StringBuilder();
			object[] array = curRowDatas;
			foreach (object value in array)
			{
				stringBuilder.Append(value).Append(";");
			}
			shutdownExecutor();
			DmError.ThrowDmException(DmErrorDefinition.ECNET_UNBINDED_PARAMETER, $"Binding parameter exceeds maxRows: {{maxRows: {rows}, currentRow: {irow}, data: {stringBuilder}}}");
		}
		curRowDatas = new object[paramCount];
	}

	public void setBlob(int parameterIndex, DmBlob blob)
	{
		try
		{
			if (parameterIndex < 1 || parameterIndex > paramCount)
			{
				DmError.ThrowDmException(DmErrorDefinition.ECNET_INVALID_SEQUENCE);
			}
			curRowDatas[parameterIndex - 1] = blob;
		}
		catch (Exception ex)
		{
			shutdownExecutor();
			DmError.ThrowDmException(DmErrorDefinition.ECNET_COMMUNITION_ERROR, ex.ToString());
		}
	}

	public void setClob(int parameterIndex, DmClob clob)
	{
		try
		{
			if (parameterIndex < 1 || parameterIndex > paramCount)
			{
				DmError.ThrowDmException(DmErrorDefinition.ECNET_INVALID_SEQUENCE);
			}
			curRowDatas[parameterIndex - 1] = clob;
		}
		catch (Exception ex)
		{
			shutdownExecutor();
			DmError.ThrowDmException(DmErrorDefinition.ECNET_COMMUNITION_ERROR, ex.ToString());
		}
	}

	public void setArray(int parameterIndex, Array x)
	{
		DmError.ThrowDmException(DmErrorDefinition.ECNET_UNSUPPORED_INTERFACE);
	}

	internal void setRowId(int parameterIndex, DmRowId x)
	{
		try
		{
			if (parameterIndex < 1 || parameterIndex > paramCount)
			{
				DmError.ThrowDmException(DmErrorDefinition.ECNET_INVALID_SEQUENCE);
			}
			ColumnInfo columnInfo = tableInfo.getColumnInfos()[parameterIndex - 1];
			if (x == null)
			{
				setNull(parameterIndex, -8);
			}
			if (8 == columnInfo.getColumnType())
			{
				setLong(parameterIndex, Convert.ToInt64(x.toString()));
			}
			else if (28 == columnInfo.getColumnType())
			{
				curRowDatas[parameterIndex - 1] = x;
			}
			else
			{
				setObject(parameterIndex, x);
			}
		}
		catch (Exception ex)
		{
			shutdownExecutor();
			DmError.ThrowDmException(DmErrorDefinition.ECNET_COMMUNITION_ERROR, ex.ToString());
		}
	}

	public void setObject(int parameterIndex, object x, int targetSqlType, int scaleOrLength)
	{
		setObject(parameterIndex, x, targetSqlType);
	}

	public DmDataReader do_executeQuery(string sql)
	{
		DmError.ThrowDmException(DmErrorDefinition.ECNET_UNSUPPORED_INTERFACE);
		return null;
	}

	public int do_executeUpdate(string sql)
	{
		DmError.ThrowDmException(DmErrorDefinition.ECNET_UNSUPPORED_INTERFACE);
		return 0;
	}

	public void close()
	{
		try
		{
			if (do_closeAsync())
			{
				while (!asyncStopFlag && ((maxError > 0 && curErrorNum.get() < maxError) || maxError == -1))
				{
				}
			}
		}
		catch (Exception ex)
		{
			Console.WriteLine(ex.StackTrace);
		}
		try
		{
			do_clearParameters();
			if (insertFlag)
			{
				if (!clusterFlag)
				{
					primaryFldr.clearEnvironment(1);
				}
				else
				{
					foreach (Fldr item in usedFldr)
					{
						List<FldrIndexInfo> retIndexInfos = item.clearEnvironment(1);
						restIndexInfo(item, retIndexInfos);
					}
				}
			}
			if (maxError > 0 && curErrorNum.get() >= maxError)
			{
				throw new Exception();
			}
		}
		catch (Exception ex2)
		{
			if (ex2 is DmException ex3)
			{
				throw ex3;
			}
			if (maxError > 0 && curErrorNum.get() >= maxError)
			{
				DmError.ThrowDmException(DmErrorDefinition.FLDR_APPROACH_MAX_ERROR);
			}
			DmError.ThrowDmException(DmErrorDefinition.ECNET_COMMUNITION_ERROR, ex2.ToString());
		}
		finally
		{
			clusterExecutorCts.Cancel();
			bindExecutorCts.Cancel();
			if (fldrErrorWriter.isAlive())
			{
				fldrErrorWriter.setStopFlag(stopFlag: true);
			}
			if (clusterFlag)
			{
				foreach (DmConnection connection in connections)
				{
					connection.do_Close();
				}
			}
		}
	}

	public void do_clearParameters()
	{
		curRowDatas = null;
	}

	private void restIndexInfo(Fldr fldr, List<FldrIndexInfo> retIndexInfos)
	{
		if (!tableInfo.dpcFlag || TableInfo.msgVersion <= 0)
		{
			return;
		}
		if (!retIndexInfos.isEmpty() && !tableInfo.indexInfos.isEmpty())
		{
			foreach (FldrIndexInfo indexInfo in tableInfo.indexInfos)
			{
				foreach (FldrIndexInfo retIndexInfo in retIndexInfos)
				{
					if (retIndexInfo.getIndexId() == indexInfo.getIndexId())
					{
						retIndexInfo.setValidFlag((byte)((retIndexInfo.getValidFlag() > 0 && indexInfo.getValidFlag() > 0) ? 1 : 0));
						break;
					}
				}
			}
		}
		primaryFldr.resetIndexInfo(retIndexInfos);
	}

	public int getMaxFieldSize()
	{
		return tableInfo.getColumnInfos().size();
	}

	public void setMaxFieldSize(int max)
	{
		DmError.ThrowDmException(DmErrorDefinition.ECNET_UNSUPPORED_INTERFACE);
	}

	public int getMaxRows()
	{
		return setEnvInfo.getBldrNumber();
	}

	public void setMaxRows(int max)
	{
		if (max > 0 && max <= 10000)
		{
			setEnvInfo.setBdtaSize(max);
		}
		DmError.ThrowDmException(DmErrorDefinition.EC_INVALID_DB_OBJECT);
	}

	public void setEscapeProcessing(bool enable)
	{
	}

	public void setQueryTimeout(int seconds)
	{
		DmError.ThrowDmException(DmErrorDefinition.ECNET_UNSUPPORED_INTERFACE);
	}

	public void do_cancel()
	{
		DmError.ThrowDmException(DmErrorDefinition.ECNET_UNSUPPORED_INTERFACE);
	}

	public void setCursorName(string name)
	{
		DmError.ThrowDmException(DmErrorDefinition.ECNET_UNSUPPORED_INTERFACE);
	}

	public bool do_execute(string sql)
	{
		DmError.ThrowDmException(DmErrorDefinition.ECNET_UNSUPPORED_INTERFACE);
		return false;
	}

	public DmDataReader getResultSet()
	{
		return null;
	}

	public int getUpdateCount()
	{
		return -1;
	}

	public bool getMoreResults()
	{
		DmError.ThrowDmException(DmErrorDefinition.ECNET_UNSUPPORED_INTERFACE);
		return false;
	}

	public void setFetchDirection(int direction)
	{
		DmError.ThrowDmException(DmErrorDefinition.ECNET_UNSUPPORED_INTERFACE);
	}

	public int getFetchDirection()
	{
		return 0;
	}

	public void setFetchSize(int rows)
	{
		DmError.ThrowDmException(DmErrorDefinition.ECNET_UNSUPPORED_INTERFACE);
	}

	public int getFetchSize()
	{
		return -1;
	}

	public int getResultSetConcurrency()
	{
		return -1;
	}

	public int getResultSetType()
	{
		return -1;
	}

	public void addBatch(string sql)
	{
		DmError.ThrowDmException(DmErrorDefinition.ECNET_UNSUPPORED_INTERFACE);
	}

	public int[] executeBatch()
	{
		if (multiRowDatas.isEmpty())
		{
			DmError.ThrowDmException(DmErrorDefinition.ECNET_NOT_ALLOW_NULL);
		}
		if (tableInfo.gethTableHead() == null)
		{
			tableInfo.hTableNameData.Add(tableName, multiRowDatas);
		}
		else
		{
			for (int i = 0; i < multiRowDatas.size(); i++)
			{
				try
				{
					tableInfo.getHTabPartition(multiRowDatas.get(i), clusterInfo, connections, fldrsMap, columnIdScale);
				}
				catch (Exception ex)
				{
					shutdownExecutor();
					if (ex is DmException)
					{
						throw;
					}
					DmError.ThrowDmException("Partition mismatched: {currentRow: " + (i + 1) + "}");
				}
			}
		}
		if (clusterFlag)
		{
			clusterProcess();
		}
		else
		{
			noClusterProcess();
		}
		tableInfo.clearData();
		do_clearBatch();
		return null;
	}

	public void do_clearBatch()
	{
		irow = 0;
		if (multiRowDatas != null)
		{
			multiRowDatas.clear();
		}
	}

	private void noClusterProcess()
	{
		foreach (KeyValuePair<string, List<object[]>> hTableNameDatum in tableInfo.hTableNameData)
		{
			string key = hTableNameDatum.getKey();
			List<object[]> value = hTableNameDatum.getValue();
			if (value != null && !value.isEmpty())
			{
				usedFldr.add(primaryFldr);
				short setId = primaryFldr.getSetId(schemaName + ":" + key, setEnvInfo.getBldrNumber());
				setFldrEnv(primaryFldr, key, setId);
				List<ColumnData> columnDataList = getColumnDataList(value);
				primaryFldr.insert(columnDataList, tableInfo, value.size(), setId, maxError, bindExecutorCts);
				insertFlag = true;
			}
		}
	}

	private void clusterProcess()
	{
		Dictionary<int, List<KeyValuePair<string, List<object[]>>>> bpInfo = getBpInfo(tableInfo.hTableNameData);
		doTaskOnEverySite(clusterExecutorCts, bpInfo, bpInfo.Count, new Callback<KeyValuePair<int, List<KeyValuePair<string, List<object[]>>>>>
		{
			run = delegate(KeyValuePair<int, List<KeyValuePair<string, List<object[]>>>> entry)
			{
				try
				{
					int key = entry.getKey();
					Fldr fldr = fldrsMap.get(key);
					Dictionary<short, string> dictionary = lastBpIdTabName.get(key);
					List<KeyValuePair<string, List<object[]>>> value = entry.getValue();
					usedFldr.add(fldr);
					SetEnvInfo setEnvInfo = (SetEnvInfo)this.setEnvInfo.clone();
					foreach (KeyValuePair<string, List<object[]>> item in value)
					{
						string key2 = item.getKey();
						List<object[]> value2 = item.getValue();
						short setId = fldr.getSetId(key + ":" + schemaName + ":" + key2, this.setEnvInfo.getBldrNumber());
						string value3 = dictionary.get(setId);
						if (!key2.Equals(value3))
						{
							setEnvInfo.setSetId(setId);
							setEnvInfo.setTableName(key2);
							string text = fldr.setEnvironment(setEnvInfo, defaultColumns, tableInfo.indexInfos);
							if (!text.Equals("SUCCESS"))
							{
								shutdownExecutor();
								throw new Exception(key2 + ": " + text);
							}
							dictionary.put(setId, key2);
							lastBpIdTabName.put(key, dictionary);
						}
						List<ColumnData> columnDataList = getColumnDataList(value2);
						fldr.insert(columnDataList, tableInfo, value2.size(), setId, maxError, bindExecutorCts);
						insertFlag = true;
					}
				}
				catch (Exception ex)
				{
					shutdownExecutor();
					throw ex;
				}
			}
		});
	}

	private Dictionary<int, List<KeyValuePair<string, List<object[]>>>> getBpInfo(Dictionary<string, List<object[]>> hTabData)
	{
		Dictionary<int, List<KeyValuePair<string, List<object[]>>>> dictionary = new Dictionary<int, List<KeyValuePair<string, List<object[]>>>>();
		try
		{
			foreach (KeyValuePair<string, List<object[]>> hTabDatum in hTabData)
			{
				int key2;
				if (tableInfo.subTableNameIdMap != null)
				{
					int key = tableInfo.subTableNameIdMap.get(hTabDatum.getKey());
					key2 = clusterInfo.tabIdToBpIdMap.get(key);
				}
				else
				{
					key2 = clusterInfo.tabIdToBpIdMap.Values.First();
				}
				List<KeyValuePair<string, List<object[]>>> list = dictionary.get(key2);
				if (list == null)
				{
					list = new List<KeyValuePair<string, List<object[]>>>();
				}
				list.add(hTabDatum);
				dictionary.put(key2, list);
				if (lastBpIdTabName.get(key2) == null)
				{
					lastBpIdTabName.put(key2, new Dictionary<short, string>());
				}
			}
			return dictionary;
		}
		catch (Exception)
		{
			shutdownExecutor();
			throw;
		}
	}

	private List<ColumnData> getColumnDataList(List<object[]> rowDataList)
	{
		if (rowDataList == null || rowDataList.isEmpty())
		{
			DmError.ThrowDmException(DmErrorDefinition.ECNET_NOT_ALLOW_NULL);
		}
		List<ColumnData> columnDataList = createColumn(rowDataList.get(0).Length, rowDataList.size());
		doTaskOnEverySite(bindExecutorCts, rowDataList, columnDataList.size(), new Callback2<List<object[]>>
		{
			run = delegate(List<object[]> list, int colIndex)
			{
				ColumnData columnData = columnDataList.get(colIndex);
				for (int i = 0; i < list.size(); i++)
				{
					object o = list.get(i)[colIndex];
					autoIncreaseProcess(colIndex, columnData, i, o);
				}
			}
		});
		return columnDataList;
	}

	private void asyncColumnDataList(object[][] arr, int seqNo)
	{
		FldrTask fldrTask = new FldrTask(this, fldrErrorWriter)
		{
			doTask = delegate
			{
				Callback<object[][]> callback = new Callback<object[][]>();
				callback.run = delegate
				{
					Dictionary<string, List<object[]>> dictionary = null;
					if (seqNo != -1)
					{
						dictionary = tableInfo.getAsyncHTabPartition(arr, columnIdScale, connections, clusterInfo, fldrsMap);
					}
					FldrBuffer fldrBuffer = null;
					if (clusterFlag)
					{
						if (seqNo == -1)
						{
							fldrBuffer = createFldrBuffer(seqNo, arr);
						}
						else
						{
							Dictionary<int, List<KeyValuePair<string, List<object[]>>>> bpInfo = getBpInfo(dictionary);
							fldrBuffer = createFldrBuffer(seqNo, bpInfo, arr);
						}
					}
					else if (dictionary == null || dictionary.isEmpty())
					{
						fldrBuffer = createFldrBuffer(seqNo, arr);
					}
					else
					{
						foreach (KeyValuePair<string, List<object[]>> item in dictionary)
						{
							string key = item.getKey();
							List<object[]> value = item.getValue();
							fldrBuffer = createFldrBuffer(fldrBuffer, seqNo, key, value, arr);
						}
					}
					while (!batchQueue.put(fldrBuffer) && !checkCancel())
					{
					}
				};
				callback.run(arr);
			}
		};
		Task.Factory.StartNew(delegate
		{
			try
			{
				bindExecutorCts.Token.ThrowIfCancellationRequested();
				fldrTask.run();
			}
			catch (Exception)
			{
				bindExecutorCts.Cancel();
				throw;
			}
		});
	}

	private bool checkCancel()
	{
		if (cancelFlag || (maxError > 0 && curErrorNum.get() >= maxError))
		{
			return true;
		}
		return false;
	}

	private FldrBuffer createFldrBuffer(int seqNo, Dictionary<int, List<KeyValuePair<string, List<object[]>>>> bpInfo, object[][] arr)
	{
		if (seqNo == -1)
		{
			return new FldrBuffer(seqNo, null, null, new Dictionary<int, Exception>(0));
		}
		FldrBuffer fldrBuffer = new FldrBuffer();
		fldrBuffer.setSeqNo(seqNo);
		fldrBuffer.sethTabFlag(tableInfo.dpcFlag);
		foreach (KeyValuePair<int, List<KeyValuePair<string, List<object[]>>>> item in bpInfo)
		{
			int key = item.getKey();
			foreach (KeyValuePair<string, List<object[]>> item2 in item.getValue())
			{
				string key2 = item2.getKey();
				List<object[]> value = item2.getValue();
				object[] data = FBHelper(key2, value, arr);
				fldrBuffer.setBpData(key, data);
			}
		}
		return fldrBuffer;
	}

	private FldrBuffer createFldrBuffer(FldrBuffer fldrBuffer, int seqNo, string subTableName, List<object[]> dataList, object[][] arr)
	{
		object[] array = new object[2];
		if (seqNo != -1)
		{
			array = FBHelper(subTableName, dataList, arr);
		}
		if (fldrBuffer != null)
		{
			return fldrBuffer.setTabDetail(subTableName, array);
		}
		return new FldrBuffer(seqNo, subTableName, (b)array[0], (List<LobData>)array[1], (Dictionary<int, Exception>)array[2]);
	}

	private object[] FBHelper(string tableName, List<object[]> dataList, object[][] arr)
	{
		List<ColumnData> list = createColumn(dataList.get(0).Length, dataList.size());
		List<LobData> list2 = Enumerable.Empty<LobData>().ToList();
		for (int i = 0; i < dataList.get(0).Length; i++)
		{
			ColumnData columnData = list.get(i);
			for (int j = 0; j < dataList.size(); j++)
			{
				object o = dataList.get(j)[i];
				autoIncreaseProcess(i, columnData, j, o);
			}
		}
		b b2 = new b();
		object[] array = FLDR_INSERT.batchEncode(b2, this, dataList.size(), (short)list.size(), list, tableInfo, maxError, arr, fldrErrorWriter);
		Dictionary<int, List<Exception>> dictionary = (Dictionary<int, List<Exception>>)array[1];
		int num = curErrorNum.addAndGet((int)array[0]);
		if (maxError <= 0 || num < maxError)
		{
			list2 = FLDR_BLOB.lobProcess(list, tableInfo);
		}
		if (tableInfo.dpcFlag)
		{
			return new object[4] { tableName, b2, list2, dictionary };
		}
		return new object[3] { b2, list2, dictionary };
	}

	private FldrBuffer createFldrBuffer(int seqNo, object[][] arr)
	{
		b buffer = null;
		List<LobData> lobList = null;
		Dictionary<int, Exception> errRowMap = null;
		if (seqNo != -1)
		{
			List<ColumnData> list = createColumn(arr[0].Length, arr.Length);
			for (int i = 0; i < arr[0].Length; i++)
			{
				ColumnData columnData = list.get(i);
				for (int j = 0; j < arr.Length; j++)
				{
					object o = arr[j][i];
					autoIncreaseProcess(i, columnData, j, o);
				}
			}
			buffer = new b();
			object[] array = FLDR_INSERT.batchEncode(buffer, this, arr.Length, (short)list.size(), list, tableInfo, maxError, arr, fldrErrorWriter);
			errRowMap = (Dictionary<int, Exception>)array[1];
			int num = curErrorNum.addAndGet((int)array[0]);
			if (maxError > 0 && num >= maxError)
			{
				asyncCancel();
				DmError.ThrowDmException(DmErrorDefinition.FLDR_APPROACH_MAX_ERROR);
			}
			else
			{
				lobList = FLDR_BLOB.lobProcess(list, tableInfo);
			}
		}
		return new FldrBuffer(seqNo, buffer, lobList, errRowMap);
	}

	private bool autoIncreaseProcess(int columnIndex, ColumnData columnData, int rowIndex, object o)
	{
		if (columnIndex == tableInfo.autoIncrementColId)
		{
			processAutoIncrementColumn(rowIndex, columnData, o);
			return true;
		}
		if (o == null || o is DBNull)
		{
			if (columnData.getSqlType() == 12 || columnData.getSqlType() == 19)
			{
				columnData.getData().add(null);
			}
			columnData.setIsAllNotNull(0);
			return true;
		}
		columnData.getData().add(o);
		columnData.getNullArr()[rowIndex] = 1;
		return false;
	}

	public void asyncCancel()
	{
		lock (objLock)
		{
			if (!cancelFlag)
			{
				cancelFlag = true;
				asyncStopFlag = true;
				close();
			}
		}
	}

	private bool setFldrEnv(Fldr fldr, string curTabName, short setId)
	{
		string value = setIdMap.get(setId);
		if (!curTabName.Equals(value))
		{
			setEnvInfo.setSetId(setId);
			setEnvInfo.setTableName(curTabName);
			string text = fldr.setEnvironment(setEnvInfo, defaultColumns, tableInfo.indexInfos);
			if (!text.Equals("SUCCESS"))
			{
				fldrErrorWriter.writeLines(schemaTable, text, StringUtil.LINE_SEPARATOR);
				DmError.ThrowDmException(curTabName + ": " + text);
				return false;
			}
			setIdMap.put(setId, curTabName);
		}
		return true;
	}

	private List<ColumnData> createColumn(int length, int rows)
	{
		List<ColumnData> list = new List<ColumnData>(length);
		for (int i = 0; i < length; i++)
		{
			ColumnData columnData = new ColumnData();
			columnData.setColumnIndex(i);
			columnData.setSqlType(tableInfo.getColumnInfos().get(i).getColumnType());
			columnData.setIsAllNotNull(1);
			columnData.setNullArr(new byte[rows]);
			columnData.setData(new List<object>());
			list.add(columnData);
		}
		return list;
	}

	private void processAutoIncrementColumn(int index, ColumnData columnData, object o)
	{
		if (setEnvInfo.getSetIdentity() == 0)
		{
			columnData.setIsAllNotNull(0);
			return;
		}
		if (o == null)
		{
			DmError.ThrowDmException(DmErrorDefinition.ECNET_INVALID_PARAMETER_VALUE, $"The auto-increasing number mismatched: {{currentRow: {index}}}");
		}
		columnData.getNullArr()[index] = 1;
		columnData.getData().add(o);
	}

	public bool getMoreResults(int current)
	{
		DmError.ThrowDmException(DmErrorDefinition.ECNET_UNSUPPORED_INTERFACE);
		return false;
	}

	public DmDataReader getGeneratedKeys()
	{
		DmError.ThrowDmException(DmErrorDefinition.ECNET_UNSUPPORED_INTERFACE);
		return null;
	}

	public int do_executeUpdate(string sql, int autoGeneratedKeys)
	{
		DmError.ThrowDmException(DmErrorDefinition.ECNET_UNSUPPORED_INTERFACE);
		return -1;
	}

	public int do_executeUpdate(string sql, int[] columnIndexes)
	{
		DmError.ThrowDmException(DmErrorDefinition.ECNET_UNSUPPORED_INTERFACE);
		return 0;
	}

	public int do_executeUpdate(string sql, string[] columnNames)
	{
		DmError.ThrowDmException(DmErrorDefinition.ECNET_UNSUPPORED_INTERFACE);
		return 0;
	}

	public bool do_execute(string sql, int autoGeneratedKeys)
	{
		DmError.ThrowDmException(DmErrorDefinition.ECNET_UNSUPPORED_INTERFACE);
		return false;
	}

	public bool do_execute(string sql, int[] columnIndexes)
	{
		DmError.ThrowDmException(DmErrorDefinition.ECNET_UNSUPPORED_INTERFACE);
		return false;
	}

	public bool do_execute(string sql, string[] columnNames)
	{
		DmError.ThrowDmException(DmErrorDefinition.ECNET_UNSUPPORED_INTERFACE);
		return false;
	}

	public int getResultSetHoldability()
	{
		return -1;
	}

	public bool do_isPoolable()
	{
		return false;
	}

	public bool do_isCloseOnCompletion()
	{
		return false;
	}

	private string processName(string name)
	{
		if (name.Contains("\"\""))
		{
			StringBuilder stringBuilder = new StringBuilder();
			foreach (char c2 in name)
			{
				if (c2 == '"')
				{
					stringBuilder.Append(c2);
				}
				stringBuilder.Append(c2);
			}
			return stringBuilder.ToString();
		}
		return name;
	}

	private void setFldrTableInfo(string schemaName, string tableName, int parallelFlag, int indexOption)
	{
		lock (objLock)
		{
			if (!firstFlag)
			{
				DmError.ThrowDmException("FldrStatement could not set tableInfo again");
			}
			if (string.IsNullOrEmpty(schemaName))
			{
				DmError.ThrowDmException(DmErrorDefinition.ECNET_NOT_ALLOW_NULL, "Schema name could not be empty");
			}
			if (string.IsNullOrEmpty(tableName))
			{
				DmError.ThrowDmException(DmErrorDefinition.ECNET_NOT_ALLOW_NULL, "Table name could not be empty");
			}
			firstFlag = false;
			this.schemaName = schemaName;
			this.tableName = tableName;
			if (parallelFlag == 0)
			{
				lockFlag = 1;
			}
			clusterFlag = false;
			primaryFldr = connection.getFldrInstance();
			tableInfo = primaryFldr.getTableInfo(this.schemaName, this.tableName, lockFlag);
			if (tableInfo.dpcFlag && TableInfo.msgVersion > 0)
			{
				List<FldrIndexInfo> indexInfo = primaryFldr.getIndexInfo(this.schemaName, this.tableName, parallelFlag, indexOption);
				if (tableInfo.indexInfos.Count == 0)
				{
					tableInfo.indexInfos = indexInfo;
				}
				else
				{
					for (int i = 0; i < tableInfo.indexInfos.Count; i++)
					{
						FldrIndexInfo fldrIndexInfo = tableInfo.indexInfos[i];
						fldrIndexInfo.setValidFlag((byte)((fldrIndexInfo.getValidFlag() > 0 || indexInfo[i].getValidFlag() > 0) ? 1 : 0));
					}
				}
			}
			tableInfo.setConn(connection);
			if (tableInfo.containDecimal || tableInfo.containChar)
			{
				getPrecAndScale(schemaName, tableName);
			}
			if (tableInfo.dpcFlag)
			{
				clusterFlag = true;
				clusterInfo = primaryFldr.getMppInfo(this.schemaName, this.tableName);
				clusterInfo.primaryFldr = primaryFldr;
				fldrsMap = new ConcurrentDictionary<int, Fldr>();
				connections = new CopyOnWriteArrayList<DmConnection>();
				doTaskOnEverySite(clusterExecutorCts, clusterInfo.ipInfoList, clusterInfo.ipInfoList.size(), new Callback<object[]>
				{
					run = delegate(object[] ipInfo)
					{
						try
						{
							DmConnection dmConnection = connection.Clone();
							dmConnection.MppType = DmMppType.LOGIN_MPP_LOCAL;
							dmConnection.ConnProperty.Host = Convert.ToString(ipInfo[1]);
							dmConnection.ConnProperty.Port = Convert.ToInt32(ipInfo[2]);
							dmConnection.ConnProperty.EPGroup = null;
							dmConnection.Open();
							connections.add(dmConnection);
							fldrsMap.put((int)ipInfo[0], dmConnection.getFldrInstance());
						}
						catch (Exception ex)
						{
							shutdownExecutor();
							DmError.ThrowDmException(DmErrorDefinition.ECNET_COMMUNITION_ERROR, ex.ToString());
						}
					}
				});
			}
			paramCount = tableInfo.getNameIdMap().Count;
			curRowDatas = new object[paramCount];
		}
	}

	public List<ColumnInfo> getTableColumnInfo()
	{
		return tableInfo.getColumnInfos();
	}

	private void getPrecAndScale(string schemaName, string tableName)
	{
		schemaName = processName(schemaName);
		tableName = processName(tableName);
		List<ColumnInfo> columnInfos = tableInfo.getColumnInfos();
		DmDataReader dmDataReader = DriverUtil.executeQuery(connection, $"SELECT * FROM \"{schemaName}\".\"{tableName}\" LIMIT 1", null);
		DmdbResultSetMetaData metaData = dmDataReader.getMetaData();
		foreach (ColumnInfo item in columnInfos)
		{
			if (item.getColumnId() == -1)
			{
				continue;
			}
			int num = metaData.do_getPrecision(item.getColumnId() + 1);
			int num2 = metaData.do_getScale(item.getColumnId() + 1);
			string text = metaData.do_getColumnTypeName(item.getColumnId() + 1);
			if (9 == item.getColumnType())
			{
				columnIdScale.Add(item.getColumnId(), new int[2] { num, num2 });
			}
			else if (item.getColumnType() <= 2)
			{
				if (text.ToUpper().StartsWith("NVARCHAR"))
				{
					item.setColumnLen((short)num2);
				}
				else
				{
					item.setColumnLen((short)num);
				}
			}
		}
		dmDataReader.do_Close();
	}

	private bool do_closeAsync()
	{
		if (usedAsyncPrep)
		{
			if (usedAsyncInsert.compareAndSet(expect: true, update: false))
			{
				setBatchData(null, -1);
				usedAsyncPrep = false;
				return true;
			}
			return false;
		}
		if (usedAsyncInsert.compareAndSet(expect: true, update: false))
		{
			setBatchData(null, -1);
			return true;
		}
		return false;
	}

	private void setFldrProperties(FldrConfig config)
	{
		if (config == null)
		{
			DmError.ThrowDmException(DmErrorDefinition.ECNET_NOT_ALLOW_NULL, "FldrConfig could not be null");
		}
		setEnvInfo = new SetEnvInfo();
		setEnvInfo.setSetIdentity(config.setIdentity ? 1 : 0);
		setEnvInfo.setSorted(config.sorted ? 1 : 0);
		setEnvInfo.setBdtaSize(config.maxRows);
		setEnvInfo.setIndexOption(config.indexOption);
		setEnvInfo.setCharset(connection.GetConnInstance().ConnProperty.ServerEncoding);
		setEnvInfo.setIgnoreConflict(0);
		setEnvInfo.setBldrNumber(config.bldrNum);
		setEnvInfo.setFlushFlag(config.flushFlag ? ((byte)1) : ((byte)0));
		setEnvInfo.setSchemaName(schemaName);
		setEnvInfo.setTableName(tableName);
		setEnvInfo.setParallelFlag(config.parallelFlag ? ((byte)1) : ((byte)0));
		rows = config.maxRows;
		maxError = ((config.maxErrorNum < -1 || config.maxErrorNum == 0) ? 1 : config.maxErrorNum);
		logDir = config.logFileName;
		fldrErrorWriter = FldrErrorWriter.getInstance(maxError, logDir);
		if (config.defualtColumns != null)
		{
			defaultColumns = config.defualtColumns;
		}
		setEnvInfo.setMsgVersion(TableInfo.msgVersion);
	}

	private void setFldrProperties(Dictionary<string, string> configMap)
	{
		if (configMap == null || configMap.isEmpty())
		{
			DmError.ThrowDmException(DmErrorDefinition.ECNET_NOT_ALLOW_NULL, "FldrConfigMap could not be null");
		}
		setEnvInfo = new SetEnvInfo();
		string text = configMap["schemaName"];
		if (text == null || text.isEmpty())
		{
			DmError.ThrowDmException(DmErrorDefinition.ECNET_SCHNAME_EMPTYSTRING);
		}
		else
		{
			schemaName = text;
			setEnvInfo.setSchemaName(schemaName);
		}
		string text2 = configMap["tableName"];
		if (text2 == null || text2.isEmpty())
		{
			DmError.ThrowDmException(DmErrorDefinition.ECNET_TABNAME_NULL);
		}
		else
		{
			tableName = text2;
			setEnvInfo.setTableName(tableName);
		}
		string text3 = configMap["setIdentity"];
		if (text3 == null || text3.isEmpty())
		{
			setEnvInfo.setSetIdentity(0);
		}
		else
		{
			setEnvInfo.setSetIdentity(text3.Equals("1") ? 1 : 0);
		}
		string text4 = configMap["sorted"];
		if (text4 == null || text4.isEmpty())
		{
			setEnvInfo.setSorted(0);
		}
		else
		{
			setEnvInfo.setSorted(text4.Equals("1") ? 1 : 0);
		}
		string text5 = configMap["maxRows"];
		if (text5 == null || text5.isEmpty())
		{
			setEnvInfo.setBdtaSize(5000);
		}
		else
		{
			setEnvInfo.setBdtaSize(Convert.ToInt32(text5));
		}
		string text6 = configMap["indexOption"];
		if (text6 == null || text6.isEmpty())
		{
			setEnvInfo.setIndexOption(1);
		}
		else
		{
			setEnvInfo.setIndexOption((Convert.ToInt32(text6) == 1) ? 1 : 1);
		}
		setEnvInfo.setCharset(connection.GetConnInstance().ConnProperty.ServerEncoding);
		setEnvInfo.setIgnoreConflict(0);
		string text7 = configMap["bldrNum"];
		if (text7 == null || text7.isEmpty())
		{
			setEnvInfo.setBldrNumber(64);
		}
		else
		{
			setEnvInfo.setBldrNumber(Convert.ToInt16(text7));
		}
		string text8 = configMap["flushFlag"];
		if (text8 == null || text8.isEmpty())
		{
			setEnvInfo.setFlushFlag(0);
		}
		else
		{
			setEnvInfo.setFlushFlag(text8.Equals("1") ? ((byte)1) : ((byte)0));
		}
		string text9 = configMap["parallelFlag"];
		if (text9 == null || text9.isEmpty())
		{
			setEnvInfo.setParallelFlag(0);
		}
		else
		{
			setEnvInfo.setParallelFlag(text9.Equals("1") ? ((byte)1) : ((byte)0));
		}
		string text10 = configMap["maxErrorNum"];
		if (text10 == null || text10.isEmpty())
		{
			maxError = 1;
		}
		else
		{
			maxError = Convert.ToInt32(text10);
			maxError = ((maxError < -1 || maxError == 0) ? 1 : maxError);
		}
		rows = setEnvInfo.getBdtaSize();
		logDir = configMap["logFileName"];
		fldrErrorWriter = FldrErrorWriter.getInstance(maxError, logDir);
		string str = configMap["defaultValue"];
		if (StringUtil.isNotEmpty(str))
		{
			string[] array = str.split(";");
			for (int i = 0; i < array.Length; i++)
			{
				defaultColumns.add(Convert.ToInt32(array[i]));
			}
		}
		setEnvInfo.setMsgVersion(TableInfo.msgVersion);
	}

	public static void doTaskOnEverySite<T>(CancellationTokenSource executorCts, List<T> list, int siteCount, Callback<T> callback)
	{
		if (siteCount == 0 || executorCts == null)
		{
			return;
		}
		CountdownEvent countDownLatch = new CountdownEvent(siteCount);
		FldrTask[] fldrTasks = new FldrTask[siteCount];
		for (int i = 0; i < list.Count; i++)
		{
			int num = i;
			T siteObj = list[num];
			fldrTasks[num] = new FldrTask(countDownLatch)
			{
				doTask = delegate
				{
					callback.run(siteObj);
				}
			};
		}
		for (int num2 = 0; num2 < list.Count; num2++)
		{
			int index = num2;
			Task.Factory.StartNew(delegate
			{
				try
				{
					executorCts.Token.ThrowIfCancellationRequested();
					fldrTasks[index].run();
				}
				catch (Exception)
				{
					executorCts.Cancel();
					throw;
				}
			});
		}
		threadCountDownHelper(countDownLatch, fldrTasks);
	}

	public static void doTaskOnEverySite<K, V>(CancellationTokenSource executorCts, Dictionary<K, V> list, int siteCount, Callback<KeyValuePair<K, V>> callback)
	{
		if (siteCount == 0 || executorCts == null)
		{
			return;
		}
		CountdownEvent countDownLatch = new CountdownEvent(siteCount);
		FldrTask[] fldrTasks = new FldrTask[siteCount];
		int num = 0;
		foreach (KeyValuePair<K, V> siteObj in list)
		{
			fldrTasks[num] = new FldrTask(countDownLatch)
			{
				doTask = delegate
				{
					callback.run(siteObj);
				}
			};
			num++;
		}
		for (int num2 = 0; num2 < list.Count; num2++)
		{
			int index = num2;
			Task.Factory.StartNew(delegate
			{
				try
				{
					executorCts.Token.ThrowIfCancellationRequested();
					fldrTasks[index].run();
				}
				catch (Exception)
				{
					executorCts.Cancel();
					throw;
				}
			});
		}
		threadCountDownHelper(countDownLatch, fldrTasks);
	}

	public static void doTaskOnEverySite<T>(CancellationTokenSource executorCts, T list, int columnSize, Callback2<T> callback2)
	{
		if (columnSize == 0 || executorCts == null)
		{
			return;
		}
		CountdownEvent countDownLatch = new CountdownEvent(columnSize);
		FldrTask[] fldrTasks = new FldrTask[columnSize];
		for (int i = 0; i < columnSize; i++)
		{
			int colIndex = i;
			fldrTasks[colIndex] = new FldrTask(countDownLatch)
			{
				doTask = delegate
				{
					callback2.run(list, colIndex);
				}
			};
		}
		for (int num = 0; num < columnSize; num++)
		{
			int index = num;
			Task.Factory.StartNew(delegate
			{
				try
				{
					executorCts.Token.ThrowIfCancellationRequested();
					fldrTasks[index].run();
				}
				catch (Exception)
				{
					executorCts.Cancel();
					throw;
				}
			});
		}
		threadCountDownHelper(countDownLatch, fldrTasks);
	}

	public static void threadCountDownHelper(CountdownEvent countDownLatch, FldrTask[] tasks)
	{
		try
		{
			countDownLatch.Wait();
		}
		catch (Exception)
		{
		}
		foreach (FldrTask fldrTask in tasks)
		{
			if (!fldrTask.isSuccess())
			{
				if (fldrTask.getError() != null)
				{
					throw fldrTask.getError();
				}
				break;
			}
		}
	}

	public void Commit()
	{
		if (clusterFlag)
		{
			foreach (Fldr item in usedFldr)
			{
				item.dbAccess.e();
			}
			return;
		}
		primaryFldr.dbAccess.e();
	}
}
