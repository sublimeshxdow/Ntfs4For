namespace Nternal.Structs;

public class RawFileRecord : GenericFileRecord
{
    public byte[] AttributeData { get; }

    public RawFileRecord(ReadOnlySpan<byte> data) : base(data)
    {
        AttributeData = data[OffsetToAttributes..(int)AllocatedSizeOfRecord].ToArray();
    }
}
