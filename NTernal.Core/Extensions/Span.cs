using System.Buffers.Binary;
using System.Text;

namespace Nternal.Core.Extensions;

public static class SpanExtensions
{
    public static short ReadInt16(this ReadOnlySpan<byte> span, int offset = 0)
    {
        return BinaryPrimitives.ReadInt16LittleEndian(span.Slice(offset, 2));
    }

    public static ushort ReadUInt16(this ReadOnlySpan<byte> span, int offset = 0)
    {
        return BinaryPrimitives.ReadUInt16LittleEndian(span.Slice(offset, 2));
    }

    public static int ReadInt32(this ReadOnlySpan<byte> span, int offset = 0)
    {
        return BinaryPrimitives.ReadInt32LittleEndian(span.Slice(offset, 4));
    }

    public static uint ReadUInt32(this ReadOnlySpan<byte> span, int offset = 0)
    {
        return BinaryPrimitives.ReadUInt32LittleEndian(span.Slice(offset, 4));
    }

    public static long ReadInt64(this ReadOnlySpan<byte> span, int offset = 0)
    {
        return BinaryPrimitives.ReadInt64LittleEndian(span.Slice(offset, 8));
    }

    public static ulong ReadUInt64(this ReadOnlySpan<byte> span, int offset = 0)
    {
        return BinaryPrimitives.ReadUInt64LittleEndian(span.Slice(offset, 8));
    }

    public static string ReadAsciiString(this ReadOnlySpan<byte> span, int offset, int length)
    {
        return Encoding.ASCII.GetString(span.Slice(offset, length));
    }

    public static string ReadUnicodeString(this ReadOnlySpan<byte> span, int offset, int length)
    {
        return Encoding.Unicode.GetString(span.Slice(offset, length));
    }

    /// <summary>
    ///     Reads an unicode string from the current span.
    /// </summary>
    /// <param name="span">The span to read the unicode string from.</param>
    /// <param name="offset">Where the unicode string starts within the span.</param>
    /// <param name="maxLength">How many bytes (not chars) the unicode string covers.</param>
    /// <param name="requiresTermination">Whether the unicode string must be zero-terminated.</param>
    /// <returns></returns>
    /// <exception cref="ArgumentException"></exception>
    public static string ReadUnicodeString(this ReadOnlySpan<byte> span, int offset, int maxLength,
        bool requiresTermination)
    {
        var end = Math.Min(offset + maxLength, span.Length);
        for (var i = offset; i + 1 < end; i += 2)
            if (span[i] == 0 && span[i + 1] == 0)
                return Encoding.Unicode.GetString(span[offset..i]);

        if (requiresTermination)
            throw new ArgumentException("String is not zero-terminated");

        return Encoding.Unicode.GetString(span.Slice(offset, end - offset));
    }
}