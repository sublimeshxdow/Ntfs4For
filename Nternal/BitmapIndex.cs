using System.Buffers;
using System.Runtime.InteropServices;
using Nternal.Core.Streams;

namespace Nternal.Ntfs;

public class BitmapIndex
{
    private ulong[]? _bitData;
    private long _pastLimit;

    public void ReadBitmap(IDataStream accessor)
    {
        if (!BitConverter.IsLittleEndian)
            throw new PlatformNotSupportedException("Bitmap bit-ordering assumes a little-endian host.");

        _bitData = new ulong[(int)(accessor.AllocatedLength / 8)];
        Span<byte> data = MemoryMarshal.Cast<ulong, byte>(_bitData.AsSpan());
        accessor.ReadExactly(0, data);
        _pastLimit = _bitData.LongLength * 64;
    }

    public bool IsInRange(long index) => index >= 0 && index < _pastLimit;

    public bool IsSet(long index)
    {
        if (_bitData == null)
            throw new InvalidOperationException("The attribute's data hasn't been loaded yet!");

        if (index < 0 || index >= _pastLimit)
            throw new ArgumentOutOfRangeException(nameof(index));

        long word = index >> 6;
        int bit = (int)(index & 63);
        return (_bitData[word] & (1UL << bit)) != 0;
    }
}
