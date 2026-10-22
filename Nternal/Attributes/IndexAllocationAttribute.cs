using Nternal.Core;

namespace Nternal.Ntfs.Attributes;

public class IndexAllocationAttribute : IParsableAttribute
{
    public const string ExpectedMagicNumber = "INDX";
    public static NtfsAttributeType AttributeType => NtfsAttributeType.IndexAllocation;

    public NtfsAttribute Underlying { get; init; } = null!;

    public void ParseFromAccessor(bool paranoid)
    {
    }

    public IDataStream GetStream()
    {
        return Underlying.Accessor;
    }
}