using Nternal.Core.Streams;
using Nternal.Extensions;
using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;

namespace Nternal.Partitioning.Mbr
{
    /// <summary>
    /// Represents the boot-state of the respective partition.
    /// </summary>
    public enum BootState
    {
        /// <summary>
        /// The partition is bootable.
        /// </summary>
        Bootable,
        /// <summary>
        /// The partition is not bootable.
        /// </summary>
        NonBootable,
        /// <summary>
        /// The partition is neither marked as bootable nor as unbootable, and the entry should be treated as corrupted.
        /// </summary>
        Invalid
    }

    /// <summary>
    /// Represents an MBR-styled partition entry.
    /// </summary>
    public readonly struct MbrPartitionEntry : IPartitionEntry
    {
        /// <summary>
        /// Constructs an MBR partition entry from a series of bytes.
        /// </summary>
        /// <param name="data">The data to interpret the partition entry from.</param>
        /// <param name="stream">The underlying stream representing the disk.</param>
        /// <exception cref="ArgumentException">Thrown when the data representing the entry is not exactly 16 bytes large.</exception>
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

        /// <summary>
        /// Creates a stream representing the partition defined by this partition entry.
        /// </summary>
        /// <returns>A stream representing the respective partition.</returns>
        /// <exception cref="InvalidOperationException">Thrown if no underlying disk stream was specified in the constructor.</exception>
        public PartitionStream CreatePartitionStream()
        {
            if (Underlying == null)
                throw new InvalidOperationException($"The MasterBootRecord used to create this MbrPartitionEntry was created without a valid disk stream!");

            return PartitionType == MbrPartitionType.Empty ? throw new InvalidOperationException($"Cannot create a partition stream for an unused partition!")
                : new PartitionStream(Underlying, this);
        }

        /// <summary>
        /// Creates a stream representing the partition defined by this partition entry.
        /// </summary>
        /// <param name="underlying">A stream representing the underlying disk.</param>
        /// <returns>A stream representing the respective partition.</returns>
        public PartitionStream CreatePartitionStream(IDataStream underlying)
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
        ulong IPartitionEntry.InclusiveEndingLba => StartingLba + SizeInSectors - 1;
    }
}
