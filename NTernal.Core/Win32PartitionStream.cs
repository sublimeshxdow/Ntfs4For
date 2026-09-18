using Microsoft.Win32.SafeHandles;
using Nternal.Core.Streams;

namespace Nternal.Core
{
    public sealed class Win32PartitionStream : IDataStream
    {
        private readonly SafeFileHandle _handle;

        public long Length { get; }
        public long AllocatedLength { get; }
        public int Alignment => 512;

        public Win32PartitionStream(string devicePath, long length)
        {
            _handle = File.OpenHandle(
                devicePath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete,
                FileOptions.RandomAccess);

            Length = length;
            AllocatedLength = length;
        }

        public void LoadIntoMemory()
        {
            throw new NotImplementedException();
        }

        public int Read(long position, Span<byte> buffer)
        {
            if (position < 0 || position > Length)
                throw new ArgumentOutOfRangeException(nameof(position));

            if (buffer.Length == 0)
                return 0;

            long remaining = Length - position;
            int count = (int)Math.Min(buffer.Length, remaining);

            return RandomAccess.Read(_handle, buffer[..count], position);
        }

        public void ReadExactly(long position, Span<byte> buffer)
        {
            int totalRead = 0;

            while (totalRead < buffer.Length)
            {
                int read = Read(position + totalRead, buffer[totalRead..]);

                if (read == 0)
                {
                    throw new EndOfStreamException(
                        $"Could not read {buffer.Length} bytes at position {position}; " +
                        $"only {totalRead} bytes were available.");
                }

                totalRead += read;
            }
        }

        public bool TryGetMemory(out Span<byte> memory)
        {
            memory = default;
            return false;
        }

        public void Dispose()
        {
            _handle.Dispose();
        }
    }
}
