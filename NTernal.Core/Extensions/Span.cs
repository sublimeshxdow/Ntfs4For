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
    }
}
