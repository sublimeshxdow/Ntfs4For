using Nternal.Attributes;
using Nternal.Core.Streams;

namespace Nternal.Ntfs;

public interface IRawAttribute
{
    NtfsAttribute Underlying { get; init; }
    IDataStream GetStream();
}
