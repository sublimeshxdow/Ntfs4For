using Nternal.Attributes;

namespace Nternal.Ntfs.Attributes;

public interface IParsableAttribute
{
    NtfsAttribute Underlying { get; init; }
    void ParseFromAccessor(bool paranoid);
}
