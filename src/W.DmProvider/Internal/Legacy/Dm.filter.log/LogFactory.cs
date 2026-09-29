using System;
using System.Collections.Generic;

namespace W.Dm.filter.log;

internal class LogFactory
{
	private static Dictionary<object, Logger> instances = new Dictionary<object, Logger>();

	public static ILogger getLog(Type clazz)
	{
		lock (instances)
		{
			if (!instances.ContainsKey(clazz))
			{
				instances[clazz] = new Logger(clazz.FullName);
			}
			return instances[clazz];
		}
	}

	public static ILogger getLog(string name)
	{
		lock (instances)
		{
			if (!instances.ContainsKey(name))
			{
				instances[name] = new Logger(name);
			}
			return instances[name];
		}
	}

	public virtual void releaseAll()
	{
		instances.Clear();
	}
}
