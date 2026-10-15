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
            Sector = (byte)(bytes[1] & 0x3F);
            Cylinder = (ushort)(((bytes[1] & 0xC0) << 2) | bytes[2]);
        }

        public byte Head { get; init; }
        public byte Sector { get; init; }
        public ushort Cylinder { get; init; }
    }
}
