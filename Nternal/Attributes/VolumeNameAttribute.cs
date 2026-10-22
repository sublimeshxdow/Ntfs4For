using System.Text;

namespace Nternal.Ntfs.Attributes;

public class VolumeNameAttribute : IParsableAttribute
{
    public string VolumeName { get; private set; }
    public static NtfsAttributeType AttributeType => NtfsAttributeType.VolumeName;

    public NtfsAttribute Underlying { get; init; } = null!;

    public void ParseFromAccessor(bool paranoid)
    {
        Span<byte> buffer = new byte[Underlying.Accessor.AllocatedLength];
        Underlying.Accessor.ReadExactly(0, buffer);
        VolumeName = Encoding.Unicode.GetString(buffer);
    }
}