using Nternal.Core.Streams;

namespace Nternal.Partitioning
{
    public class PartitionStream : IDataStream
    {
        public long Length => (long)(Entry.EndingLba - Entry.StartingLba) * Alignment;
        public long AllocatedLength => Length;
        public int Alignment => Underlying.Alignment;

        private IDataStream Underlying { get; }
        private IPartitionEntry Entry { get; }

        public PartitionStream(IDataStream underlying, IPartitionEntry partition)
        {
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
            long absolutePosition = (long)Entry.StartingLba * Alignment + position;
            return Underlying.Read(absolutePosition, buffer.Slice(0, length));
        }

        public void ReadExactly(long position, Span<byte> buffer)
        {
            int read = Read(position, buffer);
            if (read != buffer.Length)
                throw new InvalidOperationException($"Failed to read the exact number of bytes (required={buffer.Length}, read={read}).");
        }

        public bool TryGetMemory(out Span<byte> memory)
        {
            if (Underlying.TryGetMemory(out var underlying))
            {
                memory = underlying.Slice((int)((long)Entry.StartingLba * Alignment), (int)Length);
                return true;
            }

            memory = default;
            return false;
        }
    }
}
