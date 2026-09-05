using Nternal.Core.Streams;
using System;
using System.Collections.Generic;
using System.Text;

namespace Nternal.Partitioning.Mbr
{
    public class MbrPartitionStream : IDataStream
    {
        public long Length => Entry.SizeInSectors * Alignment;
        public int Alignment => Underlying.Alignment;

        private IDataStream Underlying { get; }
        private MbrPartitionEntry Entry { get; }

        public MbrPartitionStream(IDataStream underlying, MbrPartitionEntry partition)
        {
            if (partition.PartitionType == MbrPartitionType.Empty)
                throw new ArgumentException("Cannot create a stream for an empty partition.", nameof(partition));

            Underlying = underlying;
            Entry = partition;
        }

        public void Dispose()
        {

        }

        public int Read(long position, Span<byte> buffer)
        {
            if (position < 0)
                throw new ArgumentOutOfRangeException(nameof(position), "Position cannot be negative.");

            if (position % Alignment != 0 || buffer.Length % Alignment != 0)
                throw new ArgumentException($"Position and buffer length must be aligned to the sector size ({Alignment}).", nameof(buffer));

            if (position >= Length)
                return 0;

            int length = (int)Math.Min(buffer.Length, Length - position);
            long absolutePosition = Entry.StartingLba * Alignment + position;
            return Underlying.Read(absolutePosition, buffer.Slice(0, length));
        }

        public void ReadExactly(long position, Span<byte> buffer)
        {
            int read = Read(position, buffer);
            if (read != buffer.Length)
                throw new InvalidOperationException($"Failed to read the exact number of bytes (required={buffer.Length}, read={read}).");
        }

        public bool TryGetMemory(out ReadOnlyMemory<byte> memory)
        {
            if (Underlying.TryGetMemory(out var underlying))
            {
                memory = underlying.Slice((int)(Entry.StartingLba * Alignment), (int)Length);
                return true;
            }

            memory = default;
            return false;
        }
    }
}
