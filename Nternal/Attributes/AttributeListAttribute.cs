using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.Serialization;
using System.Text;
using Nternal.Core.Streams;
using Nternal.Extensions;
using Nternal.Ntfs.Attributes;
using Nternal.Structs;

namespace Nternal.Attributes
{
    public class AttributeListAttribute : IParsableAttribute
    {
        public static NtfsAttributeType AttributeType => NtfsAttributeType.AttributeList;

        public NtfsAttribute Underlying { get; init; } = null!;
        public List<Entry> Entries { get; } = [];

        public void ParseFromAccessor(bool paranoid)
        {
            Span<byte> buffer = new byte[Underlying.Accessor.AllocatedLength];
            Underlying.Accessor.ReadExactly(0, buffer);
            buffer = buffer.Slice(0, (int)Underlying.Accessor.Length);

            while (buffer.Length >= 4)
            {
                var type = (NtfsAttributeType)buffer.ReadUInt32(0);

                if (type == 0 || type == NtfsAttributeType.EndMarker)
                    break;

                ushort recordLength = buffer.ReadUInt16(4);

                Debug.Assert(recordLength != 0);

                byte nameLength = buffer[6];
                byte nameOffset = buffer[7];
                long lowestLcn = buffer.ReadInt64(8);
                FileReference baseFileReference = new FileReference(buffer.ReadUInt64(16));
                ushort attributeId = buffer.ReadUInt16(24);
                string name = buffer.ReadUnicodeString(nameOffset, nameLength * 2);

                Entries.Add(new Entry()
                {
                    BaseFileReference = baseFileReference,
                    AttributeId = attributeId,
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

            public ushort AttributeId { get; init; }
            public NtfsAttributeType Type { get; init; }
            public string Name { get; init; }
            public long LowestVcn { get; init; }
        }
    }
}
