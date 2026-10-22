using Nternal.Core;
using Nternal.Ntfs.Compression;

namespace Nternal.Ntfs.Streams;

public class CompressedDataStream : IDataStream
{
    private readonly Lock _loadLock = new();
    private readonly NonResidentDataStream _underlying;

    private byte[]? _cached;
    private bool _isCached;

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

            var bytesPerUnit = GetBytesPerUnit();
            var length = (_underlying.Length + bytesPerUnit - 1) / bytesPerUnit * bytesPerUnit;
            if (length > int.MaxValue)
                throw new NotImplementedException("Cannot load a file larger than 2GBs into memory!"); // TODO?

            _cached = new byte[length];
            var parallelismDegree = _underlying.Extents.Count <= NtfsPolicyConfig.CompressionExtentParallelismThreshold
                ? 1
                : NtfsPolicyConfig.CompressionMaxDegreeOfParallelism;

            Parallel.ForEach(_underlying.Extents, new ParallelOptions
            {
                MaxDegreeOfParallelism = parallelismDegree
            }, extent =>
            {
                var offset = (int)(extent.LowestVcn * _underlying.GetBytesPerCluster());
                var limit = (int)((extent.HighestVcn + 1) * _underlying.GetBytesPerCluster());
                Read(offset, _cached.AsSpan()[offset..limit]);
            });

            _isCached = true;
        }
    }

    public int Read(long position, Span<byte> buffer)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(position);
        if (position >= Length || buffer.Length == 0)
            return 0;

        var logicalCount = (int)Math.Min(buffer.Length, Length - position);

        if (_isCached)
        {
            _cached.AsSpan((int)position, logicalCount).CopyTo(buffer);
            return logicalCount;
        }

        if (position % Alignment != 0 || buffer.Length % Alignment != 0)
            throw new ArgumentException("Both position & buffer-size must be aligned to the cluster size!");

        var workLength = (int)Math.Min(buffer.Length, AllocatedLength - position);

        var bytesPerUnit = GetBytesPerUnit();
        var remainingStart = position;
        var remainingLength = workLength;
        var bufferOffset = 0;

        NonResidentDataStream.SparseCheckState state = default;

        while (remainingLength > 0)
        {
            var unitByteOffset = remainingStart / bytesPerUnit * bytesPerUnit;
            var withinUnitOffset = (int)(remainingStart - unitByteOffset);
            var chunkLength = Math.Min(remainingLength, bytesPerUnit - withinUnitOffset);

            ReadWithinOneUnit(unitByteOffset, withinUnitOffset, bytesPerUnit, buffer.Slice(bufferOffset, chunkLength),
                ref state);

            remainingStart += chunkLength;
            bufferOffset += chunkLength;
            remainingLength -= chunkLength;
        }

        return logicalCount;
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

    private int GetBytesPerUnit()
    {
        var compressionUnitExp = _underlying.Extents.First().CompressionUnitSize;
        var clustersPerUnit = compressionUnitExp == 0 ? 16 : 1 << compressionUnitExp;
        return clustersPerUnit * _underlying.GetBytesPerCluster();
    }

    private void ReadWithinOneUnit(long unitByteOffset, int withinUnitOffset, int bytesPerUnit, Span<byte> destination,
        ref NonResidentDataStream.SparseCheckState state)
    {
        var firstUnitVcn = unitByteOffset / _underlying.GetBytesPerCluster();
        var clustersPerUnit = bytesPerUnit / _underlying.GetBytesPerCluster();
        var isCompressed = _underlying.IsSparseVcn(firstUnitVcn + clustersPerUnit - 1, ref state);

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
}