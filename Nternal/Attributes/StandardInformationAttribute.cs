namespace Nternal.Ntfs.Attributes;

public class StandardInformationAttribute : IParsableAttribute
{
    public DateTime FileCreation { get; set; }
    public DateTime FileAltered { get; set; }
    public DateTime MftChanged { get; set; }
    public DateTime FileRead { get; set; }
    public FileFlags Attributes { get; set; }
    public uint MaxNumberOfVersions { get; set; }
    public uint VersionNumber { get; set; }
    public uint ClassId { get; set; }
    public uint? OwnerId { get; set; }
    public uint? SecurityId { get; set; }
    public ulong? QuotaCharged { get; set; }
    public ulong? UpdateSequenceNumber { get; set; }
    public static NtfsAttributeType AttributeType => NtfsAttributeType.StandardInformation;

    public NtfsAttribute Underlying { get; init; } = null!;

    public void ParseFromAccessor(bool paranoid)
    {
        var accessor = Underlying.Accessor;
        Span<byte> data = new byte[accessor.AllocatedLength];
        accessor.ReadExactly(0, data);

        FileCreation = DateTime.FromFileTimeUtc(BitConverter.ToInt64(data.Slice(0, 8)));
        FileAltered = DateTime.FromFileTimeUtc(BitConverter.ToInt64(data.Slice(8, 8)));
        MftChanged = DateTime.FromFileTimeUtc(BitConverter.ToInt64(data.Slice(16, 8)));
        FileRead = DateTime.FromFileTimeUtc(BitConverter.ToInt64(data.Slice(24, 8)));
        Attributes = (FileFlags)BitConverter.ToUInt32(data.Slice(32, 4));
        MaxNumberOfVersions = BitConverter.ToUInt32(data.Slice(36, 4));
        VersionNumber = BitConverter.ToUInt32(data.Slice(40, 4));
        ClassId = BitConverter.ToUInt32(data.Slice(44, 4));

        if (accessor.Length >= 52)
            OwnerId = BitConverter.ToUInt32(data.Slice(48, 4));
        if (accessor.Length >= 56)
            SecurityId = BitConverter.ToUInt32(data.Slice(52, 4));
        if (accessor.Length >= 64)
            QuotaCharged = BitConverter.ToUInt64(data.Slice(56, 8));
        if (accessor.Length >= 72)
            UpdateSequenceNumber = BitConverter.ToUInt64(data.Slice(64, 8));
    }
}