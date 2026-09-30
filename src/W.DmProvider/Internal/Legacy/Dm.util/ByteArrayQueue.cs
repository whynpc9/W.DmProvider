using System;
using System.IO;

namespace W.Dm.util;

public class ByteArrayQueue
{
	public class Element
	{
		public byte[] bytes;

		public int start;

		public int length;

		public Element(byte[] bytes, int offset, int len)
		{
			this.bytes = bytes;
			start = offset;
			length = len;
		}

		public int writeBytes(BufferedStream _output, int len)
		{
			len = ((length > len) ? len : length);
			_output.Write(bytes, start, len);
			start += len;
			length -= len;
			return len;
		}

		public int writeBytes(byte[] buffer, int offset, int len)
		{
			len = ((length > len) ? len : length);
			Array.Copy(bytes, start, buffer, offset, len);
			start += len;
			length -= len;
			return len;
		}

		public byte getByte(int offset)
		{
			return bytes[start + offset];
		}
	}

	private BlockingQueue<Element> byteArrayList = new BlockingQueue<Element>();

	public Element current;

	private int _length;

	public int length()
	{
		return _length;
	}

	public void putBytes(byte[] bytes, int offset, int len)
	{
		if (len != 0)
		{
			Element item = new Element(bytes, offset, len);
			if (current == null)
			{
				current = item;
			}
			else
			{
				byteArrayList.Enqueue(item);
			}
			_length += len;
		}
	}

	public int writeBytes(BufferedStream _output, int len)
	{
		int num = 0;
		int num2 = 0;
		while (num < len && current != null)
		{
			num2 = current.writeBytes(_output, len - num);
			if (current.length == 0)
			{
				next();
			}
			num += num2;
			_length -= num2;
		}
		return num;
	}

	public int writeBytes(byte[] buffer, int offset, int len)
	{
		int num = 0;
		int num2 = 0;
		while (num < len && current != null)
		{
			num2 = current.writeBytes(buffer, offset, len - num);
			if (current.length == 0)
			{
				next();
			}
			num += num2;
			_length -= num2;
			offset += num2;
		}
		return num;
	}

	public byte getByte(int offset)
	{
		int num = offset;
		Element element = current;
		while (num > 0 && element != null)
		{
			if (element.length != 0)
			{
				if (num <= element.length - 1)
				{
					break;
				}
				num -= element.length;
				element = byteArrayList.Peek();
			}
		}
		return element.getByte(num);
	}

	public void append(ByteArrayQueue buffer)
	{
		if (buffer.length() != 0)
		{
			Element element = null;
			while ((element = buffer.current) != null)
			{
				addElement(element);
				buffer.next();
			}
			buffer._length = 0;
		}
	}

	private void addElement(Element e)
	{
		if (e.length != 0)
		{
			if (current == null)
			{
				current = e;
			}
			else
			{
				byteArrayList.Enqueue(e);
			}
			_length += e.length;
		}
	}

	private void next()
	{
		current = byteArrayList.Poll();
	}

	public void clear()
	{
		byteArrayList.Clear();
		current = null;
		_length = 0;
	}

	public byte[] toBytes()
	{
		byte[] array = new byte[_length];
		Element element = current;
		int num = 0;
		int num2 = array.Length;
		int num3 = 0;
		while (element != null)
		{
			if (element.length > 0)
			{
				num3 = ((num2 > element.length) ? element.length : num2);
				Array.Copy(element.bytes, element.start, array, num, num3);
				num += num3;
				num2 -= num3;
			}
			element = byteArrayList.Poll();
		}
		return array;
	}

	public byte[] getBytes()
	{
		Element[] array = byteArrayList.ToArray();
		if (array.Length == 0)
		{
			return new byte[0];
		}
		byte[] array2 = new byte[_length];
		int num = 0;
		Element element = array[num++];
		int num2 = 0;
		int num3 = array2.Length;
		int num4 = 0;
		while (element != null && element != current)
		{
			element = array[num++];
		}
		while (element != null)
		{
			if (element.length > 0)
			{
				num4 = ((num3 > element.length) ? element.length : num3);
				Array.Copy(element.bytes, element.start, array2, num2, num4);
				num2 += num4;
				num3 -= num4;
			}
			element = array[num++];
		}
		return array2;
	}
}
