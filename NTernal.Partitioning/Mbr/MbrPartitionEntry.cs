using Nternal.Core.Streams;
using Nternal.Extensions;
using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Text;

namespace Nternal.Partitioning.Mbr
{
    public enum BootState
    {
        Bootable,
        NonBootable,
        Invalid
    }

    public struct MbrPartitionEntry
    {
        public MbrPartitionEntry(ReadOnlySpan<byte> data)
        {
            if (data.Length != 16)
                throw new ArgumentException("Partition entry must be exactly 16 bytes long.", nameof(data));

            switch (data[0])
            {
                case 0: Bootable = BootState.NonBootable; break;
                case 0x80: Bootable = BootState.Bootable; break;
                default: Bootable = BootState.Invalid; break;
            }

            PartitionType = (MbrPartitionType)data[4];
            ChsStart = new ChsAddress(data.Slice(1, 3));
            ChsEnd = new ChsAddress(data.Slice(5, 3));
            StartingLba = data.ReadUInt32(8);
            SizeInSectors = data.ReadUInt32(12);
        }

        public MbrPartitionStream CreateDataStream(IDataStream underlying)
        {
            return new MbrPartitionStream(underlying, this);
        }

        public BootState Bootable { get; set; }
        public MbrPartitionType PartitionType { get; set; }
        public ChsAddress ChsStart { get; set; }
        public ChsAddress ChsEnd { get; set; }
        public uint StartingLba { get; set; }
        public uint SizeInSectors { get; set; }
    }
}
