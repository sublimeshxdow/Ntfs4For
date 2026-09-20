using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Nternal.Attributes;

namespace Nternal.Ntfs.Attributes;

public class BitmapAttribute : BitmapIndex, IParsableAttribute
{
    public static NtfsAttributeType AttributeType => NtfsAttributeType.Bitmap;
  
    public NtfsAttribute Underlying { get; init; } = null!;

    public void ParseFromAccessor(bool paranoid)
    {
        ReadBitmap(Underlying.Accessor);
    }
}