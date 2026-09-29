using System;
using System.Runtime.InteropServices;

namespace W.Dm;

public class DmFldrDllCall
{
	private const int FLDR_SUCCESS = 0;

	public const int FLDR_ATTR_SERVER = 1;

	public const int FLDR_ATTR_UID = 2;

	public const int FLDR_ATTR_PWD = 3;

	public const int FLDR_ATTR_PORT = 4;

	public const int FLDR_ATTR_BAD_FILE = 17;

	public const int FLDR_ATTR_DATA_CHAR_SET = 19;

	public const int FLDR_ATTR_ERRORS_PERMIT = 30;

	public const int FLDR_ATTR_LOAD_MODE = 31;

	public const int FLDR_ATTR_MPP_LOCAL_FLAG = 37;

	public const int FLDR_ATTR_TASK_THREAD_NUM = 42;

	public const int FLDR_ATTR_EXPORT_MODE = 87;

	private static bool FLDR_SUCCEEDED(int ret)
	{
		if (ret != 0)
		{
			return false;
		}
		return true;
	}

	[DllImport("dmfldr")]
	public static extern int fldr_alloc(out nint fsinst);

	public static void AllocSinst(out nint fsinst)
	{
		if (!FLDR_SUCCEEDED(fldr_alloc(out fsinst)))
		{
			DmError.ThrowDmException(DmErrorDefinition.EC_FAIL);
		}
	}

	[DllImport("dmfldr", EntryPoint = "fldr_fetch_data")]
	private static extern int fetch_data(nint fsinst, nint handle, ref byte data, int dataLen);

	public static void FetchData(nint fsinst, nint handle, ref byte data, int dataLen)
	{
		if (!FLDR_SUCCEEDED(fetch_data(fsinst, handle, ref data, dataLen)))
		{
			fldr_free(fsinst);
			DmError.ThrowDmException(DmErrorDefinition.ECNET_READ_NO_DATA);
		}
	}

	[DllImport("dmfldr", EntryPoint = "fldr_export")]
	private static extern int export(nint fsinst, string ctlBuf);

	public static void Export(nint fsinst, string ctlBuf)
	{
		if (!FLDR_SUCCEEDED(export(fsinst, ctlBuf)))
		{
			fldr_free(fsinst);
			DmError.ThrowDmException(DmErrorDefinition.EC_CTL_FILE_ERROR);
		}
	}

	[DllImport("dmfldr", EntryPoint = "fldr_fetch_data_len")]
	private static extern int fetch_data_len(nint fsinst, out nint handle, ref int dataLen);

	public static void FetchDataLen(nint fsinst, out nint handle, ref int dataLen)
	{
		if (!FLDR_SUCCEEDED(fetch_data_len(fsinst, out handle, ref dataLen)))
		{
			fldr_free(fsinst);
			DmError.ThrowDmException(DmErrorDefinition.EC_RN_INVALID_DATA);
		}
	}

	[DllImport("dmfldr")]
	public static extern int fldr_free(nint fsinst);

	public static void FreeSinst(nint fsinst)
	{
		if (!FLDR_SUCCEEDED(fldr_free(fsinst)))
		{
			throw new Exception("释放实例失败");
		}
	}

	[DllImport("dmfldr", EntryPoint = "fldr_set_attr")]
	public static extern int fldr_set_attr_1(nint fsinst, int attr, nint value, int length);

	[DllImport("dmfldr", EntryPoint = "fldr_set_attr")]
	public static extern int fldr_set_attr_2(nint fsinst, int attr, string value, int length);

	public static void SetAttr(nint fsinst, int attr, object value, int length)
	{
		int ret;
		switch (attr)
		{
		case 4:
		case 19:
		case 30:
		case 31:
		case 37:
		case 42:
		case 87:
			ret = fldr_set_attr_1(fsinst, attr, (int)value, length);
			break;
		default:
			ret = fldr_set_attr_2(fsinst, attr, (string)value, length);
			break;
		}
		if (!FLDR_SUCCEEDED(ret))
		{
			DmError.ThrowDmException(DmErrorDefinition.EC_FAIL);
		}
	}

	internal int GetTypeCode(Type type)
	{
		if (type.Equals(typeof(byte[])))
		{
			return 19;
		}
		if (type.Equals(typeof(bool)))
		{
			return 5;
		}
		return (int)Type.GetTypeCode(type);
	}

	private int getCharset(string serverEncoding)
	{
		if (serverEncoding.Equals("UTF-8", StringComparison.OrdinalIgnoreCase))
		{
			return 1;
		}
		if (serverEncoding.Equals("GB18030", StringComparison.OrdinalIgnoreCase))
		{
			return 10;
		}
		return 0;
	}
}
