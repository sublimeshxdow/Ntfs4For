using Nternal.Extensions;
using System;
using System.Collections.Generic;
using System.Text;
using Nternal.Core.Streams;

namespace Nternal.Partitioning.Gpt
{
    /// <summary>
    /// Represents a GPT-styled partition entry.
    /// </summary>
    public readonly struct GptPartitionEntry : IPartitionEntry
    {
        /// <summary>
        /// Constructs a partition entry from a series of bytes.
        /// </summary>
        /// <param name="buffer">The bytes to interpret as partition entry.</param>
        /// <exception cref="InvalidDataException">Thrown if the specified input buffer does not contain enough bytes to be a valid partition entry.</exception>
        public GptPartitionEntry(ReadOnlySpan<byte> buffer)
        {
            if (buffer.Length < MinimumEntrySize)
                throw new InvalidDataException(
                    $"The minimum GPT partition entry size is {MinimumEntrySize}, but only got {buffer.Length} bytes!");

            TypeGuid = new Guid(buffer[0..16]);
            PartitionGuid = new Guid(buffer[16..32]);
            StartingLba = buffer.ReadUInt64(32);
            InclusiveEndingLba = buffer.ReadUInt64(40);
            Flags = (GptPartitionFlags)buffer.ReadUInt64(48);
            Name = buffer.ReadUnicodeString(56, 72, false);
        }

        /// <summary>
        /// Creates a stream representing the partition defined by this partition entry.
        /// </summary>
        /// <param name="underlying">A stream representing the underlying disk.</param>
        /// <returns>A stream representing the respective partition.</returns>
        public PartitionStream CreatePartitionStream(IDataStream underlying)
        {
            ArgumentNullException.ThrowIfNull(underlying);
            return IsUnused ? throw new InvalidOperationException($"Cannot create a partition stream for an unused partition!")
                : new PartitionStream(underlying, this);
        }

        public const uint MinimumEntrySize = 128;

        public Guid TypeGuid { get; init; }
        public Guid PartitionGuid { get; init; }
        public ulong StartingLba { get; init; }
        public ulong InclusiveEndingLba { get; init; }
        public GptPartitionFlags Flags { get; init; }
        public string Name { get; init; }

        public bool IsUnused => TypeGuid == Guid.Empty;
    }
}
