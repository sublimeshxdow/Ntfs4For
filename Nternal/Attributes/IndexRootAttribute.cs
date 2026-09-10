using Nternal.Attributes;
using Nternal.Core.Streams;
using Nternal.Extensions;
using Nternal.Ntfs.Utils.DecodingUtils;
using Nternal.Structs;

namespace Nternal.Ntfs.Attributes;

public class IndexRootAttribute : IParsableAttribute, IRawAttribute
{
    public NtfsAttribute Underlying { get; init; } = null!;
    public IDataStream GetStream() => Underlying.Accessor;

    public enum Flags : byte
    {
        None = 0x00,
        LargeIndex = 0x01
    }

    public NtfsAttributeType IndexedType { get; private set; }
    public CollationRules CollationRule { get; private set; }
    public uint IndexBlockSize { get; private set; }
    public int BytesPerIndexBlock { get; private set; }
    public int OffsetToFirstEntry { get; private set; }
    public uint BytesInUse { get; private set; }
    public uint BytesAllocated { get; private set; }
    public Flags IndexFlags { get; private set; }

    public void ParseFromAccessor(bool paranoid)
    {
        if (paranoid && Underlying.IsNonResident)
            throw new InvalidDataException(
                $"Expected $Index_Root to be resident, but came out as non-resident - this should never be the case!");

        if (!Underlying.Accessor.TryGetMemory(out Span<byte> buffer))
        {
            buffer = new byte[Underlying.Accessor.AllocatedLength];
            Underlying.Accessor.ReadExactly(0, buffer);
        }

        IndexedType = (NtfsAttributeType)buffer.ReadUInt32(0);
        CollationRule = (CollationRules)buffer.ReadUInt32(4);
        IndexBlockSize = buffer.ReadUInt32(8);
        BytesPerIndexBlock = RawOrLogEncoding.ResolveSize(unchecked((sbyte)buffer[12]), Underlying.BytesPerCluster);

        OffsetToFirstEntry = buffer.ReadInt32(16);
        BytesInUse = buffer.ReadUInt32(20);
        BytesAllocated = buffer.ReadUInt32(24);
        IndexFlags = (Flags)buffer[28];
    }
}
