using System;
using System.Runtime.InteropServices;

namespace W.Dm;

internal class ThirdPartCipherDLL : IDisposable
{
	[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
	internal delegate int cipher_get_count();

	[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
	internal delegate long cipher_get_info(int seqno, ref int cipher_id, ref nint cipher_name, ref byte type, ref int blk_size, ref int kh_size);

	[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
	internal delegate int cipher_encrypt_init(int inner_id, byte[] key, int key_size, out nint encrypt_para);

	[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
	internal delegate int cipher_get_cipher_text_size(int inner_id, nint cipher_para, int plain_text_size);

	[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
	internal delegate int cipher_encrypt(int inner_id, nint encrypt_para, byte[] plain_text, int plain_text_size, byte[] cipher_text, int cipher_text_buf_size);

	[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
	internal delegate void cipher_cleanup(int inner_id, nint cipher_para);

	[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
	internal delegate int cipher_decrypt_init(int inner_id, byte[] key, int key_size, out nint decrypt_para);

	[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
	internal delegate int cipher_decrypt(int inner_id, nint cipher_para, byte[] cipher_text, int cipher_text_size, byte[] plain_text, int plain_text_buf_size);

	[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
	internal delegate int cipher_hash_init(int inner_id, out nint hash_para);

	[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
	internal delegate void cipher_hash_update(int inner_id, nint hash_para, byte[] msg, int msg_size);

	[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
	internal delegate int cipher_hash_final(int inner_id, nint hash_para, byte[] digest, int digest_buf_size);

	internal cipher_get_count getCount;

	internal cipher_get_info getInfo;

	internal cipher_encrypt_init encryptInit;

	internal cipher_get_cipher_text_size getCipherTextSize;

	internal cipher_encrypt encrypt;

	internal cipher_cleanup cleanup;

	internal cipher_decrypt_init decryptInit;

	internal cipher_decrypt decrypt;

	internal cipher_hash_init hashInit;

	internal cipher_hash_update hashUpdate;

	internal cipher_hash_final hashFinal;

	internal static nint LoadLibrary(string cipherPath) =>
		throw new NotSupportedException("Third-party native ciphers are not enabled by this provider version.");

	internal static nint GetProcAddress(nint hModule, string procName) =>
		throw new NotSupportedException("Third-party native ciphers are not enabled by this provider version.");

	internal static nint FreeLibrary(nint hModule) =>
		throw new NotSupportedException("Third-party native ciphers are not enabled by this provider version.");

	internal Delegate GetAddress(nint hModule, string procName, Type t)
	{
		nint procAddress = GetProcAddress(hModule, procName);
		if (procAddress == IntPtr.Zero)
		{
			return null;
		}
		return Marshal.GetDelegateForFunctionPointer(procAddress, t);
	}

	internal ThirdPartCipherDLL(string cipherPath)
	{
		throw new NotSupportedException("Third-party native ciphers are not enabled by this provider version.");
	}

	public void Dispose()
	{
	}
}
