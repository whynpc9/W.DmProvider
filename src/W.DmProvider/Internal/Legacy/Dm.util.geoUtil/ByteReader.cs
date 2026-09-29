using System;
using System.IO;

namespace W.Dm.util.geoUtil;

public class ByteReader
{
	private MemoryStream reader;

	private int readerByteOrder;

	private int outByteOrder;

	private long markPosition;

	public ByteReader(byte[] buf)
	{
		reader = new MemoryStream(buf);
		readerByteOrder = 1;
		outByteOrder = 1;
	}

	public void SetReaderByteOrder(int byteOrder)
	{
		readerByteOrder = byteOrder;
	}

	public void SetOutByteOrder(int byteOrder)
	{
		outByteOrder = byteOrder;
	}

	public int getReaderByteOrder()
	{
		return readerByteOrder;
	}

	public void mark()
	{
		markPosition = reader.Position;
	}

	public void reset()
	{
		reader.Seek(markPosition, SeekOrigin.Begin);
	}

	public byte[] ReadPart(int len)
	{
		byte[] array = new byte[len];
		reader.Read(array, 0, len);
		if (readerByteOrder != outByteOrder)
		{
			for (int i = 0; i < len / 2; i++)
			{
				byte b = array[i];
				array[i] = array[len - i - 1];
				array[len - i - 1] = b;
			}
		}
		return array;
	}

	public byte[] readPart(int len, int endian)
	{
		byte[] array = new byte[len];
		reader.Read(array, 0, len);
		if (readerByteOrder != endian)
		{
			for (int i = 0; i < len / 2; i++)
			{
				byte b = array[i];
				array[i] = array[len - i - 1];
				array[len - i - 1] = b;
			}
		}
		return array;
	}

	public int Read()
	{
		return reader.ReadByte();
	}

	public void skip(int offset)
	{
		reader.Seek(offset, SeekOrigin.Current);
	}

	public int ReadSrid()
	{
		return (Read() << 16) + (Read() << 8) + Read();
	}

	public int readInt()
	{
		if (readerByteOrder == 1)
		{
			return Read() + (Read() << 8) + (Read() << 16) + (Read() << 24);
		}
		return (Read() << 24) + (Read() << 16) + (Read() << 8) + Read();
	}

	public double ReadDouble()
	{
		long num = 0L;
		if (readerByteOrder == 1)
		{
			for (int i = 0; i < 8; i++)
			{
				num += (long)Read() << i * 8;
			}
			return BitConverter.Int64BitsToDouble(num);
		}
		for (int num2 = 7; num2 >= 0; num2--)
		{
			num += (long)Read() << num2 * 8;
		}
		return BitConverter.Int64BitsToDouble(num);
	}

	public int getRepPartValue(int len)
	{
		int num = 0;
		if (readerByteOrder == 1)
		{
			for (int i = 0; i < len; i++)
			{
				num += Read() << i * 8;
			}
		}
		else
		{
			for (int j = 0; j < len; j++)
			{
				num += Read() << (len - j - 1) * 8;
			}
		}
		return num;
	}
}
