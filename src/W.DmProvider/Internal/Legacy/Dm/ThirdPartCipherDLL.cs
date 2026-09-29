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

	[DllImport("Kernel32.dll")]
	internal static extern nint LoadLibrary(string cipherPath);

	[DllImport("Kernel32.dll")]
	internal static extern nint GetProcAddress(nint hModule, string procName);

	[DllImport("Kernel32.dll")]
	internal static extern nint FreeLibrary(nint hModule);

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
		nint num = LoadLibrary(cipherPath);
		if (num == IntPtr.Zero)
		{
			throw new SystemException("load thirdPart dll failed!");
		}
		getCount = (cipher_get_count)GetAddress(num, "cipher_get_count", typeof(cipher_get_count));
		getInfo = (cipher_get_info)GetAddress(num, "cipher_get_info", typeof(cipher_get_info));
		encryptInit = (cipher_encrypt_init)GetAddress(num, "cipher_encrypt_init", typeof(cipher_encrypt_init));
		getCipherTextSize = (cipher_get_cipher_text_size)GetAddress(num, "cipher_get_cipher_text_size", typeof(cipher_get_cipher_text_size));
		encrypt = (cipher_encrypt)GetAddress(num, "cipher_encrypt", typeof(cipher_encrypt));
		cleanup = (cipher_cleanup)GetAddress(num, "cipher_cleanup", typeof(cipher_cleanup));
		decryptInit = (cipher_decrypt_init)GetAddress(num, "cipher_decrypt_init", typeof(cipher_decrypt_init));
		decrypt = (cipher_decrypt)GetAddress(num, "cipher_decrypt", typeof(cipher_decrypt));
		hashInit = (cipher_hash_init)GetAddress(num, "cipher_hash_init", typeof(cipher_hash_init));
		hashUpdate = (cipher_hash_update)GetAddress(num, "cipher_hash_update", typeof(cipher_hash_update));
		hashFinal = (cipher_hash_final)GetAddress(num, "cipher_hash_final", typeof(cipher_hash_final));
	}

	public void Dispose()
	{
	}
}
