using Nternal.Attributes;
using Nternal.Core.Streams;

namespace Nternal.Ntfs.Attributes;

public class DataAttribute : IParsableAttribute
{
    public NtfsAttribute Underlying { get; init; } = null!;
    
    public void ParseFromAccessor(bool paranoid) { }

    public IDataStream GetStream() => Underlying.Accessor;
}
