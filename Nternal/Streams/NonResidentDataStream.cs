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

        public List<Extent> Extents { get; } = [];

        public NonResidentDataStream(IDataStream underlying, int bytesPerCluster)
        {
            _underlying = underlying;
            _bytesPerCluster = bytesPerCluster;
        }

        public int Read(long position, Span<byte> buffer)
        {
            if (position % Alignment != 0 && buffer.Length % Alignment != 0)
                throw new ArgumentException("Both position & buffer-size must be aligned to the cluster size!");
            
            ReadIntoBuffer(buffer, position / _bytesPerCluster, buffer.Length / _bytesPerCluster);
            return buffer.Length;
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

        public void AddExtent(Extent extent)
        {
            Extents.Add(extent);
        }

        public void Dispose() { }
    }
}
