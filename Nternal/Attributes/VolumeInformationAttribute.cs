using System.Buffers.Binary;
using Nternal.Attributes;
using Nternal.Extensions;

namespace Nternal.Ntfs.Attributes;

public class VolumeInformationAttribute : IParsableAttribute
{
    public static NtfsAttributeType AttributeType => NtfsAttributeType.VolumeInfo;

    public NtfsAttribute Underlying { get; init; } = null!;

    [Flags]
    public enum VolumeFlags : ushort
    {
        None = 0x0000,
        Dirty = 0x0001,
        ResizeLogFile = 0x0002,
        UpgradeOnMount = 0x0004,
        MountedOnNt4 = 0x0008, /* aka Object IDs/quotas/USN journal metadata can be corrupt */
        DeleteUsnUnderway = 0x0010,
        RepairObjectIds = 0x0020,
        VolumeCorruptBugchecked = 0x0040,
        PersistentNoTunnelingNo8Dot3 = 0x0080,
        FullChkdskScanRequired = 0x0100,
        ProactiveScanRequired = 0x0200,
        PersistentTxfDisabld = 0x0400,
        VolumeScrubDisabled = 0x0800,


        HeatGatheringDisabled = 0x2000,
        ChkdskUnderway = 0x4000,
        ModifiedByChkdsk = 0x8000
    }

    public byte MajorVersion { get; private set; }
    public byte MinorVersion { get; private set; }
    public VolumeFlags Flags { get; private set; }

    public void ParseFromAccessor(bool paranoid)
    {
        long length = Underlying.Accessor.AllocatedLength;
        if (paranoid && length != 12)
        {
            if (paranoid)
                throw new InvalidDataException($"$ObjectId should be 12 bytes in length - got {length} bytes instead!");

            length = Math.Min(length, 12);
        }

        Span<byte> data = stackalloc byte[(int)length];
        Underlying.Accessor.ReadExactly(0, data);

        MajorVersion = data[8];
        MinorVersion = data[9];
        Flags = (VolumeFlags)data.ReadUInt16(10);
    }

    public string NtfsVersion => $"{MajorVersion}.{MinorVersion}";
}