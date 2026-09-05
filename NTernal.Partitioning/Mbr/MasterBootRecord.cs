using Nternal.Extensions;

namespace Nternal.Partitioning.Mbr
{
    public class MasterBootRecord
    {
        public MasterBootRecord(ReadOnlySpan<byte> data, bool paranoid = true)
        {
            if (data.Length != 512)
                throw new ArgumentException("MBR must be exactly 512 bytes long.", nameof(data));

            BootstrapCode = data.Slice(0, 440).ToArray();
            DiskSignature = data.ReadUInt32(440);

            for (int i = 0; i < NumberOfBootEntries; i++)
            {
                var entryData = data.Slice(446 + i * 16, 16);
                PartitionEntries[i] = new MbrPartitionEntry(entryData);
            }

            BootSignature = data.ReadUInt16(510);
            if (paranoid && BootSignature != ExpectedBootSignature)
                throw new InvalidDataException($"Invalid boot signature: {BootSignature:X4}. Expected: {ExpectedBootSignature:X4}.");
        }

        public const ushort ExpectedBootSignature = 0xAA55;
        public const byte NumberOfBootEntries = 4;

        public byte[] BootstrapCode { get; }
        public uint DiskSignature { get; }

        public MbrPartitionEntry[] PartitionEntries { get; } = new MbrPartitionEntry[NumberOfBootEntries];

        public bool IsGptProtective => PartitionEntries.Any(p => p.PartitionType == MbrPartitionType.GptProtective);
        public bool IsValid => BootSignature == ExpectedBootSignature;

        public ushort BootSignature { get; }
    }
}
