using Nternal.Attributes;
using Nternal.Core.Streams;

namespace Nternal.Ntfs.Attributes;

public class DataAttribute : NtfsAttribute
{
    public IDataStream GetDataStream() => Accessor;
}
