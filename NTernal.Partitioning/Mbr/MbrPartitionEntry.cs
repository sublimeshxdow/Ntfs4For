using Nternal.Core.Streams;
using Nternal.Extensions;
using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;

namespace Nternal.Partitioning.Mbr
{
    public enum BootState
    {
        Bootable,
        NonBootable,
        Invalid
    }

    public readonly struct MbrPartitionEntry : IPartitionEntry
    {
        public MbrPartitionEntry(ReadOnlySpan<byte> data, IDataStream? stream = null)
        {
            Underlying = stream;
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

        public PartitionStream CreatePartitionStream()
        {
            if (Underlying == null)
                throw new InvalidOperationException($"The MasterBootRecord used to create this MbrPartitionEntry was created without a valid disk stream!");
            
            return PartitionType == MbrPartitionType.Empty ? throw new InvalidOperationException($"Cannot create a partition stream for an unused partition!")
                : new PartitionStream(Underlying, this);
        }

        public PartitionStream CreateDataStream(IDataStream underlying)
        {
            return new PartitionStream(underlying, this);
        }

        private IDataStream? Underlying { get; }

        public BootState Bootable { get; }
        public MbrPartitionType PartitionType { get; }
        public ChsAddress ChsStart { get; }
        public ChsAddress ChsEnd { get; }
        public uint StartingLba { get; }
        public uint SizeInSectors { get; }

        ulong IPartitionEntry.StartingLba => StartingLba;
        ulong IPartitionEntry.EndingLba => StartingLba + SizeInSectors;
    }
}
