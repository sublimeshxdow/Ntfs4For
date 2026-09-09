using Nternal.Attributes;
using Nternal.Extensions;
using Nternal.Ntfs.Attributes;
using Nternal.Structs;

namespace Nternal.Ntfs.Index;

public class IndexEntry : IComparable<IndexEntry>
{
    public enum IndexFlags : ushort
    {
        HasSubnode = 0x0001,
        IsLast = 0x0002
    }
    
    public IndexFlags Flags { get; }

    protected FileReference? FileReference { get;  }

    protected ReadOnlyMemory<byte> _FullData { get; }
    public ReadOnlySpan<byte> RawData => _FullData.Span;
    public ushort EntryLength { get; }

    public long? SubnodeVcn { get; }
    
    public ReadOnlySpan<byte> GetKey()
    {
        var keyOffset = IndexRoot.IndexedType == NtfsAttributeType.FileName ? 16 : 8;
        var keyLength = RawData.ReadUInt16(10);
        return RawData[keyOffset..(keyOffset + keyLength)];
    }

    public ReadOnlySpan<byte> GetData() => RawData[RawData.ReadUInt16(0)..RawData.ReadUInt16(2)];

    protected IndexRootAttribute IndexRoot { get; init; }

    public IndexEntry(ReadOnlyMemory<byte> fullData, IndexRootAttribute root)
    {
        _FullData = fullData;
        IndexRoot = root;
        EntryLength = RawData.ReadUInt16(8);

        if (root.IndexedType == NtfsAttributeType.FileName)
            FileReference = new FileReference(RawData.ReadUInt64(0));

        Flags = (IndexFlags)RawData.ReadUInt16(12);
        if (Flags.HasFlag(IndexFlags.HasSubnode))
            SubnodeVcn = RawData.ReadInt64(EntryLength - 8);
    }

    public int CompareTo(IndexEntry? other)
    {
        if (other == null)
            return 1;

        if (IndexRoot.IndexedType != other.IndexRoot.IndexedType)
            throw new InvalidOperationException(
                $"Attempted to compare index-entries of different types ({IndexRoot.IndexedType} != {other.IndexRoot.IndexedType})");
        if (IndexRoot.CollationRule != other.IndexRoot.CollationRule)
            throw new InvalidOperationException(
                $"Attempted to compare index-entries of different collation rules ({IndexRoot.CollationRule} != {other.IndexRoot.CollationRule})");

        return ComparisonUtils.Compare(RawData, other.RawData, IndexRoot.CollationRule);
    }
}

public class I30IndexEntry : IndexEntry
{
    public I30IndexEntry(ReadOnlyMemory<byte> fullData, IndexRootAttribute root) : base(fullData, root)
    {
        if (root.Type != NtfsAttributeType.FileName)
            throw new InvalidOperationException(
                "Attempted to create an I30-Index-Entry with an index-root indexing another type of data!");
    }

    public KeyValuePair<FileReference, FileNameData> ParseIndexEntry(bool paranoid)
    {
        var data = new FileNameData();
        data.Parse(GetKey(), paranoid);
        return new KeyValuePair<FileReference, FileNameData>(FileReference!.Value, data);
    }
}