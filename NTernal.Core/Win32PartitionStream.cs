using Microsoft.Win32.SafeHandles;
using Nternal.Core.Streams;
using System.Diagnostics;

namespace Nternal.Core
{
    public sealed class Win32PartitionStream : IDataStream
    {
        private readonly SafeFileHandle _handle;

        public long Length { get; }
        public long AllocatedLength { get; }
        public int Alignment { get; }

        /// <summary>
        /// For cases where the logical partition sector size is larger than the logical physical sector size
        /// (f.e. HDDs without 512e support)
        /// </summary>
        /// <returns></returns>
        private UnalignedDataStream FallbackAlignerStream { get; }

        public Win32PartitionStream(string devicePath)
        {
            _handle = File.OpenHandle(
                devicePath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete,
                FileOptions.RandomAccess);

            var sizes = DiskInfo.GetSectorSizes(devicePath);
            Alignment = (int)sizes.Logical;

            Length = DiskInfo.GetLength(_handle);
            AllocatedLength = Length;

            FallbackAlignerStream = new(this);
        }

        public void LoadIntoMemory()
        {
            throw new NotSupportedException($"Reading an entire drive into memory is NOT supported!");
        }

        public int Read(long position, Span<byte> buffer)
        {
            if (position % Alignment != 0 || buffer.Length % Alignment != 0)
            {
                Debugger.Break();
                return FallbackAlignerStream.Read(position, buffer);
            }

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
            if (position % Alignment != 0 || buffer.Length % Alignment != 0)
            {
                Debugger.Break();
                FallbackAlignerStream.Read(position, buffer);
                return;
            }

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
