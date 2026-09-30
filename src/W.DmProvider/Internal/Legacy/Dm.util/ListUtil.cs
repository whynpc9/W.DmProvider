using System.Collections.Generic;
using W.Dm.filter;

namespace W.Dm.util;

internal static class ListUtil
{
	internal static IFilter[] toArray(IList<IFilter> filterList)
	{
		IFilter[] array = new IFilter[filterList.Count];
		for (int i = 0; i < array.Length; i++)
		{
			array[i] = filterList[i];
		}
		return array;
	}
}
