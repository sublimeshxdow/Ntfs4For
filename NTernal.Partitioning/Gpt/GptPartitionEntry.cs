using Nternal.Extensions;
using System;
using System.Collections.Generic;
using System.Text;
using Nternal.Core.Streams;

namespace Nternal.Partitioning.Gpt
{
    public readonly struct GptPartitionEntry : IPartitionEntry
    {
        public GptPartitionEntry(ReadOnlySpan<byte> buffer, bool paranoid)
        {
            if (buffer.Length < MinimumEntrySize)
                throw new InvalidDataException(
                    $"The minimum GPT partition entry size is {MinimumEntrySize}, but only got {buffer.Length} bytes!");

            TypeGuid = new Guid(buffer[0..16]);
            PartitionGuid = new Guid(buffer[16..32]);
            StartingLba = buffer.ReadUInt64(32);
            EndingLba = buffer.ReadUInt64(40);
            Flags = (GptPartitionFlags)buffer.ReadUInt64(48);
            Name = buffer.ReadUnicodeString(56, 72, paranoid);
        }
        
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
        public ulong EndingLba { get; init; }
        public GptPartitionFlags Flags { get; init; }
        public string Name { get; init; }

        public bool IsUnused => TypeGuid == Guid.Empty;
    }
}
