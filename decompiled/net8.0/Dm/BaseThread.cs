using System;
using System.Collections.Generic;
using System.Threading;

namespace Dm;

public class BaseThread
{
	private static List<BaseThread> threadList;

	protected Thread _basethread;

	public BaseThread(string name)
	{
		_basethread = new Thread(Run);
		_basethread.Name = "DmProvider-" + name;
		_basethread.IsBackground = true;
		threadList.Add(this);
	}

	static BaseThread()
	{
		threadList = new List<BaseThread>();
		AppDomain.CurrentDomain.ProcessExit += OnProcessExit;
	}

	public static void OnProcessExit(object sender, EventArgs eventArgs)
	{
		if (threadList == null)
		{
			return;
		}
		foreach (BaseThread thread in threadList)
		{
			try
			{
				if (thread.isAlive())
				{
					thread.BeforeExit();
				}
			}
			catch (Exception)
			{
			}
		}
		threadList.Clear();
	}

	protected virtual void BeforeExit()
	{
	}

	public bool isAlive()
	{
		return _basethread.IsAlive;
	}

	public virtual void Run()
	{
	}
}
