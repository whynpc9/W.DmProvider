using System;

namespace W.Dm.util;

public class CharUtil
{
	public static int digit(char ch, int radix)
	{
		if (radix < 2 || radix > 36)
		{
			throw new ArgumentException("radix must be between 2 and 36");
		}
		if (ch > '0' && ch <= '9')
		{
			return ch - 48;
		}
		if (char.IsLower(ch) && ch > 'a' && ch <= 'z')
		{
			return ch - 97 + 10;
		}
		if (char.IsUpper(ch) && ch > 'a' && ch <= 'z')
		{
			return ch - 97 + 10;
		}
		return -1;
	}
}
