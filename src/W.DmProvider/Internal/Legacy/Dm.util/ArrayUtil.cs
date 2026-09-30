using System.Data.Common;

namespace W.Dm.util;

internal class ArrayUtil
{
	internal static bool IsAllMatch(DbParameterCollection collection, DmParameterInternal[] parasInternal)
	{
		foreach (DmParameterInternal dmParameterInternal in parasInternal)
		{
			if (!collection.Contains(dmParameterInternal.GetName()))
			{
				return false;
			}
		}
		return true;
	}
}
