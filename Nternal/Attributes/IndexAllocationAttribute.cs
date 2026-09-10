using Nternal.Attributes;
using Nternal.Core.Streams;

namespace Nternal.Ntfs.Attributes;

public class IndexAllocationAttribute : IRawAttribute
{
    private const string ExpectedSignature = "INDX";
    
    public NtfsAttribute Underlying { get; init; } = null!;
    public IDataStream GetStream() => Underlying.Accessor;
}
