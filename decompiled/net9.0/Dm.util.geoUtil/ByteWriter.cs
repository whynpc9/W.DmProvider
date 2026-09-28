using System.IO;

namespace Dm.util.geoUtil;

public class ByteWriter
{
	private MemoryStream writer;

	private int byteOrder;

	public ByteWriter()
	{
		writer = new MemoryStream();
		byteOrder = 1;
	}

	public ByteWriter(int byteOrder)
	{
		writer = new MemoryStream();
		this.byteOrder = byteOrder;
	}

	public int Size()
	{
		return (int)writer.Length;
	}

	public void SetByteOrder(int byteOrder)
	{
		this.byteOrder = byteOrder;
	}

	public int GetByteOrder()
	{
		return byteOrder;
	}

	public void Write(int value)
	{
		writer.WriteByte((byte)value);
	}

	public void WritePart(byte[] buf)
	{
		writer.Write(buf, 0, buf.Length);
	}

	public void WritePart(byte[] buf, int endian)
	{
		int num = buf.Length;
		if (endian != byteOrder)
		{
			for (int i = 0; i < num / 2; i++)
			{
				byte b = buf[i];
				buf[i] = buf[num - i - 1];
				buf[num - i - 1] = b;
			}
		}
		writer.Write(buf, 0, num);
	}

	public void WriteSrid(int srid)
	{
		Write((srid >> 16) & 0xFF);
		Write((srid >> 8) & 0xFF);
		Write(srid & 0xFF);
	}

	public void WriteInt(int value)
	{
		if (byteOrder == 1)
		{
			Write(value & 0xFF);
			Write((value >> 8) & 0xFF);
			Write((value >> 16) & 0xFF);
			Write((value >> 24) & 0xFF);
		}
		else
		{
			Write((value >> 24) & 0xFF);
			Write((value >> 16) & 0xFF);
			Write((value >> 8) & 0xFF);
			Write(value & 0xFF);
		}
	}

	public byte[] ToByteArray()
	{
		return writer.ToArray();
	}
}
