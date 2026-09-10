using Nternal.Attributes;
using Nternal.Core.Streams;

namespace Nternal.Ntfs.Attributes;

public class DataAttribute : IRawAttribute
{
    public NtfsAttribute Underlying { get; init; } = null!;
    public IDataStream GetStream() => Underlying.Accessor;
}
