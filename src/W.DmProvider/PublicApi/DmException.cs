using System;
using System.Data.Common;
using System.Runtime.Serialization;
using W.Dm.util;
using W.Dm.Internal.Sessions;

namespace W.Dm;

[Serializable]
public class DmException : DbException
{
	private DmErrorCollection m_ErrorCollection = new DmErrorCollection();
	public DmFailureInfo FailureInfo { get; private set; }
	public DmErrorKind ErrorKind => FailureInfo?.ErrorKind ?? DmErrorKind.Unknown;
	public string DriverErrorCode => FailureInfo?.ErrorCode;
	internal void SetFailureInfo(DmFailureInfo info) => FailureInfo = info;
	internal bool HasVerifiedServerResponse { get; private set; }
	internal bool CanPreserveSessionAfterServerError { get; private set; }
	internal OperationIdentity VerifiedResponseIdentity { get; private set; }

	internal void MarkVerifiedServerResponse(OperationIdentity identity, bool preserveSession)
	{
		VerifiedResponseIdentity = identity;
		CanPreserveSessionAfterServerError = preserveSession;
		HasVerifiedServerResponse = true;
	}

	public int Number => m_ErrorCollection.Count == 0 ? 0 : m_ErrorCollection[0].State;

	public string Schema => m_ErrorCollection.Count == 0 ? string.Empty : m_ErrorCollection[0].Schema;

	public string Table => m_ErrorCollection.Count == 0 ? string.Empty : m_ErrorCollection[0].Table;

	public string Col => m_ErrorCollection.Count == 0 ? string.Empty : m_ErrorCollection[0].Col;

	internal DmException(string message)
		: base(message)
	{
	}

	protected DmException(string message, Exception innerException)
		: base(message, innerException)
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
			m_ErrorCollection = m_ErrorCollection,
			FailureInfo = FailureInfo
		};
	}
}
