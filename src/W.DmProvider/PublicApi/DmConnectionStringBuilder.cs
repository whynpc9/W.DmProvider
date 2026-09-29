using System;
using System.Collections;
using System.Collections.Generic;
using System.Data.Common;
using System.Threading;
using W.Dm.Config;
using W.Dm.filter;
using W.Dm.util;

namespace W.Dm;

public class DmConnectionStringBuilder : DbConnectionStringBuilder, IFilterInfo
{
	internal long id = -1L;

	internal static long idGenerator;

	private static List<DmOption> options;

	internal Dictionary<string, object> property = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);

	internal Dictionary<string, object> setProperty = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);

	public long ID
	{
		get
		{
			if (id < 0)
			{
				id = Interlocked.Increment(ref idGenerator);
			}
			return id;
		}
	}

	public BaseFilter filterHead { get; set; }

	public LogInfo LogInfo { get; set; }

	public RWInfo RWInfo { get; set; }

	public RecoverInfo RecoverInfo { get; set; }

	public string Server
	{
		get
		{
			return do_getThis(DmConst.PROP_KEY_SERVER).ToString();
		}
		set
		{
			do_setThis(DmConst.PROP_KEY_SERVER, value);
		}
	}

	public string User
	{
		get
		{
			return do_getThis(DmConst.PROP_KEY_USER).ToString();
		}
		set
		{
			do_setThis(DmConst.PROP_KEY_USER, value);
		}
	}

	public string Password
	{
		get
		{
			return do_getThis(DmConst.PROP_KEY_PASSWORD).ToString();
		}
		set
		{
			do_setThis(DmConst.PROP_KEY_PASSWORD, value);
		}
	}

	public int Port
	{
		get
		{
			return Convert.ToInt32(do_getThis(DmConst.PROP_KEY_PORT));
		}
		set
		{
			do_setThis(DmConst.PROP_KEY_PORT, value);
		}
	}

	public string Encoding
	{
		get
		{
			return Convert.ToString(do_getThis(DmConst.PROP_KEY_ENCODING));
		}
		set
		{
			do_setThis(DmConst.PROP_KEY_ENCODING, value);
		}
	}

	public bool Enlist
	{
		get
		{
			return Convert.ToBoolean(do_getThis(DmConst.PROP_KEY_ENLIST));
		}
		set
		{
			do_setThis(DmConst.PROP_KEY_ENLIST, value);
		}
	}

	public int ConnectionTimeout
	{
		get
		{
			return Convert.ToInt32(do_getThis(DmConst.PROP_KEY_CONNECTION_TIMEOUT));
		}
		set
		{
			do_setThis(DmConst.PROP_KEY_CONNECTION_TIMEOUT, value);
		}
	}

	public int CommandTimeout
	{
		get
		{
			return Convert.ToInt32(do_getThis(DmConst.PROP_KEY_COMMAND_TIMEOUT));
		}
		set
		{
			do_setThis(DmConst.PROP_KEY_COMMAND_TIMEOUT, value);
		}
	}

	public int StmtPoolSize
	{
		get
		{
			return Convert.ToInt32(do_getThis(DmConst.PROP_KEY_STMT_POOL_SIZE));
		}
		set
		{
			do_setThis(DmConst.PROP_KEY_STMT_POOL_SIZE, value);
		}
	}

	public bool StmtPooling
	{
		get
		{
			return Convert.ToBoolean(do_getThis(DmConst.PROP_KEY_STMT_POOLING));
		}
		set
		{
			do_setThis(DmConst.PROP_KEY_STMT_POOLING, value);
		}
	}

	public bool PreparePooling
	{
		get
		{
			return Convert.ToBoolean(do_getThis(DmConst.PROP_KEY_PSTMT_POOLING));
		}
		set
		{
			do_setThis(DmConst.PROP_KEY_PSTMT_POOLING, value);
		}
	}

	public int PreparePoolSize
	{
		get
		{
			return Convert.ToInt32(do_getThis(DmConst.PROP_KEY_PSTMT_POOL_SIZE));
		}
		set
		{
			do_setThis(DmConst.PROP_KEY_PSTMT_POOL_SIZE, value);
		}
	}

	public bool ConnPooling
	{
		get
		{
			return Convert.ToBoolean(do_getThis(DmConst.PROP_KEY_CONN_POOLING));
		}
		set
		{
			do_setThis(DmConst.PROP_KEY_CONN_POOLING, value);
		}
	}

	public int ConnPoolSize
	{
		get
		{
			return Convert.ToInt32(do_getThis(DmConst.PROP_KEY_CONN_POOL_SIZE));
		}
		set
		{
			do_setThis(DmConst.PROP_KEY_CONN_POOL_SIZE, value);
		}
	}

	public bool ConnPoolCheck
	{
		get
		{
			return Convert.ToBoolean(do_getThis(DmConst.PROP_KEY_CONN_POOL_CHECK));
		}
		set
		{
			do_setThis(DmConst.PROP_KEY_CONN_POOL_CHECK, value);
		}
	}

	public int ConnPoolTimeout
	{
		get
		{
			return Convert.ToInt32(do_getThis(DmConst.PROP_KEY_CONN_POOL_TIMEOUT));
		}
		set
		{
			do_setThis(DmConst.PROP_KEY_CONN_POOL_TIMEOUT, value);
		}
	}

	public int ConnPoolIdleExpiredTime
	{
		get
		{
			return Convert.ToInt32(do_getThis(DmConst.PROP_KEY_CONN_POOL_IDLE_EXPIRED_TIME));
		}
		set
		{
			do_setThis(DmConst.PROP_KEY_CONN_POOL_IDLE_EXPIRED_TIME, value);
		}
	}

	public int ConnPoolIdleClearInterval
	{
		get
		{
			return Convert.ToInt32(do_getThis(DmConst.PROP_KEY_CONN_POOL_IDLE_CLEAR_INTERVAL));
		}
		set
		{
			do_setThis(DmConst.PROP_KEY_CONN_POOL_IDLE_CLEAR_INTERVAL, value);
		}
	}

	public bool EscapeProcess
	{
		get
		{
			return Convert.ToBoolean(do_getThis(DmConst.PROP_KEY_ESCAPE_PROCESS));
		}
		set
		{
			do_setThis(DmConst.PROP_KEY_ESCAPE_PROCESS, value);
		}
	}

	public short Time_Zone
	{
		get
		{
			return Convert.ToInt16(do_getThis(DmConst.PROP_KEY_TIMEZONE));
		}
		set
		{
			do_setThis(DmConst.PROP_KEY_TIMEZONE, value);
		}
	}

	public int Language
	{
		get
		{
			return Convert.ToInt32(do_getThis(DmConst.PROP_KEY_LANGUAGE));
		}
		set
		{
			do_setThis(DmConst.PROP_KEY_LANGUAGE, value);
		}
	}

	public string[] Keywords
	{
		get
		{
			return do_getThis(DmConst.PROP_KEY_KEYWORDS) as string[];
		}
		set
		{
			do_setThis(DmConst.PROP_KEY_KEYWORDS, value);
		}
	}

	public LoginModeFlag LoginMode
	{
		get
		{
			return (LoginModeFlag)do_getThis(DmConst.PROP_KEY_LOGIN_MODE);
		}
		set
		{
			do_setThis(DmConst.PROP_KEY_LOGIN_MODE, value);
		}
	}

	public string Schema
	{
		get
		{
			return Convert.ToString(do_getThis(DmConst.PROP_KEY_SCHEMA));
		}
		set
		{
			do_setThis(DmConst.PROP_KEY_SCHEMA, value);
		}
	}

	public bool SchemaSensitive
	{
		get
		{
			return Convert.ToBoolean(do_getThis(DmConst.PROP_KEY_SCHEMA_SENSITIVE));
		}
		set
		{
			do_setThis(DmConst.PROP_KEY_SCHEMA_SENSITIVE, value);
		}
	}

	public string AppName
	{
		get
		{
			return Convert.ToString(do_getThis(DmConst.PROP_KEY_APPNAME));
		}
		set
		{
			do_setThis(DmConst.PROP_KEY_APPNAME, value);
		}
	}

	public string Database
	{
		get
		{
			return Convert.ToString(do_getThis(DmConst.PROP_KEY_DATABASE));
		}
		set
		{
			do_setThis(DmConst.PROP_KEY_DATABASE, value);
		}
	}

	public string Host
	{
		get
		{
			return Convert.ToString(do_getThis(DmConst.PROP_KEY_HOST));
		}
		set
		{
			do_setThis(DmConst.PROP_KEY_HOST, value);
		}
	}

	public string OS
	{
		get
		{
			return Convert.ToString(do_getThis(DmConst.PROP_KEY_OS));
		}
		set
		{
			do_setThis(DmConst.PROP_KEY_OS, value);
		}
	}

	public string SvcConfPath
	{
		get
		{
			return Convert.ToString(do_getThis(DmConst.PROP_KEY_DM_SVC_PATH));
		}
		set
		{
			do_setThis(DmConst.PROP_KEY_DM_SVC_PATH, value);
		}
	}

	public LogLevel LogLevel
	{
		get
		{
			return (LogLevel)do_getThis(DmConst.PROP_KEY_LOG_LEVEL);
		}
		set
		{
			do_setThis(DmConst.PROP_KEY_LOG_LEVEL, value);
		}
	}

	public string LogDir
	{
		get
		{
			return Convert.ToString(do_getThis(DmConst.PROP_KEY_LOG_DIR));
		}
		set
		{
			do_setThis(DmConst.PROP_KEY_LOG_DIR, value);
		}
	}

	public int LogSize
	{
		get
		{
			return Convert.ToInt32(do_getThis(DmConst.PROP_KEY_LOG_SIZE));
		}
		set
		{
			do_setThis(DmConst.PROP_KEY_LOG_SIZE, value);
		}
	}

	public int RwSeparate
	{
		get
		{
			return Convert.ToInt32(do_getThis(DmConst.PROP_KEY_RW_SEPARATE));
		}
		set
		{
			do_setThis(DmConst.PROP_KEY_RW_SEPARATE, value);
			if (Convert.ToInt32(value) > 0)
			{
				LoginMode = LoginModeFlag.onlyprimary;
			}
		}
	}

	public int RwPercent
	{
		get
		{
			return Convert.ToInt32(do_getThis(DmConst.PROP_KEY_RW_PERCENT));
		}
		set
		{
			do_setThis(DmConst.PROP_KEY_RW_PERCENT, value);
		}
	}

	public int Compress
	{
		get
		{
			return Convert.ToInt32(do_getThis(DmConst.PROP_KEY_COMPRESS));
		}
		set
		{
			do_setThis(DmConst.PROP_KEY_COMPRESS, value);
		}
	}

	public int CompressId
	{
		get
		{
			return Convert.ToInt32(do_getThis(DmConst.PROP_KEY_COMPRESS_ID));
		}
		set
		{
			do_setThis(DmConst.PROP_KEY_COMPRESS_ID, value);
		}
	}

	public bool LoginEncrypt
	{
		get
		{
			return Convert.ToBoolean(do_getThis(DmConst.PROP_KEY_LOGIN_ENCRYPT));
		}
		set
		{
			do_setThis(DmConst.PROP_KEY_LOGIN_ENCRYPT, value);
		}
	}

	public string CipherPath
	{
		get
		{
			return Convert.ToString(do_getThis(DmConst.PROP_KEY_CIPHER_PATH));
		}
		set
		{
			do_setThis(DmConst.PROP_KEY_CIPHER_PATH, value);
		}
	}

	public bool EnRsCache
	{
		get
		{
			return Convert.ToBoolean(do_getThis(DmConst.PROP_KEY_ENABLE_RS_CACHE));
		}
		set
		{
			do_setThis(DmConst.PROP_KEY_ENABLE_RS_CACHE, value);
		}
	}

	public int RsCacheSize
	{
		get
		{
			return Convert.ToInt32(do_getThis(DmConst.PROP_KEY_RS_CACHE_SIZE));
		}
		set
		{
			do_setThis(DmConst.PROP_KEY_RS_CACHE_SIZE, value);
		}
	}

	public int RsRefreshFreq
	{
		get
		{
			return Convert.ToInt32(do_getThis(DmConst.PROP_KEY_RS_REFRESH_FREQ));
		}
		set
		{
			do_setThis(DmConst.PROP_KEY_RS_REFRESH_FREQ, value);
		}
	}

	public int LobMode
	{
		get
		{
			return Convert.ToInt32(do_getThis(DmConst.PROP_KEY_LOB_MODE));
		}
		set
		{
			do_setThis(DmConst.PROP_KEY_LOB_MODE, value);
		}
	}

	public bool AutoCommit
	{
		get
		{
			return Convert.ToBoolean(do_getThis(DmConst.PROP_KEY_AUTO_COMMIT));
		}
		set
		{
			do_setThis(DmConst.PROP_KEY_AUTO_COMMIT, value);
		}
	}

	public bool AlwaysAllowAutoCommit
	{
		get
		{
			return Convert.ToBoolean(do_getThis(DmConst.PROP_KEY_ALWAYS_ALLOW_COMMIT));
		}
		set
		{
			do_setThis(DmConst.PROP_KEY_ALWAYS_ALLOW_COMMIT, value);
		}
	}

	public int BatchType
	{
		get
		{
			return Convert.ToInt32(do_getThis(DmConst.PROP_KEY_BATCH_TYPE));
		}
		set
		{
			do_setThis(DmConst.PROP_KEY_BATCH_TYPE, value);
		}
	}

	public bool BatchContinueOnError
	{
		get
		{
			return Convert.ToBoolean(do_getThis(DmConst.PROP_KEY_BATCH_CONTINUE_ON_ERROR));
		}
		set
		{
			do_setThis(DmConst.PROP_KEY_BATCH_CONTINUE_ON_ERROR, value);
		}
	}

	public bool BatchNotOnCall
	{
		get
		{
			return Convert.ToBoolean(do_getThis(DmConst.PROP_KEY_BATCH_NOT_ON_CALL));
		}
		set
		{
			do_setThis(DmConst.PROP_KEY_BATCH_NOT_ON_CALL, value);
		}
	}

	public int BatchAllowMaxErrors
	{
		get
		{
			return Convert.ToInt32(do_getThis(DmConst.PROP_KEY_BATCH_ALLOW_MAX_ERRORS));
		}
		set
		{
			do_setThis(DmConst.PROP_KEY_BATCH_ALLOW_MAX_ERRORS, value);
		}
	}

	public int BufPrefetch
	{
		get
		{
			return Convert.ToInt32(do_getThis(DmConst.PROP_KEY_BUF_PREFETCH));
		}
		set
		{
			do_setThis(DmConst.PROP_KEY_BUF_PREFETCH, value);
		}
	}

	public bool ClobAsString
	{
		get
		{
			return Convert.ToBoolean(do_getThis(DmConst.PROP_KEY_CLOB_AS_STRING));
		}
		set
		{
			do_setThis(DmConst.PROP_KEY_CLOB_AS_STRING, value);
		}
	}

	public bool ColumnNameUpperCase
	{
		get
		{
			return Convert.ToBoolean(do_getThis(DmConst.PROP_KEY_COLUMN_NAME_UPPERCASE));
		}
		set
		{
			do_setThis(DmConst.PROP_KEY_COLUMN_NAME_UPPERCASE, value);
		}
	}

	public ColumnNameCase ColumnNameCase
	{
		get
		{
			return (ColumnNameCase)do_getThis(DmConst.PROP_KEY_COLUMN_NAME_CASE);
		}
		set
		{
			do_setThis(DmConst.PROP_KEY_COLUMN_NAME_CASE, value);
		}
	}

	public string DatabaseProductName
	{
		get
		{
			return Convert.ToString(do_getThis(DmConst.PROP_KEY_DATABASE_PRODUCT_NAME));
		}
		set
		{
			do_setThis(DmConst.PROP_KEY_DATABASE_PRODUCT_NAME, value);
		}
	}

	public CompatibleMode CompatibleMode
	{
		get
		{
			return (CompatibleMode)Convert.ToInt32(do_getThis(DmConst.PROP_KEY_COMPATIBLE_MODE));
		}
		set
		{
			do_setThis(DmConst.PROP_KEY_COMPATIBLE_MODE, value);
		}
	}

	public bool IgnoreCase
	{
		get
		{
			return Convert.ToBoolean(do_getThis(DmConst.PROP_KEY_IGNORE_CASE));
		}
		set
		{
			do_setThis(DmConst.PROP_KEY_IGNORE_CASE, value);
		}
	}

	public bool IsBdtaRs
	{
		get
		{
			return Convert.ToBoolean(do_getThis(DmConst.PROP_KEY_IS_BDTA_RS));
		}
		set
		{
			do_setThis(DmConst.PROP_KEY_IS_BDTA_RS, value);
		}
	}

	public long MaxRows
	{
		get
		{
			return Convert.ToInt64(do_getThis(DmConst.PROP_KEY_MAX_ROWS));
		}
		set
		{
			do_setThis(DmConst.PROP_KEY_MAX_ROWS, value);
		}
	}

	public int SocketTimeout
	{
		get
		{
			return Convert.ToInt32(do_getThis(DmConst.PROP_KEY_SOCKET_TIMEOUT));
		}
		set
		{
			do_setThis(DmConst.PROP_KEY_SOCKET_TIMEOUT, value);
		}
	}

	public string AddressRemap
	{
		get
		{
			return Convert.ToString(do_getThis(DmConst.PROP_KEY_ADDRESS_REMAP));
		}
		set
		{
			do_setThis(DmConst.PROP_KEY_ADDRESS_REMAP, value);
		}
	}

	public string UserRemap
	{
		get
		{
			return Convert.ToString(do_getThis(DmConst.PROP_KEY_USER_REMAP));
		}
		set
		{
			do_setThis(DmConst.PROP_KEY_USER_REMAP, value);
		}
	}

	public EpSelector EpSelector
	{
		get
		{
			return (EpSelector)do_getThis(DmConst.PROP_KEY_EP_SELECTOR);
		}
		set
		{
			do_setThis(DmConst.PROP_KEY_EP_SELECTOR, value);
		}
	}

	public int SwitchTimes
	{
		get
		{
			return Convert.ToInt32(do_getThis(DmConst.PROP_KEY_SWITCH_TIMES));
		}
		set
		{
			do_setThis(DmConst.PROP_KEY_SWITCH_TIMES, value);
		}
	}

	public int SwitchInterval
	{
		get
		{
			return Convert.ToInt32(do_getThis(DmConst.PROP_KEY_SWITCH_INTERVAL));
		}
		set
		{
			do_setThis(DmConst.PROP_KEY_SWITCH_INTERVAL, value);
		}
	}

	public LoginStatus LoginStatus
	{
		get
		{
			return (LoginStatus)do_getThis(DmConst.PROP_KEY_LOGIN_STATUS);
		}
		set
		{
			do_setThis(DmConst.PROP_KEY_LOGIN_STATUS, value);
		}
	}

	public bool LoginDscCtrl
	{
		get
		{
			return Convert.ToBoolean(do_getThis(DmConst.PROP_KEY_LOGIN_DSC_CTRL));
		}
		set
		{
			do_setThis(DmConst.PROP_KEY_LOGIN_DSC_CTRL, value);
		}
	}

	public int RwStandbyRecoverTime
	{
		get
		{
			return Convert.ToInt32(do_getThis(DmConst.PROP_KEY_RW_STANDBY_RECOVER_TIME));
		}
		set
		{
			do_setThis(DmConst.PROP_KEY_RW_STANDBY_RECOVER_TIME, value);
		}
	}

	public bool RWHA
	{
		get
		{
			return Convert.ToBoolean(do_getThis(DmConst.PROP_KEY_RW_HA));
		}
		set
		{
			do_setThis(DmConst.PROP_KEY_RW_HA, value);
		}
	}

	public bool RwAutoDistribute
	{
		get
		{
			return Convert.ToBoolean(do_getThis(DmConst.PROP_KEY_RW_AUTO_DISTRIBUTE));
		}
		set
		{
			do_setThis(DmConst.PROP_KEY_RW_AUTO_DISTRIBUTE, value);
		}
	}

	public long RwFilterType
	{
		get
		{
			return Convert.ToInt64(do_getThis(DmConst.PROP_KEY_RW_FILTER_TYPE));
		}
		set
		{
			do_setThis(DmConst.PROP_KEY_RW_FILTER_TYPE, value);
		}
	}

	public DoSwitch DoSwitch
	{
		get
		{
			return (DoSwitch)do_getThis(DmConst.PROP_KEY_DO_SWITCH);
		}
		set
		{
			do_setThis(DmConst.PROP_KEY_DO_SWITCH, value);
		}
	}

	public CLUSTER Cluster
	{
		get
		{
			return (CLUSTER)do_getThis(DmConst.PROP_KEY_CLUSTER);
		}
		set
		{
			do_setThis(DmConst.PROP_KEY_CLUSTER, value);
		}
	}

	public int DbAliveCheckFreq
	{
		get
		{
			return Convert.ToInt32(do_getThis(DmConst.PROP_KEY_DB_ALIVE_CHECK_FREQ));
		}
		set
		{
			do_setThis(DmConst.PROP_KEY_DB_ALIVE_CHECK_FREQ, value);
		}
	}

	public int DbAliveCheckTimeout
	{
		get
		{
			return Convert.ToInt32(do_getThis(DmConst.PROP_KEY_DB_ALIVE_CHECK_TIMEOUT));
		}
		set
		{
			do_setThis(DmConst.PROP_KEY_DB_ALIVE_CHECK_TIMEOUT, value);
		}
	}

	public int MaxLobDataLenPerMsg
	{
		get
		{
			return Convert.ToInt32(do_getThis(DmConst.PROP_KEY_MAX_LOB_DATA_LEN_PER_MSG));
		}
		set
		{
			do_setThis(DmConst.PROP_KEY_MAX_LOB_DATA_LEN_PER_MSG, value);
		}
	}

	public bool DbTimeToTimeSpan
	{
		get
		{
			return Convert.ToBoolean(do_getThis(DmConst.PROP_KEY_DBTIME_TO_TIMESPAN));
		}
		set
		{
			do_setThis(DmConst.PROP_KEY_DBTIME_TO_TIMESPAN, value);
		}
	}

	public bool CaseSensitive
	{
		get
		{
			return Convert.ToBoolean(do_getThis(DmConst.PROP_KEY_CASE_SENSITIVE));
		}
		set
		{
			do_setThis(DmConst.PROP_KEY_CASE_SENSITIVE, value);
		}
	}

	public bool Varchar36ToGuid
	{
		get
		{
			return Convert.ToBoolean(do_getThis(DmConst.PROP_KEY_VARCHAR36_TO_GUID));
		}
		set
		{
			do_setThis(DmConst.PROP_KEY_VARCHAR36_TO_GUID, value);
		}
	}

	public bool UseSkyWalking
	{
		get
		{
			return Convert.ToBoolean(do_getThis(DmConst.PROP_KEY_USE_SKYWALKING));
		}
		set
		{
			do_setThis(DmConst.PROP_KEY_USE_SKYWALKING, value);
		}
	}

	public IntervalMode IntervalMode
	{
		get
		{
			return (IntervalMode)do_getThis(DmConst.PROP_KEY_INTERVAL_MODE);
		}
		set
		{
			do_setThis(DmConst.PROP_KEY_INTERVAL_MODE, value);
		}
	}

	public string SslFilesPath
	{
		get
		{
			return Convert.ToString(do_getThis(DmConst.PROP_KEY_SSL_FILE_PATH));
		}
		set
		{
			do_setThis(DmConst.PROP_KEY_SSL_FILE_PATH, value);
		}
	}

	public string SslKeyPass
	{
		get
		{
			return Convert.ToString(do_getThis(DmConst.PROP_KEY_SSL_KEY_PASS));
		}
		set
		{
			do_setThis(DmConst.PROP_KEY_SSL_KEY_PASS, value);
		}
	}

	public bool ConvertToTz
	{
		get
		{
			return Convert.ToBoolean(do_getThis(DmConst.PROP_KEY_CONVERT_TO_TZ));
		}
		set
		{
			do_setThis(DmConst.PROP_KEY_CONVERT_TO_TZ, value);
		}
	}

	public string Catalog
	{
		get
		{
			return Convert.ToString(do_getThis(DmConst.PROP_KEY_CATALOG));
		}
		set
		{
			do_setThis(DmConst.PROP_KEY_CATALOG, value);
		}
	}

	public string DBAPassword
	{
		get
		{
			return Convert.ToString(do_getThis(DmConst.PROP_KEY_DBA_PASSWORD));
		}
		set
		{
			do_setThis(DmConst.PROP_KEY_DBA_PASSWORD, value);
		}
	}

	public string InitialCatalog
	{
		set
		{
			do_setThis(DmConst.PROP_KEY_USER, value);
			if (value.ToString().Length > 48)
			{
				do_setThis(DmConst.PROP_KEY_PASSWORD, value.ToString().Substring(0, 48));
			}
			else
			{
				do_setThis(DmConst.PROP_KEY_PASSWORD, value);
			}
		}
	}

	public bool ShowExtraInfo
	{
		get
		{
			return Convert.ToBoolean(do_getThis(DmConst.PROP_KEY_SHOW_EXTRA_INFO));
		}
		set
		{
			do_setThis(DmConst.PROP_KEY_SHOW_EXTRA_INFO, value);
		}
	}

	public bool EFCoreNextResult
	{
		get
		{
			return Convert.ToBoolean(do_getThis(DmConst.PROP_KEY_EFCORE_NEXT_RESULT));
		}
		set
		{
			do_setThis(DmConst.PROP_KEY_EFCORE_NEXT_RESULT, value);
		}
	}

	internal bool do_IsFixedSize => base.IsFixedSize;

	internal int do_Count => base.Count;

	internal ICollection do_Keys => base.Keys;

	internal ICollection do_Values => base.Values;

	public override object this[string keyword]
	{
		get
		{
			if (filterHead == null)
			{
				return do_getThis(keyword);
			}
			return filterHead.getThis(this, keyword);
		}
		set
		{
			if (filterHead == null)
			{
				do_setThis(keyword, value);
			}
			else
			{
				filterHead.setThis(this, keyword, value);
			}
		}
	}

	public override bool IsFixedSize
	{
		get
		{
			if (filterHead == null)
			{
				return do_IsFixedSize;
			}
			return filterHead.getIsFixedSize(this);
		}
	}

	public override int Count
	{
		get
		{
			if (filterHead == null)
			{
				return do_Count;
			}
			return filterHead.getCount(this);
		}
	}

	public override ICollection Keys
	{
		get
		{
			if (filterHead == null)
			{
				return do_Keys;
			}
			return filterHead.getKeys(this);
		}
	}

	public override ICollection Values
	{
		get
		{
			if (filterHead == null)
			{
				return do_Values;
			}
			return filterHead.getValues(this);
		}
	}

	static DmConnectionStringBuilder()
	{
		idGenerator = 0L;
		options = new List<DmOption>();
		options.Add(new DmOption(DmConst.PROP_KEY_EP_GROUP, typeof(EPGroup), null, null, null, 0L));
		options.Add(new DmOption(DmConst.PROP_KEY_TIMEZONE, typeof(long), maxvalue: 840L, minvalue: -779L, defaultvalue: DmOptionHelper.timezonedef));
		options.Add(new DmOption(DmConst.PROP_KEY_LANGUAGE, defaultvalue: DmOptionHelper.languagedef, basetype: typeof(SupportedLanguage), syn: null, maxvalue: null, minvalue: 0L));
		options.Add(new DmOption(DmConst.PROP_KEY_COMPRESS, syn: DmConst.PROP_KEYSYN_COMPRESS, defaultvalue: DmOptionHelper.compressDef, basetype: typeof(long), maxvalue: null, minvalue: 0L));
		options.Add(new DmOption(DmConst.PROP_KEY_COMPRESS_ID, syn: DmConst.PROP_KEYSYN_COMPRESS_ID, defaultvalue: DmOptionHelper.compressIdDef, basetype: typeof(long), maxvalue: null, minvalue: 0L));
		options.Add(new DmOption(DmConst.PROP_KEY_LOGIN_ENCRYPT, syn: DmConst.PROP_KEYSYN_LOGIN_ENCRYPT, defaultvalue: DmOptionHelper.loginEncryptDef, basetype: typeof(bool), maxvalue: null, minvalue: 0L));
		options.Add(new DmOption(DmConst.PROP_KEY_CIPHER_PATH, syn: DmConst.PROP_KEYSYN_CIPHER_PATH, defaultvalue: DmOptionHelper.cipherPathDef, basetype: typeof(string), maxvalue: null, minvalue: 0L));
		options.Add(new DmOption(DmConst.PROP_KEY_DIRECT, defaultvalue: DmOptionHelper.directDef, basetype: typeof(bool), syn: null, maxvalue: null, minvalue: 0L));
		options.Add(new DmOption(DmConst.PROP_KEY_KEYWORDS, syn: DmConst.PROP_KEYSYN_KEYWORDS, defaultvalue: DmOptionHelper.keyWordsDef, basetype: typeof(string), maxvalue: null, minvalue: 0L));
		options.Add(new DmOption(DmConst.PROP_KEY_ENABLE_RS_CACHE, syn: DmConst.PROP_KEYSYN_ENABLE_RS_CACHE, defaultvalue: DmOptionHelper.enableRsCacheDef, basetype: typeof(bool), maxvalue: null, minvalue: 0L));
		options.Add(new DmOption(DmConst.PROP_KEY_RS_CACHE_SIZE, syn: DmConst.PROP_KEYSYN_RS_CACHE_SIZE, defaultvalue: DmOptionHelper.rsCacheSizeDef, basetype: typeof(long), maxvalue: 65535L, minvalue: 1L));
		options.Add(new DmOption(DmConst.PROP_KEY_RS_REFRESH_FREQ, syn: DmConst.PROP_KEYSYN_RS_REFRESH_FREQ, defaultvalue: DmOptionHelper.rsRefreshFreqDef, basetype: typeof(long), maxvalue: 10000L, minvalue: 0L));
		options.Add(new DmOption(DmConst.PROP_KEY_LOGIN_MODE, syn: DmConst.PROP_KEYSYN_LOGIN_MODE, defaultvalue: DmOptionHelper.login_modedef, basetype: typeof(LoginModeFlag), maxvalue: null, minvalue: 0L));
		options.Add(new DmOption(DmConst.PROP_KEY_RW_SEPARATE, syn: DmConst.PROP_KEYSYN_RW_SEPARATE, defaultvalue: DmOptionHelper.rwSeparateDef, basetype: typeof(int), maxvalue: null, minvalue: 0L));
		options.Add(new DmOption(DmConst.PROP_KEY_RW_PERCENT, syn: DmConst.PROP_KEYSYN_RW_PERCENT, defaultvalue: DmOptionHelper.rwPercentDef, basetype: typeof(long), maxvalue: 100L, minvalue: 0L));
		options.Add(new DmOption(DmConst.PROP_KEY_LOG_LEVEL, syn: DmConst.PROP_KEYSYN_LOG_LEVEL, basetype: typeof(LogLevel), defaultvalue: DmOptionHelper.logleveldef, maxvalue: null, minvalue: 0L));
		options.Add(new DmOption(DmConst.PROP_KEY_LOG_DIR, syn: DmConst.PROP_KEYSYN_LOG_DIR, basetype: typeof(string), defaultvalue: DmOptionHelper.logdirdef, maxvalue: null, minvalue: 0L));
		options.Add(new DmOption(DmConst.PROP_KEY_LOG_SIZE, syn: DmConst.PROP_KEYSYN_LOG_SIZE, basetype: typeof(long), defaultvalue: DmOptionHelper.logSizedef, maxvalue: null, minvalue: 0L));
		options.Add(new DmOption(DmConst.PROP_KEY_SERVER, syn: DmConst.PROP_KEYSYN_SERVER, defaultvalue: DmOptionHelper.serverDef, basetype: typeof(string), maxvalue: null, minvalue: 0L));
		options.Add(new DmOption(DmConst.PROP_KEY_USER, syn: DmConst.PROP_KEYSYN_USER, defaultvalue: DmOptionHelper.userDef, basetype: typeof(string), maxvalue: null, minvalue: 0L));
		options.Add(new DmOption(DmConst.PROP_KEY_PASSWORD, syn: DmConst.PROP_KEYSYN_PASSWORD, defaultvalue: DmOptionHelper.passwordDef, basetype: typeof(string), maxvalue: null, minvalue: 0L));
		options.Add(new DmOption(DmConst.PROP_KEY_PORT, defaultvalue: DmOptionHelper.portDef, basetype: typeof(long), syn: null, maxvalue: 65535L, minvalue: 1L));
		options.Add(new DmOption(DmConst.PROP_KEY_ENCODING, syn: DmConst.PROP_KEYSYN_ENCODING, defaultvalue: DmOptionHelper.encodingDef, basetype: typeof(string), maxvalue: null, minvalue: 0L));
		options.Add(new DmOption(DmConst.PROP_KEY_ENLIST, syn: DmConst.PROP_KEYSYN_ENLIST, defaultvalue: DmOptionHelper.enlistDef, basetype: typeof(bool), maxvalue: null, minvalue: 0L));
		options.Add(new DmOption(DmConst.PROP_KEY_CONNECTION_TIMEOUT, syn: DmConst.PROP_KEYSYN_CONNECTION_TIMEOUT, defaultvalue: DmOptionHelper.connectionTimeoutDef, basetype: typeof(long), maxvalue: null, minvalue: 0L));
		options.Add(new DmOption(DmConst.PROP_KEY_COMMAND_TIMEOUT, syn: DmConst.PROP_KEYSYN_COMMAND_TIMEOUT, defaultvalue: DmOptionHelper.commandTimeoutDef, basetype: typeof(long), maxvalue: null, minvalue: 0L));
		options.Add(new DmOption(DmConst.PROP_KEY_STMT_POOL_SIZE, syn: DmConst.PROP_KEYSYN_STMT_POOL_SIZE, defaultvalue: DmOptionHelper.poolSizeDef, basetype: typeof(long), maxvalue: null, minvalue: 0L));
		options.Add(new DmOption(DmConst.PROP_KEY_STMT_POOLING, syn: DmConst.PROP_KEYSYN_STMT_POOLING, defaultvalue: DmOptionHelper.stmtPoolingDef, basetype: typeof(bool), maxvalue: null, minvalue: 0L));
		options.Add(new DmOption(DmConst.PROP_KEY_PSTMT_POOLING, syn: DmConst.PROP_KEYSYN_PSTMT_POOLING, defaultvalue: DmOptionHelper.preparePoolingDef, basetype: typeof(bool), maxvalue: null, minvalue: 0L));
		options.Add(new DmOption(DmConst.PROP_KEY_PSTMT_POOL_SIZE, syn: DmConst.PROP_KEYSYN_PSTMT_POOL_SIZE, defaultvalue: DmOptionHelper.preparePoolSizeDef, basetype: typeof(long), maxvalue: null, minvalue: 0L));
		options.Add(new DmOption(DmConst.PROP_KEY_CONN_POOLING, syn: DmConst.PROP_KEYSYN_CONN_POOLING, defaultvalue: DmOptionHelper.connPoolingDef, basetype: typeof(bool), maxvalue: null, minvalue: 0L));
		options.Add(new DmOption(DmConst.PROP_KEY_CONN_POOL_SIZE, syn: DmConst.PROP_KEYSYN_CONN_POOL_SIZE, defaultvalue: DmOptionHelper.connPoolSizeDef, basetype: typeof(long), maxvalue: null, minvalue: 0L));
		options.Add(new DmOption(DmConst.PROP_KEY_CONN_POOL_CHECK, syn: DmConst.PROP_KEYSYN_CONN_POOL_CHECK, defaultvalue: DmOptionHelper.connPoolCheckDef, basetype: typeof(bool), maxvalue: null, minvalue: 0L));
		options.Add(new DmOption(DmConst.PROP_KEY_CONN_POOL_TIMEOUT, syn: DmConst.PROP_KEYSYN_CONN_POOL_TIMEOUT, defaultvalue: DmOptionHelper.connPoolTimeoutDef, basetype: typeof(long), maxvalue: null, minvalue: 0L));
		options.Add(new DmOption(DmConst.PROP_KEY_CONN_POOL_IDLE_EXPIRED_TIME, syn: DmConst.PROP_KEYSYN_CONN_POOL_IDLE_EXPIRED_TIME, defaultvalue: DmOptionHelper.connPoolExpiredTimeDef, basetype: typeof(long), maxvalue: null, minvalue: 0L));
		options.Add(new DmOption(DmConst.PROP_KEY_CONN_POOL_IDLE_CLEAR_INTERVAL, syn: DmConst.PROP_KEYSYN_CONN_POOL_IDLE_CLEAR_INTERVAL, defaultvalue: DmOptionHelper.connPoolClearIntervalDef, basetype: typeof(long), maxvalue: null, minvalue: 0L));
		options.Add(new DmOption(DmConst.PROP_KEY_ESCAPE_PROCESS, syn: DmConst.PROP_KEYSYN_ESCAPE_PROCESS, defaultvalue: DmOptionHelper.escapeProcessDef, basetype: typeof(bool), maxvalue: null, minvalue: 0L));
		options.Add(new DmOption(DmConst.PROP_KEY_SCHEMA, defaultvalue: DmOptionHelper.schemaDef, basetype: typeof(string), syn: null, maxvalue: null, minvalue: 0L));
		options.Add(new DmOption(DmConst.PROP_KEY_SCHEMA_SENSITIVE, defaultvalue: DmOptionHelper.schemaSensitiveDef, basetype: typeof(bool), syn: null, maxvalue: null, minvalue: 0L));
		options.Add(new DmOption(DmConst.PROP_KEY_APPNAME, syn: DmConst.PROP_KEYSYN_APPNAME, defaultvalue: DmOptionHelper.appnameDef, basetype: typeof(string), maxvalue: null, minvalue: 0L));
		options.Add(new DmOption(DmConst.PROP_KEY_HOST, defaultvalue: DmOptionHelper.hostDef, basetype: typeof(string), syn: null, maxvalue: null, minvalue: 0L));
		options.Add(new DmOption(DmConst.PROP_KEY_OS, syn: DmConst.PROP_KEYSYN_OS, defaultvalue: DmOptionHelper.osDef, basetype: typeof(string), maxvalue: null, minvalue: 0L));
		options.Add(new DmOption(DmConst.PROP_KEY_LOB_MODE, syn: DmConst.PROP_KEYSYN_LOB_MODE, defaultvalue: DmOptionHelper.lobModeDef, basetype: typeof(long), maxvalue: null, minvalue: 0L));
		options.Add(new DmOption(DmConst.PROP_KEY_AUTO_COMMIT, syn: DmConst.PROP_KEYSYN_AUTO_COMMIT, defaultvalue: DmOptionHelper.autoCommitDef, basetype: typeof(bool), maxvalue: null, minvalue: 0L));
		options.Add(new DmOption(DmConst.PROP_KEY_ALWAYS_ALLOW_COMMIT, syn: DmConst.PROP_KEYSYN_ALWAYS_ALLOW_COMMIT, defaultvalue: DmOptionHelper.alwaysAllowCommitDef, basetype: typeof(bool), maxvalue: null, minvalue: 0L));
		options.Add(new DmOption(DmConst.PROP_KEY_BATCH_TYPE, syn: DmConst.PROP_KEYSYN_BATCH_TYPE, defaultvalue: DmOptionHelper.batchTypeDef, basetype: typeof(long), maxvalue: null, minvalue: 0L));
		options.Add(new DmOption(DmConst.PROP_KEY_BATCH_ALLOW_MAX_ERRORS, syn: DmConst.PROP_KEYSYN_BATCH_ALLOW_MAX_ERRORS, defaultvalue: DmOptionHelper.batchAllowMaxErrorsDef, basetype: typeof(long), maxvalue: null, minvalue: 0L));
		options.Add(new DmOption(DmConst.PROP_KEY_BATCH_CONTINUE_ON_ERROR, syn: DmConst.PROP_KEYSYN_BATCH_CONTINUE_ON_ERROR, defaultvalue: DmOptionHelper.batchContinueOnErrorDef, basetype: typeof(bool), maxvalue: null, minvalue: 0L));
		options.Add(new DmOption(DmConst.PROP_KEY_BATCH_NOT_ON_CALL, syn: DmConst.PROP_KEYSYN_BATCH_NOT_ON_CALL, defaultvalue: DmOptionHelper.batchNotOnCallDef, basetype: typeof(bool), maxvalue: null, minvalue: 0L));
		options.Add(new DmOption(DmConst.PROP_KEY_BUF_PREFETCH, syn: DmConst.PROP_KEYSYN_BUF_PREFETCH, defaultvalue: DmOptionHelper.bufPrefetchDef, basetype: typeof(long), maxvalue: null, minvalue: 0L));
		options.Add(new DmOption(DmConst.PROP_KEY_CLOB_AS_STRING, syn: DmConst.PROP_KEYSYN_CLOB_AS_STRING, defaultvalue: DmOptionHelper.clobAsStringDef, basetype: typeof(bool), maxvalue: null, minvalue: 0L));
		options.Add(new DmOption(DmConst.PROP_KEY_COLUMN_NAME_UPPERCASE, syn: DmConst.PROP_KEYSYN_COLUMN_NAME_UPPERCASE, defaultvalue: DmOptionHelper.columnNameUpperCaseDef, basetype: typeof(bool), maxvalue: null, minvalue: 0L));
		options.Add(new DmOption(DmConst.PROP_KEY_COLUMN_NAME_CASE, syn: DmConst.PROP_KEYSYN_COLUMN_NAME_CASE, defaultvalue: DmOptionHelper.columnNameCaseDef, basetype: typeof(ColumnNameCase), maxvalue: null, minvalue: 0L));
		options.Add(new DmOption(DmConst.PROP_KEY_DATABASE_PRODUCT_NAME, syn: DmConst.PROP_KEYSYN_DATABASE_PRODUCT_NAME, defaultvalue: DmOptionHelper.databaseProductNameDef, basetype: typeof(string), maxvalue: null, minvalue: 0L));
		options.Add(new DmOption(DmConst.PROP_KEY_COMPATIBLE_MODE, syn: DmConst.PROP_KEYSYN_COMPATIBLE_MODE, defaultvalue: DmOptionHelper.compatibleModeDef, basetype: typeof(CompatibleMode), maxvalue: null, minvalue: 0L));
		options.Add(new DmOption(DmConst.PROP_KEY_IGNORE_CASE, syn: DmConst.PROP_KEYSYN_IGNORE_CASE, defaultvalue: DmOptionHelper.ignoreCaseDef, basetype: typeof(bool), maxvalue: null, minvalue: 0L));
		options.Add(new DmOption(DmConst.PROP_KEY_IS_BDTA_RS, syn: DmConst.PROP_KEYSYN_IS_BDTA_RS, defaultvalue: DmOptionHelper.isBdtaRsDef, basetype: typeof(bool), maxvalue: null, minvalue: 0L));
		options.Add(new DmOption(DmConst.PROP_KEY_MAX_ROWS, syn: DmConst.PROP_KEYSYN_MAX_ROWS, defaultvalue: DmOptionHelper.maxRowsDef, basetype: typeof(long), maxvalue: null, minvalue: 0L));
		options.Add(new DmOption(DmConst.PROP_KEY_SOCKET_TIMEOUT, syn: DmConst.PROP_KEYSYN_SOCKET_TIMEOUT, defaultvalue: DmOptionHelper.socketTimeoutDef, basetype: typeof(long), maxvalue: null, minvalue: 0L));
		options.Add(new DmOption(DmConst.PROP_KEY_ADDRESS_REMAP, syn: DmConst.PROP_KEYSYN_ADDRESS_REMAP, defaultvalue: DmOptionHelper.addressRemapDef, basetype: typeof(string), maxvalue: null, minvalue: 0L));
		options.Add(new DmOption(DmConst.PROP_KEY_USER_REMAP, syn: DmConst.PROP_KEYSYN_USER_REMAP, defaultvalue: DmOptionHelper.userRemapDef, basetype: typeof(string), maxvalue: null, minvalue: 0L));
		options.Add(new DmOption(DmConst.PROP_KEY_EP_SELECTOR, syn: DmConst.PROP_KEYSYN_EP_SELECTOR, defaultvalue: DmOptionHelper.epSelectorDef, basetype: typeof(EpSelector), maxvalue: null, minvalue: 0L));
		options.Add(new DmOption(DmConst.PROP_KEY_SWITCH_TIMES, syn: DmConst.PROP_KEYSYN_SWITCH_TIMES, defaultvalue: DmOptionHelper.switchTimesDef, basetype: typeof(long), maxvalue: null, minvalue: 0L));
		options.Add(new DmOption(DmConst.PROP_KEY_SWITCH_INTERVAL, syn: DmConst.PROP_KEYSYN_SWITCH_INTERVAL, defaultvalue: DmOptionHelper.switchIntervalDef, basetype: typeof(long), maxvalue: null, minvalue: 0L));
		options.Add(new DmOption(DmConst.PROP_KEY_LOGIN_STATUS, syn: DmConst.PROP_KEYSYN_LOGIN_STATUS, defaultvalue: DmOptionHelper.loginStatusDef, basetype: typeof(LoginStatus), maxvalue: null, minvalue: 0L));
		options.Add(new DmOption(DmConst.PROP_KEY_LOGIN_DSC_CTRL, syn: DmConst.PROP_KEYSYN_LOGIN_DSC_CTRL, defaultvalue: DmOptionHelper.loginDscCtrlDef, basetype: typeof(bool), maxvalue: null, minvalue: 0L));
		options.Add(new DmOption(DmConst.PROP_KEY_RW_STANDBY_RECOVER_TIME, syn: DmConst.PROP_KEYSYN_RW_STANDBY_RECOVER_TIME, defaultvalue: DmOptionHelper.rwStandbyRecoverTimeDef, basetype: typeof(long), maxvalue: null, minvalue: 0L));
		options.Add(new DmOption(DmConst.PROP_KEY_RW_HA, syn: DmConst.PROP_KEYSYN_RW_HA, defaultvalue: DmOptionHelper.rwHADef, basetype: typeof(bool), maxvalue: null, minvalue: 0L));
		options.Add(new DmOption(DmConst.PROP_KEY_RW_AUTO_DISTRIBUTE, syn: DmConst.PROP_KEYSYN_RW_AUTO_DISTRIBUTE, defaultvalue: DmOptionHelper.rwAutoDistributeDef, basetype: typeof(bool), maxvalue: null, minvalue: 0L));
		options.Add(new DmOption(DmConst.PROP_KEY_RW_FILTER_TYPE, syn: DmConst.PROP_KEYSYN_RW_FILTER_TYPE, defaultvalue: DmOptionHelper.rwFilterTypeDef, basetype: typeof(long), maxvalue: null, minvalue: 0L));
		options.Add(new DmOption(DmConst.PROP_KEY_DO_SWITCH, syn: DmConst.PROP_KEYSYN_DO_SWITCH, defaultvalue: DmOptionHelper.doSwitchDef, basetype: typeof(DoSwitch), maxvalue: null, minvalue: 0L));
		options.Add(new DmOption(DmConst.PROP_KEY_CLUSTER, defaultvalue: DmOptionHelper.clusterDef, basetype: typeof(CLUSTER), syn: null, maxvalue: null, minvalue: 0L));
		options.Add(new DmOption(DmConst.PROP_KEY_DB_ALIVE_CHECK_FREQ, syn: DmConst.PROP_KEYSYN_DB_ALIVE_CHECK_FREQ, defaultvalue: DmOptionHelper.dbAliveCheckFreqDef, basetype: typeof(long), maxvalue: null, minvalue: 0L));
		options.Add(new DmOption(DmConst.PROP_KEY_DB_ALIVE_CHECK_TIMEOUT, syn: DmConst.PROP_KEYSYN_DB_ALIVE_CHECK_TIMEOUT, defaultvalue: DmOptionHelper.dbAliveCheckTimeoutDef, basetype: typeof(long), maxvalue: null, minvalue: 0L));
		options.Add(new DmOption(DmConst.PROP_KEY_MAX_LOB_DATA_LEN_PER_MSG, syn: DmConst.PROP_KEYSYN_MAX_LOB_DATA_LEN_PER_MSG, defaultvalue: DmOptionHelper.maxLobDataLenPerMsgDef, basetype: typeof(long), maxvalue: 104857600L, minvalue: 1024L));
		options.Add(new DmOption(DmConst.PROP_KEY_DBTIME_TO_TIMESPAN, syn: DmConst.PROP_KEYSYN_DBTIME_TO_TIMESPAN, defaultvalue: DmOptionHelper.dbTimeToTimeSpanDef, basetype: typeof(bool), maxvalue: null, minvalue: 0L));
		options.Add(new DmOption(DmConst.PROP_KEY_CASE_SENSITIVE, defaultvalue: DmOptionHelper.caseSensitiveDef, basetype: typeof(bool), syn: null, maxvalue: null, minvalue: 0L));
		options.Add(new DmOption(DmConst.PROP_KEY_VARCHAR36_TO_GUID, defaultvalue: DmOptionHelper.varchar36ToGuidDef, basetype: typeof(bool), syn: null, maxvalue: null, minvalue: 0L));
		options.Add(new DmOption(DmConst.PROP_KEY_DATABASE, defaultvalue: DmOptionHelper.databaseDef, basetype: typeof(string), syn: null, maxvalue: null, minvalue: 0L));
		options.Add(new DmOption(DmConst.PROP_KEY_DM_SVC_PATH, syn: DmConst.PROP_KEYSYN_DM_SVC_PATH, basetype: typeof(string), defaultvalue: DmOptionHelper.dm_svc_confdef, maxvalue: null, minvalue: 0L));
		options.Add(new DmOption(DmConst.PROP_KEY_USE_SKYWALKING, defaultvalue: DmOptionHelper.useSkyWalkingDef, basetype: typeof(bool), syn: null, maxvalue: null, minvalue: 0L));
		options.Add(new DmOption(DmConst.PROP_KEY_INTERVAL_MODE, defaultvalue: DmOptionHelper.intervalModeDef, basetype: typeof(IntervalMode), syn: null, maxvalue: null, minvalue: 0L));
		options.Add(new DmOption(DmConst.PROP_KEY_SSL_KEY_PASS, defaultvalue: DmOptionHelper.sslKeyPass, basetype: typeof(string), syn: null, maxvalue: null, minvalue: 0L));
		options.Add(new DmOption(DmConst.PROP_KEY_SSL_FILE_PATH, defaultvalue: DmOptionHelper.sslFilePath, basetype: typeof(string), syn: null, maxvalue: null, minvalue: 0L));
		options.Add(new DmOption(DmConst.PROP_KEY_CONVERT_TO_TZ, defaultvalue: DmOptionHelper.convertToTz, basetype: typeof(bool), syn: null, maxvalue: null, minvalue: 0L));
		options.Add(new DmOption(DmConst.PROP_KEY_CATALOG, defaultvalue: DmOptionHelper.catalog, basetype: typeof(string), syn: null, maxvalue: null, minvalue: 0L));
		options.Add(new DmOption(DmConst.PROP_KEY_DBA_PASSWORD, defaultvalue: DmOptionHelper.DbaPasswordDef, basetype: typeof(string), syn: null, maxvalue: null, minvalue: 0L));
		options.Add(new DmOption(DmConst.PROP_KEY_SHOW_EXTRA_INFO, defaultvalue: DmOptionHelper.ShowExtraInfo, basetype: typeof(bool), syn: null, maxvalue: null, minvalue: 0L));
		options.Add(new DmOption(DmConst.PROP_KEY_EFCORE_NEXT_RESULT, defaultvalue: DmOptionHelper.EFCoreNextResultDef, basetype: typeof(bool), syn: null, maxvalue: null, minvalue: 0L));
	}

	public DmConnectionStringBuilder()
	{
		do_Clear();
	}

	public DmConnectionStringBuilder(string connectionstring)
		: this()
	{
		lock (this)
		{
			base.ConnectionString = connectionstring;
		}
	}

	private void ParseServer(string keyword, string value)
	{
		int num = value.ToString().IndexOf(":");
		string value2 = value.ToString().Substring(0, num);
		if (!string.IsNullOrEmpty(value2))
		{
			do_setThis(keyword, value2);
			string value3 = value.ToString().Substring(num + 1);
			if (!string.IsNullOrEmpty(value3))
			{
				do_setThis(DmConst.PROP_KEY_PORT, value3);
			}
		}
	}

	internal object do_getThis(string keyword)
	{
		try
		{
			return property[keyword];
		}
		catch (KeyNotFoundException)
		{
			throw new NotSupportedException(keyword + " does not exist");
		}
	}

	internal void do_setThis(string keyword, object value)
	{
		DmOption option = DmOptionHelper.GetOption(keyword, options);
		if (option != null)
		{
			keyword = keyword.Trim();
			do_Remove(keyword);
			value = option.ValidateValue(value);
			if (option.Equals(DmOptionHelper.GetOption(DmConst.PROP_KEY_SERVER, options)) && !value.ToString().StartsWith("(") && !value.ToString().StartsWith("[") && value.ToString().Contains(":"))
			{
				ParseServer(keyword, value.ToString());
				return;
			}
			lock (this)
			{
				DmOptionHelper.SetProperty(option, value, property);
				base[keyword] = value;
				DmOptionHelper.SetProperty(option, value, setProperty);
				if (keyword.Equals(DmConst.PROP_KEY_LOG_LEVEL, StringComparison.OrdinalIgnoreCase) || keyword.Equals(DmConst.PROP_KEYSYN_LOG_LEVEL, StringComparison.OrdinalIgnoreCase))
				{
					DmSvcConfig.logLevel = (LogLevel)value;
				}
				else if (keyword.Equals(DmConst.PROP_KEY_LOG_DIR, StringComparison.OrdinalIgnoreCase) || keyword.Equals(DmConst.PROP_KEYSYN_LOG_DIR, StringComparison.OrdinalIgnoreCase))
				{
					DmSvcConfig.logDir = DriverUtil.FormatDir(Convert.ToString(value));
				}
				else if (keyword.Equals(DmConst.PROP_KEY_LOG_SIZE, StringComparison.OrdinalIgnoreCase) || keyword.Equals(DmConst.PROP_KEYSYN_LOG_SIZE, StringComparison.OrdinalIgnoreCase))
				{
					DmSvcConfig.logSize = Convert.ToInt32(value);
				}
				else if (keyword.Equals(DmConst.PROP_KEY_DB_ALIVE_CHECK_FREQ, StringComparison.OrdinalIgnoreCase) || keyword.Equals(DmConst.PROP_KEYSYN_DB_ALIVE_CHECK_FREQ, StringComparison.OrdinalIgnoreCase))
				{
					DmSvcConfig.dbAliveCheckFreq = Convert.ToInt32(value);
				}
				else if (keyword.Equals(DmConst.PROP_KEY_DB_ALIVE_CHECK_TIMEOUT, StringComparison.OrdinalIgnoreCase) || keyword.Equals(DmConst.PROP_KEYSYN_DB_ALIVE_CHECK_TIMEOUT, StringComparison.OrdinalIgnoreCase))
				{
					DmSvcConfig.dbAliveCheckTimeout = Convert.ToInt32(value);
				}
				return;
			}
		}
		throw new NotSupportedException(keyword + " does not exist");
	}

	internal void do_Clear()
	{
		base.Clear();
		lock (this)
		{
			foreach (DmOption option in options)
			{
				DmOptionHelper.SetProperty(option, option.Defaultvalue, property);
			}
			setProperty.Clear();
		}
	}

	internal bool do_ContainsKey(string keyword)
	{
		return property.ContainsKey(keyword);
	}

	internal bool do_Remove(string keyword)
	{
		DmOption option = DmOptionHelper.GetOption(keyword, options);
		if (option != null)
		{
			keyword = keyword.Trim();
			lock (this)
			{
				DmOptionHelper.SetProperty(option, option.Defaultvalue, property);
				setProperty.Remove(option.Keyword);
				if (option.Synonym != null)
				{
					string[] synonym = option.Synonym;
					foreach (string key in synonym)
					{
						setProperty.Remove(key);
					}
				}
				if (base.Remove(option.Keyword))
				{
					return true;
				}
				if (option.Synonym != null)
				{
					string[] synonym = option.Synonym;
					foreach (string keyword2 in synonym)
					{
						if (base.Remove(keyword2))
						{
							return true;
						}
					}
				}
			}
		}
		return false;
	}

	internal bool do_EquivalentTo(DmConnectionStringBuilder connectionStringBuilder)
	{
		return base.EquivalentTo(connectionStringBuilder);
	}

	internal bool do_ShouldSerialize(string keyword)
	{
		return base.ShouldSerialize(keyword);
	}

	internal bool do_TryGetValue(string keyword, out object value)
	{
		return base.TryGetValue(keyword, out value);
	}

	internal void do_GetProperties(Hashtable propertyDescriptors)
	{
		base.GetProperties(propertyDescriptors);
	}

	public override void Clear()
	{
		if (filterHead == null)
		{
			do_Clear();
		}
		else
		{
			filterHead.Clear(this);
		}
	}

	public override bool ContainsKey(string keyword)
	{
		if (filterHead == null)
		{
			return do_ContainsKey(keyword);
		}
		return filterHead.ContainsKey(this, keyword);
	}

	public override bool Remove(string keyword)
	{
		if (filterHead == null)
		{
			return do_Remove(keyword);
		}
		return filterHead.Remove(this, keyword);
	}

	public override bool EquivalentTo(DbConnectionStringBuilder connectionStringBuilder)
	{
		if (filterHead == null)
		{
			return do_EquivalentTo((DmConnectionStringBuilder)connectionStringBuilder);
		}
		return filterHead.EquivalentTo(this, (DmConnectionStringBuilder)connectionStringBuilder);
	}

	public override bool ShouldSerialize(string keyword)
	{
		if (filterHead == null)
		{
			return do_ShouldSerialize(keyword);
		}
		return filterHead.ShouldSerialize(this, keyword);
	}

	public override bool TryGetValue(string keyword, out object value)
	{
		if (filterHead == null)
		{
			return do_TryGetValue(keyword, out value);
		}
		return filterHead.TryGetValue(this, keyword, out value);
	}

	protected override void GetProperties(Hashtable propertyDescriptors)
	{
		if (filterHead == null)
		{
			do_GetProperties(propertyDescriptors);
		}
		else
		{
			filterHead.GetProperties(this, propertyDescriptors);
		}
	}
}
