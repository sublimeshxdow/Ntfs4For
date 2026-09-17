using Nternal.Core.Streams;
using Nternal.Ntfs;
using Nternal.Ntfs.Compression;
using Nternal.Streams;

public class CompressedDataStream : IDataStream
{
    private readonly NonResidentDataStream _underlying;

    private byte[]? _cached;
    private bool _isCached = false;
    private readonly Lock _loadLock = new();

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

    public void LoadIntoMemory()
    {
        if (_isCached)
            return;

        lock (_loadLock)
        {
            if (_isCached)
                return;

            int bytesPerUnit = GetBytesPerUnit();
            long length = ((_underlying.Length + bytesPerUnit - 1) / bytesPerUnit) * bytesPerUnit;
            if (length > int.MaxValue)
                throw new InvalidOperationException("Cannot load a file larger than 2GBs into memory!"); // TODO?

            _cached = new byte[length];
            int parallelismDegree = _underlying.Extents.Count <= NtfsPolicyConfig.CompressionExtentParallelismThreshold
                ? 1
                : NtfsPolicyConfig.CompressionMaxDegreeOfParallelism;

            Parallel.ForEach(_underlying.Extents, new ParallelOptions()
            {
                MaxDegreeOfParallelism = parallelismDegree
            }, extent =>
            {
                int offset = (int)(extent.LowestVcn * _underlying.GetBytesPerCluster());
                int limit = (int)((extent.HighestVcn + 1) * _underlying.GetBytesPerCluster());
                Read(offset, _cached.AsSpan()[offset..limit]);
            });

            _isCached = true;
        }
    }

    public int Read(long position, Span<byte> buffer)
    {
        if (_isCached)
        {
            ArgumentOutOfRangeException.ThrowIfNegative(position, nameof(position));
            if (position >= _underlying.Length || buffer.Length == 0)
                return 0;

            int count = Math.Min(buffer.Length, (int)(_underlying.Length - position));
            _cached.AsSpan((int)position, count).CopyTo(buffer);
            return count;
        }

        if (position % Alignment != 0 || buffer.Length % Alignment != 0)
            throw new ArgumentException("Both position & buffer-size must be aligned to the cluster size!");

        int bytesPerUnit = GetBytesPerUnit();
        long remainingStart = position;
        int remainingLength = buffer.Length;
        int bufferOffset = 0;

        NonResidentDataStream.SparseCheckState state = default;

        while (remainingLength > 0)
        {
            long unitByteOffset = (remainingStart / bytesPerUnit) * bytesPerUnit;
            int withinUnitOffset = (int)(remainingStart - unitByteOffset);
            int chunkLength = Math.Min(remainingLength, bytesPerUnit - withinUnitOffset);

            ReadWithinOneUnit(unitByteOffset, withinUnitOffset, bytesPerUnit, buffer.Slice(bufferOffset, chunkLength),
                ref state);

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

    private void ReadWithinOneUnit(long unitByteOffset, int withinUnitOffset, int bytesPerUnit, Span<byte> destination,
        ref NonResidentDataStream.SparseCheckState state)
    {
        long firstUnitVcn = unitByteOffset / _underlying.GetBytesPerCluster();
        int clustersPerUnit = bytesPerUnit / _underlying.GetBytesPerCluster();
        bool isCompressed = _underlying.IsSparseVcn(firstUnitVcn + clustersPerUnit - 1, ref state);

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