using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;
using static System.Runtime.InteropServices.JavaScript.JSType;

namespace Nternal.Helpers
{
    internal static class FixupHelper
    {
        const int SequenceNumberStride = 512;

        public class NtfsFixupException : Exception
        {
            public NtfsFixupException(string message = "") : base(message) { }
        }

        public static void FixupRecord(Span<byte> record)
        {
            ushort usaOffset = BinaryPrimitives.ReadUInt16LittleEndian(record[0x04..]);
            ushort usaEntryCount = BinaryPrimitives.ReadUInt16LittleEndian(record[0x06..]);

            if (usaEntryCount < 2 || (usaEntryCount - 1) * SequenceNumberStride != record.Length
                || usaOffset + usaEntryCount * 2 > SequenceNumberStride - 2 || (usaOffset & 1) != 0)
                throw new NtfsFixupException("Malformed update sequence array!");

            ushort usn = BinaryPrimitives.ReadUInt16LittleEndian(record[usaOffset..]);
            for (int i = 1; i < usaEntryCount; i++)
            {
                var tail = record.Slice(i * SequenceNumberStride - 2, 2);
                if (BinaryPrimitives.ReadUInt16LittleEndian(tail) != usn)
                    throw new NtfsFixupException($"USN mismatch in stride {i - 1}!");

                record.Slice(usaOffset + i * 2, 2).CopyTo(tail);
            }
        }
    }
}
