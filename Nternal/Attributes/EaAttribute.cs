using System;
using System.Collections.Generic;
using System.Text;
using Nternal.Extensions;
using Nternal.Ntfs.Attributes;

namespace Nternal.Attributes
{
    [Flags]
    public enum EaFlag
    {
        None = 0x00,
        FileNeedEa = 0x80
    }

    public class EaAttribute : IParsableAttribute
    {
        public NtfsAttribute Underlying { get; init; } = null!;

        public class Entry
        {
            public string Name { get; init; }
            public byte[] Value { get; init; }
            public EaFlag Flags { get; init; }
        }

        public Entry[] EaEntries;

        public void ParseFromAccessor(bool paranoid)
        {
            List<Entry> entries = new();
            Span<byte> data = new byte[Underlying.Accessor.AllocatedLength];
            Underlying.Accessor.Read(0, data);

            while (true)
            {
                int offsetToNextEa = (int)data.ReadUInt32(0);
                EaFlag flags = (EaFlag)data[4];
                byte nameLength = data[5];
                ushort valueLength = data.ReadUInt16(6);
                int valueOffset = 8 + nameLength + 1;

                entries.Add(new Entry
                {
                    Name = Encoding.ASCII.GetString(data.Slice(8, nameLength)),
                    Value = data[valueOffset..(valueOffset + valueLength)].ToArray(),
                    Flags = flags,
                });

                if (offsetToNextEa == 0)
                    break;

                data = data.Slice(offsetToNextEa);
            }

            EaEntries = entries.ToArray();
        }
    }
}