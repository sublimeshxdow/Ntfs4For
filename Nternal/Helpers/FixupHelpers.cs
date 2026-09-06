using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;

namespace Nternal.Helpers
{
    public class FixupHelpers
    {
        public class NtfsFixupException : Exception
        {
            public NtfsFixupException(string message = "") : base(message) { }
        }

        public static void FixupRecord(Span<byte> record, ushort bytesPerSector)
        {
            // Confirmed this is the case for both $Mft-Records & Index-Buffers
            ushort offsetToUs = BinaryPrimitives.ReadUInt16LittleEndian(record.Slice(0x04, 0x02));
            ushort sizeOfUsa = BinaryPrimitives.ReadUInt16LittleEndian(record.Slice(0x06, 0x02));

            ushort usn = BinaryPrimitives.ReadUInt16LittleEndian(record.Slice(offsetToUs, 2));
            var sectorUsa = MemoryMarshal.Cast<byte, ushort>(record.Slice(offsetToUs + 2, (sizeOfUsa - 1) * 2));

            for (int i = 0; i < sectorUsa.Length; i++)
            {
                var sectorUsnSlice = record.Slice((i + 1) * bytesPerSector - 2, 2);
                ushort currentEosValue = BinaryPrimitives.ReadUInt16LittleEndian(sectorUsnSlice);

                if (currentEosValue != usn)
                    throw new NtfsFixupException($"Failed to fixup record - USN-Value of Sector {i} ({currentEosValue})" +
                        $" does not match expected USN-Value ({usn})!");

                BinaryPrimitives.WriteUInt16LittleEndian(sectorUsnSlice, sectorUsa[i]);
            }
        }
    }
}
