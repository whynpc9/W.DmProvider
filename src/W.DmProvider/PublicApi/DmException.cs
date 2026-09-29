using System;
using System.Data.Common;
using System.Runtime.Serialization;
using W.Dm.util;

namespace W.Dm;

[Serializable]
public sealed class DmException : DbException
{
	private DmErrorCollection m_ErrorCollection = new DmErrorCollection();

	public int Number => m_ErrorCollection[0].State;

	public string Schema => m_ErrorCollection[0].Schema;

	public string Table => m_ErrorCollection[0].Table;

	public string Col => m_ErrorCollection[0].Col;

	internal DmException(string message)
		: base(message)
	{
	}

	internal DmException(DmError err)
		: base(err.Message)
	{
		m_ErrorCollection.Add(err);
		Data["Server Error Code"] = err.State;
	}

	internal DmException(string message, DmError err)
		: base(message, err.State)
	{
		m_ErrorCollection.Add(err);
		Data["Server Error Code"] = err.State;
	}

	internal DmException(SerializationInfo info, StreamingContext context)
		: base(info, context)
	{
	}

	public DmException CreateCopy()
	{
		return new DmException(ToString() + StringUtil.LINE_SEPARATOR)
		{
			m_ErrorCollection = m_ErrorCollection
		};
	}
}
