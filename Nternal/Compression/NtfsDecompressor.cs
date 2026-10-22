using System.Numerics;
using Nternal.Core.Extensions;

namespace Nternal.Ntfs.Compression;

public class NtfsDecompressor
{
    public static Span<byte> DecompressUnit(ReadOnlySpan<byte> source, Span<byte> destination)
    {
        int read = 0, written = 0;
        while (source.Length - read >= 2 && source.ReadUInt16(read) != 0)
        {
            int lastRead = 0, lastWritten = 0;
            DecompressChunk(source[read..], destination[written..], ref lastRead, ref lastWritten);
            read += lastRead;
            written += lastWritten;
        }

        return destination;
    }

    private static Span<byte> DecompressChunk(ReadOnlySpan<byte> source, Span<byte> destination,
        ref int sourceBytesProcessed, ref int outputBytesWritten)
    {
        if (source.Length < 2)
            throw new InvalidDataException(
                $"LZNT1 chunk requires at least 2 bytes for the header, got {source.Length}.");

        var header = source.ReadUInt16();

        // zero-header => stop
        if (header == 0)
            return Span<byte>.Empty;

        if (((header >> 12) & 0x07) != 0x03)
            throw new InvalidDataException(
                $"Invalid compression header signature (expected 011, got {(header >> 12) & 0x07:X}).");

        var compressed = (header & 0x8000) != 0;
        var payloadLength = (ushort)((header & 0xFFF) + 1);
        sourceBytesProcessed = 2 + payloadLength;

        if (source.Length < 2 + payloadLength)
            throw new InvalidDataException(
                $"LZNT1 chunk declares a payload of {payloadLength} bytes, but only {source.Length - 2} are available.");

        if (!compressed)
        {
            if (destination.Length < payloadLength)
                throw new InvalidDataException(
                    $"Destination buffer ({destination.Length} bytes) is too small for the uncompressed payload ({payloadLength} bytes).");

            source.Slice(2, payloadLength).CopyTo(destination);
            outputBytesWritten = payloadLength;
            return destination[..payloadLength];
        }

        DecompressChunkInternal(source.Slice(2, payloadLength), destination, ref outputBytesWritten);
        return destination[..outputBytesWritten];
    }

    private static void DecompressChunkInternal(ReadOnlySpan<byte> compressed, Span<byte> destination, ref int written)
    {
        int inputPosition = 0, outputPosition = 0;

        while (true)
        {
            if (inputPosition >= compressed.Length)
            {
                written = outputPosition;
                return;
            }

            var flag = compressed[inputPosition++];
            for (var b = 0; b < 8; b++)
            {
                // Check whether we are done
                if (inputPosition >= compressed.Length)
                {
                    written = outputPosition;
                    return;
                }

                var isLiteral = (flag & (1 << b)) == 0;

                // Simple literal => copy
                if (isLiteral)
                {
                    if (outputPosition >= destination.Length)
                        throw new InvalidDataException(
                            $"LZNT1 chunk decompresses beyond the destination buffer ({destination.Length} bytes) while writing a literal.");

                    var literal = compressed[inputPosition++];
                    destination[outputPosition++] = literal;
                }

                // Back-reference
                else
                {
                    if (inputPosition + 2 > compressed.Length)
                        throw new InvalidDataException(
                            "LZNT1 chunk ends mid-way through a back-reference symbol (need 2 bytes).");

                    var reference = compressed.ReadUInt16(inputPosition);
                    inputPosition += 2;

                    var exp = 4;
                    if (outputPosition > 16)
                    {
                        var bitLength = BitOperations.Log2((uint)(outputPosition - 1)) + 1;
                        exp = Math.Min(bitLength, 12);
                    }

                    int lengthBits = 16 - exp, mask = (1 << lengthBits) - 1;
                    int extractOffset = (reference >> lengthBits) + 1, extractLength = (reference & mask) + 3;
                    var absExtractOffset = outputPosition - extractOffset;

                    if (absExtractOffset < 0)
                        throw new InvalidDataException(
                            $"LZNT1 back-reference offset {extractOffset} reads before the start of the output " +
                            $"(current position {outputPosition}).");

                    if (outputPosition + extractLength > destination.Length)
                        throw new InvalidDataException(
                            $"LZNT1 back-reference would write {outputPosition + extractLength} bytes, " +
                            $"exceeding the destination buffer ({destination.Length} bytes).");

                    // Copy
                    var remaining = extractLength;
                    while (remaining > 0)
                    {
                        var toCopy = Math.Min(remaining, extractOffset);
                        destination.Slice(absExtractOffset, toCopy).CopyTo(destination.Slice(outputPosition));
                        outputPosition += toCopy;
                        remaining -= toCopy;
                    }
                }
            }
        }
    }
}