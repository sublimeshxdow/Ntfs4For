using System.Text;
using Nternal.Attributes;

namespace Nternal.Ntfs.Attributes;

public class VolumeNameAttribute : IParsableAttribute
{
    public NtfsAttribute Underlying { get; init; } = null!;
    public string VolumeName { get; private set; }
    
    public void ParseFromAccessor(bool paranoid)
    {
        Span<byte> buffer = new byte[Underlying.Accessor.AllocatedLength];
        Underlying.Accessor.ReadExactly(0, buffer);
        VolumeName = Encoding.Unicode.GetString(buffer);
    }
}
