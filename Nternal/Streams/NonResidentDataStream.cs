using System.Runtime.InteropServices;
using Nternal.Core;

namespace Nternal.Ntfs.Streams;

public class DataRun
{
    public long? StartingCluster { get; set; }
    public long Length { get; set; }
    public bool Sparse { get; set; }
}

public class Extent
{
    public long LowestVcn { get; set; }
    public long HighestVcn { get; set; }
    public List<DataRun> DataRuns { get; set; } = [];
    public AttributeSource Source { get; set; }

    public ushort CompressionUnitSize { get; set; }
    public long? AllocatedSize { get; set; }
    public long? RealSize { get; set; }
    public long? InitializedSize { get; set; }
}

public unsafe partial class NonResidentDataStream : IDataStream
{
    private readonly Lock _loadLock = new();
    private readonly IDataStream _underlying;

    private IntPtr MemoryBuffer = IntPtr.Zero;
    private bool _hasCacheLoaded;

    public NonResidentDataStream(IDataStream underlying, int bytesPerCluster)
    {
        _underlying = underlying;
        Alignment = bytesPerCluster;
    }

    public List<Extent> Extents { get; } = [];

    public long Length => Extents.First().RealSize!.Value;
    public long AllocatedLength => Extents.First().AllocatedSize!.Value;
    public int Alignment { get; }

    public void LoadIntoMemory()
    {
        const int blockSize = 16777216;
        using var readingStream = new UnalignedDataStream(this, true);

        lock (_loadLock)
        {
            if (MemoryBuffer != IntPtr.Zero)
                return;

            long processed = 0, remaining = Length;
            MemoryBuffer = Marshal.AllocHGlobal((nint)Length);

            while (true)
            {
                var buffer = new Span<byte>((byte*)MemoryBuffer + processed, (int)Math.Min(remaining, blockSize));
                var read = readingStream.Read(processed, buffer);
                if (read < blockSize)
                    break;

                processed += read;
                remaining -= read;
            }

            _hasCacheLoaded = true;
        }
    }

    public void Dispose()
    {
        lock (_loadLock)
        {
            if (MemoryBuffer != IntPtr.Zero)
                Marshal.FreeHGlobal(MemoryBuffer);

            MemoryBuffer = IntPtr.Zero;
            _hasCacheLoaded = false;
        }
    }

    public int Read(long position, Span<byte> buffer)
    {
        if (_hasCacheLoaded)
        {
            ArgumentOutOfRangeException.ThrowIfNegative(position);
            if (position >= Length || buffer.Length == 0)
                return 0;

            var count = Math.Min(buffer.Length, Length - position);
            var span = new Span<byte>((byte*)MemoryBuffer + position, (int)count);
            span.CopyTo(buffer[..(int)count]);
            return (int)count;
        }

        if (position % Alignment != 0 || buffer.Length % Alignment != 0)
            throw new ArgumentException("Both position & buffer-size must be aligned to the cluster size!");

        var readClusters = ReadIntoBuffer(buffer, position / Alignment, buffer.Length / Alignment, false);
        return (int)(readClusters * Alignment);
    }

    public void ReadExactly(long position, Span<byte> buffer)
    {
        long read;
        if ((read = Read(position, buffer)) != buffer.Length)
            throw new EndOfStreamException();
    }

    public bool TryGetMemory(out Span<byte> memory)
    {
        memory = default;
        return false;
    }

    public int GetBytesPerCluster()
    {
        return Alignment;
    }

    public long? GetFirstNonSparseVcn()
    {
        long vcn = 0;

        foreach (var extent in Extents)
        foreach (var run in extent.DataRuns)
            if (run.Sparse)
                vcn += run.Length;
            else
                return vcn;

        return null;
    }

    internal bool IsSparseVcn(long vcn, ref SparseCheckState resumeState)
    {
        if (vcn < resumeState._resumeVcn)
        {
            // Caller went backwards
            resumeState._resumeExtentIndex = resumeState._resumeRunIndex = 0;
            resumeState._resumeVcn = 0;
        }

        for (var e = resumeState._resumeExtentIndex; e < Extents.Count; e++)
        {
            var runs = Extents[e].DataRuns;
            for (var r = e == resumeState._resumeExtentIndex ? resumeState._resumeRunIndex : 0;
                 r < runs.Count;
                 r++)
            {
                var run = runs[r];
                if (vcn >= resumeState._resumeVcn && vcn < resumeState._resumeVcn + run.Length)
                {
                    resumeState._resumeExtentIndex = e;
                    resumeState._resumeRunIndex = r;
                    return run.Sparse;
                }

                resumeState._resumeVcn += run.Length;
            }
        }

        throw new ArgumentOutOfRangeException(nameof(vcn),
            $"Attempted to retrieve VCN {vcn}, but this stream only contains {resumeState._resumeVcn} clusters!");
    }

    public void AddExtent(Extent extent)
    {
        Extents.Add(extent);
    }

    internal ref struct SparseCheckState
    {
        internal int _resumeExtentIndex;
        internal int _resumeRunIndex;
        internal long _resumeVcn;
    }
}