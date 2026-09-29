using System;
using System.Collections.Generic;
using System.Data;
using System.Data.Common;
using System.IO;
using W.Dm.Config;
using W.Dm.filter.log;

namespace W.Dm.util;

internal static class DriverUtil
{
	public static ILogger log = LogFactory.getLog(typeof(DriverUtil));

	internal const string SQL_GET_DSC_EP_SITE = "SELECT dsc.ep_seqno, (CASE mal.MAL_INST_HOST WHEN '' THEN mal.MAL_HOST ELSE mal.MAL_INST_HOST END) as ep_host, dcr.EP_PORT, dsc.EP_STATUS FROM V$DSC_EP_INFO dsc LEFT join V$DM_MAL_INI mal on dsc.EP_NAME = mal.MAL_INST_NAME LEFT join (SELECT grp.GROUP_TYPE GROUP_TYPE, ep.* FROM SYS.\"V$DCR_GROUP\" grp, SYS.\"V$DCR_EP\" ep where grp.GROUP_NAME = ep.GROUP_NAME) dcr on dsc.EP_NAME = dcr.EP_NAME and GROUP_TYPE = 'DB' order by  dsc.ep_seqno asc;";

	internal static bool isLocalHost(string host)
	{
		if (StringUtil.isEmpty(host))
		{
			return false;
		}
		if ("localhost".Equals(host, StringComparison.OrdinalIgnoreCase) || "127.0.0.1".Equals(host) || "::1".Equals(host))
		{
			return true;
		}
		return false;
	}

	internal static void executeSetSchema(DmConnection conn)
	{
		conn.GetConnInstance().ConnProperty.AutoCommit = true;
		if (conn.ConnProperty.Schema != DmOptionHelper.schemaDef && conn.ConnProperty.Schema != conn.ConnProperty.User)
		{
			if (conn.ConnProperty.SchemaSensitive)
			{
				executeNonQuery(conn, "set schema \"" + conn.ConnProperty.Schema + "\"", null);
			}
			else
			{
				executeNonQuery(conn, "set schema " + conn.ConnProperty.Schema, null);
			}
		}
		conn.GetConnInstance().ConnProperty.ClearAutoCommit();
	}

	internal static void executeNonQuery(DmConnection conn, string sql, DmParameter[] parameters)
	{
		DmCommand dmCommand = conn.CreateCommand(sql);
		dmCommand.do_DbParameterCollection.do_AddRange(parameters);
		dmCommand.do_ExecuteNonQuery();
		dmCommand.Close();
	}

	internal static DmDataReader executeQuery(DmConnection conn, string sql, DmParameter[] parameters)
	{
		DmCommand dmCommand = conn.CreateCommand(sql);
		dmCommand.do_DbParameterCollection.do_AddRange(parameters);
		DmDataReader result = dmCommand.do_ExecuteDbDataReader(CommandBehavior.Default);
		dmCommand.Close();
		return result;
	}

	internal static List<EP> loadDscEpSites(DmConnection connection)
	{
		try
		{
			List<EP> list = new List<EP>();
			DmDataReader dmDataReader = executeQuery(connection, "SELECT dsc.ep_seqno, (CASE mal.MAL_INST_HOST WHEN '' THEN mal.MAL_HOST ELSE mal.MAL_INST_HOST END) as ep_host, dcr.EP_PORT, dsc.EP_STATUS FROM V$DSC_EP_INFO dsc LEFT join V$DM_MAL_INI mal on dsc.EP_NAME = mal.MAL_INST_NAME LEFT join (SELECT grp.GROUP_TYPE GROUP_TYPE, ep.* FROM SYS.\"V$DCR_GROUP\" grp, SYS.\"V$DCR_EP\" ep where grp.GROUP_NAME = ep.GROUP_NAME) dcr on dsc.EP_NAME = dcr.EP_NAME and GROUP_TYPE = 'DB' order by  dsc.ep_seqno asc;", null);
			while (dmDataReader.do_Read())
			{
				EP eP = new EP(dmDataReader.do_GetString(1), dmDataReader.do_GetInt32(2));
				eP.epSeqno = dmDataReader.do_GetInt32(0);
				eP.epStatus = (dmDataReader.do_GetString(3).Equals("OK", StringComparison.OrdinalIgnoreCase) ? 1 : 2);
				list.Add(eP);
			}
			dmDataReader.do_Close();
			return list;
		}
		catch (DbException ex)
		{
			log.Info("Get ep sites failed!" + ex.Message);
		}
		return null;
	}

	public static string FormatDir(string dir)
	{
		dir = StringUtil.trimToEmpty(dir);
		if (StringUtil.isNotEmpty(dir) && !dir.EndsWith(Path.DirectorySeparatorChar.ToString()))
		{
			dir += Path.DirectorySeparatorChar;
		}
		return dir;
	}

	public static int checkCompleteCharLen(byte[] bytes, int offset, int len, string encoding)
	{
		int num = 0;
		int num2 = 0;
		for (int i = offset; i < offset + len; i += num2)
		{
			num2 = calcCharLen(bytes, i, encoding);
			if (num2 <= 0 || num + num2 > len)
			{
				break;
			}
			num += num2;
		}
		return num;
	}

	private static int calcCharLen(byte[] bytes, int offset, string encoding)
	{
		try
		{
			byte b = bytes[offset];
			if (encoding.ToUpper().Equals("GB18030"))
			{
				int num = b & 0xFF;
				if (num <= 128)
				{
					return 1;
				}
				num = bytes[offset + 1] & 0xFF;
				if (num >= 64 && num <= 254 && num != 127)
				{
					return 2;
				}
				return 4;
			}
			if (encoding.ToUpper().Equals("EUCKR"))
			{
				if ((b & 0xFF) <= 128)
				{
					return 1;
				}
				return 2;
			}
			int result = 1;
			if ((b & 0x80) == 0)
			{
				result = 1;
			}
			else if ((b & 0xE0) == 192)
			{
				result = 2;
			}
			else if ((b & 0xF0) == 224)
			{
				result = 3;
			}
			else if ((b & 0xF8) == 240)
			{
				result = 4;
			}
			else if ((b & 0xFC) == 248)
			{
				result = 5;
			}
			else if ((b & 0xFE) == 252)
			{
				result = 6;
			}
			return result;
		}
		catch (Exception)
		{
			return -1;
		}
	}
}
