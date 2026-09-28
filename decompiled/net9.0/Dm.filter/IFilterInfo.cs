namespace Dm.filter;

internal interface IFilterInfo
{
	long ID { get; }

	BaseFilter filterHead { get; set; }

	LogInfo LogInfo { get; set; }

	RWInfo RWInfo { get; set; }

	RecoverInfo RecoverInfo { get; set; }
}
