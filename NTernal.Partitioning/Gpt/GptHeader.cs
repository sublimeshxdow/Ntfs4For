using Nternal.Extensions;
using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO.Hashing;
using System.Text;

namespace Nternal.Partitioning.Gpt
{
    public class GptHeader
    {
        public const int MinimumHeaderSize = 92;
        public const string ExpectedSignature = "EFI PART";

        public GptHeader(Span<byte> data, bool paranoid, uint blockSize)
        {
            if (data.Length < MinimumHeaderSize)
                throw new ArgumentException($"Data is too short. Expected at least {MinimumHeaderSize} bytes, but got {data.Length}.", nameof(data));

            CalculateChecksum(data[0..MinimumHeaderSize], paranoid);

            Signature = data.ReadAsciiString(0, 8);
            if (paranoid && Signature != ExpectedSignature)
                throw new ArgumentException($"Invalid GPT signature. Expected '{ExpectedSignature}', but got '{Signature}'.", nameof(data));

            uint headerSize = data.ReadUInt32(12);
            if (paranoid && headerSize > blockSize)
                throw new ArgumentException($"Invalid GPT header size. Expected no more than {blockSize} bytes, but got {headerSize}.", nameof(data));

            Revision = data.ReadUInt32(8);
            HeaderCrc32 = data.ReadUInt32(16);
            LbaOfHeader = data.ReadUInt64(24);
            LbaOfHeaderCopy = data.ReadUInt64(32);
            FirstUsableLba = data.ReadUInt64(40);
            LastUsableLba = data.ReadUInt64(48);
            DiskGuid = new Guid(data.Slice(56, 16));
            StartOfPartitionEntries = data.ReadUInt64(72);
            NumberOfPartitionEntries = data.ReadUInt32(80);
            SizeOfPartitionEntry = data.ReadUInt32(84);
            PartitionArrayCrc32 = data.ReadUInt32(88);

            if (paranoid)
                for (int i = MinimumHeaderSize; i < headerSize; i++)
                    if (data[i] != 0)
                        throw new InvalidDataException("The rest of the header must be zero!");
        }

        public void CalculateChecksum(Span<byte> data, bool validate)
        {
            uint writtenCrc32 = data.ReadUInt32(16);

            // Temporarily insert zero-bytes for hashing
            BinaryPrimitives.WriteUInt32LittleEndian(data[16..20], 0);
            uint calculatedCrc32 = Crc32.HashToUInt32(data);
            BinaryPrimitives.WriteUInt32LittleEndian(data[16..20], writtenCrc32);

            if (calculatedCrc32 != writtenCrc32)
                throw new InvalidDataException($"Calculated header checksum ({calculatedCrc32.ToString("X8")}) does not match written checksum ({writtenCrc32.ToString("X8")})!");
        }

        public string Signature { get; }
        public uint Revision { get; }
        public uint HeaderCrc32 { get; }
        public ulong LbaOfHeader { get; }
        public ulong LbaOfHeaderCopy { get; }
        public ulong FirstUsableLba { get; }
        public ulong LastUsableLba { get; }
        public Guid DiskGuid { get; }
        public ulong StartOfPartitionEntries { get; }
        public uint NumberOfPartitionEntries { get; }
        public uint SizeOfPartitionEntry { get; }
        public uint PartitionArrayCrc32 { get; }
    }
}
