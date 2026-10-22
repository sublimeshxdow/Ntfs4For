using System.Buffers.Binary;

namespace Nternal.Ntfs.Helpers;

internal static class FixupHelper
{
    private const int SequenceNumberStride = 512;

    public static void FixupRecord(Span<byte> record)
    {
        var usaOffset = BinaryPrimitives.ReadUInt16LittleEndian(record[0x04..]);
        var usaEntryCount = BinaryPrimitives.ReadUInt16LittleEndian(record[0x06..]);

        if (usaEntryCount < 2 || (usaEntryCount - 1) * SequenceNumberStride != record.Length
                              || usaOffset + usaEntryCount * 2 > SequenceNumberStride - 2 || (usaOffset & 1) != 0)
            throw new NtfsFixupException("Malformed update sequence array!");

        var usn = BinaryPrimitives.ReadUInt16LittleEndian(record[usaOffset..]);
        for (var i = 1; i < usaEntryCount; i++)
        {
            var tail = record.Slice(i * SequenceNumberStride - 2, 2);
            if (BinaryPrimitives.ReadUInt16LittleEndian(tail) != usn)
                throw new NtfsFixupException($"USN mismatch in stride {i - 1}!");

            record.Slice(usaOffset + i * 2, 2).CopyTo(tail);
        }
    }

    public class NtfsFixupException : Exception
    {
        public NtfsFixupException(string message = "") : base(message)
        {
        }
    }
}