using System;
using System.Collections;
using System.Collections.Generic;
using System.Data.Common;
using System.IO;
using System.Threading;
using W.Dm.Config;
using W.Dm.filter;
using W.Dm.util;

namespace W.Dm;

public class DmConnectionStringBuilder : DbConnectionStringBuilder, IFilterInfo
{
	private const string TlsCaCertificatePathKey = "tls_ca_certificate_path";
	private const string TlsClientCertificatePathKey = "tls_client_certificate_path";
	private const string TlsClientPrivateKeyPathKey = "tls_client_private_key_path";
	private const string TlsClientCertificatePasswordKey = "tls_client_certificate_password";
	private const string TlsRevocationModeKey = "tls_revocation_mode";
	internal long id = -1L;

	internal static long idGenerator;

	private static List<DmOption> options;
	private static readonly Dictionary<string, string> aliases = new(StringComparer.OrdinalIgnoreCase);

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

	private BaseFilter legacyFilterHead;
	public BaseFilter filterHead
	{
		get => legacyFilterHead;
		set
		{
			if (value != null) throw new NotSupportedException("Legacy connection filters are not supported.");
			legacyFilterHead = null;
		}
	}

	public LogInfo LogInfo
	{
		get => null;
		set { if (value != null) throw new NotSupportedException("Legacy logging is not supported."); }
	}

	public RWInfo RWInfo
	{
		get => null;
		set { if (value != null) throw new NotSupportedException("Legacy read/write routing is not supported."); }
	}

	public RecoverInfo RecoverInfo
	{
		get => null;
		set { if (value != null) throw new NotSupportedException("Legacy automatic recovery is not supported."); }
	}

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
		get => string.Empty;
		set
		{
			SetCore("initial catalog", value, strictDuplicate: false);
		}
	}

	/// <summary>Typed connection deadline. The legacy ConnectionTimeout property is milliseconds.</summary>
	public TimeSpan ConnectTimeout
	{
		get => TimeSpan.FromMilliseconds(ConnectionTimeout);
		set => SetMilliseconds(DmConst.PROP_KEY_CONNECTION_TIMEOUT, value, allowZero: true);
	}

	public TimeSpan PoolAcquireTimeout
	{
		get => TimeSpan.FromMilliseconds(ConnPoolTimeout);
		set => SetMilliseconds(DmConst.PROP_KEY_CONN_POOL_TIMEOUT, value, allowZero: true);
	}

	public TimeSpan ReadIdleTimeout
	{
		get => TimeSpan.FromMilliseconds(SocketTimeout);
		set => SetMilliseconds(DmConst.PROP_KEY_SOCKET_TIMEOUT, value, allowZero: true);
	}

	public TimeSpan CleanupTimeout
	{
		get => TimeSpan.FromMilliseconds(Convert.ToInt32(do_getThis("cleanup_timeout")));
		set => SetMilliseconds("cleanup_timeout", value, allowZero: false);
	}

	public DmTransportSecurity TransportSecurity
	{
		get => (DmTransportSecurity)do_getThis("transport_security");
		set => do_setThis("transport_security", value);
	}

	public string TlsCaCertificatePath
	{
		get => Convert.ToString(do_getThis(TlsCaCertificatePathKey));
		set => do_setThis(TlsCaCertificatePathKey, value);
	}

	public string TlsClientCertificatePath
	{
		get => Convert.ToString(do_getThis(TlsClientCertificatePathKey));
		set => do_setThis(TlsClientCertificatePathKey, value);
	}

	public string TlsClientPrivateKeyPath
	{
		get => Convert.ToString(do_getThis(TlsClientPrivateKeyPathKey));
		set => do_setThis(TlsClientPrivateKeyPathKey, value);
	}

	public string TlsClientCertificatePassword
	{
		get => Convert.ToString(do_getThis(TlsClientCertificatePasswordKey));
		set => do_setThis(TlsClientCertificatePasswordKey, value);
	}

	public DmTlsRevocationMode TlsRevocationMode
	{
		get => (DmTlsRevocationMode)do_getThis(TlsRevocationModeKey);
		set => do_setThis(TlsRevocationModeKey, value);
	}

	public bool PersistSecurityInfo
	{
		get => (bool)do_getThis("persist_security_info");
		set => do_setThis("persist_security_info", value);
	}

	public int MaxMessageSize
	{
		get => DmConnectionSettings.DefaultMaxMessageSize;
		set => do_setThis("max_message_size", value);
	}

	public int MaxMaterializedLobSize
	{
		get => DmConnectionSettings.DefaultMaxMaterializedLobSize;
		set => do_setThis("max_materialized_lob_size", value);
	}

	public int LobChunkSize
	{
		get => DmConnectionSettings.DefaultLobChunkSize;
		set => do_setThis("lob_chunk_size", value);
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
		get => do_getThis(keyword);
		set
		{
			try { SetCore(keyword, value, strictDuplicate: true); }
			catch { invalidAfterFailedAssignment = true; throw; }
		}
	}

	public override bool IsFixedSize => base.IsFixedSize;
	public override int Count => base.Count;
	public override ICollection Keys => base.Keys;
	public override ICollection Values => base.Values;

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
		foreach (DmOption option in options)
		{
			aliases.Add(option.Keyword, option.Keyword);
			if (option.Synonym != null)
				foreach (string synonym in option.Synonym)
					if (!aliases.TryAdd(synonym, option.Keyword) && !aliases[synonym].Equals(option.Keyword, StringComparison.OrdinalIgnoreCase))
						throw new InvalidOperationException("Conflicting legacy connection aliases.");
		}
	}

	private bool invalidAfterFailedAssignment;

	public DmConnectionStringBuilder()
	{
		do_Clear();
	}

	public DmConnectionStringBuilder(string connectionString) : this()
	{
		ConnectionString = connectionString;
	}

	// DbConnectionStringBuilder.ConnectionString is non-virtual. This typed entry point
	// validates on a temporary builder, then replaces the current state.
	public new string ConnectionString
	{
		get
		{
			EnsureValid();
			return base.ConnectionString;
		}
		set
		{
			var candidate = new DmConnectionStringBuilder();
			try
			{
				foreach (var (key, rawValue) in DmConnectionStringParser.Parse(value))
					candidate[key] = rawValue;
				candidate.EnsureValid();
				candidate.ToSettings();
			}
			catch (NotSupportedException)
			{
				throw;
			}
			catch (Exception)
			{
				throw new ArgumentException("Invalid connection string.", nameof(value));
			}
			var priorEntries = new List<KeyValuePair<string, object>>();
			foreach (string key in base.Keys) priorEntries.Add(new(key, base[key]));
			var priorProperty = new Dictionary<string, object>(property, StringComparer.OrdinalIgnoreCase);
			var priorSetProperty = new Dictionary<string, object>(setProperty, StringComparer.OrdinalIgnoreCase);
			bool priorInvalid = invalidAfterFailedAssignment;
			try
			{
				CopyValidated(candidate);
			}
			catch
			{
				base.Clear();
				foreach (var entry in priorEntries) base[entry.Key] = entry.Value;
				property.Clear();
				foreach (var entry in priorProperty) property[entry.Key] = entry.Value;
				setProperty.Clear();
				foreach (var entry in priorSetProperty) setProperty[entry.Key] = entry.Value;
				invalidAfterFailedAssignment = priorInvalid;
				throw new InvalidOperationException("Unable to replace connection settings.");
			}
		}
	}

	private void CopyValidated(DmConnectionStringBuilder candidate)
	{
		base.Clear();
		foreach (string key in candidate.baseKeys) base[key] = candidate.baseValue(key);
		property.Clear();
		foreach (var entry in candidate.property) property[entry.Key] = entry.Value;
		setProperty.Clear();
		foreach (var entry in candidate.setProperty) setProperty[entry.Key] = entry.Value;
		invalidAfterFailedAssignment = false;
	}

	private ICollection baseKeys => base.Keys;
	private object baseValue(string key) => base[key];

	internal DmConnectionSettings ToSettings()
	{
		EnsureValid();
		return new DmConnectionSettings(this);
	}

	public string ToRedactedString() => ToSettings().ToConnectionString(includeSecrets: false);

	private void EnsureValid()
	{
		if (invalidAfterFailedAssignment)
			throw new InvalidOperationException("The connection string builder must be cleared or assigned a valid connection string.");
	}

	private void SetMilliseconds(string key, TimeSpan value, bool allowZero)
	{
		if (value < TimeSpan.Zero || (!allowZero && value == TimeSpan.Zero) ||
			value.Ticks % TimeSpan.TicksPerMillisecond != 0 || value.TotalMilliseconds > int.MaxValue)
			throw new ArgumentOutOfRangeException(nameof(value), "The timeout must be a whole number of milliseconds within range.");
		do_setThis(key, checked((int)value.TotalMilliseconds));
	}

	private static string Canonical(string keyword)
	{
		if (keyword == null) throw new ArgumentNullException(nameof(keyword));
		keyword = keyword.Trim();
		if (keyword.Length == 0) throw new ArgumentException("A connection setting name is required.", nameof(keyword));
		if (keyword.Equals("host", StringComparison.OrdinalIgnoreCase)) return DmConst.PROP_KEY_SERVER;
		foreach (string newKey in new[] { "initial_catalog", "transport_security", "persist_security_info", "cleanup_timeout", "max_message_size", "max_materialized_lob_size", "lob_chunk_size",
			TlsCaCertificatePathKey, TlsClientCertificatePathKey, TlsClientPrivateKeyPathKey, TlsClientCertificatePasswordKey, TlsRevocationModeKey })
			if (keyword.Equals(newKey, StringComparison.OrdinalIgnoreCase)) return newKey;
		if (keyword.Equals("initial catalog", StringComparison.OrdinalIgnoreCase) ||
			keyword.Equals("initialcatalog", StringComparison.OrdinalIgnoreCase)) return "initial_catalog";
		if (keyword.Equals("transportsecurity", StringComparison.OrdinalIgnoreCase)) return "transport_security";
		if (keyword.Equals("tlscacertificatepath", StringComparison.OrdinalIgnoreCase)) return TlsCaCertificatePathKey;
		if (keyword.Equals("tlsclientcertificatepath", StringComparison.OrdinalIgnoreCase)) return TlsClientCertificatePathKey;
		if (keyword.Equals("tlsclientprivatekeypath", StringComparison.OrdinalIgnoreCase)) return TlsClientPrivateKeyPathKey;
		if (keyword.Equals("tlsclientcertificatepassword", StringComparison.OrdinalIgnoreCase)) return TlsClientCertificatePasswordKey;
		if (keyword.Equals("tlsrevocationmode", StringComparison.OrdinalIgnoreCase)) return TlsRevocationModeKey;
		if (keyword.Equals("persistsecurityinfo", StringComparison.OrdinalIgnoreCase)) return "persist_security_info";
		if (keyword.Equals("poolacquiretimeout", StringComparison.OrdinalIgnoreCase)) return DmConst.PROP_KEY_CONN_POOL_TIMEOUT;
		if (keyword.Equals("readidletimeout", StringComparison.OrdinalIgnoreCase)) return DmConst.PROP_KEY_SOCKET_TIMEOUT;
		if (keyword.Equals("cleanuptimeout", StringComparison.OrdinalIgnoreCase)) return "cleanup_timeout";
		if (keyword.Equals("maxmessagesize", StringComparison.OrdinalIgnoreCase)) return "max_message_size";
		if (keyword.Equals("maxmaterializedlobsize", StringComparison.OrdinalIgnoreCase)) return "max_materialized_lob_size";
		if (keyword.Equals("lobchunksize", StringComparison.OrdinalIgnoreCase)) return "lob_chunk_size";
		return aliases.TryGetValue(keyword, out string canonical) ? canonical :
			throw new NotSupportedException("Unknown connection setting.");
	}

	private static bool IsNewSetting(string key) => key is "initial_catalog" or "transport_security" or
		"persist_security_info" or "cleanup_timeout" or "max_message_size" or
		"max_materialized_lob_size" or "lob_chunk_size" or
		TlsCaCertificatePathKey or TlsClientCertificatePathKey or TlsClientPrivateKeyPathKey or
		TlsClientCertificatePasswordKey or TlsRevocationModeKey;

	private static bool ParseBoolean(object value, string key)
	{
		if (value is bool b) return b;
		string text = Convert.ToString(value, System.Globalization.CultureInfo.InvariantCulture)?.Trim() ?? string.Empty;
		if (text.Equals("true", StringComparison.OrdinalIgnoreCase) || text.Equals("yes", StringComparison.OrdinalIgnoreCase) || text == "1") return true;
		if (text.Equals("false", StringComparison.OrdinalIgnoreCase) || text.Equals("no", StringComparison.OrdinalIgnoreCase) || text == "0") return false;
		throw new ArgumentException($"Invalid boolean setting '{key}'.");
	}

	private static int ParseInt(object value, string key, int min, int max)
	{
		string text = Convert.ToString(value, System.Globalization.CultureInfo.InvariantCulture) ?? string.Empty;
		if (!int.TryParse(text, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out int number) || number < min || number > max)
			throw new ArgumentOutOfRangeException(key, $"Setting '{key}' is outside its supported range.");
		return number;
	}

	private static int ParseLanguage(object value)
	{
		string text = Convert.ToString(value, System.Globalization.CultureInfo.InvariantCulture) ?? string.Empty;
		if (int.TryParse(text, out int number) && number is >= 0 and <= 3) return number;
		if (Enum.TryParse<SupportedLanguage>(text, true, out var language) && Enum.IsDefined(language)) return (int)language;
		throw new ArgumentException("Invalid language setting.");
	}

	private static DmTransportSecurity ParseTransport(object value)
	{
		if (value is DmTransportSecurity policy && Enum.IsDefined(policy)) return policy;
		string text = Convert.ToString(value, System.Globalization.CultureInfo.InvariantCulture) ?? string.Empty;
		if (text.Equals(nameof(DmTransportSecurity.RequireTls), StringComparison.OrdinalIgnoreCase)) return DmTransportSecurity.RequireTls;
		if (text.Equals(nameof(DmTransportSecurity.PlaintextAllowed), StringComparison.OrdinalIgnoreCase)) return DmTransportSecurity.PlaintextAllowed;
		throw new ArgumentException("Invalid transport security setting.");
	}

	private static DmTlsRevocationMode ParseTlsRevocationMode(object value)
	{
		if (value is DmTlsRevocationMode mode && Enum.IsDefined(mode)) return mode;
		string text = Convert.ToString(value, System.Globalization.CultureInfo.InvariantCulture) ?? string.Empty;
		if (text.Equals(nameof(DmTlsRevocationMode.Online), StringComparison.OrdinalIgnoreCase)) return DmTlsRevocationMode.Online;
		if (text.Equals(nameof(DmTlsRevocationMode.NoCheck), StringComparison.OrdinalIgnoreCase)) return DmTlsRevocationMode.NoCheck;
		throw new ArgumentException("Invalid TLS revocation mode.");
	}

	private static string ParseTlsPath(object value)
	{
		string path = Convert.ToString(value) ?? string.Empty;
		if (path.Length == 0) return string.Empty;
		if (path.IndexOfAny(new[] { '\0', '\r', '\n' }) >= 0 || !Path.IsPathFullyQualified(path))
			throw new ArgumentException("TLS certificate paths must be absolute.");
		return path;
	}

	private static (string host, int? port) ParseServerValue(object value)
	{
		string server = Convert.ToString(value, System.Globalization.CultureInfo.InvariantCulture) ?? string.Empty;
		if (server.Length == 0) return (string.Empty, null);
		if (server.StartsWith('['))
		{
			int closing = server.IndexOf(']');
			if (closing <= 1) throw new ArgumentException("Invalid server address.");
			string host = server.Substring(1, closing - 1);
			if (!System.Net.IPAddress.TryParse(host, out var bracketAddress) || bracketAddress.AddressFamily != System.Net.Sockets.AddressFamily.InterNetworkV6)
				throw new ArgumentException("Invalid server address.");
			if (closing == server.Length - 1) return (host, null);
			if (server[closing + 1] != ':' || closing + 2 >= server.Length) throw new ArgumentException("Invalid server address.");
			return (host, ParseInt(server.Substring(closing + 2), "port", 1, 65535));
		}
		int colon = server.IndexOf(':');
		if (colon == 0) throw new ArgumentException("Invalid server address.");
		if (colon > 0 && colon == server.LastIndexOf(':'))
			return (server.Substring(0, colon), ParseInt(server.Substring(colon + 1), "port", 1, 65535));
		if (colon >= 0 && (!System.Net.IPAddress.TryParse(server, out var address) || address.AddressFamily != System.Net.Sockets.AddressFamily.InterNetworkV6))
			throw new ArgumentException("Invalid server address.");
		return (server, null); // bare IPv6 literal or hostname without a colon
	}

	private static bool IsSupported(string key) => key.Equals(DmConst.PROP_KEY_SERVER, StringComparison.OrdinalIgnoreCase) ||
		key.Equals(DmConst.PROP_KEY_PORT, StringComparison.OrdinalIgnoreCase) ||
		key.Equals(DmConst.PROP_KEY_USER, StringComparison.OrdinalIgnoreCase) ||
		key.Equals(DmConst.PROP_KEY_PASSWORD, StringComparison.OrdinalIgnoreCase) ||
		key.Equals(DmConst.PROP_KEY_SCHEMA, StringComparison.OrdinalIgnoreCase) ||
		key.Equals(DmConst.PROP_KEY_LANGUAGE, StringComparison.OrdinalIgnoreCase) ||
		key.Equals(DmConst.PROP_KEY_CONNECTION_TIMEOUT, StringComparison.OrdinalIgnoreCase) ||
		key.Equals(DmConst.PROP_KEY_COMMAND_TIMEOUT, StringComparison.OrdinalIgnoreCase) ||
		key.Equals(DmConst.PROP_KEY_CONN_POOL_TIMEOUT, StringComparison.OrdinalIgnoreCase) ||
		key.Equals(DmConst.PROP_KEY_SOCKET_TIMEOUT, StringComparison.OrdinalIgnoreCase) ||
		IsNewSetting(key);

	private static object Normalize(string key, object value)
	{
		if (key == "initial_catalog")
		{
			if (string.IsNullOrEmpty(Convert.ToString(value))) return string.Empty;
			throw new NotSupportedException("A nonempty InitialCatalog is not supported.");
		}
		if (key == "transport_security") return ParseTransport(value);
		if (key == TlsRevocationModeKey) return ParseTlsRevocationMode(value);
		if (key is TlsCaCertificatePathKey or TlsClientCertificatePathKey or TlsClientPrivateKeyPathKey) return ParseTlsPath(value);
		if (key == TlsClientCertificatePasswordKey) return Convert.ToString(value) ?? string.Empty;
		if (key == "persist_security_info") return ParseBoolean(value, key);
		if (key == "max_message_size") return ParseInt(value, key, DmConnectionSettings.DefaultMaxMessageSize, DmConnectionSettings.DefaultMaxMessageSize);
		if (key == "max_materialized_lob_size") return ParseInt(value, key, DmConnectionSettings.DefaultMaxMaterializedLobSize, DmConnectionSettings.DefaultMaxMaterializedLobSize);
		if (key == "lob_chunk_size") return ParseInt(value, key, DmConnectionSettings.DefaultLobChunkSize, DmConnectionSettings.DefaultLobChunkSize);
		if (key == "cleanup_timeout") return ParseInt(value, key, 1, int.MaxValue);
		if (key.Equals(DmConst.PROP_KEY_PORT, StringComparison.OrdinalIgnoreCase)) return ParseInt(value, key, 1, 65535);
		if (key.Equals(DmConst.PROP_KEY_LANGUAGE, StringComparison.OrdinalIgnoreCase)) return ParseLanguage(value);
		if (key.Equals(DmConst.PROP_KEY_CONNECTION_TIMEOUT, StringComparison.OrdinalIgnoreCase) ||
			key.Equals(DmConst.PROP_KEY_COMMAND_TIMEOUT, StringComparison.OrdinalIgnoreCase) ||
			key.Equals(DmConst.PROP_KEY_CONN_POOL_TIMEOUT, StringComparison.OrdinalIgnoreCase) ||
			key.Equals(DmConst.PROP_KEY_SOCKET_TIMEOUT, StringComparison.OrdinalIgnoreCase)) return ParseInt(value, key, 0, int.MaxValue);
		if (key.Equals(DmConst.PROP_KEY_SERVER, StringComparison.OrdinalIgnoreCase)) return ParseServerValue(value).host;
		if (key.Equals(DmConst.PROP_KEY_SCHEMA, StringComparison.OrdinalIgnoreCase))
			return DmSchemaValidator.Normalize(Convert.ToString(value));
		if (key.Equals(DmConst.PROP_KEY_USER, StringComparison.OrdinalIgnoreCase) ||
			key.Equals(DmConst.PROP_KEY_PASSWORD, StringComparison.OrdinalIgnoreCase)) return Convert.ToString(value) ?? string.Empty;
		DmOption option = DmOptionHelper.GetOption(key, options);
		if (option == null) throw new NotSupportedException("Unknown connection setting.");
		object defaultValue = option.Defaultvalue;
		if (defaultValue is bool defaultBoolean)
		{
			bool parsedBoolean = ParseBoolean(value, key);
			if (parsedBoolean != defaultBoolean) throw new NotSupportedException($"Setting '{key}' is not supported with a nondefault value.");
			return parsedBoolean;
		}
		if (defaultValue is string || defaultValue == null)
		{
			string text = Convert.ToString(value) ?? string.Empty;
			if (!string.Equals(text, Convert.ToString(defaultValue) ?? string.Empty, StringComparison.Ordinal))
				throw new NotSupportedException($"Setting '{key}' is not supported with a nondefault value.");
			return text;
		}
		if (defaultValue is Enum)
		{
			string text = Convert.ToString(value) ?? string.Empty;
			if (text.Equals(defaultValue.ToString(), StringComparison.OrdinalIgnoreCase) ||
				(long.TryParse(text, out long ordinal) && ordinal == Convert.ToInt64(defaultValue))) return defaultValue;
			throw new NotSupportedException($"Setting '{key}' is not supported with a nondefault value.");
		}
		string numericText = Convert.ToString(value, System.Globalization.CultureInfo.InvariantCulture) ?? string.Empty;
		if (!long.TryParse(numericText, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out long parsed) ||
			parsed != Convert.ToInt64(defaultValue))
			throw new NotSupportedException($"Setting '{key}' is not supported with a nondefault value.");
		return parsed;
	}

	private void SetCore(string keyword, object value, bool strictDuplicate)
	{
		string key = Canonical(keyword);
		if (value != null && value is not string && value is not bool && value is not byte &&
			value is not short && value is not int && value is not long && value is not Enum)
			throw new ArgumentException("Unsupported connection setting value type.");
		object normalized = Normalize(key, value);
		if (key == "initial_catalog")
		{
			base.Remove(key);
			return;
		}
		if (key.Equals(DmConst.PROP_KEY_SERVER, StringComparison.OrdinalIgnoreCase))
		{
			var parsed = ParseServerValue(value);
			if (parsed.port.HasValue)
			{
				if (strictDuplicate && setProperty.TryGetValue(DmConst.PROP_KEY_PORT, out var existingPort) && !Equals(existingPort, parsed.port.Value))
					throw new ArgumentException("Conflicting values for connection setting 'port'.");
			}
		}
		if (strictDuplicate && setProperty.TryGetValue(key, out var existing) && !Equals(existing, normalized))
			throw new ArgumentException($"Conflicting values for connection setting '{key}'.");
		property[key] = normalized;
		setProperty[key] = normalized;
		base[key] = normalized;
		if (key.Equals(DmConst.PROP_KEY_SERVER, StringComparison.OrdinalIgnoreCase))
		{
			var parsed = ParseServerValue(value);
			if (parsed.port.HasValue) SetCore(DmConst.PROP_KEY_PORT, parsed.port.Value, strictDuplicate);
		}
	}

	internal object do_getThis(string keyword)
	{
		EnsureValid();
		string key = Canonical(keyword);
		return property.TryGetValue(key, out object value) ? value : throw new NotSupportedException("Unknown connection setting.");
	}

	internal void do_setThis(string keyword, object value) => SetCore(keyword, value, strictDuplicate: false);

	internal void do_Clear()
	{
		base.Clear();
		property.Clear();
		setProperty.Clear();
		foreach (DmOption option in options)
			property[option.Keyword] = option.Defaultvalue;
		property[DmConst.PROP_KEY_SERVER] = string.Empty;
		property[DmConst.PROP_KEY_USER] = string.Empty;
		property[DmConst.PROP_KEY_PASSWORD] = string.Empty;
		property[DmConst.PROP_KEY_PORT] = 5236;
		property[DmConst.PROP_KEY_LANGUAGE] = 0;
		property[DmConst.PROP_KEY_CONNECTION_TIMEOUT] = 5000;
		property[DmConst.PROP_KEY_COMMAND_TIMEOUT] = 30;
		property[DmConst.PROP_KEY_CONN_POOL_TIMEOUT] = 5000;
		property[DmConst.PROP_KEY_SOCKET_TIMEOUT] = 0;
		property["cleanup_timeout"] = 5000;
		property["transport_security"] = DmTransportSecurity.RequireTls;
		property[TlsCaCertificatePathKey] = string.Empty;
		property[TlsClientCertificatePathKey] = string.Empty;
		property[TlsClientPrivateKeyPathKey] = string.Empty;
		property[TlsClientCertificatePasswordKey] = string.Empty;
		property[TlsRevocationModeKey] = DmTlsRevocationMode.Online;
		property["persist_security_info"] = false;
		property["max_message_size"] = DmConnectionSettings.DefaultMaxMessageSize;
		property["max_materialized_lob_size"] = DmConnectionSettings.DefaultMaxMaterializedLobSize;
		property["lob_chunk_size"] = DmConnectionSettings.DefaultLobChunkSize;
		invalidAfterFailedAssignment = false;
	}

	internal bool do_ContainsKey(string keyword) => property.ContainsKey(Canonical(keyword));
	internal bool do_Remove(string keyword)
	{
		string key = Canonical(keyword);
		bool removed = base.Remove(key);
		setProperty.Remove(key);
		if (key == "initial_catalog") return removed;
		DmOption option = DmOptionHelper.GetOption(key, options);
		if (option != null) property[key] = option.Defaultvalue;
		else property.Remove(key);
		if (key.Equals(DmConst.PROP_KEY_SERVER, StringComparison.OrdinalIgnoreCase) || key.Equals(DmConst.PROP_KEY_USER, StringComparison.OrdinalIgnoreCase) || key.Equals(DmConst.PROP_KEY_PASSWORD, StringComparison.OrdinalIgnoreCase)) property[key] = string.Empty;
		if (key.Equals(DmConst.PROP_KEY_COMMAND_TIMEOUT, StringComparison.OrdinalIgnoreCase)) property[key] = 30;
		if (key.Equals(DmConst.PROP_KEY_LANGUAGE, StringComparison.OrdinalIgnoreCase)) property[key] = 0;
		if (key == "transport_security") property[key] = DmTransportSecurity.RequireTls;
		if (key is TlsCaCertificatePathKey or TlsClientCertificatePathKey or TlsClientPrivateKeyPathKey or TlsClientCertificatePasswordKey) property[key] = string.Empty;
		if (key == TlsRevocationModeKey) property[key] = DmTlsRevocationMode.Online;
		if (key == "persist_security_info") property[key] = false;
		if (key == "cleanup_timeout") property[key] = 5000;
		if (key == "max_message_size") property[key] = DmConnectionSettings.DefaultMaxMessageSize;
		if (key == "max_materialized_lob_size") property[key] = DmConnectionSettings.DefaultMaxMaterializedLobSize;
		if (key == "lob_chunk_size") property[key] = DmConnectionSettings.DefaultLobChunkSize;
		if (key.Equals(DmConst.PROP_KEY_SOCKET_TIMEOUT, StringComparison.OrdinalIgnoreCase)) property[key] = 0;
		return removed;
	}

	internal bool do_EquivalentTo(DmConnectionStringBuilder other) => base.EquivalentTo(other);
	internal bool do_ShouldSerialize(string keyword) => base.ShouldSerialize(Canonical(keyword));
	internal bool do_TryGetValue(string keyword, out object value)
	{
		string key = Canonical(keyword);
		return property.TryGetValue(key, out value);
	}
	internal void do_GetProperties(Hashtable propertyDescriptors) => base.GetProperties(propertyDescriptors);

	public override void Clear() => do_Clear();
	public override bool ContainsKey(string keyword) => do_ContainsKey(keyword);
	public override bool Remove(string keyword) => do_Remove(keyword);
	public override bool EquivalentTo(DbConnectionStringBuilder connectionStringBuilder) =>
		connectionStringBuilder is DmConnectionStringBuilder other && do_EquivalentTo(other);
	public override bool ShouldSerialize(string keyword) => do_ShouldSerialize(keyword);
	public override bool TryGetValue(string keyword, out object value) => do_TryGetValue(keyword, out value);
	protected override void GetProperties(Hashtable propertyDescriptors) => do_GetProperties(propertyDescriptors);
}
