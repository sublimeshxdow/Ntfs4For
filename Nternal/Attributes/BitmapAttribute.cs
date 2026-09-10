using System.Runtime.InteropServices;
using Nternal.Attributes;

namespace Nternal.Ntfs.Attributes;

public class BitmapAttribute : IParsableAttribute
{
    public NtfsAttribute Underlying { get; init; } = null!;
    private ulong[]? _bitData;

    public void ParseFromAccessor(bool paranoid)
    {
        if (!BitConverter.IsLittleEndian)
            throw new PlatformNotSupportedException("Bitmap bit-ordering assumes a little-endian host.");
        
        _bitData = new ulong[(int)(Underlying.Accessor.AllocatedLength / 8)];
        Span<byte> data = MemoryMarshal.Cast<ulong, byte>(_bitData.AsSpan());
        Underlying.Accessor.ReadExactly(0, data);
    }

    public bool IsSet(long index)
    {
        if (_bitData == null)
            throw new InvalidOperationException("The attribute's data hasn't been loaded yet!");
        
        if (index < 0 || index  >= _bitData.LongLength * 64)
            throw new ArgumentOutOfRangeException(nameof(index));
        
        long word = index >> 6;
        int bit = (int)(index & 63);
        return (_bitData[word] & (1UL << bit)) != 0;
    }
}
