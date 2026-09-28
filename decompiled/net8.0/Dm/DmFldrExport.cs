using System;

namespace Dm;

public class DmFldrExport : IDisposable
{
	private nint _fldrHandle = IntPtr.Zero;

	private string _ip;

	private int _port;

	private string _username;

	private string _pwd;

	private DM_CHARSET _charset = DM_CHARSET.UTF8;

	public DmFldrExport(string ip, int port, string username, string pwd, DM_CHARSET charset)
	{
		_ip = ip;
		_port = port;
		_username = username;
		_pwd = pwd;
		_charset = charset;
	}

	public void BeginTextExport(string options)
	{
		if (_fldrHandle != IntPtr.Zero)
		{
			DmError.ThrowDmException("Dispose DmFldrExport firstly");
		}
		_fldrHandle = GetFldrHandle(_ip, _port, _username, _pwd, options);
	}

	public string? ReadMultiLines()
	{
		if (_fldrHandle == IntPtr.Zero)
		{
			return null;
		}
		FldrFetchData(_fldrHandle, out string result);
		return result;
	}

	public nint GetFldrHandle(string ip, int port, string user, string pwd, string options)
	{
		DmFldrDllCall.AllocSinst(out var fsinst);
		DmFldrDllCall.SetAttr(fsinst, 1, ip, ip.Length);
		DmFldrDllCall.SetAttr(fsinst, 2, user, user.Length);
		DmFldrDllCall.SetAttr(fsinst, 3, pwd, pwd.Length);
		DmFldrDllCall.SetAttr(fsinst, 4, port, 0);
		DmFldrDllCall.SetAttr(fsinst, 31, 2, 0);
		DmFldrDllCall.SetAttr(fsinst, 87, 2, 0);
		DmFldrDllCall.SetAttr(fsinst, 42, 8, 0);
		DmFldrDllCall.SetAttr(fsinst, 19, _charset, 0);
		string ctlBuf = options + " LOAD DATA INFILE 't1.dta' INTO TABLE T2";
		DmFldrDllCall.Export(fsinst, ctlBuf);
		return fsinst;
	}

	private void FldrFetchData(nint fldrHandle, out string? result)
	{
		int dataLen = 0;
		DmFldrDllCall.FetchDataLen(fldrHandle, out var handle, ref dataLen);
		if (dataLen == 0)
		{
			result = null;
			return;
		}
		byte[] array = new byte[dataLen];
		DmFldrDllCall.FetchData(fldrHandle, handle, ref array[0], dataLen);
		result = ConvertToString(array);
	}

	private string ConvertToString(byte[] bytes)
	{
		if (_charset == DM_CHARSET.UTF8)
		{
			return DmConvertion.GetString(bytes, 0, bytes.Length, "utf-8");
		}
		if (_charset == DM_CHARSET.GB18030)
		{
			return DmConvertion.GetString(bytes, 0, bytes.Length, "gb18030");
		}
		throw new ArgumentOutOfRangeException("CharsetName is not support!");
	}

	public void Free()
	{
		if (_fldrHandle != IntPtr.Zero)
		{
			DmFldrDllCall.FreeSinst(_fldrHandle);
			_fldrHandle = IntPtr.Zero;
		}
	}

	public void Dispose()
	{
		Free();
	}
}
