using System.Text;
using Nternal.Core.Extensions;

namespace Nternal.Ntfs.Attributes;

[Flags]
public enum EaFlag
{
    None = 0x00,
    FileNeedEa = 0x80
}

public class EaAttribute : IParsableAttribute
{
    public Entry[] EaEntries;
    public static NtfsAttributeType AttributeType => NtfsAttributeType.Ea;

    public NtfsAttribute Underlying { get; init; } = null!;

    public void ParseFromAccessor(bool paranoid)
    {
        List<Entry> entries = new();
        Span<byte> data = new byte[Underlying.Accessor.AllocatedLength];
        Underlying.Accessor.Read(0, data);

        while (data.Length > 0)
        {
            var offsetToNextEa = (int)data.ReadUInt32();
            var flags = (EaFlag)data[4];
            var nameLength = data[5];
            var valueLength = data.ReadUInt16(6);
            var valueOffset = 8 + nameLength + 1;

            entries.Add(new Entry
            {
                Name = Encoding.ASCII.GetString(data.Slice(8, nameLength)),
                Value = data[valueOffset..(valueOffset + valueLength)].ToArray(),
                Flags = flags
            });

            data = data.Slice(offsetToNextEa);
        }

        EaEntries = entries.ToArray();
    }

    public class Entry
    {
        public string Name { get; init; }
        public byte[] Value { get; init; }
        public EaFlag Flags { get; init; }
    }
}