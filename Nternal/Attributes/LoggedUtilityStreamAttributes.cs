using Nternal.Core;

namespace Nternal.Ntfs.Attributes;

public class LoggedUtilityStreamAttribute : IParsableAttribute
{
    public static NtfsAttributeType AttributeType => NtfsAttributeType.LoggedUtilityStream;

    public NtfsAttribute Underlying { get; init; } = null!;

    public void ParseFromAccessor(bool paranoid)
    {
    }

    public IDataStream GetStream()
    {
        return Underlying.Accessor;
    }
}