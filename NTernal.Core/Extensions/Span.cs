using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Text;

namespace Nternal.Extensions
{
    public static class SpanExtensions
    {
        public static short ReadInt16(this ReadOnlySpan<byte> span, int offset = 0) => BinaryPrimitives.ReadInt16LittleEndian(span.Slice(offset, 2));
        public static ushort ReadUInt16(this ReadOnlySpan<byte> span, int offset = 0) => BinaryPrimitives.ReadUInt16LittleEndian(span.Slice(offset, 2));
        public static int ReadInt32(this ReadOnlySpan<byte> span, int offset = 0) => BinaryPrimitives.ReadInt32LittleEndian(span.Slice(offset, 4));
        public static uint ReadUInt32(this ReadOnlySpan<byte> span, int offset = 0) => BinaryPrimitives.ReadUInt32LittleEndian(span.Slice(offset, 4));
        public static long ReadInt64(this ReadOnlySpan<byte> span, int offset = 0) => BinaryPrimitives.ReadInt64LittleEndian(span.Slice(offset, 8));
        public static ulong ReadUInt64(this ReadOnlySpan<byte> span, int offset = 0) => BinaryPrimitives.ReadUInt64LittleEndian(span.Slice(offset, 8));
        public static string ReadAsciiString(this ReadOnlySpan<byte> span, int offset, int length) => Encoding.ASCII.GetString(span.Slice(offset, length));
        public static string ReadUnicodeString(this ReadOnlySpan<byte> span, int offset, int length) => Encoding.Unicode.GetString(span.Slice(offset, length));
        public static string ReadUnicodeString(this ReadOnlySpan<byte> span, int offset, int maxLength, bool requiresTermination)
        {
            int end = Math.Min(offset + maxLength, span.Length);
            for (int i = offset; i + 1 < end; i += 2)
                if (span[i] == 0 && span[i + 1] == 0)
                    return Encoding.Unicode.GetString(span[offset..i]);

            if (requiresTermination)
                throw new ArgumentException("String is not zero-terminated");

            return Encoding.Unicode.GetString(span.Slice(offset, end - offset));
        }
    }
}
