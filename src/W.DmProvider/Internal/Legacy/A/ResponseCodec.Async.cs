using System;
using System.Collections.Generic;
using System.Data;
using System.Threading;
using System.Threading.Tasks;
using W.Dm;
using W.Dm.Config;
using W.Dm.util;
using W.Dm.Internal.Sessions;
using W.Dm.Internal.Transport;
using W.Dm.Internal.Protocol;
using AsyncProtocol = W.Dm.Internal.Legacy.A.B;
namespace W.Dm.Internal.Legacy.A;
internal partial class c
{
	private static async Task AAsync(DmParameter P_0, b P_1, A P_2, DmParameterInternal P_3, DmGetValue P_4, int P_5, bool P_6, CancellationToken cancellationToken = default)
	{
		cancellationToken = AsyncProtocol.AsyncCancellationToken(cancellationToken);
		object obj = null;
		byte[] array = null;
		int num = P_1.D();
		if ((num == 65534 || num == 65533) && P_0.DmSqlType != DmDbType.Cursor && P_0.DmSqlType != DmDbType.RefCursor)
		{
			P_0.do_Value = DBNull.Value;
			return;
		}
		if (num >= 0 && num != 65534 && num != 65533)
		{
			array = P_1.F(num);
		}
		switch (P_0.DmSqlType)
		{
		case DmDbType.Cursor:
		case DmDbType.RefCursor:
			if (P_3.GetCType() == 120)
			{
				obj = P_3;
				if (P_0.do_Direction == ParameterDirection.ReturnValue)
				{
					await P_2.h().AAsync(P_2.f().RetRefCursorStmt, (short)1, cancellationToken).ConfigureAwait(false);
				}
				else
				{
					await P_2.h().AAsync(P_0.refCursorStmt, (short)1, cancellationToken).ConfigureAwait(false);
				}
			}
			break;
		case DmDbType.Blob:
		case DmDbType.Binary:
		case DmDbType.VarBinary:
			obj = await P_4.GetBytesAsync(P_5, array, P_3.GetCType(), P_3.GetPrecision(), P_3.GetScale(), cancellationToken).ConfigureAwait(false);
			break;
		case DmDbType.Bit:
		{
			bool boolean = P_4.GetBoolean(P_5, array, P_3.GetCType(), P_3.GetPrecision(), P_3.GetScale());
			obj = ((P_2.G().ConnProperty.CompatibleMode != CompatibleMode.MYSQL) ? ((object)boolean) : ((object)(boolean ? 1 : 0)));
			break;
		}
		case DmDbType.Char:
		case DmDbType.Clob:
		case DmDbType.Text:
		case DmDbType.VarChar:
			obj = await P_4.GetStringAsync(P_5, array, P_3.GetCType(), P_3.GetPrecision(), P_3.GetScale(), cancellationToken).ConfigureAwait(false);
			break;
		case DmDbType.Time:
			obj = P_4.GetTime(P_5, array, P_3.GetCType(), P_3.GetPrecision(), P_3.GetScale());
			break;
		case DmDbType.Date:
			obj = P_4.GetDate(P_5, array, P_3.GetCType(), P_3.GetPrecision(), P_3.GetScale());
			break;
		case DmDbType.DateTime:
			obj = P_4.GetTimestamp(P_5, array, P_3.GetCType(), P_3.GetPrecision(), P_3.GetScale());
			break;
		case DmDbType.DateTimeOffset:
			obj = P_4.GetTimestampTZ(P_5, array, P_3.GetCType(), P_3.GetPrecision(), P_3.GetScale());
			break;
		case DmDbType.TimeOffset:
			obj = P_4.GetTimeTZ(P_5, array, P_3.GetCType(), P_3.GetPrecision(), P_3.GetScale());
			break;
		case DmDbType.Decimal:
			obj = P_4.GetBigDecimal(P_5, array, P_3.GetCType(), P_3.GetPrecision(), P_3.GetScale());
			break;
		case DmDbType.XDEC:
			obj = P_4.GetDmDecimal(P_5, array, P_3.GetCType(), P_3.GetPrecision(), P_3.GetScale());
			break;
		case DmDbType.Double:
			obj = P_4.GetDouble(P_5, array, P_3.GetCType(), P_3.GetPrecision(), P_3.GetScale());
			break;
		case DmDbType.Float:
			obj = P_4.GetFloat(P_5, array, P_3.GetCType(), P_3.GetPrecision(), P_3.GetScale());
			break;
		case DmDbType.IntervalDayToSecond:
			obj = P_4.GetINTERVALDT(P_5, array, P_3.GetCType(), P_3.GetPrecision(), P_3.GetScale());
			break;
		case DmDbType.IntervalYearToMonth:
			obj = P_4.GetINTERVALYM(P_5, array, P_3.GetCType(), P_3.GetPrecision(), P_3.GetScale());
			break;
		case DmDbType.Int16:
			obj = P_4.GetShort(P_5, array, P_3.GetCType(), P_3.GetPrecision(), P_3.GetScale());
			break;
		case DmDbType.Int32:
			obj = P_4.GetInt(P_5, array, P_3.GetCType(), P_3.GetPrecision(), P_3.GetScale());
			break;
		case DmDbType.Int64:
			obj = P_4.GetLong(P_5, array, P_3.GetCType(), P_3.GetPrecision(), P_3.GetScale());
			break;
		case DmDbType.SByte:
			obj = P_4.GetSByte(P_5, array, P_3.GetCType(), P_3.GetPrecision(), P_3.GetScale());
			break;
		case DmDbType.Byte:
			obj = P_4.GetByte(P_5, array, P_3.GetCType(), P_3.GetPrecision(), P_3.GetScale());
			break;
		case DmDbType.UInt16:
			obj = P_4.GetUshort(P_5, array, P_3.GetCType(), P_3.GetPrecision(), P_3.GetScale());
			break;
		case DmDbType.UInt32:
			obj = P_4.GetUint(P_5, array, P_3.GetCType(), P_3.GetPrecision(), P_3.GetScale());
			break;
		case DmDbType.UInt64:
			obj = P_4.GetUlong(P_5, array, P_3.GetCType(), P_3.GetPrecision(), P_3.GetScale());
			break;
		case DmDbType.ARRAY:
			obj = P_4.ToComplexType(array, P_3, P_2.G().Conn);
			if (obj is DmArray)
			{
				ComplexTypeData[] arrData = ((DmArray)obj).m_arrData;
				if (arrData == null || arrData.Length == 0)
				{
					obj = null;
					break;
				}
				object[] array2 = new object[arrData.Length];
				for (int i = 0; i < arrData.Length; i++)
				{
					AsyncProtocol.CheckAsyncTermination(cancellationToken);
					array2[i] = arrData[i].m_dumyData;
				}
				obj = array2;
			}
			else if (obj is DmStruct)
			{
				throw new NotSupportedException("GetParamData");
			}
			break;
		default:
			array = P_1.F(num);
			obj = await P_4.GetObjectAsync(P_5, array, P_3.GetCType(), P_3.GetPrecision(), P_3.GetScale(), cancellationToken).ConfigureAwait(false);
			break;
		}
		if (P_6)
		{
			P_0.do_Value = obj;
		}
	}
	private static async Task aAsync(b P_0, A P_1, CancellationToken cancellationToken = default)
	{
		cancellationToken = AsyncProtocol.AsyncCancellationToken(cancellationToken);
		string serverEncoding = P_1.G().ConnProperty.ServerEncoding;
		bool newLobFlag = P_1.G().ConnProperty.NewLobFlag;
		DmField[] paramsInfo = P_1.F().GetParamsInfo();
		DmGetValue dmGetValue = new DmGetValue(serverEncoding, P_1, newLobFlag, paramsInfo);
		DmParameterInternal[] ParamsInfo = null;
		DmParameterInternal dmParameterInternal = null;
		DmParameter dmParameter = null;
		P_1.H().GetParamsInfo(out ParamsInfo);
		bool flag = ArrayUtil.IsAllMatch(P_1.f().Parameters, ParamsInfo);
		bool flag2 = P_1.G().ConnProperty.msgVersion >= 12;
		for (int i = 0; i < P_1.H().GetParameterCount(); i++)
		{
			AsyncProtocol.CheckAsyncTermination(cancellationToken);
			dmParameterInternal = ParamsInfo[i];
			int sqlType = dmParameterInternal.GetSqlType();
			int scale = dmParameterInternal.GetScale();
			if (sqlType == 12 && scale == 5)
			{
				throw new InvalidOperationException("UNSUPPORT USERDEFINED TYPE");
			}
			dmParameter = (flag ? ((DmParameter)P_1.f().Parameters[dmParameterInternal.GetName()]) : ((DmParameter)P_1.f().Parameters[i]));
			if (dmParameter.do_Direction == ParameterDirection.Input)
			{
				continue;
			}
			if (flag2)
			{
				int num = A(P_0.F(4));
				for (int j = 0; j < num; j++)
				{
					AsyncProtocol.CheckAsyncTermination(cancellationToken);
					await AAsync(dmParameter, P_0, P_1, dmParameterInternal, dmGetValue, i, j == 0, cancellationToken).ConfigureAwait(false);
				}
			}
			else
			{
				await AAsync(dmParameter, P_0, P_1, dmParameterInternal, dmGetValue, i, true, cancellationToken).ConfigureAwait(false);
			}
		}
	}
	public static async Task AAsync(b P_0, A P_1, DmConnProperty P_2, short requestOpcode = 0, bool executionVsPrepare = false, CancellationToken cancellationToken = default)
	{
		cancellationToken = AsyncProtocol.AsyncCancellationToken(cancellationToken);
		int num = P_0.L();
		if (num < 0)
		{
            ThrowOwnedStatementServerError(P_0, P_1, P_2, requestOpcode, allowPreservation: true);
		}
		DmInfo dmInfo = new DmInfo(P_1.G());
		dmInfo.SetParaNum(P_1.F().GetParameterCount());
		dmInfo.SetParamsInfo(P_1.F().GetParamsInfo());
		int num2 = P_0.j();
		P_0.k();
		short num3 = P_0.aC();
		short num4 = P_0.ac();
		long num5 = P_0.aD();
		int num6 = P_0.ad();
		byte num7 = P_0.aE();
		int num8 = P_0.ae();
		int num9 = P_0.aF();
		DmRowId rowId = null;
		dmInfo.Execid = P_0.aH();
		int num10 = P_0.ah();
		if (num4 < 0 || num8 < 0 || num9 < 0 || num10 < 0) throw new System.IO.InvalidDataException("Negative result metadata length or count.");
		DmConnInstance dmConnInstance = P_1.G();
		dmConnInstance.trxStatus = P_0.aI();
		dmConnInstance.do_setTrxFinish(dmConnInstance.trxStatus);
		short rsBdtaRowidCol = -1;
		if (num3 == 160 || num3 == 162)
		{
			dmInfo.rsBdta = P_0.aG() == 2;
			rsBdtaRowidCol = P_0.ag();
		}
		else if (P_2.msgVersion < 9 && (num3 == 157 || num3 == 159 || num3 == 158))
		{
			rowId = DmRowId.valueOf(P_0.A(43, 12));
		}
		dmInfo.SetRetStmtType(num3);
		dmInfo.SetOutParamNum(num6);
		if (num3 == 159 || num3 == 158 || num3 == 157 || num3 == 160 || num3 == 162 || num3 == 164)
		{
			dmInfo.SetRowCount(num5);
		}
		else
		{
			dmInfo.SetRowCount(-1L);
		}
		switch (num3)
		{
		case 157:
		case 158:
		case 159:
		case 162:
			dmInfo.SetRecordsAffected(num5);
			break;
		case 160:
			dmInfo.SetRecordsAffected(-1L);
			break;
		}
		if (num7 == 1)
		{
			dmInfo.SetUpdatable(val: true);
		}
		else
		{
			dmInfo.SetUpdatable(val: false);
		}
		if (num9 > 0)
		{
			string printMsg = P_0.A(num9, P_1.G().ConnProperty.ServerEncoding);
			dmInfo.SetPrintMsg(printMsg);
		}
		if (P_2.msgVersion >= 9 && (num3 == 157 || num3 == 159 || num3 == 200))
		{
			rowId = DmRowId.valueOf(P_0.F(12));
		}
		if (num6 > 0)
		{
			await aAsync(P_0, P_1, cancellationToken).ConfigureAwait(false);
		}
		int num11;
		switch (num3)
		{
		case 150:
		case 166:
		{
			short num13 = P_0.C();
			P_0.f(1);
			if (num3 == 166)
			{
				IsolationLevel trxISO = IsolationLevel.ReadCommitted;
				switch (num13)
				{
				case 1:
					trxISO = IsolationLevel.ReadCommitted;
					break;
				case 3:
					trxISO = IsolationLevel.Serializable;
					break;
				case 0:
					trxISO = IsolationLevel.ReadUncommitted;
					break;
				}
				P_1.G().SetTrxISO(trxISO);
				P_1.h().A(true);
			}
			else
			{
				// This is a receipt for one transaction's SET, not the connection's
				// default isolation. The caller validates its expected value and owner.
				dmInfo.RecordTransactionIsolationReceipt(num13, requestOpcode, num,
					DmInvocation.Current?.Identity ?? default);
				if (P_1.f() != null)
				{
					P_1.f().SetStmtSerial(num13);
				}
				if (P_1.G().Transaction != null)
				{
					P_1.G().Transaction.SetStmtSerial(num13);
				}
			}
			break;
		}
		case 165:
			P_2.TimeZone = P_0.C();
			break;
		case 147:
			P_2.Commited = true;
			break;
		case 148:
			P_2.Commited = true;
			P_1.G().RestAllStmt();
			break;
		case 200:
		case 201:
			if (num != 107)
			{
				break;
			}
			foreach (A stmt in P_1.G().Stmts)
			{
				AsyncProtocol.CheckAsyncTermination(cancellationToken);
				if (stmt.g() == num2)
				{
					P_1.__t02_method_060008B0(stmt.l());
					break;
				}
			}
			await P_1.h().AAsync(P_1, P_1.l().statement.F(), cancellationToken).ConfigureAwait(false);
			break;
		case 153:
		{
			int num14 = P_0.d();
			string currentSchema = P_0.A(num14, P_2.ServerEncoding);
			P_2.CurrentSchema = currentSchema;
			break;
		}
		case 160:
			dmInfo.SetHasResultSet(hasResultSet: true);
			goto IL_0474;
		case 162:
			if (num4 > 0 || num8 > 0)
			{
				dmInfo.SetHasResultSet(hasResultSet: true);
			}
			goto IL_0474;
		case 157:
		case 159:
			dmInfo.SetRowId(rowId);
			break;
		case 149:
			P_1.b(P_0.__t02_method_06000ACD(P_2.ServerEncoding));
			break;
		case 251:
			P_2.oracleDateFormat = P_0.a(P_2.ServerEncoding);
			break;
		case 253:
			P_2.oracleTimestampFormat = P_0.a(P_2.ServerEncoding);
			break;
		case 254:
			P_2.oracleTimestampTZFormat = P_0.a(P_2.ServerEncoding);
			break;
		case 252:
			P_2.oracleTimeFormat = P_0.a(P_2.ServerEncoding);
			break;
		case 255:
			P_2.oracleTimeTZFormat = P_0.a(P_2.ServerEncoding);
			break;
		case 256:
			P_2.nlsDateLang = P_0.__t02_method_06000ABD();
			break;
		case 271:
			{
				P_2.formatNumericChars = P_0.A(2, P_2.ServerEncoding);
				break;
			}
			IL_0474:
			if (num4 != 0)
			{
				A(P_0, num4, dmInfo, P_1.G(), P_1.c());
				num11 = dmInfo.GetColumnsInfo().Length;
			}
			else if (P_1.F() != null)
			{
				DmColumn[] columnsInfo = P_1.F().GetColumnsInfo();
				dmInfo.SetColumnsInfo(columnsInfo);
				num11 = ((columnsInfo != null) ? columnsInfo.Length : 0);
			}
			else
			{
				num11 = 0;
			}
			if (P_1.l() == null)
			{
				P_1.__t02_method_060008B0(new DmResultSetCache(P_1, num11, dmInfo.GetRowCount()));
			}
			else
			{
				P_1.l().SetCols(num11);
				P_1.l().totalRowCount = dmInfo.GetRowCount();
			}
			if (num8 > 0)
			{
				DmFrameReader.ValidateRows(num8, num11, P_0.a(false));
				P_1.l().FillRows(0L, num8, P_0, dmInfo.rsBdta, rsBdtaRowidCol);
				if (P_1.G().ConnProperty.EnRsCache && num10 > 0 && dmInfo.GetRowCount() == num8)
				{
					RsKey key = new RsKey(P_1.G().ConnProperty.Guid, P_1.G().ConnProperty.CurrentSchema, P_1.C(), P_1.f().do_DbParameterCollection.Count, P_1.f().do_DbParameterCollection);
					DmConnection.rsLRUCache.Add(key, P_1.l());
				}
			}
			if (num10 > 0)
			{
				P_0.f(num10 - P_0.a());
				short num12 = P_0.C();
				DmFrameReader.ValidateCount(num12, 12, P_0.a(false));
				int[] array = new int[num12];
				long[] array2 = new long[num12];
				for (int i = 0; i < num12; i++)
				{
					AsyncProtocol.CheckAsyncTermination(cancellationToken);
					array[i] = P_0.d();
					array2[i] = P_0.e();
				}
				P_1.l().ids = array;
				P_1.l().tss = array2;
				P_1.l().lastCheckDt = DateTime.Now;
			}
			break;
		}
		if (requestOpcode == 44 && P_0.I() == 0 && P_0.L() == DmErrorDefinition.EC_RESULT_SET_EMPTY &&
			dmInfo.GetRetStmtType() == 0 && dmInfo.GetColumnCount() == 0 &&
			!dmInfo.GetHasResultSet() && dmInfo.GetRowCount() == -1)
			dmInfo.MarkTerminal();
		P_1.__t02_method_06000898(dmInfo);
		if (dmConnInstance.Session.ObserveTransactionResponse(
			DmInvocation.Current?.Identity ?? default, requestOpcode, dmInfo.GetRetStmtType(),
			dmConnInstance.trxStatus, dmInfo.IsTerminal, executionVsPrepare, P_2.ServerVersion))
		{
			dmConnInstance.ClearTrx();
			dmConnInstance.ConnProperty.ClearAutoCommit();
		}
		DmResultProtocolTrace.RecordStatement(dmInfo);
	}
	public static async Task aAsync(b P_0, A P_1, DmConnProperty P_2, CancellationToken cancellationToken = default)
	{
		cancellationToken = AsyncProtocol.AsyncCancellationToken(cancellationToken);
		if (P_0.L() == 111)
		{
			P_1.__t02_method_06000898(new DmInfo(P_1.G()));
		}
		else
		{
			await AAsync(P_0, P_1, P_2, cancellationToken: cancellationToken).ConfigureAwait(false);
		}
	}
}
