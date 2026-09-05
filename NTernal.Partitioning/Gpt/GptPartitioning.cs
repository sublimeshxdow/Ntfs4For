using Nternal.Core.Streams;
using System;
using System.Collections.Generic;
using System.IO.Hashing;
using System.Text;

namespace Nternal.Partitioning.Gpt
{
    public class GptPartitioning
    {
        public GptPartitioning(bool paranoid = true)
        {
            _paranoid = paranoid;
        }

        public void ParseFromStream(IDataStream stream)
        {
            _stream = stream;
            Span<byte> header = new byte[stream.Alignment];
            string? errorMessage = "Unknown";
            ulong? alternateLba = null;

            try
            {
                stream.Read(stream.Alignment, header);
                alternateLba = GptHeader.GetBackupLBA(header);
                MainHeader = new(header, _paranoid, (uint)stream.Alignment);
            }
            catch (Exception ex)
            {
                errorMessage = ex.Message;
            }

            if (alternateLba == null)
                throw new InvalidOperationException($"Failed to parse GPT-Partitioning - could not"
                    + $" even retrieve the LBA of the GPT header copy ({errorMessage}) - cannot proceed!");

            try
            {
                stream.Read((long)alternateLba * stream.Alignment, header);
                BackupHeader = new(header, _paranoid, (uint)stream.Alignment);
            }
            catch
            { }

            ParsePartitionArray();
        }

        private void ParsePartitionArray()
        {
            long arrayBytes = Math.Max(16384L, (long)Header.NumberOfPartitionEntries * Header.SizeOfPartitionEntry);
            long arraySectors = (arrayBytes + _stream!.Alignment - 1) / _stream!.Alignment;
            long actualBytesToRead = arraySectors * _stream!.Alignment;

            Span<byte> partitionBytes = new byte[actualBytesToRead];
            _stream.ReadExactly((long)Header.LbaStartOfPartitionEntries * _stream.Alignment, partitionBytes);

            if (_paranoid)
            {
                int realArrayBytes = checked((int)Header.NumberOfPartitionEntries * (int)Header.SizeOfPartitionEntry);
                uint computedCrc = Crc32.HashToUInt32(partitionBytes[0..realArrayBytes]);

                if (computedCrc != Header.PartitionArrayCrc32)
                    throw new InvalidDataException($"The calculated checksum of the partition array ({computedCrc:X8})"
                        + $" does not match the one present in the header ({Header.PartitionArrayCrc32:X8})!");
            }

            List<GptPartitionEntry> entries = new((int)Header.NumberOfPartitionEntries);

            for (int i = 0; i < Header.NumberOfPartitionEntries; i++)
            {
                Span<byte> partitionEntry = partitionBytes.Slice((int)(i * Header.SizeOfPartitionEntry), (int)Header.SizeOfPartitionEntry);
                var entry = new GptPartitionEntry(partitionEntry, _paranoid);

                if (!entry.IsUnused)
                    entries.Add(entry);
            }

            GptPartitionEntries = entries.ToArray();
        }

        public string RevisionString
        {
            get
            {
                ushort major = (ushort)(Header.Revision >> 16);
                ushort minor = (ushort)(Header.Revision & 0xFFFF);
                int minorUpper = minor / 10;
                int minorLower = minor % 10;
                return minorLower == 0 ? $"{major}.{minorUpper}" : $"{major}.{minorUpper}.{minorLower}";
            }
        }

        private bool _paranoid;
        private IDataStream? _stream;

        public GptHeader? MainHeader { get; private set; }
        public GptHeader? BackupHeader { get; private set; }

        public GptPartitionEntry[] GptPartitionEntries { get; private set; } = [];

        public GptHeader Header => MainHeader ?? BackupHeader ?? throw new Exception("No valid GPT-Header has been parsed yet!");
    }
}
