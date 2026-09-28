using System.IO;
using Dm.util;

namespace Dm;

public class DmClob : AbstractLob
{
	public string data = "";

	public string serverEncoding;

	internal DmClob(byte[] value, DmConnInstance connInstance, DmField column, bool fetchAll)
		: base(value, 1, connInstance, column)
	{
		serverEncoding = connInstance.ConnProperty.ServerEncoding;
		if (storageType == 1)
		{
			int headSize = getHeadSize();
			data = ByteUtil.getString(value, headSize, (int)bytesLength, serverEncoding);
			m_length = data.length();
		}
		else if (fetchAll)
		{
			loadAllData();
		}
	}

	internal DmClob(string data, DmConnInstance connInstance)
		: base(1, connInstance)
	{
		serverEncoding = connInstance.ConnProperty.ServerEncoding;
		this.data = data;
		m_length = this.data.length();
	}

	internal static DmClob newInstance(byte[] data, DmConnInstance conn, DmField column, bool fetchAll)
	{
		return new DmClob(data, conn, column, fetchAll);
	}

	public static DmClob newInstance(string data, DmConnection connection)
	{
		return new DmClob(data, connection.m_ConnInst);
	}

	public string getSubString(long pos, int len)
	{
		return do_getSubString(pos + 1, len);
	}

	public string do_getSubString(long pos, int len)
	{
		if (pos < 1 || len < 0)
		{
			DmError.ThrowDmException(DmErrorDefinition.ECNET_INVALID_LENGTH_OR_OFFSET);
		}
		pos--;
		long num = do_length() - pos;
		if (num < 0)
		{
			DmError.ThrowDmException(DmErrorDefinition.ECNET_INVALID_LENGTH_OR_OFFSET);
		}
		len = (int)((len > num) ? num : len);
		if (local || storageType == 1 || fetchAll)
		{
			if (pos > do_length())
			{
				DmError.ThrowDmException(DmErrorDefinition.ECNET_INVALID_LENGTH_OR_OFFSET);
			}
			return data.substring((int)pos, (int)pos + len);
		}
		return ConnInstance.GetCsi().A(this, pos, len);
	}

	public int SetString(long pos, string str)
	{
		if (str == null)
		{
			return do_setString(pos + 1, "", 0, 0);
		}
		return do_setString(pos + 1, str, 0, str.length());
	}

	public int SetString(long pos, string str, int offset, int len)
	{
		return do_setString(pos + 1, str, offset, len);
	}

	public int do_setString(long pos, string str, int offset, int len)
	{
		if (pos < 1 || offset < 0 || len < 0 || offset + len > str.length())
		{
			DmError.ThrowDmException(DmErrorDefinition.ECNET_INVALID_LENGTH_OR_OFFSET);
		}
		if (!updateable)
		{
			DmError.ThrowDmException(DmErrorDefinition.EC_UPDATE_READONLY_CURSOR);
		}
		str = str.Substring(offset, len);
		pos--;
		int num = 0;
		if (local || fetchAll)
		{
			if (pos > do_length())
			{
				DmError.ThrowDmException(DmErrorDefinition.ECNET_INVALID_LENGTH_OR_OFFSET);
			}
			setLocalData((int)pos, str);
			return str.length();
		}
		int result = ConnInstance.GetCsi().A(this, pos, str, serverEncoding);
		if (storageType == 1)
		{
			setLocalData((int)pos, str);
		}
		return result;
	}

	public void Truncate(long len)
	{
		do_truncate(len);
	}

	public void do_truncate(long len)
	{
		if (len < 0)
		{
			DmError.ThrowDmException(DmErrorDefinition.ECNET_INVALID_LENGTH_OR_OFFSET);
		}
		if (!updateable)
		{
			DmError.ThrowDmException(DmErrorDefinition.EC_UPDATE_READONLY_CURSOR);
		}
		if (local || fetchAll)
		{
			if (len <= do_length())
			{
				data = data.substring(0, (int)len);
				m_length = data.length();
			}
		}
		else
		{
			m_length = ConnInstance.GetCsi().A(this, (int)len);
			if (storageType == 1)
			{
				data = data.Substring(0, (int)m_length);
			}
		}
	}

	public void loadAllData()
	{
		if (!local && storageType != 1 && !fetchAll)
		{
			data = do_getSubString(1L, (int)do_length());
			m_length = data.length();
			fetchAll = true;
		}
	}

	private void setLocalData(int pos, string str)
	{
		if (pos + str.length() >= m_length)
		{
			data = data.substring(0, pos) + str;
		}
		else
		{
			data = data.substring(0, pos) + str + data.substring(pos + str.length(), data.length());
		}
		m_length = data.length();
	}

	public byte[] GetBytes(long pos, int len)
	{
		return ByteUtil.fromString(do_getSubString(pos + 1, len), serverEncoding);
	}

	internal Stream GetStream()
	{
		int num = (int)m_length;
		MemoryStream memoryStream = new MemoryStream(num);
		memoryStream.Write(GetBytes(0L, (int)do_length()), 0, num);
		memoryStream.Position = 0L;
		return memoryStream;
	}

	public string GetString(long pos, int length)
	{
		return getSubString(pos, length);
	}

	public long length()
	{
		return do_length();
	}
}
