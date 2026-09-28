using System;
using System.Collections.Generic;
using Dm.Config;

namespace Dm;

internal class WellDistributeSelector : EPSelector
{
	private int curServerPos = -1;

	private object obj = new object();

	internal WellDistributeSelector(List<EP> serverList)
		: base(serverList)
	{
		curServerPos = ((serverList == null || serverList.Count == 0) ? (-1) : (new Random().Next(serverList.Count) - 1));
	}

	public override EP[] sortDBList(bool firstTime)
	{
		EP[] array = null;
		if (firstTime)
		{
			int num = dbs.Length;
			int num2 = 0;
			array = new EP[num];
			lock (obj)
			{
				curServerPos = (curServerPos + 1) % num;
				num2 = curServerPos;
			}
			for (int i = 0; i < num; i++)
			{
				array[i] = dbs[(i + num2) % num];
			}
		}
		else
		{
			array = dbs;
		}
		sortByServerMode(array, firstTime);
		return array;
	}

	protected void sortByServerMode(EP[] sortEps, bool firstTime)
	{
		Array.Sort(sortEps, delegate(EP x, EP y)
		{
			if (x.getSort(firstTime) > y.getSort(firstTime))
			{
				return -1;
			}
			return (x.getSort(firstTime) != y.getSort(firstTime)) ? 1 : 0;
		});
	}

	public override bool checkServerMode(bool lastTime, DmConnection conn)
	{
		if (conn.ConnProperty.LoginDscCtrl && !conn.ConnProperty.DscControl)
		{
			conn.do_Close();
			DmError.ThrowDmException(DmErrorDefinition.ECNET_INVALID_SERVER_MODE);
		}
		if (conn.ConnProperty.LoginStatus > LoginStatus.OFF && conn.GetConnInstance().ConnProperty.SvrStat != (int)conn.ConnProperty.LoginStatus)
		{
			conn.do_Close();
			DmError.ThrowDmException(DmErrorDefinition.ECNET_INVALID_SERVER_MODE);
		}
		if (lastTime)
		{
			return conn.ConnProperty.LoginMode switch
			{
				LoginModeFlag.onlyprimary => conn.GetConnInstance().ConnProperty.SvrMode == 1, 
				LoginModeFlag.onlystandby => conn.GetConnInstance().ConnProperty.SvrMode == 2, 
				_ => true, 
			};
		}
		switch (conn.ConnProperty.LoginMode)
		{
		case LoginModeFlag.normalfirst:
			return conn.GetConnInstance().ConnProperty.SvrMode == 0;
		case LoginModeFlag.primaryfirst:
		case LoginModeFlag.onlyprimary:
			return conn.GetConnInstance().ConnProperty.SvrMode == 1;
		case LoginModeFlag.onlystandby:
		case LoginModeFlag.standbyfirst:
			return conn.GetConnInstance().ConnProperty.SvrMode == 2;
		default:
			return false;
		}
	}
}
