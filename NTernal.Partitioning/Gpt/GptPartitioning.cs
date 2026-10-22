using System.IO.Hashing;
using Nternal.Core;

namespace Nternal.Partitioning.Gpt;

/// <summary>
///     Represents the GPT (Guid Partition Table) partitioning scheme, the successor of MBR.
/// </summary>
public class GptPartitioning
{
    private readonly bool _paranoid;
    private IDataStream? _stream;

    /// <summary>
    ///     Craetes a new instance of a GPT partitioning scheme.
    /// </summary>
    /// <param name="paranoid">Whether this instance should throw upon encountering corrupted / unusual data.</param>
    public GptPartitioning(bool paranoid = true)
    {
        _paranoid = paranoid;
    }

    /// <summary>
    ///     Yields the revision string of the EFI header.
    /// </summary>
    public string RevisionString
    {
        get
        {
            var major = (ushort)(Header.Revision >> 16);
            var minor = (ushort)(Header.Revision & 0xFFFF);
            var minorUpper = minor / 10;
            var minorLower = minor % 10;
            return minorLower == 0 ? $"{major}.{minorUpper}" : $"{major}.{minorUpper}.{minorLower}";
        }
    }

    /// <summary>
    ///     Represents the main header.
    /// </summary>
    public GptHeader? MainHeader { get; private set; }

    /// <summary>
    ///     Represents the backup header.
    /// </summary>
    public GptHeader? BackupHeader { get; private set; }

    /// <summary>
    ///     Represents all GPT partitions on this disk.
    /// </summary>
    public GptPartitionEntry[] GptPartitionEntries { get; private set; } = [];

    /// <summary>
    ///     Represents the GPT header to be used.
    /// </summary>
    public GptHeader Header =>
        MainHeader ?? BackupHeader ?? throw new Exception("No valid GPT-Header has been parsed yet!");

    /// <summary>
    ///     Interprets the disk as GPT-partition-styled device in order to parse the partitioning header and partition entries.
    /// </summary>
    /// <param name="stream">The stream representing the underlying disk.</param>
    /// <exception cref="InvalidOperationException">
    ///     Thrown if the partitioning scheme could not be parsed (most likely due to
    ///     corruption).
    /// </exception>
    public void ParseFromStream(IDataStream stream)
    {
        _stream = stream;
        Span<byte> header = new byte[stream.Alignment];
        var errorMessage = "Unknown";
        ulong? alternateLba = null;

        try
        {
            stream.Read(stream.Alignment, header);
            alternateLba = GptHeader.GetBackupLBA(header);
            MainHeader = new GptHeader(header, _paranoid, (uint)stream.Alignment);
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
            BackupHeader = new GptHeader(header, _paranoid, (uint)stream.Alignment);
        }
        catch
        {
        }

        ParsePartitionArray();
    }

    private void ParsePartitionArray()
    {
        var arrayBytes = Math.Max(16384, (long)Header.NumberOfPartitionEntries * Header.SizeOfPartitionEntry);
        var arraySectors = (arrayBytes + _stream!.Alignment - 1) / _stream!.Alignment;
        var actualBytesToRead = arraySectors * _stream!.Alignment;

        Span<byte> partitionBytes = new byte[actualBytesToRead];
        _stream.ReadExactly((long)Header.LbaStartOfPartitionEntries * _stream.Alignment, partitionBytes);

        if (_paranoid)
        {
            var realArrayBytes = checked((int)Header.NumberOfPartitionEntries * (int)Header.SizeOfPartitionEntry);
            var computedCrc = Crc32.HashToUInt32(partitionBytes[..realArrayBytes]);

            if (computedCrc != Header.PartitionArrayCrc32)
                throw new InvalidDataException($"The calculated checksum of the partition array ({computedCrc:X8})"
                                               + $" does not match the one present in the header ({Header.PartitionArrayCrc32:X8})!");
        }

        List<GptPartitionEntry> entries = new((int)Header.NumberOfPartitionEntries);
        for (var i = 0; i < Header.NumberOfPartitionEntries; i++)
        {
            var partitionEntry =
                partitionBytes.Slice((int)(i * Header.SizeOfPartitionEntry), (int)Header.SizeOfPartitionEntry);
            var entry = new GptPartitionEntry(partitionEntry);

            if (!entry.IsUnused)
                entries.Add(entry);
        }

        GptPartitionEntries = entries.ToArray();
    }
}