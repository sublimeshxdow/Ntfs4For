using Nternal.Attributes;
using Nternal.Ntfs.Security;

namespace Nternal.Ntfs.Attributes;

public class SecurityDescriptorAttribute : NtfsAttribute
{
    public SecurityDescriptor Descriptor { get; private set; }

    public override void ParseFromAccessor(bool paranoid)
    {
        Span<byte> data = new byte[Accessor.Length];
        Accessor.ReadExactly(0, data);
        Descriptor = new SecurityDescriptor(data, paranoid);
    }
}