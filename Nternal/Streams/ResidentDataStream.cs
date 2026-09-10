using Nternal.Core.Streams;
using System;
using System.Collections.Generic;
using System.Text;

namespace Nternal.Streams
{
    public class ResidentDataStream : IDataStream
    {
        private byte[] _data;

        public long Length => _data.Length;
        public long AllocatedLength => _data.Length;
        public int Alignment => 1;

        public ResidentDataStream(byte[] data)
        {
            _data = data;
        }

        public int Read(long position, Span<byte> buffer)
        {
            ArgumentOutOfRangeException.ThrowIfNegative(position, nameof(position));
            if (position >= _data.Length || buffer.Length == 0)
                return 0;

            int count = Math.Min(buffer.Length, _data.Length - (int)position);
            _data.AsSpan((int)position, count).CopyTo(buffer);
            return count;
        }

        public void ReadExactly(long position, Span<byte> buffer)
        {
            ArgumentOutOfRangeException.ThrowIfNegative(position, nameof(position));
            if ((ulong)position + (ulong)buffer.Length > (ulong)_data.Length)
                throw new EndOfStreamException();

            _data.AsSpan((int)position, buffer.Length).CopyTo(buffer);
        }

        public bool TryGetMemory(out Span<byte> memory)
        {
            memory = _data;
            return true;
        }

        public void Dispose() { }
    }
}
