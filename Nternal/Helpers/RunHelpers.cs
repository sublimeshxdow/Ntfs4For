using Nternal.Ntfs.Attributes;
using Nternal.Ntfs.Internal;
using Nternal.Ntfs.Streams;

namespace Nternal.Ntfs.Helpers;

public class RunHelpers
{
    public static Extent GetExtentOfNrh(RawNonResidentAttributeHeader header, ReadOnlySpan<byte> dataruns)
    {
        var isFirstExtent = header.StartingVcn == 0;

        return new Extent
        {
            LowestVcn = header.StartingVcn,
            HighestVcn = header.LastVcn,
            DataRuns = ParseDataRuns(dataruns,
                header.Common.Flags.HasFlag(NtfsAttributeFlags.Sparse)
                || header.Common.Flags.HasFlag(NtfsAttributeFlags.Compressed)),
            CompressionUnitSize = header.CompressionUnitSize,

            AllocatedSize = isFirstExtent ? header.AllocatedSize : null,
            RealSize = isFirstExtent ? header.RealSize : null,
            InitializedSize = isFirstExtent ? header.InitializedSize : null
        };
    }

    private static List<DataRun> ParseDataRuns(ReadOnlySpan<byte> data, bool acceptSparse)
    {
        List<DataRun> dataruns = new(8);
        var offset = 0;
        long lastOffset = 0;

        while (offset < data.Length && data[offset] != 0)
        {
            var lengthField = data[offset++];
            var lengthOfLength = lengthField & 0x0f;
            var lengthOfOffset = lengthField >> 4;

            if (lengthOfLength > 8 || lengthOfOffset > 8)
                throw new InvalidDataException(
                    $"Invalid data-run header 0x{lengthField:X2}: field size must be 0-8 bytes.");

            // Let's treat the list shape as authoritative - not only compressed (not sparse-flagged)
            // have sparse runs, but so does $BadClus:$Bad
            /*
            if (!acceptSparse && lengthOfOffset == 0)
                throw new InvalidDataException(
                    $"Attempted to parse a data-run with a zero-length offset field, but the extend is not flagged as sparse!");
            */

            long currentOffsetRaw = 0, currentLength = 0;
            for (var i = 0; i < lengthOfLength; i++)
                currentLength |= (long)data[offset++] << (i * 8);

            int offsetBytes;
            for (offsetBytes = 0; offsetBytes < lengthOfOffset; offsetBytes++)
                currentOffsetRaw |= (long)data[offset++] << (offsetBytes * 8);

            var currentOffset = ReadSignedLittleEndian(currentOffsetRaw, offsetBytes * 8);
            lastOffset += currentOffset;

            var sparse = lengthOfOffset == 0;
            dataruns.Add(new DataRun
            {
                StartingCluster = sparse ? null : lastOffset,
                Length = currentLength,
                Sparse = sparse
            });
        }

        return dataruns;
    }

    private static long ReadSignedLittleEndian(long value, int bits)
    {
        if (bits < 64)
        {
            var signBit = 1L << (bits - 1);
            if ((value & signBit) != 0)
                value |= -1L << bits;
        }

        return value;
    }
}