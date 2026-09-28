using System;
using System.Collections.Generic;
using System.IO;
using Dm.util;

namespace Dm.Config;

internal class DmOptionHelper
{
	internal delegate object SetDefaultDelegate();

	internal static SetDefaultDelegate localtimezone = () => Convert.ToInt16(TimeZone.CurrentTimeZone.GetUtcOffset(DateTime.Now).TotalMinutes);

	internal static SetDefaultDelegate defaultsvc_conf = delegate
	{
		if (Environment.OSVersion.VersionString.Contains("Windows"))
		{
			return Environment.GetFolderPath(Environment.SpecialFolder.Windows) + Path.DirectorySeparatorChar + "Sysnative" + Path.DirectorySeparatorChar + "dm_svc.conf";
		}
		string text = Environment.GetEnvironmentVariable("DM_SVC_PATH", EnvironmentVariableTarget.Process);
		if (StringUtil.isEmpty(text))
		{
			text = "/etc/dm_svc.conf";
		}
		return text;
	};

	internal static string serverDef = "localhost";

	internal static string userDef = "SYSDBA";

	internal static string passwordDef = "SYSDBA";

	internal static int portDef = 5236;

	internal static string encodingDef = "";

	internal static bool enlistDef = false;

	internal static int connectionTimeoutDef = 5000;

	internal static int commandTimeoutDef = 0;

	internal static int poolSizeDef = 100;

	internal static bool connPoolingDef = false;

	internal static bool stmtPoolingDef = true;

	internal static bool preparePoolingDef = false;

	internal static int preparePoolSizeDef = 100;

	internal static int connPoolSizeDef = 100;

	internal static bool connPoolCheckDef = false;

	internal static int connPoolTimeoutDef = connectionTimeoutDef;

	internal static int connPoolExpiredTimeDef = 0;

	internal static int connPoolClearIntervalDef = 10000;

	internal static bool escapeProcessDef = true;

	internal static string keyWordsDef = null;

	internal static string schemaDef = "";

	internal static bool schemaSensitiveDef = false;

	internal static string appnameDef = "";

	internal static string osDef = "";

	internal static string initialCatalogDef = "";

	internal static string databaseDef = "";

	internal static string hostDef = "";

	internal static bool loginEncryptDef = false;

	internal static string cipherPathDef = "";

	internal static bool directDef = true;

	internal static bool enableRsCacheDef = false;

	internal static int rsCacheSizeDef = 20;

	internal static int rsRefreshFreqDef = 10;

	internal static int rwSeparateDef = 0;

	internal static int rwPercentDef = 25;

	internal static int compressDef = DmConst.MSG_COMPRESS_NO;

	internal static int compressIdDef = DmConst.MSG_CPR_FUN_ID_ZIP;

	internal static LoginModeFlag login_modedef = LoginModeFlag.normalfirst;

	internal static TraceFlag tracedef = TraceFlag.none;

	internal static SupportedLanguage languagedef = SupportedLanguage.cn;

	internal static int timezonedef = (short)localtimezone();

	internal static string dm_svc_confdef = defaultsvc_conf().ToString();

	internal static LogLevel logleveldef = LogLevel.OFF;

	internal static string logdirdef = DriverUtil.FormatDir(Environment.CurrentDirectory);

	internal static int logSizedef = 104857600;

	internal static int logFlushFreqdef = 30;

	internal static int logBufferSizedef = 32768;

	internal static int lobModeDef = 1;

	internal static bool autoCommitDef = true;

	internal static bool alwaysAllowCommitDef = true;

	internal static int batchTypeDef = 1;

	internal static int batchAllowMaxErrorsDef = 0;

	internal static bool batchContinueOnErrorDef = false;

	internal static bool batchNotOnCallDef = true;

	internal static int bufPrefetchDef = 0;

	internal static bool clobAsStringDef = false;

	internal static bool columnNameUpperCaseDef = false;

	internal static ColumnNameCase columnNameCaseDef = ColumnNameCase.OFF;

	internal static string databaseProductNameDef = "";

	internal static CompatibleMode compatibleModeDef = CompatibleMode.OFF;

	internal static bool ignoreCaseDef = true;

	internal static bool isBdtaRsDef = false;

	internal static int maxRowsDef = 0;

	internal static int socketTimeoutDef = 0;

	internal static string addressRemapDef = "";

	internal static string userRemapDef = "";

	internal static EpSelector epSelectorDef = EpSelector.WELL_DISTRIBUTE;

	internal static int switchTimesDef = 1;

	internal static int switchIntervalDef = 200;

	internal static LoginStatus loginStatusDef = LoginStatus.OFF;

	internal static bool loginDscCtrlDef = false;

	internal static int rwStandbyRecoverTimeDef = 60000;

	internal static bool rwHADef = false;

	internal static bool rwAutoDistributeDef = true;

	internal static int rwFilterTypeDef = 2;

	internal static DoSwitch doSwitchDef = DoSwitch.OFF;

	internal static CLUSTER clusterDef = CLUSTER.NORMAL;

	internal static int dbAliveCheckFreqDef = 0;

	internal static int dbAliveCheckTimeoutDef = 10000;

	internal static int maxLobDataLenPerMsgDef = 32000;

	internal static bool dbTimeToTimeSpanDef = false;

	internal static bool caseSensitiveDef = false;

	internal static bool varchar36ToGuidDef = false;

	internal static bool useSkyWalkingDef = false;

	internal static IntervalMode intervalModeDef = IntervalMode.OFF;

	internal static string sslKeyPass = "changeit";

	internal static string sslFilePath = "";

	internal static bool convertToTz = false;

	internal static string catalog = "";

	internal static string DbaPasswordDef = "";

	internal static bool ShowExtraInfo = false;

	internal static bool EFCoreNextResultDef = true;

	internal static DmOption GetOption(string keyword, List<DmOption> options)
	{
		if (keyword == null)
		{
			throw new ArgumentNullException(keyword + "is a null reference");
		}
		keyword = keyword.Trim();
		foreach (DmOption option in options)
		{
			if (option.HasKey(keyword))
			{
				return option;
			}
		}
		return null;
	}

	internal static void SetProperty(DmOption option, object value, Dictionary<string, object> property)
	{
		property[option.Keyword] = value;
		if (option.Synonym != null)
		{
			string[] synonym = option.Synonym;
			foreach (string key in synonym)
			{
				property[key] = value;
			}
		}
	}
}
