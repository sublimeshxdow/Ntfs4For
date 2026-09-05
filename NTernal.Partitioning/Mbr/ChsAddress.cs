using System;
using System.Collections.Generic;
using System.Text;

namespace Nternal.Partitioning.Mbr
{
    public struct ChsAddress
    {
        public ChsAddress(ReadOnlySpan<byte> bytes)
        {
            if (bytes.Length != 3)
                throw new ArgumentException("CHS address must be exactly 3 bytes long.", nameof(bytes));

            Head = bytes[0];
            Sector = (byte)(bytes[1] & 0x3F); // Lower 6 bits for sector
            Cylinder = (ushort)(((bytes[1] & 0xC0) << 2) | bytes[2]); // Upper 2 bits of sector and full cylinder)
        }

        public byte Head { get; init; }
        public byte Sector { get; init; }
        public ushort Cylinder { get; init; }
    }
}
