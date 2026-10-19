using Nternal.Partitioning.Mbr;

namespace PartitionViewer
{
    /// <summary>Human-readable names for GPT partition type GUIDs and MBR type bytes.</summary>
    internal static class KnownTypes
    {
        private static readonly Dictionary<Guid, string> GptTypes = new()
        {
            // UEFI / generic
            [new("C12A7328-F81F-11D2-BA4B-00A0C93EC93B")] = "EFI System",
            [new("024DEE41-33E7-11D3-9D69-0008C781F39F")] = "MBR partition scheme",
            [new("21686148-6449-6E6F-744E-656564454649")] = "BIOS boot",

            // Windows
            [new("E3C9E316-0B5C-4DB8-817D-F92DF00215AE")] = "Microsoft reserved",
            [new("EBD0A0A2-B9E5-4433-87C0-68B6B72699C7")] = "Microsoft basic data",
            [new("DE94BBA4-06D1-4D40-A16A-BFD50179D6AC")] = "Windows recovery",
            [new("5808C8AA-7E8F-42E0-85D2-E1E90434CFB3")] = "Windows LDM metadata",
            [new("AF9B60A0-1431-4F62-BC68-3311714A69AD")] = "Windows LDM data",
            [new("E75CAF8F-F680-4CEE-AFA3-B001E56EFC2D")] = "Storage Spaces",
            [new("558D43C5-A1AC-43C0-AAC8-D1472B2923D1")] = "Storage Replica",

            // Linux
            [new("0FC63DAF-8483-4772-8E79-3D69D8477DE4")] = "Linux filesystem",
            [new("0657FD6D-A4AB-43C4-84E5-0933C84B4F4F")] = "Linux swap",
            [new("E6D6D379-F507-44C2-A23C-238F2A3DF928")] = "Linux LVM",
            [new("A19D880F-05FC-4D3B-A006-743F0F84911E")] = "Linux RAID",
            [new("CA7D7CCB-63ED-4C53-861C-1742536059CC")] = "Linux LUKS",
            [new("4F68BCE3-E8CD-4DB1-96E7-FBCAF984B709")] = "Linux root (x86-64)",
            [new("44479540-F297-41B2-9AF7-D131D5F0458A")] = "Linux root (x86)",
            [new("B921B045-1DF0-41C3-AF44-4C6F280D3FAE")] = "Linux root (ARM64)",
            [new("BC13C2FF-59E6-4262-A352-B275FD6F7172")] = "Linux extended boot",
            [new("933AC7E1-2EB4-4F13-B844-0E14E2AEF915")] = "Linux home",
            [new("3B8F8425-20E0-4F3B-907F-1A25A76F98E8")] = "Linux server data",
            [new("8DA63339-0007-60C0-C436-083AC8230908")] = "Linux reserved",

            // Apple
            [new("48465300-0000-11AA-AA11-00306543ECAC")] = "Apple HFS+",
            [new("7C3457EF-0000-11AA-AA11-00306543ECAC")] = "Apple APFS",
            [new("426F6F74-0000-11AA-AA11-00306543ECAC")] = "Apple boot",
            [new("52637672-7900-11AA-AA11-00306543ECAC")] = "Apple Core Storage",

            // BSD / Solaris / others
            [new("516E7CB4-6ECF-11D6-8FF8-00022D09712B")] = "FreeBSD data",
            [new("83BD6B9D-7F41-11DC-BE0B-001560B84F0F")] = "FreeBSD boot",
            [new("516E7CB5-6ECF-11D6-8FF8-00022D09712B")] = "FreeBSD swap",
            [new("516E7CB6-6ECF-11D6-8FF8-00022D09712B")] = "FreeBSD UFS",
            [new("516E7CBA-6ECF-11D6-8FF8-00022D09712B")] = "FreeBSD ZFS",
            [new("6A898CC3-1DD2-11B2-99A6-080020736631")] = "Solaris /usr / ZFS",
            [new("AA31E02A-400F-11DB-9590-000C2911D1B8")] = "VMware VMFS",
            [new("FE3A2A5D-4F32-41A7-B725-ACCC3285A309")] = "ChromeOS kernel",
            [new("3CB8E202-3B7E-47DD-8A3C-7FF2A13CFCEC")] = "ChromeOS root",
        };

        public static string GptTypeName(Guid type) =>
            GptTypes.TryGetValue(type, out string? name) ? name : type.ToString().ToUpperInvariant();

        /// <summary>Types whose attribute bits 60-63 carry Microsoft's meanings (read-only, hidden, no drive letter, ...).</summary>
        public static bool UsesMicrosoftAttributes(Guid type) =>
            type == new Guid("EBD0A0A2-B9E5-4433-87C0-68B6B72699C7")    // basic data
            || type == new Guid("DE94BBA4-06D1-4D40-A16A-BFD50179D6AC"); // recovery

        public static string MbrTypeName(MbrPartitionType type) => type switch
        {
            MbrPartitionType.IfsHpfsNtfsExFat => "NTFS / exFAT / HPFS",
            MbrPartitionType.HiddenNtfs => "Hidden NTFS (recovery)",
            MbrPartitionType.Fat32_LBA or MbrPartitionType.Fat32_CHS => "FAT32",
            MbrPartitionType.Fat16_LBA or MbrPartitionType.Fat16_CHS or MbrPartitionType.Fat16_Small => "FAT16",
            MbrPartitionType.Fat12 => "FAT12",
            MbrPartitionType.Extended_CHS or MbrPartitionType.Extended_LBA => "Extended",
            MbrPartitionType.LinuxNative => "Linux",
            MbrPartitionType.LinuxSwap_SolarisX86 => "Linux swap / Solaris",
            MbrPartitionType.LinuxLvm => "Linux LVM",
            MbrPartitionType.GptProtective => "GPT protective",
            MbrPartitionType.EfiSystemPartition => "EFI System",
            _ when Enum.IsDefined(type) => type.ToString().Replace('_', ' '),
            _ => "Unknown",
        } + $" (0x{(byte)type:X2})";
    }

    /// <summary>
    /// Identifies the file system inside a partition from well-known signatures in its first sectors.
    /// </summary>
    internal static class FileSystemProbe
    {
        /// <summary>How much of the partition start to read; enough for Btrfs' superblock at 64 KiB.</summary>
        public const int ProbeSize = 128 * 1024;

        public static string? Identify(ReadOnlySpan<byte> start)
        {
            if (start.Length < 512)
                return null;

            // Boot-sector based file systems: OEM ID at offset 3.
            if (Matches(start, 3, "NTFS    ")) return "NTFS";
            if (Matches(start, 3, "EXFAT   ")) return "exFAT";
            if (Matches(start, 3, "-FVE-FS-")) return "BitLocker";
            if (Matches(start, 3, "ReFS")) return "ReFS";
            if (Matches(start, 0x52, "FAT32   ")) return "FAT32";
            if (Matches(start, 0x36, "FAT16   ")) return "FAT16";
            if (Matches(start, 0x36, "FAT12   ")) return "FAT12";

            if (Matches(start, 0, "XFSB")) return "XFS";
            if (Matches(start, 0, "LUKSº¾")) return "LUKS";
            if (Matches(start, 32, "NXSB")) return "APFS";
            if (Matches(start, 0x18, "LABELONE") || Matches(start, 0x218, "LVM2 001")) return "LVM";

            if (start.Length >= 1024 + 0x3A && start[1024 + 0x38] == 0x53 && start[1024 + 0x39] == 0xEF)
                return "ext2/3/4";
            if (start.Length >= 1026 && (Matches(start, 1024, "H+") || Matches(start, 1024, "HX")))
                return "HFS+";
            if (start.Length >= 4096 && Matches(start, 4096 - 10, "SWAPSPACE2"))
                return "Linux swap";
            if (start.Length >= 0x10048 && Matches(start, 0x10040, "_BHRfS_M"))
                return "Btrfs";

            return null;
        }

        private static bool Matches(ReadOnlySpan<byte> data, int offset, string signature)
        {
            if (offset + signature.Length > data.Length)
                return false;

            for (int i = 0; i < signature.Length; i++)
                if (data[offset + i] != (byte)signature[i])
                    return false;

            return true;
        }
    }
}
