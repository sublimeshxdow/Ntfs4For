using System.Text;
using Nternal.Attributes;

namespace Nternal.Ntfs.Attributes;

public class VolumeNameAttribute : NtfsAttribute
{
    public string VolumeName { get; private set; }
    
    public override void ParseFromAccessor(bool paranoid)
    {
        Span<byte> buffer = new byte[Accessor.Length];
        Accessor.ReadExactly(0, buffer);
        VolumeName = Encoding.Unicode.GetString(buffer);
    }
}