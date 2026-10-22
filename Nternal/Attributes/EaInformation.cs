using Nternal.Core.Extensions;

namespace Nternal.Ntfs.Attributes;

public class EaInformationAttribute : IParsableAttribute
{
    public ushort SizeOfPackedEAs { get; set; }
    public ushort NumberOfNeededEAs { get; set; }
    public uint SizeOfUnpackedEAs { get; set; }
    public static NtfsAttributeType AttributeType => NtfsAttributeType.EaInfo;

    public NtfsAttribute Underlying { get; init; } = null!;

    public void ParseFromAccessor(bool paranoid)
    {
        if (paranoid && Underlying.Accessor.Length != 8)
            throw new InvalidDataException("This attribute should have an exact size of 8 bytes!");

        var buffer = Underlying.Accessor.Length <= 1024
            ? stackalloc byte[(int)Underlying.Accessor.Length]
            : new byte[Underlying.Accessor.Length];
        Underlying.Accessor.ReadExactly(0, buffer);

        SizeOfPackedEAs = buffer.ReadUInt16();
        NumberOfNeededEAs = buffer.ReadUInt16(2);
        SizeOfUnpackedEAs = buffer.ReadUInt32(4);
    }
}