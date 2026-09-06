using Nternal.Attributes;
using Nternal.Internal;
using Nternal.Streams;

namespace Nternal.Helpers;

public class RunHelpers
{
    public static Extent GetExtentOfNrh(RawNonResidentAttributeHeader header, ReadOnlySpan<byte> dataruns)
    {
        bool isFirstExtent = header.StartingVcn == 0;

        return new Extent()
        {
            LowestVcn = header.StartingVcn,
            HighestVcn = header.LastVcn,
            DataRuns = ParseDataRuns(dataruns, header.Common.Flags.HasFlag(NtfsAttributeFlags.Sparse)),
            
            CompressionUnitSize = header.CompressionUnitSize,
            
            AllocatedSize = isFirstExtent ? header.AllocatedSize : null,
            RealSize = isFirstExtent ? header.RealSize : null,
            InitializedSize  = isFirstExtent ? header.InitializedSize : null
        };
    }
    
    static List<DataRun> ParseDataRuns(ReadOnlySpan<byte> data, bool acceptSparse)
    {
        List<DataRun> dataruns = new(8);
        int offset = 0;
        long lastOffset = 0;
        
        while (offset < data.Length && data[offset] != 0)
        {
            byte lengthField = data[offset++];
            int lengthOfLength = lengthField & 0x0f;
            int lengthOfOffset = lengthField >> 4;
            
            if (lengthOfLength > 8 || lengthOfOffset > 8)
                throw new InvalidDataException($"Invalid data-run header 0x{lengthField:X2}: field size must be 0-8 bytes.");

            if (!acceptSparse && lengthOfOffset == 0)
                throw new InvalidDataException($"Attempted to parse a data-run with a zero-length offset field, but the extend is not flagged as sparse!");

            long currentOffsetRaw = 0, currentLength = 0;
            for (int i = 0; i < lengthOfLength; i++)
                currentLength |= (long)data[offset++] << (i * 8);

            int offsetBytes;
            for (offsetBytes = 0; offsetBytes < lengthOfOffset; offsetBytes++)
                currentOffsetRaw |= (long)data[offset++] << (offsetBytes * 8);

            long currentOffset = ReadSignedLittleEndian(currentOffsetRaw, offsetBytes * 8);
            lastOffset += currentOffset;

            bool sparse = lengthOfOffset == 0;
            dataruns.Add(new DataRun()
            {
                StartingCluster = sparse ? null : lastOffset,
                Length = currentLength,
                Sparse = sparse
            });
        }

        return dataruns;
    }
    
    static long ReadSignedLittleEndian(long value, int bits)
    {
        if (bits < 64)
        {
            long signBit = 1L << (bits - 1);
            if ((value & signBit) != 0)
                value |= -1L << bits;
        }

        return value;
    }
}