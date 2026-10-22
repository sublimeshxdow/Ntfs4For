namespace Nternal.Ntfs.Generic;

public class RawFileRecord : GenericFileRecord
{
    public RawFileRecord(ReadOnlySpan<byte> data) : base(data)
    {
        AttributeData = data[OffsetToAttributes..(int)AllocatedSizeOfRecord].ToArray();
    }

    public byte[] AttributeData { get; }
}