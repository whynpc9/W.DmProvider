namespace W.Dm;

public class FldrIndexInfo
{
	private int indexId;

	private int nth;

	private byte validFlag;

	public FldrIndexInfo()
	{
	}

	public FldrIndexInfo(int indexId, int nth, byte validFlag)
	{
		this.indexId = indexId;
		this.nth = nth;
		this.validFlag = validFlag;
	}

	public int getIndexId()
	{
		return indexId;
	}

	public void setIndexId(int indexId)
	{
		this.indexId = indexId;
	}

	public int getNth()
	{
		return nth;
	}

	public void setNth(int nth)
	{
		this.nth = nth;
	}

	public byte getValidFlag()
	{
		return validFlag;
	}

	public void setValidFlag(byte validFlag)
	{
		this.validFlag = validFlag;
	}

	public override string ToString()
	{
		return "FldrIndexInfo{indexId=" + indexId + ", nth=" + nth + ", validFlag=" + validFlag + "}";
	}
}
