using System;
using System.Collections.Generic;

namespace W.Dm;

public class SetEnvInfo : ICloneable
{
	private int setIdentity;

	private int sorted = 1;

	private int bdtaSize = 5000;

	private int indexOption = 1;

	private int noMpp = 1;

	private int charset;

	private int lobFromMsg;

	private int ignoreConflict;

	private short setId;

	private short bldrNumber = 64;

	private string schemaName;

	private string tableName;

	private byte flushFlag;

	private byte parallelFlag;

	private int msgVersion;

	private List<SequenceNumInfo> sequenceNumInfos;

	public SetEnvInfo()
	{
	}

	public SetEnvInfo(int setIdentity, int sorted, int bdtaSize, int indexOption, int noMpp, int charset, int lobFromMsg, int ignoreConflict, short setId, short bldrNumber, string schemaName, string tableName, byte flushFlag, byte parallelFlag)
	{
		this.setIdentity = setIdentity;
		this.sorted = sorted;
		this.bdtaSize = bdtaSize;
		this.indexOption = indexOption;
		this.noMpp = noMpp;
		this.charset = charset;
		this.lobFromMsg = lobFromMsg;
		this.ignoreConflict = ignoreConflict;
		this.setId = setId;
		this.bldrNumber = bldrNumber;
		this.schemaName = schemaName;
		this.tableName = tableName;
		this.flushFlag = flushFlag;
		this.parallelFlag = parallelFlag;
	}

	public int getSetIdentity()
	{
		return setIdentity;
	}

	public void setSetIdentity(int setIdentity)
	{
		this.setIdentity = setIdentity;
	}

	public int getSorted()
	{
		return sorted;
	}

	public void setSorted(int sorted)
	{
		this.sorted = sorted;
	}

	public int getBdtaSize()
	{
		return bdtaSize;
	}

	public void setBdtaSize(int bdtaSize)
	{
		if (bdtaSize > 10000 || bdtaSize < 100)
		{
			DmError.ThrowDmException(DmErrorDefinition.ECNET_INVALID_PARAMETER_VALUE, "maxRows range is [100, 10000]");
		}
		this.bdtaSize = bdtaSize;
	}

	public int getIndexOption()
	{
		return indexOption;
	}

	public void setIndexOption(int indexOption)
	{
		this.indexOption = indexOption;
	}

	public int getNoMpp()
	{
		return noMpp;
	}

	public void setNoMpp(int noMpp)
	{
		this.noMpp = noMpp;
	}

	public int getCharset()
	{
		return charset;
	}

	public int getLobFromMsg()
	{
		return lobFromMsg;
	}

	public void setLobFromMsg(int lobFromMsg)
	{
		this.lobFromMsg = lobFromMsg;
	}

	public int getIgnoreConflict()
	{
		return ignoreConflict;
	}

	public void setIgnoreConflict(int ignoreConflict)
	{
		this.ignoreConflict = ignoreConflict;
	}

	public short getSetId()
	{
		return setId;
	}

	public void setSetId(short setId)
	{
		this.setId = setId;
	}

	public short getBldrNumber()
	{
		return bldrNumber;
	}

	public void setBldrNumber(short bldrNumber)
	{
		if (bldrNumber <= 0 || bldrNumber > 1024)
		{
			DmError.ThrowDmException(DmErrorDefinition.ECNET_INVALID_PARAMETER_VALUE, "bldrNumber should be [1, 1024]");
		}
		this.bldrNumber = bldrNumber;
	}

	public string getSchemaName()
	{
		return schemaName;
	}

	public void setSchemaName(string schemaName)
	{
		this.schemaName = schemaName;
	}

	public string getTableName()
	{
		return tableName;
	}

	public void setTableName(string tableName)
	{
		this.tableName = tableName;
	}

	public byte getFlushFlag()
	{
		return flushFlag;
	}

	public void setFlushFlag(byte flushFlag)
	{
		this.flushFlag = flushFlag;
	}

	public byte getParallelFlag()
	{
		return parallelFlag;
	}

	public void setParallelFlag(byte parallelFlag)
	{
		this.parallelFlag = parallelFlag;
	}

	public List<SequenceNumInfo> getSequenceNumInfos()
	{
		return sequenceNumInfos;
	}

	public void setSequenceNumInfos(List<SequenceNumInfo> sequenceNumInfos)
	{
		this.sequenceNumInfos = sequenceNumInfos;
	}

	public void setCharset(string charset)
	{
		if ("UTF-8".Equals(charset))
		{
			this.charset = 1;
		}
		else if ("GBK".Equals(charset))
		{
			this.charset = 2;
		}
		else if ("BIG5".Equals(charset))
		{
			this.charset = 3;
		}
		else if ("ISO-8859-9".Equals(charset))
		{
			this.charset = 4;
		}
		else if ("EUC_JP".Equals(charset))
		{
			this.charset = 5;
		}
		else if ("EUC-KR".Equals(charset))
		{
			this.charset = 6;
		}
		else if ("KOI8_R".Equals(charset))
		{
			this.charset = 7;
		}
		else if ("ISO-8859-1".Equals(charset))
		{
			this.charset = 8;
		}
		else if ("US-ASCII".Equals(charset))
		{
			this.charset = 9;
		}
		else if ("GB18030".Equals(charset))
		{
			this.charset = 10;
		}
		else if ("UTF-16".Equals(charset))
		{
			this.charset = 12;
		}
		else
		{
			this.charset = 0;
		}
	}

	public int getMsgVersion()
	{
		return msgVersion;
	}

	public void setMsgVersion(int msgVersion)
	{
		this.msgVersion = msgVersion;
	}

	public object clone()
	{
		return Clone();
	}

	public object Clone()
	{
		return MemberwiseClone();
	}
}
