namespace W.Dm.util.buffer;

public abstract class BufferNode<T>
{
	public T buffer;

	public int capacity;

	public int read;

	public int write;

	public BufferNode<object> prev;

	public BufferNode<object> next;

	public int extra;

	public BufferNode(T buffer, int capacity)
	{
		this.buffer = buffer;
		this.capacity = capacity;
	}

	public override string ToString()
	{
		return "capacity=" + capacity + " read=" + read + " write=" + write;
	}

	public abstract void Clear(int offset);

	public abstract void Rewind(int offset);

	public abstract int Capacity();

	public abstract int Length(bool forWrite);

	public abstract int Offset(bool forWrite);

	public abstract int Available(bool forWrite);

	public abstract int Skip(int length, bool forWrite, bool forward);

	public abstract int Load(object obj, int len, bool fully);

	public abstract int Flush(object obj, bool fully);

	public abstract int WriteByte(byte b);

	public abstract int WriteShort(short s);

	public abstract int WriteInt(int i);

	public abstract int WriteLong(long l);

	public abstract int WriteFloat(float f);

	public abstract int WriteDouble(double d);

	public abstract int WriteUb1(int i);

	public abstract int WriteUb2(int i);

	public abstract int WriteUb4(long l);

	public abstract int WriteBytes(byte[] srcBytes, int srcOffset, int len);

	public abstract byte ReadByte();

	public abstract short ReadShort();

	public abstract int ReadInt();

	public abstract long ReadLong();

	public abstract float ReadFloat();

	public abstract double ReadDouble();

	public abstract int ReadUb1();

	public abstract int ReadUb2();

	public abstract long ReadUb4();

	public abstract int ReadBytes(byte[] objBytes, int objOffset, int len);

	public abstract int SetByte(int offset, byte b);

	public abstract int SetShort(int offset, short s);

	public abstract int SetInt(int offset, int i);

	public abstract int SetLong(int offset, long l);

	public abstract int SetFloat(int offset, float f);

	public abstract int SetDouble(int offset, double d);

	public abstract int SetUb1(int offset, int i);

	public abstract int SetUb2(int offset, int i);

	public abstract int SetUB4(int offset, long l);

	public abstract int SetBytes(int offset, byte[] srcBytes, int srcOffset, int len);

	public abstract byte GetByte(int offset);

	public abstract short GetShort(int offset);

	public abstract int GetInt(int offset);

	public abstract long GetLong(int offset);

	public abstract float GetFloat(int offset);

	public abstract double GetDouble(int offset);

	public abstract int GetUB1(int offset);

	public abstract int GetUB2(int offset);

	public abstract long GetUB4(int offset);

	public abstract int GetBytes(int offset, byte[] objBytes, int objOffset, int len);
}
