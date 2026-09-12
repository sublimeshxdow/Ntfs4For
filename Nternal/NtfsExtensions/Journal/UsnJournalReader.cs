using Nternal.Attributes;
using Nternal.Extensions;
using Nternal.Ntfs.Attributes;
using Nternal.Streams;

namespace Nternal.Ntfs.NtfsExtensions.Journal;

public class UsnJournalReader
{
    private NtfsFile _file;
    private int _clusterSize;

    public ulong MaximumSize { get; private set; }
    public ulong AllocationDelta { get; private set; }
    public ulong UsnId { get; private set; }
    public long LowestValidUsn { get; private set; }

    public UsnJournalReader(NtfsFile file, int clusterSize)
    {
        _file = file;
        _clusterSize = clusterSize;
        ParseMaxStream();
    }

    public void ParseMaxStream()
    {
        var maxAttribute = _file.GetAttributeData<DataAttribute>(NtfsAttributeType.Data, "$Max");
        if (maxAttribute == null)
            throw new InvalidOperationException($"Attempted to parse $UsnJrnl:$Max, but $Max was not present");

        var maxStream = maxAttribute.GetStream();

        Span<byte> data;
        if (!maxStream.TryGetMemory(out data))
        {
            data = new byte[maxStream.AllocatedLength];
            maxStream.ReadExactly(0, data);
        }

        if (NtfsPolicyConfig.PolicyFlags.HasFlag(NtfsPolicyConfig.Flags.ParanoidParsing)
            && data.Length != 32)
            throw new InvalidDataException(
                $"$UsnJrnl:$Max should be exactly 32 bytes long, but got {data.Length} bytes!");

        MaximumSize = data.ReadUInt64(0);
        AllocationDelta = data.ReadUInt64(8);
        UsnId = data.ReadUInt64(16);
        LowestValidUsn = data.ReadInt64(24);
    }

    public IEnumerable<IUsnEntry> GetJournalEntries()
    {
        var buffer = ReadUsnJournal().ToArray();
        int offset = 0;
        bool paranoid = NtfsPolicyConfig.PolicyFlags.HasFlag(NtfsPolicyConfig.Flags.ParanoidParsingUrnJrnl);

        while (offset + 6 <= buffer.Length)
        {
            uint length = buffer.ReadUInt32(offset);
            if (length < 0x3C)
            {
                long nextBoundary = ((offset / _clusterSize) + 1) * _clusterSize;
                if (nextBoundary >= buffer.Length)
                    break;

                offset = (int)nextBoundary;
                continue; // retry parsing on the next cluster
            }

            ushort majorVersion = buffer.ReadUInt16(offset + 4);
            if (offset + length > buffer.Length)
                break;

            ReadOnlySpan<byte> record = buffer.AsSpan().Slice(offset, (int)length);
            switch (majorVersion)
            {
                case 2:
                case 3:
                    yield return BasicUsnEntry.Parse(record, paranoid);
                    break;
                case 4:
                    yield return ExtentUsnEntry.Parse(record, paranoid);
                    break;

                default:
                    if (paranoid)
                        throw new InvalidDataException($"Found unknown Usn-Record type (Version {majorVersion})!");
                    break;
            }

            offset += (int)length;
        }
    }

    private Span<byte> ReadUsnJournal()
    {
        var journal = _file.GetAttributeData<DataAttribute>(NtfsAttributeType.Data, "$J");
        if (journal == null)
            return Span<byte>.Empty;

        if (!journal.Underlying.IsNonResident)
        {
            if (NtfsPolicyConfig.PolicyFlags.HasFlag(NtfsPolicyConfig.Flags.ParanoidParsing))
                throw new InvalidDataException($"$UsnJrnl:$J should never be resident!");

            ((ResidentDataStream)journal.GetStream()).TryGetMemory(out var data);
            return data;
        }

        var nonResidentStream = (NonResidentDataStream)journal.GetStream();
        long offset = (nonResidentStream.GetFirstNonSparseVcn() ?? 0) * nonResidentStream.GetBytesPerCluster();
        long diskAllocatedLength = nonResidentStream.AllocatedLength - offset;
        long actualLength = nonResidentStream.Length - offset;
        
        if (diskAllocatedLength > int.MaxValue)
            throw new NotImplementedException($"Support for journals larger than 2 GBs has not been implemented yet!");

        Span<byte> buffer = new byte[diskAllocatedLength];
        nonResidentStream.ReadExactly(offset, buffer);
        return buffer[0..(int)actualLength];
    }
}
