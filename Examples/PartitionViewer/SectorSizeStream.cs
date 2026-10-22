using Nternal.Core;

namespace PartitionViewer;

/// <summary>
///     Presents a stream with a different logical sector size, e.g. a 4Kn disk image that the OS reports
///     as having 512-byte sectors. Reads are passed through unchanged.
/// </summary>
internal sealed class SectorSizeStream(IDataStream underlying, int sectorSize) : IDataStream
{
    public long Length => underlying.Length;
    public long AllocatedLength => underlying.AllocatedLength;
    public int Alignment => sectorSize;

    public void LoadIntoMemory()
    {
        underlying.LoadIntoMemory();
    }

    public int Read(long position, Span<byte> buffer)
    {
        return underlying.Read(position, buffer);
    }

    public void ReadExactly(long position, Span<byte> buffer)
    {
        underlying.ReadExactly(position, buffer);
    }

    public bool TryGetMemory(out Span<byte> memory)
    {
        return underlying.TryGetMemory(out memory);
    }

    public void Dispose()
    {
        // The underlying stream is owned by the caller.
    }
}