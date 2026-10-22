using Nternal.Core.Extensions;
using Nternal.Ntfs.Generic;

namespace Nternal.Ntfs.Attributes;

public class AttributeListAttribute : IParsableAttribute
{
    public List<Entry> Entries { get; } = [];
    public static NtfsAttributeType AttributeType => NtfsAttributeType.AttributeList;

    public NtfsAttribute Underlying { get; init; } = null!;

    public void ParseFromAccessor(bool paranoid)
    {
        Span<byte> buffer = new byte[Underlying.Accessor.AllocatedLength];
        Underlying.Accessor.Read(0, buffer);
        buffer = buffer.Slice(0, (int)Underlying.Accessor.Length);

        while (buffer.Length >= 4)
        {
            var type = (NtfsAttributeType)buffer.ReadUInt32();

            if (type == 0 || type == NtfsAttributeType.EndMarker)
                break;

            var recordLength = buffer.ReadUInt16(4);
            if (recordLength == 0)
                throw new InvalidDataException("An attribute list's record entry may never be of length zero!");

            var nameLength = buffer[6];
            var nameOffset = buffer[7];
            var lowestLcn = buffer.ReadInt64(8);
            var baseFileReference = new FileReference(buffer.ReadUInt64(16));
            var attributeId = buffer.ReadUInt16(24);
            var name = buffer.ReadUnicodeString(nameOffset, nameLength * 2);

            Entries.Add(new Entry
            {
                BaseFileReference = baseFileReference,
                Id = attributeId,
                Type = type,
                Name = name,
                LowestVcn = lowestLcn
            });

            buffer = buffer.Slice(recordLength);
        }
    }

    public class Entry
    {
        public FileReference BaseFileReference { get; init; }

        public ushort Id { get; init; }
        public NtfsAttributeType Type { get; init; }
        public string Name { get; init; }
        public long LowestVcn { get; init; }
    }
}