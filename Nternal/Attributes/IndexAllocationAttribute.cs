using Nternal.Attributes;
using Nternal.Core.Streams;

namespace Nternal.Ntfs.Attributes;

public class IndexAllocationAttribute : IParsableAttribute
{
    private const string ExpectedSignature = "INDX";
    
    public NtfsAttribute Underlying { get; init; } = null!;
    public void ParseFromAccessor(bool paranoid)
    { }

    public IDataStream GetStream() => Underlying.Accessor;
}
