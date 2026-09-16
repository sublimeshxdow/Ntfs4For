using Nternal.Core.Streams;
using Nternal.Ntfs.Compression;
using Nternal.Streams;

public class CompressedDataStream : IDataStream
{
    private readonly NonResidentDataStream _underlying;

    public CompressedDataStream(NonResidentDataStream underlying)
    {
        _underlying = underlying;
    }

    public void Dispose()
    {
        
    }

    public long Length => _underlying.Length;
    public long AllocatedLength => _underlying.AllocatedLength;
    public int Alignment => _underlying.Alignment;

    public int Read(long position, Span<byte> buffer)
    {
        if (position % Alignment != 0 || buffer.Length % Alignment != 0)
            throw new ArgumentException("Both position & buffer-size must be aligned to the cluster size!");

        int bytesPerUnit = GetBytesPerUnit();
        long remainingStart = position;
        int remainingLength = buffer.Length;
        int bufferOffset = 0;

        while (remainingLength > 0)
        {
            long unitByteOffset = (remainingStart / bytesPerUnit) * bytesPerUnit;
            int withinUnitOffset = (int)(remainingStart - unitByteOffset);
            int chunkLength = Math.Min(remainingLength, bytesPerUnit - withinUnitOffset);

            ReadWithinOneUnit(unitByteOffset, withinUnitOffset, bytesPerUnit, buffer.Slice(bufferOffset, chunkLength));

            remainingStart += chunkLength;
            bufferOffset += chunkLength;
            remainingLength -= chunkLength;
        }

        return buffer.Length;
    }

    private int GetBytesPerUnit()
    {
        ushort compressionUnitExp = _underlying.Extents.First().CompressionUnitSize;
        int clustersPerUnit = compressionUnitExp == 0 ? 16 : 1 << compressionUnitExp;
        return clustersPerUnit * _underlying.GetBytesPerCluster();
    }

    private void ReadWithinOneUnit(long unitByteOffset, int withinUnitOffset, int bytesPerUnit, Span<byte> destination)
    {
        long firstUnitVcn = unitByteOffset / _underlying.GetBytesPerCluster();
        int clustersPerUnit = bytesPerUnit / _underlying.GetBytesPerCluster();
        bool isCompressed = _underlying.IsSparseVcn(firstUnitVcn + clustersPerUnit - 1);

        if (!isCompressed)
        {
            // Uncompressed unit - read the requested slice directly.
            _underlying.Read(unitByteOffset + withinUnitOffset, destination);
            return;
        }

        Span<byte> rawUnitBuffer = new byte[bytesPerUnit];
        _underlying.Read(unitByteOffset, rawUnitBuffer);

        Span<byte> decompressedUnit = new byte[bytesPerUnit];
        NtfsDecompressor.DecompressUnit(rawUnitBuffer, decompressedUnit);

        decompressedUnit.Slice(withinUnitOffset, destination.Length).CopyTo(destination);
    }

    public void ReadExactly(long position, Span<byte> buffer)
    {
        if (Read(position, buffer) != buffer.Length)
            throw new EndOfStreamException();
    }

    public bool TryGetMemory(out Span<byte> memory)
    {
        memory = default;
        return false;
    }
}
