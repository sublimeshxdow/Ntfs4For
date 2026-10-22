using Nternal.Core;
using Nternal.Core.Extensions;

namespace Nternal.Partitioning.Mbr;

public class MasterBootRecord
{
    public const int MbrSize = 512;
    public const ushort ExpectedBootSignature = 0xAA55;
    public const byte NumberOfBootEntries = 4;

    private IDataStream? Underlying { get; }

    public byte[] BootstrapCode { get; private set; }
    public uint DiskSignature { get; private set; }

    public MbrPartitionEntry[] PartitionEntries { get; } = new MbrPartitionEntry[NumberOfBootEntries];

    /// <summary>
    ///     Whether the underlying disk belongs to a GPT partition scheme (which should be used instead).
    /// </summary>
    public bool IsGptProtective => PartitionEntries.Any(p => p.PartitionType == MbrPartitionType.GptProtective);

    /// <summary>
    ///     Whether this MBR may be considered valid.
    /// </summary>
    public bool IsValid => BootSignature == ExpectedBootSignature;

    public ushort BootSignature { get; private set; }

    public void Parse(ReadOnlySpan<byte> data, bool paranoid = true)
    {
        if (data.Length != 512)
            throw new ArgumentException("MBR must be exactly 512 bytes long.", nameof(data));

        BootstrapCode = data.Slice(0, 440).ToArray();
        DiskSignature = data.ReadUInt32(440);

        for (var i = 0; i < NumberOfBootEntries; i++)
        {
            var entryData = data.Slice(446 + i * 16, 16);
            PartitionEntries[i] = new MbrPartitionEntry(entryData, Underlying);
        }

        BootSignature = data.ReadUInt16(510);
        if (paranoid && BootSignature != ExpectedBootSignature)
            throw new InvalidDataException(
                $"Invalid boot signature: {BootSignature:X4}. Expected: {ExpectedBootSignature:X4}.");
    }
#pragma warning disable CS8618 // Non-nullable field must contain a non-null value when exiting constructor. Consider adding the 'required' modifier or declaring as nullable.
    /// <summary>
    ///     Represents a MBR (Master Boot Record). The MBR is located on the first sector of a disk, and contains at most 4
    ///     partition entries.
    ///     It mostly has been superseded by the partitioning scheme GPT (Guid Partition Table).
    /// </summary>
    /// <param name="drive"></param>
    /// <param name="paranoid"></param>
    public MasterBootRecord(IDataStream drive, bool paranoid = true)
    {
        Underlying = drive;

        Span<byte> buffer = stackalloc byte[MbrSize];
        drive.ReadExactly(0, buffer);

        Parse(buffer, paranoid);
    }

    public MasterBootRecord(ReadOnlySpan<byte> data, bool paranoid = true)
    {
        Parse(data, paranoid);
    }
#pragma warning restore CS8618 // Non-nullable field must contain a non-null value when exiting constructor. Consider adding the 'required' modifier or declaring as nullable.
}