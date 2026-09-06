using Nternal.Attributes;

namespace Nternal.Ntfs.Attributes;

public class ObjectIdAttribute : NtfsAttribute
{
    public Guid ObjectId { get; private set; }
    public Guid? BirthVolumeId { get; private set; }
    public Guid? BirthObjectId { get; private set; }
    public Guid? DomainId { get; private set; }

    public override void ParseFromAccessor(bool paranoid)
    {
        long length = Accessor.Length;
        if (length is not (16 or 32 or 48 or 64))
        {
            if (paranoid)
                throw new InvalidDataException($"$ObjectId should be either 16, 32, 48 or 64 bytes in length - got {length} bytes instead!");
            
            length = Math.Min(length, 64);
        }
        
        Span<byte> data = stackalloc byte[(int)length];
        Accessor.ReadExactly(0, data);
        
        ObjectId = new Guid(data[0..16]);
        if (length >= 32)
            BirthVolumeId = new Guid(data[16..32]);
        if (length >= 48)
            BirthObjectId = new Guid(data[32..48]);
        if (length >= 64)
            DomainId = new Guid(data[48..64]);
    }
}
