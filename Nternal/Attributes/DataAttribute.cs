using Nternal.Attributes;
using Nternal.Core.Streams;
using Nternal.Streams;

namespace Nternal.Ntfs.Attributes;

public class DataAttribute : IParsableAttribute
{
    public NtfsAttribute Underlying { get; init; } = null!;

    public void ParseFromAccessor(bool paranoid)
    {
        
    }

    public IDataStream GetStream()
    {
        if (Underlying.Flags == NtfsAttributeFlags.None || Underlying.Flags == NtfsAttributeFlags.Sparse)
            return Underlying.Accessor;
        if (Underlying.Flags == NtfsAttributeFlags.Compressed)
            return new CompressedDataStream((NonResidentDataStream)Underlying.Accessor);
        if (Underlying.Flags == NtfsAttributeFlags.Encrypted)
            throw new NotImplementedException($"At the moment, encrypted files are not supported!");

        throw new InvalidOperationException($"Invalid combination of Attribute Flags - {Underlying.Flags}!");
    }
}
