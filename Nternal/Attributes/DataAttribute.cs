using Nternal.Attributes;
using Nternal.Core;
using Nternal.Core.Streams;
using Nternal.Streams;

namespace Nternal.Ntfs.Attributes;

public class DataAttribute : IParsableAttribute
{
    public static NtfsAttributeType AttributeType => NtfsAttributeType.Data;
   
    public NtfsAttribute Underlying { get; init; } = null!;

    public void ParseFromAccessor(bool paranoid)
    {
    }

    public IDataStream GetStream()
    {
        if (Underlying.Flags.HasFlag(NtfsAttributeFlags.Compressed) &&
            Underlying.Flags.HasFlag(NtfsAttributeFlags.Encrypted))
            throw new NotImplementedException(
                $"Cannot retrieve Stream of attribute $Data - combination of Compression & Encryption is not supported!");

        IDataStream baseStream;
        // Normal data
        if (Underlying.Flags == NtfsAttributeFlags.None || Underlying.Flags == NtfsAttributeFlags.Sparse)
            baseStream = Underlying.Accessor;
        // Compressed stream
        else if (Underlying.Flags == NtfsAttributeFlags.Compressed)
            baseStream = new CompressedDataStream((NonResidentDataStream)Underlying.Accessor);
        // Encrypted (maybe also sparse) stream
        else if (Underlying.Flags.HasFlag(NtfsAttributeFlags.Encrypted))
            throw new NotImplementedException($"At the moment, encrypted files are not supported!");
        else
            throw new InvalidOperationException($"Invalid combination of Attribute Flags - {Underlying.Flags}!");

        return new UnalignedDataStream(baseStream);
    }
}
