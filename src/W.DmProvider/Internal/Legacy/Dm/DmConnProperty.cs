using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Text;
using W.Dm.Config;

namespace W.Dm;

internal class DmConnProperty
{
	private byte m_MppType;

	private bool m_NewLobFlag = true;

	private bool m_LongLobFlag = true;

	private int m_HeartBeatTimeout;

	private bool? m_AutoCommit;

	private int m_MaxRowSize;

	private int m_DDLAutoCommit;

	private bool m_CaseSensitive = true;

	private byte m_backslashEsc;

	private int m_FailedAttempts;

	private string m_LastLoginIP;

	private string m_LastLoginTime;

	private int m_LoginWarningID;

	private int m_GracetimeRemainder;

	private string m_guid;

	private string m_CurSchema;

	private IsolationLevel m_ISOlvl = IsolationLevel.ReadCommitted;

	private int m_encrypt;

	private int m_serials;

	private short m_DbTimeZone;

	private int m_sessClt = 3;

	private byte m_accessMode;

	private int m_MaxSession;

	private byte m_c2p = 1;

	private bool m_commited;

	private string m_serverVersion;

	private int m_verNum;

	private bool m_DbrwSeparate;

	private string m_standbyIp;

	private int m_standbyPort = 5236;

	private int m_standbyNum;

	private long _sessId = -1L;

	private bool rwStandby;

	private int svrMode;

	private int svrStat;

	private bool dscControl;

	internal string oracleDateFormat = "";

	internal string oracleTimestampFormat = "";

	internal string oracleTimestampTZFormat = "";

	internal string oracleTimeFormat = "";

	internal string oracleTimeTZFormat = "";

	internal string formatNumericChars;

	internal string proxyClient;

	private string serverEncoding = "gb18030";

	internal string m_servName;

	internal string m_serverActual;

	internal int m_portActual;

	internal bool m_crcBody;

	internal int m_propertyHashCode;

	internal int nlsDateLang;

	internal int msgVersion = 21;

	internal int lobOffRowLen = 16384;

	internal int rowidNBitsEpno;

	internal long rowidMaxHpno;

	internal int rowidMaxEpno;

	internal byte[] serverPubKey;

	internal int encryptType = -1;

	internal int hashType = -1;

	internal bool encryptPwd;

	internal bool encryptMsg;

	internal int algorithm;



	internal DmConnectionSettings Settings { get; private set; }

	internal Dictionary<string, object> property;

	internal static Dictionary<string, Encoding> encodingMap;

	internal string ConnectionString
	{
		get => Settings?.ToConnectionString(includeSecrets: true) ?? string.Empty;
		set => BindSettings(DmConnectionSettings.Parse(value));
	}

	internal void BindSettings(DmConnectionSettings settings)
	{
		if (settings == null) throw new ArgumentNullException(nameof(settings));
		var values = settings.ToLegacyProperties();
		property = values;
		Settings = settings;
		ServName = Server + ":" + Port;
		PropertyHashCode = 0; // No password-derived pool key or process hash.
	}

	internal EPGroup EPGroup
	{
		get
		{
			if (!property.ContainsKey(DmConst.PROP_KEY_EP_GROUP) || property[DmConst.PROP_KEY_EP_GROUP] == null)
			{
				return new EPGroup(new List<EP>
				{
					new EP(Server, Port)
				});
			}
			return (EPGroup)property[DmConst.PROP_KEY_EP_GROUP];
		}
		set
		{
			property[DmConst.PROP_KEY_EP_GROUP] = null;
		}
	}

	internal string Server
	{
		get
		{
			return Convert.ToString(property[DmConst.PROP_KEY_SERVER]);
		}
		set
		{
			property[DmConst.PROP_KEY_SERVER] = value;
		}
	}

	internal string User
	{
		get
		{
			return Convert.ToString(property[DmConst.PROP_KEY_USER]);
		}
		set
		{
			property[DmConst.PROP_KEY_USER] = value;
		}
	}

	internal string Pwd
	{
		get
		{
			return Convert.ToString(property[DmConst.PROP_KEY_PASSWORD]);
		}
		set
		{
			property[DmConst.PROP_KEY_PASSWORD] = value;
		}
	}

	internal int Port
	{
		get
		{
			return Convert.ToInt32(property[DmConst.PROP_KEY_PORT]);
		}
		set
		{
			property[DmConst.PROP_KEY_PORT] = value;
		}
	}

	internal string ServerEncoding
	{
		get
		{
			string text = property[DmConst.PROP_KEY_ENCODING].ToString();
			if (!text.Equals(""))
			{
				return text;
			}
			return serverEncoding;
		}
		set
		{
			serverEncoding = value;
		}
	}

	internal bool Enlist
	{
		get
		{
			try
			{
				return Convert.ToBoolean(property[DmConst.PROP_KEY_ENLIST]);
			}
			catch (KeyNotFoundException)
			{
				return false;
			}
		}
	}

	internal int ConnectionTimeout => Convert.ToInt32(property[DmConst.PROP_KEY_CONNECTION_TIMEOUT]);

	internal int CommandTimeout => Convert.ToInt32(property[DmConst.PROP_KEY_COMMAND_TIMEOUT]);

	internal int StmtPoolSize => Convert.ToInt32(property[DmConst.PROP_KEY_STMT_POOL_SIZE]);

	internal bool StmtPooling => Convert.ToBoolean(property[DmConst.PROP_KEY_STMT_POOLING]);

	internal bool PreparePooling => Convert.ToBoolean(property[DmConst.PROP_KEY_PSTMT_POOLING]);

	internal int PreparePoolSize => Convert.ToInt32(property[DmConst.PROP_KEY_PSTMT_POOL_SIZE]);

	internal bool ConnPooling
	{
		get
		{
			return Convert.ToBoolean(property[DmConst.PROP_KEY_CONN_POOLING]);
		}
		set
		{
			property[DmConst.PROP_KEY_CONN_POOLING] = value;
		}
	}

	internal int ConnPoolSize
	{
		get
		{
			return Convert.ToInt32(property[DmConst.PROP_KEY_CONN_POOL_SIZE]);
		}
		set
		{
			property[DmConst.PROP_KEY_CONN_POOL_SIZE] = value;
		}
	}

	internal bool ConnPoolCheck
	{
		get
		{
			return Convert.ToBoolean(property[DmConst.PROP_KEY_CONN_POOL_CHECK]);
		}
		set
		{
			property[DmConst.PROP_KEY_CONN_POOL_CHECK] = value;
		}
	}

	internal int ConnPoolTimeout
	{
		get
		{
			return Convert.ToInt32(property[DmConst.PROP_KEY_CONN_POOL_TIMEOUT]);
		}
		set
		{
			property[DmConst.PROP_KEY_CONN_POOL_TIMEOUT] = value;
		}
	}

	internal int ConnPoolIdleExpiredTime
	{
		get
		{
			return Convert.ToInt32(property[DmConst.PROP_KEY_CONN_POOL_IDLE_EXPIRED_TIME]);
		}
		set
		{
			property[DmConst.PROP_KEY_CONN_POOL_IDLE_EXPIRED_TIME] = value;
		}
	}

	internal int ConnPoolIdleClearInterval
	{
		get
		{
			return Convert.ToInt32(property[DmConst.PROP_KEY_CONN_POOL_IDLE_CLEAR_INTERVAL]);
		}
		set
		{
			property[DmConst.PROP_KEY_CONN_POOL_IDLE_CLEAR_INTERVAL] = value;
		}
	}

	internal bool EscapeProcess
	{
		get
		{
			return Convert.ToBoolean(property[DmConst.PROP_KEY_ESCAPE_PROCESS]);
		}
		set
		{
			property[DmConst.PROP_KEY_ESCAPE_PROCESS] = value;
		}
	}

	internal short TimeZone
	{
		get
		{
			return Convert.ToInt16(property[DmConst.PROP_KEY_TIMEZONE]);
		}
		set
		{
			property[DmConst.PROP_KEY_TIMEZONE] = value;
		}
	}

	internal int Language => Convert.ToInt32(property[DmConst.PROP_KEY_LANGUAGE]);

	internal string[] ResveredList
	{
		get
		{
			if (property[DmConst.PROP_KEY_KEYWORDS] == null)
			{
				return null;
			}
			return property[DmConst.PROP_KEY_KEYWORDS].ToString().Split(new char[1] { ',' });
		}
	}

	internal LoginModeFlag LoginMode
	{
		get
		{
			return (LoginModeFlag)property[DmConst.PROP_KEY_LOGIN_MODE];
		}
		set
		{
			property[DmConst.PROP_KEY_LOGIN_MODE] = value;
		}
	}

	internal string Schema
	{
		get
		{
			return Convert.ToString(property[DmConst.PROP_KEY_SCHEMA]);
		}
		set
		{
			property[DmConst.PROP_KEY_SCHEMA] = value;
		}
	}

	internal bool SchemaSensitive
	{
		get
		{
			return Convert.ToBoolean(property[DmConst.PROP_KEY_SCHEMA_SENSITIVE]);
		}
		set
		{
			property[DmConst.PROP_KEY_SCHEMA_SENSITIVE] = value;
		}
	}

	internal string AppName => Convert.ToString(property[DmConst.PROP_KEY_APPNAME]);

	internal string Database
	{
		get
		{
			if (!property.ContainsKey(DmConst.PROP_KEY_DATABASE))
			{
				return "";
			}
			return Convert.ToString(property[DmConst.PROP_KEY_DATABASE]);
		}
		set
		{
			property[DmConst.PROP_KEY_DATABASE] = value;
		}
	}

	internal string Host
	{
		get
		{
			return Convert.ToString(property[DmConst.PROP_KEY_HOST]);
		}
		set
		{
			property[DmConst.PROP_KEY_HOST] = value;
		}
	}

	internal string OS => Convert.ToString(property[DmConst.PROP_KEY_OS]);

	internal string SvcConfPath => Convert.ToString(property[DmConst.PROP_KEY_DM_SVC_PATH]);

	internal LogLevel LogLevel => (LogLevel)property[DmConst.PROP_KEY_LOG_LEVEL];

	internal string LogDir => Convert.ToString(property[DmConst.PROP_KEY_LOG_DIR]);

	internal int LogSize => Convert.ToInt32(property[DmConst.PROP_KEY_LOG_SIZE]);

	internal int RwSeparate
	{
		get
		{
			return Convert.ToInt32(property[DmConst.PROP_KEY_RW_SEPARATE]);
		}
		set
		{
			property[DmConst.PROP_KEY_RW_SEPARATE] = value;
			if (Convert.ToInt32(property[DmConst.PROP_KEY_RW_SEPARATE]) > 0)
			{
				property[DmConst.PROP_KEY_LOGIN_MODE] = LoginModeFlag.onlyprimary;
			}
		}
	}

	internal int RwPercent => Convert.ToInt32(property[DmConst.PROP_KEY_RW_PERCENT]);

	internal int Compress
	{
		get
		{
			return Convert.ToInt32(property[DmConst.PROP_KEY_COMPRESS]);
		}
		set
		{
			property[DmConst.PROP_KEY_COMPRESS] = value;
		}
	}

	internal int CompressId => Convert.ToInt32(property[DmConst.PROP_KEY_COMPRESS_ID]);

	internal bool LoginEncrypt => Convert.ToBoolean(property[DmConst.PROP_KEY_LOGIN_ENCRYPT]);

	internal string CipherPath => Convert.ToString(property[DmConst.PROP_KEY_CIPHER_PATH]);

	internal bool EnRsCache => Convert.ToBoolean(property[DmConst.PROP_KEY_ENABLE_RS_CACHE]);

	internal int RsCacheSize => Convert.ToInt32(property[DmConst.PROP_KEY_RS_CACHE_SIZE]);

	internal int RsRefreshFreq => Convert.ToInt32(property[DmConst.PROP_KEY_RS_REFRESH_FREQ]);

	internal int LobMode => Convert.ToInt32(property[DmConst.PROP_KEY_LOB_MODE]);

	internal bool AutoCommit
	{
		get
		{
			if (!m_AutoCommit.HasValue)
			{
				m_AutoCommit = Convert.ToBoolean(property[DmConst.PROP_KEY_AUTO_COMMIT]);
			}
			return m_AutoCommit.Value;
		}
		set
		{
			m_AutoCommit = value;
		}
	}

	internal bool AlwaysAllowAutoCommit => Convert.ToBoolean(property[DmConst.PROP_KEY_ALWAYS_ALLOW_COMMIT]);

	internal int BatchType => Convert.ToInt32(property[DmConst.PROP_KEY_BATCH_TYPE]);

	internal bool BatchContinueOnError => Convert.ToBoolean(property[DmConst.PROP_KEY_BATCH_CONTINUE_ON_ERROR]);

	internal bool BatchNotOnCall => Convert.ToBoolean(property[DmConst.PROP_KEY_BATCH_NOT_ON_CALL]);

	internal int BatchAllowMaxErrors => Convert.ToInt32(property[DmConst.PROP_KEY_BATCH_ALLOW_MAX_ERRORS]);

	internal int BufPrefetch
	{
		get
		{
			return Convert.ToInt32(property[DmConst.PROP_KEY_BUF_PREFETCH]);
		}
		set
		{
			property[DmConst.PROP_KEY_BUF_PREFETCH] = value;
		}
	}

	internal bool ClobAsString => Convert.ToBoolean(property[DmConst.PROP_KEY_CLOB_AS_STRING]);

	internal bool ColumnNameUpperCase => Convert.ToBoolean(property[DmConst.PROP_KEY_COLUMN_NAME_UPPERCASE]);

	internal ColumnNameCase ColumnNameCase => (ColumnNameCase)Convert.ToInt32(property[DmConst.PROP_KEY_COLUMN_NAME_CASE]);

	internal string DatabaseProductName => Convert.ToString(property[DmConst.PROP_KEY_DATABASE_PRODUCT_NAME]);

	internal CompatibleMode CompatibleMode => (CompatibleMode)Convert.ToInt32(property[DmConst.PROP_KEY_COMPATIBLE_MODE]);

	internal bool IgnoreCase => Convert.ToBoolean(property[DmConst.PROP_KEY_IGNORE_CASE]);

	internal bool IsBdtaRs
	{
		get
		{
			return Convert.ToBoolean(property[DmConst.PROP_KEY_IS_BDTA_RS]);
		}
		set
		{
			property[DmConst.PROP_KEY_IS_BDTA_RS] = value;
		}
	}

	internal long MaxRows => Convert.ToInt64(property[DmConst.PROP_KEY_MAX_ROWS]);

	internal int SocketTimeout
	{
		get
		{
			if (m_HeartBeatTimeout != 0)
			{
				return m_HeartBeatTimeout;
			}
			return Convert.ToInt32(property[DmConst.PROP_KEY_SOCKET_TIMEOUT]);
		}
	}

	internal string AddressRemap => Convert.ToString(property[DmConst.PROP_KEY_ADDRESS_REMAP]);

	internal string UserRemap => Convert.ToString(property[DmConst.PROP_KEY_USER_REMAP]);

	internal EpSelector EpSelector => (EpSelector)property[DmConst.PROP_KEY_EP_SELECTOR];

	internal int SwitchTimes
	{
		get
		{
			return Convert.ToInt32(property[DmConst.PROP_KEY_SWITCH_TIMES]);
		}
		set
		{
			property[DmConst.PROP_KEY_SWITCH_TIMES] = value;
		}
	}

	internal int SwitchInterval => Convert.ToInt32(property[DmConst.PROP_KEY_SWITCH_INTERVAL]);

	internal LoginStatus LoginStatus => (LoginStatus)property[DmConst.PROP_KEY_LOGIN_STATUS];

	internal bool LoginDscCtrl => Convert.ToBoolean(property[DmConst.PROP_KEY_LOGIN_DSC_CTRL]);

	internal int RwStandbyRecoverTime => Convert.ToInt32(property[DmConst.PROP_KEY_RW_STANDBY_RECOVER_TIME]);

	internal bool RWHA => Convert.ToBoolean(property[DmConst.PROP_KEY_RW_HA]);

	internal bool RwAutoDistribute => Convert.ToBoolean(property[DmConst.PROP_KEY_RW_AUTO_DISTRIBUTE]);

	internal long RwFilterType => Convert.ToInt64(property[DmConst.PROP_KEY_RW_FILTER_TYPE]);

	internal DoSwitch DoSwitch => (DoSwitch)property[DmConst.PROP_KEY_DO_SWITCH];

	internal CLUSTER Cluster => (CLUSTER)property[DmConst.PROP_KEY_CLUSTER];

	internal int DbAliveCheckFreq => Convert.ToInt32(property[DmConst.PROP_KEY_DB_ALIVE_CHECK_FREQ]);

	internal int DbAliveCheckTimeout => Convert.ToInt32(property[DmConst.PROP_KEY_DB_ALIVE_CHECK_TIMEOUT]);

	internal int MaxLobDataLenPerMsg => Convert.ToInt32(property[DmConst.PROP_KEY_MAX_LOB_DATA_LEN_PER_MSG]);

	internal bool DbTimeToTimeSpan => Convert.ToBoolean(property[DmConst.PROP_KEY_DBTIME_TO_TIMESPAN]);

	internal bool caseSensitive => Convert.ToBoolean(property[DmConst.PROP_KEY_CASE_SENSITIVE]);

	internal bool Varchar36ToGuid => Convert.ToBoolean(property[DmConst.PROP_KEY_VARCHAR36_TO_GUID]);

	internal bool UseSkyWalking => Convert.ToBoolean(property[DmConst.PROP_KEY_USE_SKYWALKING]);

	internal IntervalMode IntervalMode => (IntervalMode)property[DmConst.PROP_KEY_INTERVAL_MODE];

	internal string SslFilesPath => Convert.ToString(property[DmConst.PROP_KEY_SSL_FILE_PATH]);

	internal string SslKeyPass => Convert.ToString(property[DmConst.PROP_KEY_SSL_KEY_PASS]);

	internal bool ConvertToTz => Convert.ToBoolean(property[DmConst.PROP_KEY_CONVERT_TO_TZ]);

	internal string Catalog => Convert.ToString(property[DmConst.PROP_KEY_CATALOG]);

	internal string DBAPassword => Convert.ToString(property[DmConst.PROP_KEY_DBA_PASSWORD]);

	internal bool ShowExtraInfo => Convert.ToBoolean(property[DmConst.PROP_KEY_SHOW_EXTRA_INFO]);

	internal bool EFCoreNextResult => Convert.ToBoolean(property[DmConst.PROP_KEY_EFCORE_NEXT_RESULT]);

	internal string ServName
	{
		get
		{
			return m_servName;
		}
		set
		{
			m_servName = value;
		}
	}

	internal string ServerActual
	{
		get
		{
			return m_serverActual;
		}
		set
		{
			m_serverActual = value;
		}
	}

	internal int PortActual
	{
		get
		{
			return m_portActual;
		}
		set
		{
			m_portActual = value;
		}
	}

	internal int StandbyNum
	{
		get
		{
			return m_standbyNum;
		}
		set
		{
			m_standbyNum = value;
		}
	}

	internal string StandbyIp
	{
		get
		{
			return m_standbyIp;
		}
		set
		{
			m_standbyIp = value;
		}
	}

	internal int StandbyPort
	{
		get
		{
			return m_standbyPort;
		}
		set
		{
			m_standbyPort = value;
		}
	}

	internal long SessId
	{
		get
		{
			return _sessId;
		}
		set
		{
			_sessId = value;
		}
	}

	internal bool DbrwSeparate
	{
		get
		{
			return m_DbrwSeparate;
		}
		set
		{
			m_DbrwSeparate = value;
		}
	}

	internal byte MppType
	{
		get
		{
			return m_MppType;
		}
		set
		{
			m_MppType = value;
		}
	}

	internal bool NewLobFlag
	{
		get
		{
			return m_NewLobFlag;
		}
		set
		{
			m_NewLobFlag = value;
		}
	}

	internal bool LongLobFlag
	{
		get
		{
			return m_LongLobFlag;
		}
		set
		{
			m_LongLobFlag = value;
		}
	}

	internal int HeartBeatTimeout
	{
		get
		{
			return m_HeartBeatTimeout;
		}
		set
		{
			m_HeartBeatTimeout = value;
		}
	}

	internal int VerNum
	{
		get
		{
			return m_verNum;
		}
		set
		{
			m_verNum = value;
		}
	}

	internal string ServerVersion
	{
		get
		{
			return m_serverVersion;
		}
		set
		{
			m_serverVersion = value;
		}
	}

	internal int Serials
	{
		get
		{
			return m_serials;
		}
		set
		{
			m_serials = value;
		}
	}

	internal int Encrypt
	{
		get
		{
			return m_encrypt;
		}
		set
		{
			m_encrypt = value;
		}
	}

	internal bool Commited
	{
		get
		{
			return m_commited;
		}
		set
		{
			m_commited = value;
		}
	}

	internal byte C2p
	{
		get
		{
			return m_c2p;
		}
		set
		{
			m_c2p = value;
		}
	}

	internal byte BackslashEsc
	{
		get
		{
			return m_backslashEsc;
		}
		set
		{
			m_backslashEsc = value;
		}
	}

	internal int MaxSession
	{
		get
		{
			return m_MaxSession;
		}
		set
		{
			m_MaxSession = value;
		}
	}

	internal byte AccessMode
	{
		get
		{
			return m_accessMode;
		}
		set
		{
			m_accessMode = value;
		}
	}

	internal int SessClt => m_sessClt;

	internal IsolationLevel IsolationLevel
	{
		get
		{
			return m_ISOlvl;
		}
		set
		{
			m_ISOlvl = value;
		}
	}

	internal string CurrentSchema
	{
		get
		{
			return m_CurSchema;
		}
		set
		{
			if (string.IsNullOrEmpty(Schema) || Schema.ToUpper().Equals(value))
			{
				Schema = value;
				m_CurSchema = value;
			}
		}
	}

	internal short DbTimeZone
	{
		get
		{
			return m_DbTimeZone;
		}
		set
		{
			m_DbTimeZone = value;
		}
	}

	internal int MaxRowSize
	{
		get
		{
			return m_MaxRowSize;
		}
		set
		{
			m_MaxRowSize = value;
		}
	}

	internal int DDLAutoCommit
	{
		get
		{
			return m_DDLAutoCommit;
		}
		set
		{
			m_DDLAutoCommit = value;
		}
	}

	internal bool CaseSensitive
	{
		get
		{
			return m_CaseSensitive;
		}
		set
		{
			m_CaseSensitive = value;
		}
	}

	internal int FailedAttempts
	{
		get
		{
			return m_FailedAttempts;
		}
		set
		{
			m_FailedAttempts = value;
		}
	}

	internal string LastLoginIP
	{
		get
		{
			return m_LastLoginIP;
		}
		set
		{
			m_LastLoginIP = value;
		}
	}

	internal string LastLoginTime
	{
		get
		{
			return m_LastLoginTime;
		}
		set
		{
			m_LastLoginTime = value;
		}
	}

	internal int LoginWarningID
	{
		get
		{
			return m_LoginWarningID;
		}
		set
		{
			m_LoginWarningID = value;
		}
	}

	internal int GracetimeRemainder
	{
		get
		{
			return m_GracetimeRemainder;
		}
		set
		{
			m_GracetimeRemainder = value;
		}
	}

	internal string Guid
	{
		get
		{
			return m_guid;
		}
		set
		{
			m_guid = value;
		}
	}

	internal bool RWStandby
	{
		get
		{
			return rwStandby;
		}
		set
		{
			rwStandby = value;
		}
	}

	internal int SvrMode
	{
		get
		{
			return svrMode;
		}
		set
		{
			svrMode = value;
		}
	}

	internal int SvrStat
	{
		get
		{
			return svrStat;
		}
		set
		{
			svrStat = value;
		}
	}

	internal bool DscControl
	{
		get
		{
			return dscControl;
		}
		set
		{
			dscControl = value;
		}
	}

	internal int PropertyHashCode
	{
		get
		{
			return m_propertyHashCode;
		}
		set
		{
			m_propertyHashCode = value;
		}
	}

	internal bool crcBody
	{
		get
		{
			return m_crcBody;
		}
		set
		{
			m_crcBody = value;
		}
	}

	private void AdjustProperty()
	{
		if (RwSeparate > 0)
		{
			LoginMode = LoginModeFlag.onlyprimary;
			ConnPooling = false;
		}
	}

	static DmConnProperty()
	{
		encodingMap = new Dictionary<string, Encoding>();
		if (encodingMap.Count != 0)
		{
			return;
		}
		lock (encodingMap)
		{
			if (encodingMap.Count == 0)
			{
				Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
				encodingMap.Add("default", Encoding.Default);
				encodingMap.Add("utf-8", Encoding.GetEncoding("utf-8"));
				encodingMap.Add("UTF-8", Encoding.GetEncoding("UTF-8"));
				encodingMap.Add("gb18030", Encoding.GetEncoding("gb18030"));
				encodingMap.Add("GB18030", Encoding.GetEncoding("GB18030"));
				encodingMap.Add("BIG5", Encoding.GetEncoding("BIG5"));
			}
		}
	}

	internal DmConnProperty(string connectionstring)
		: this()
	{
		ConnectionString = connectionstring;
	}

	internal DmConnProperty()
	{
		property = new Dictionary<string, object>(new DmConnectionStringBuilder().property, StringComparer.OrdinalIgnoreCase);
	}

	internal void ClearAutoCommit()
	{
		m_AutoCommit = null;
	}

	public DmConnProperty Clone()
	{
		DmConnProperty dmConnProperty = new DmConnProperty();
		dmConnProperty.m_MppType = m_MppType;
		dmConnProperty.m_NewLobFlag = m_NewLobFlag;
		dmConnProperty.m_LongLobFlag = m_LongLobFlag;
		dmConnProperty.m_HeartBeatTimeout = m_HeartBeatTimeout;
		dmConnProperty.m_AutoCommit = m_AutoCommit;
		dmConnProperty.m_MaxRowSize = m_MaxRowSize;
		dmConnProperty.m_DDLAutoCommit = m_DDLAutoCommit;
		dmConnProperty.m_CaseSensitive = m_CaseSensitive;
		dmConnProperty.m_backslashEsc = m_backslashEsc;
		dmConnProperty.m_FailedAttempts = m_FailedAttempts;
		dmConnProperty.m_LastLoginIP = m_LastLoginIP;
		dmConnProperty.m_FailedAttempts = m_FailedAttempts;
		dmConnProperty.m_LastLoginTime = m_LastLoginTime;
		dmConnProperty.m_LoginWarningID = m_LoginWarningID;
		dmConnProperty.m_GracetimeRemainder = m_GracetimeRemainder;
		dmConnProperty.m_guid = m_guid;
		dmConnProperty.m_CurSchema = m_CurSchema;
		dmConnProperty.m_ISOlvl = m_ISOlvl;
		dmConnProperty.m_serials = m_serials;
		dmConnProperty.m_DbTimeZone = m_DbTimeZone;
		dmConnProperty.m_sessClt = m_sessClt;
		dmConnProperty.m_MaxSession = m_MaxSession;
		dmConnProperty.m_c2p = m_c2p;
		dmConnProperty.m_commited = m_commited;
		dmConnProperty.m_serverVersion = m_serverVersion;
		dmConnProperty.m_DbrwSeparate = m_DbrwSeparate;
		dmConnProperty.m_standbyIp = m_standbyIp;
		dmConnProperty.m_standbyPort = m_standbyPort;
		dmConnProperty.m_standbyNum = m_standbyNum;
		dmConnProperty._sessId = _sessId;
		dmConnProperty.rwStandby = rwStandby;
		dmConnProperty.svrMode = svrMode;
		dmConnProperty.svrStat = svrStat;
		dmConnProperty.dscControl = dscControl;
		dmConnProperty.oracleDateFormat = oracleDateFormat;
		dmConnProperty.oracleTimestampFormat = oracleTimestampFormat;
		dmConnProperty.oracleTimestampTZFormat = oracleTimestampTZFormat;
		dmConnProperty.oracleTimeFormat = oracleTimeFormat;
		dmConnProperty.oracleTimeTZFormat = oracleTimeTZFormat;
		dmConnProperty.formatNumericChars = formatNumericChars;
		dmConnProperty.proxyClient = proxyClient;
		dmConnProperty.serverEncoding = serverEncoding;
		dmConnProperty.m_servName = m_servName;
		dmConnProperty.m_serverActual = m_serverActual;
		dmConnProperty.m_portActual = m_portActual;
		dmConnProperty.m_crcBody = m_crcBody;
		dmConnProperty.m_propertyHashCode = m_propertyHashCode;
		dmConnProperty.nlsDateLang = nlsDateLang;
		dmConnProperty.msgVersion = msgVersion;
		dmConnProperty.lobOffRowLen = lobOffRowLen;
		dmConnProperty.rowidNBitsEpno = rowidNBitsEpno;
		dmConnProperty.rowidMaxHpno = rowidMaxHpno;
		dmConnProperty.rowidMaxEpno = rowidMaxEpno;
		dmConnProperty.serverPubKey = serverPubKey;
		dmConnProperty.encryptType = encryptType;
		dmConnProperty.hashType = hashType;
		dmConnProperty.encryptPwd = encryptPwd;
		dmConnProperty.encryptMsg = encryptMsg;
		if (Settings != null)
			dmConnProperty.BindSettings(Settings);
		dmConnProperty.algorithm = algorithm;
		return dmConnProperty;
	}
}
