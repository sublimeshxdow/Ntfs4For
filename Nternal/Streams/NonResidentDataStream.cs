using Nternal.Core.Streams;
using System;
using System.Collections.Generic;
using System.Net.Sockets;
using System.Text;

namespace Nternal.Streams
{
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

        public ushort CompressionUnitSize { get; set; }
        public long? AllocatedSize { get; set; }
        public long? RealSize { get; set; }
        public long? InitializedSize { get; set; }
    }

    public partial class NonResidentDataStream : IDataStream
    {
        private IDataStream _underlying;
        private int _bytesPerCluster;

        public long Length => Extents.First().RealSize!.Value;
        public long AllocatedLength => Extents.First().AllocatedSize!.Value;
        public int Alignment => _bytesPerCluster;
        public int GetBytesPerCluster() => _bytesPerCluster;

        public List<Extent> Extents { get; } = [];

        public NonResidentDataStream(IDataStream underlying, int bytesPerCluster)
        {
            _underlying = underlying;
            _bytesPerCluster = bytesPerCluster;
        }

        public int Read(long position, Span<byte> buffer)
        {
            if (position % Alignment != 0 || buffer.Length % Alignment != 0)
                throw new ArgumentException("Both position & buffer-size must be aligned to the cluster size!");

            ReadIntoBuffer(buffer, position / _bytesPerCluster, buffer.Length / _bytesPerCluster);
            return buffer.Length;
        }

        public void ReadExactly(long position, Span<byte> buffer)
        {
            if (Read(position, buffer) != buffer.Length)
                throw new EndOfStreamException();
        }

        public long? GetFirstNonSparseVcn()
        {
            long vcn = 0;

            foreach (var extent in Extents)
            {
                foreach (var run in extent.DataRuns)
                    if (run.Sparse)
                        vcn += run.Length;
                    else
                        return vcn;
            }
            
            return null;
        }

        private int _resumeExtentIndex = 0;
        private int _resumeRunIndex = 0;
        private long _resumeVcn = 0;

        public bool IsSparseVcn(long vcn)
        {
            if (vcn < _resumeVcn)
            {
                // Caller went backwards
                _resumeExtentIndex = _resumeRunIndex = 0;
                _resumeVcn = 0;
            }

            for (int e = _resumeExtentIndex; e < Extents.Count; e++)
            {
                var runs = Extents[e].DataRuns;
                for (int r = (e == _resumeExtentIndex ? _resumeRunIndex : 0); r < runs.Count; r++)
                {
                    var run = runs[r];
                    if (vcn >= _resumeVcn && vcn < _resumeVcn + run.Length)
                    {
                        _resumeExtentIndex = e;
                        _resumeRunIndex = r;
                        return run.Sparse;
                    }
                    _resumeVcn += run.Length;
                }
            }

            throw new ArgumentOutOfRangeException(nameof(vcn),
                $"Attempted to retrieve VCN {vcn}, but this stream only contains {_resumeVcn} clusters!");
        }

        public bool TryGetMemory(out Span<byte> memory)
        {
            memory = default;
            return false;
        }

        public void AddExtent(Extent extent)
        {
            Extents.Add(extent);
        }

        public void Dispose()
        {
        }
    }
}