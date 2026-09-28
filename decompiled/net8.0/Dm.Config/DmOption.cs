using System;
using System.Collections.Generic;
using System.IO;

namespace Dm.Config;

internal class DmOption
{
	private static Dictionary<string, EPGroup> epGroupMap;

	private static object epGroupMapLock = new object();

	internal string Keyword { get; private set; }

	internal string[] Synonym { get; private set; }

	internal Type BaseType { get; private set; }

	internal object Defaultvalue { get; private set; }

	internal long? Maxvalue { get; private set; }

	internal long? Minvalue { get; private set; }

	internal DmOption(string key, Type basetype, object defaultvalue = null, string syn = null, long? maxvalue = null, long? minvalue = 0L)
	{
		Keyword = key;
		Synonym = syn?.Split(',');
		Defaultvalue = defaultvalue;
		BaseType = basetype;
		Maxvalue = maxvalue;
		Minvalue = minvalue;
	}

	internal bool HasKey(string key)
	{
		if (string.Compare(key, Keyword, StringComparison.OrdinalIgnoreCase) == 0)
		{
			return true;
		}
		if (Synonym != null)
		{
			string[] synonym = Synonym;
			foreach (string strB in synonym)
			{
				if (string.Compare(key, strB, StringComparison.OrdinalIgnoreCase) == 0)
				{
					return true;
				}
			}
		}
		return false;
	}

	internal object ValidateValue(object value)
	{
		try
		{
			if (value == null)
			{
				value = Defaultvalue;
			}
			if (BaseType == typeof(bool))
			{
				if (value.GetType() == typeof(string) && (string.Compare("yes", value.ToString(), StringComparison.OrdinalIgnoreCase) == 0 || string.Compare("y", value.ToString(), StringComparison.OrdinalIgnoreCase) == 0))
				{
					return true;
				}
				if (value.GetType() == typeof(string) && (string.Compare("no", value.ToString(), StringComparison.OrdinalIgnoreCase) == 0 || string.Compare("n", value.ToString(), StringComparison.OrdinalIgnoreCase) == 0))
				{
					return false;
				}
				if (bool.TryParse(value.ToString(), out var result))
				{
					return result;
				}
				if (int.TryParse(value.ToString(), out var result2))
				{
					return result2 > 0;
				}
			}
			else
			{
				if ((BaseType == typeof(long) || BaseType == typeof(int)) && long.TryParse(value.ToString(), out var result3))
				{
					if (Maxvalue.HasValue && result3 > Maxvalue.Value)
					{
						return Defaultvalue;
					}
					if (Minvalue.HasValue && result3 < Minvalue.Value)
					{
						return Defaultvalue;
					}
					if (result3 > long.MaxValue || result3 < long.MinValue)
					{
						throw new ArgumentException("over flow");
					}
					return result3;
				}
				if (BaseType == typeof(string))
				{
					if (value.ToString().Trim().Equals(string.Empty))
					{
						return Defaultvalue;
					}
					if (value.ToString().Trim().Length > 128)
					{
						DmError.ThrowDmException(DmErrorDefinition.ECNET_NAME_TOO_LONG);
					}
					if (string.Compare("encoding", Keyword, StringComparison.OrdinalIgnoreCase) == 0 && string.Compare("utf-8", value.ToString(), StringComparison.OrdinalIgnoreCase) != 0 && string.Compare("gb18030", value.ToString(), StringComparison.OrdinalIgnoreCase) != 0 && string.Compare("euc-kr", value.ToString(), StringComparison.OrdinalIgnoreCase) != 0)
					{
						return Defaultvalue;
					}
					if (string.Compare("dm_svc_conf", Keyword, StringComparison.OrdinalIgnoreCase) == 0)
					{
						try
						{
							new StreamReader(value.ToString().Trim());
						}
						catch (Exception)
						{
							return Defaultvalue;
						}
					}
					return value.ToString().Trim();
				}
				if (BaseType == typeof(string[]))
				{
					if (value is string[])
					{
						return value;
					}
					if (value.ToString().Length == 0)
					{
						return Defaultvalue;
					}
					return value.ToString().Split(new char[1] { ',' }, StringSplitOptions.RemoveEmptyEntries);
				}
				if (BaseType == typeof(EPGroup))
				{
					if (value is string[])
					{
						return ParseEPGroup(value as string[]);
					}
					if (value.ToString().Length == 0)
					{
						return Defaultvalue;
					}
					return ParseEPGroup(value.ToString().Split(new char[1] { ',' }, StringSplitOptions.RemoveEmptyEntries));
				}
				if (BaseType.BaseType == typeof(Enum))
				{
					if (long.TryParse(value.ToString(), out var result4))
					{
						Array values = Enum.GetValues(BaseType);
						for (int i = 0; i < values.Length; i++)
						{
							if (values.GetValue(i).Equals(Enum.ToObject(BaseType, result4)))
							{
								return values.GetValue(i);
							}
						}
						return Defaultvalue;
					}
					object obj = Enum.Parse(BaseType, value.ToString().Trim(), ignoreCase: true);
					if (obj != null)
					{
						return obj;
					}
				}
			}
			return Defaultvalue;
		}
		catch (Exception)
		{
			return Defaultvalue;
		}
	}

	internal static EPGroup ParseEPGroup(string[] servers)
	{
		List<EP> list = new List<EP>(servers.Length);
		EP eP = null;
		foreach (string text in servers)
		{
			int num = text.IndexOf("[");
			int num2 = -1;
			if (num != -1)
			{
				num2 = text.IndexOf("]", num);
			}
			if (num2 != -1)
			{
				string host = text.Substring(num, num2 - num + 1);
				int port = DmOptionHelper.portDef;
				int num3 = text.IndexOf(":", num2);
				if (num3 != -1)
				{
					try
					{
						port = Convert.ToInt32(text.Substring(num3 + 1).Trim());
					}
					catch (Exception)
					{
					}
				}
				eP = new EP(host, port);
				eP.epSeqno = list.Count;
				list.Add(eP);
				continue;
			}
			string[] array = text.Split(new char[1] { ':' }, StringSplitOptions.RemoveEmptyEntries);
			if (array.Length == 1)
			{
				eP = new EP(array[0], DmOptionHelper.portDef);
				list.Add(eP);
			}
			else if (array.Length == 2)
			{
				int port2 = DmOptionHelper.portDef;
				try
				{
					port2 = Convert.ToInt32(array[1]);
				}
				catch (Exception)
				{
				}
				eP = new EP(array[0], port2);
				eP.epSeqno = list.Count;
				list.Add(eP);
			}
		}
		return new EPGroup(list);
	}
}
