using Nternal.Ntfs.Security;

namespace Nternal.Ntfs.Attributes;

public class SecurityDescriptorAttribute : IParsableAttribute
{
    public SecurityDescriptor Descriptor { get; private set; }
    public static NtfsAttributeType AttributeType => NtfsAttributeType.SecurityDescriptor;

    public NtfsAttribute Underlying { get; init; } = null!;

    public void ParseFromAccessor(bool paranoid)
    {
        Span<byte> data = new byte[Underlying.Accessor.AllocatedLength];
        Underlying.Accessor.ReadExactly(0, data);
        Descriptor = new SecurityDescriptor(data, paranoid);
    }
}