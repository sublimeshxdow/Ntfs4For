using Nternal.Core.Streams;
using System;
using System.Collections.Generic;
using System.Net.Sockets;
using System.Text;

namespace Nternal.Streams
{
    public class DataRun
    {
        public long StartingCluster { get; set; }
        public long Length { get; set; }
        public bool Sparse { get; set; }

        public DataRun(long startingCluster, long length, bool sparse)
        {
            StartingCluster = startingCluster;
            Length = length;
            Sparse = sparse;
        }
    }

    public class Extent
    {
        public long LowestVcn { get; set; }
        public long HighestVcn { get; set; }
        public List<DataRun> DataRuns { get; private set; } = [];

        public ushort CompressionUnitSize { get; set; }
        public long AllocatedSize { get; set; }
        public long RealSize { get; set; }
        public long InitializedSize { get; set; }
    }

    public class NonResidentDataStream : IDataStream
    {
        private IDataStream _underlying;

        public long Length => _underlying.Length;
        public int Alignment => _underlying.Alignment;

        public List<Extent> Extents { get; } = [];

        public NonResidentDataStream(IDataStream underlying)
        {
            _underlying = underlying;
        }

        public int Read(long position, Span<byte> buffer)
        {
            throw new NotImplementedException();
        }

        public void ReadExactly(long position, Span<byte> buffer)
        {
            throw new NotImplementedException();
        }

        public bool TryGetMemory(out ReadOnlyMemory<byte> memory)
        {
            memory = default;
            return false;
        }

        public void AddExtent(Extent extent)
        {
            Extents.Add(extent);
        }

        public void Dispose() { }
    }
}
