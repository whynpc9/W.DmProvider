using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Text;

namespace W.Dm.util;

public static class ExtensionUtil
{
	public static readonly ConcurrentDictionary<string, Encoding> encodingCache = new ConcurrentDictionary<string, Encoding>();

	public static bool isEmpty<T>(this List<T> list)
	{
		if (list != null)
		{
			return list.Count == 0;
		}
		return true;
	}

	public static int size<T>(this List<T> list)
	{
		return list.Count;
	}

	public static void add<T>(this List<T> list, T item)
	{
		list.Add(item);
	}

	public static void add<T>(this List<T> list, int index, T item)
	{
		list.Insert(index, item);
	}

	public static T get<T>(this List<T> list, int index)
	{
		return list[index];
	}

	public static void set<T>(this List<T> list, int index, T item)
	{
		list[index] = item;
	}

	public static void clear<T>(this List<T> list)
	{
		list.Clear();
	}

	public static K getKey<K, V>(this KeyValuePair<K, V> kv)
	{
		return kv.Key;
	}

	public static V getValue<K, V>(this KeyValuePair<K, V> kv)
	{
		return kv.Value;
	}

	public static bool add<K>(this HashSet<K> set, K item)
	{
		return set.Add(item);
	}

	public static V get<K, V>(this Dictionary<K, V> map, K key)
	{
		if (map.TryGetValue(key, out var value))
		{
			return value;
		}
		return default(V);
	}

	public static void put<K, V>(this Dictionary<K, V> set, K key, V value)
	{
		set[key] = value;
	}

	public static bool isEmpty<K, V>(this Dictionary<K, V> set)
	{
		return set.Count == 0;
	}

	public static bool startsWith(this string str, string value)
	{
		return str.StartsWith(value);
	}

	public static string substring(this string str, int startIndex)
	{
		return str.Substring(startIndex);
	}

	public static string substring(this string str, int startIndex, int endIndex)
	{
		return str.Substring(startIndex, endIndex - startIndex);
	}

	public static bool isEmpty(this string str)
	{
		return string.IsNullOrEmpty(str);
	}

	public static string[] split(this string str, string separator)
	{
		return str.Split(new string[1] { separator }, StringSplitOptions.None);
	}

	public static int length(this string str)
	{
		return str.Length;
	}

	public static char charAt(this string str, int index)
	{
		return str[index];
	}

	public static byte[] getBytes(this string str, string encoding)
	{
		return encodingCache.GetOrAdd(encoding, Encoding.GetEncoding).GetBytes(str);
	}

	public static int indexOf(this string str, string substr)
	{
		return str.IndexOf(substr);
	}

	public static string replace(this string str, string oldValue, string newValue)
	{
		return str.Replace(oldValue, newValue);
	}

	public static char[] toCharArray(this string str)
	{
		return str.ToCharArray();
	}

	public static int compareTo(this string str, string value)
	{
		return str.CompareTo(value);
	}

	public static bool contains(this string str, string value)
	{
		return str.Contains(value);
	}

	public static StringBuilder append(this StringBuilder str, string value)
	{
		return str.Append(value);
	}

	public static string substring(this StringBuilder str, int startIndex)
	{
		return str.ToString().Substring(startIndex);
	}

	public static int compareTo(this decimal value1, decimal value2)
	{
		return value1.CompareTo(value2);
	}

	public static int signum(this decimal value)
	{
		if (!(value > 0m))
		{
			if (!(value < 0m))
			{
				return 0;
			}
			return -1;
		}
		return 1;
	}

	public static decimal stripTrailingZeros(this decimal value)
	{
		if (value == 0m)
		{
			return 0m;
		}
		int[] bits = decimal.GetBits(value);
		int num = (bits[3] >> 16) & 0xFF;
		bool flag = (bits[3] & int.MinValue) != 0;
		ulong num2 = (uint)bits[0];
		ulong num3 = (uint)bits[1];
		ulong num4 = (uint)bits[2];
		while (num > 0)
		{
			ulong remainder = 0uL;
			ulong num5 = divRem(num4, 10uL, ref remainder);
			ulong num6 = divRem(num3 + (remainder << 32), 10uL, ref remainder);
			ulong num7 = divRem(num2 + (remainder << 32), 10uL, ref remainder);
			if (remainder != 0L)
			{
				break;
			}
			num4 = num5;
			num3 = num6;
			num2 = num7;
			num--;
		}
		bits[0] = (int)num2;
		bits[1] = (int)num3;
		bits[2] = (int)num4;
		bits[3] = (num << 16) | (flag ? int.MinValue : 0);
		return new decimal(bits);
	}

	private static ulong divRem(ulong value, ulong divisor, ref ulong remainder)
	{
		remainder = value % divisor;
		return value / divisor;
	}

	public static int precision(this decimal value)
	{
		decimal num = Math.Abs(value);
		return ((num == 0m) ? 1 : ((int)(Math.Floor(Math.Log10((double)num)) + 1.0))) + value.scale();
	}

	public static int scale(this decimal value)
	{
		return (decimal.GetBits(value)[3] >> 16) & 0xFF;
	}

	public static decimal negate(this decimal value)
	{
		return -value;
	}

	public static decimal setScale(this decimal value, int scale)
	{
		return decimal.Round(value, scale, MidpointRounding.AwayFromZero);
	}

	public static decimal setScale(this decimal value, int scale, MidpointRounding midpointRounding)
	{
		if (scale <= value.scale())
		{
			return decimal.Round(value, scale, midpointRounding);
		}
		return decimal.Parse(value.ToString("F" + scale));
	}

	public static decimal movePointRight(this decimal value, int weight)
	{
		if (weight < 0)
		{
			throw new ArgumentException("weight cannot be negative", "weight");
		}
		decimal num = (decimal)Math.Pow(10.0, weight);
		return value * num;
	}

	public static decimal movePointLeft(this decimal value, int weight)
	{
		if (weight < 0)
		{
			throw new ArgumentException("weight cannot be negative", "weight");
		}
		decimal num = (decimal)Math.Pow(10.0, weight);
		return value / num;
	}

	public static string toPlainString(this decimal value)
	{
		return value.ToString();
	}

	public static decimal add(this decimal dec1, decimal dec2)
	{
		return dec1 + dec2;
	}

	public static decimal subtract(this decimal dec1, decimal dec2)
	{
		return dec1 - dec2;
	}

	public static void put<K, V>(this ConcurrentDictionary<K, V> map, K key, V value)
	{
		map[key] = value;
	}

	public static V get<K, V>(this ConcurrentDictionary<K, V> map, K key)
	{
		if (map.TryGetValue(key, out var value))
		{
			return value;
		}
		return default(V);
	}
}
