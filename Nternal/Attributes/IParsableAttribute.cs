using Nternal.Attributes;
using Nternal.Core;
using Nternal.Core.Streams;

namespace Nternal.Ntfs.Attributes;

public interface IParsableAttribute
{
    static abstract NtfsAttributeType AttributeType { get; }

    NtfsAttribute Underlying { get; init; }
    void ParseFromAccessor(bool paranoid);
}
