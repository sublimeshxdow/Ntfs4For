namespace Nternal.Partitioning.Mbr;

/// <summary>
///     Represents the type of partition an MBR partition may be defined as.
/// </summary>
public enum MbrPartitionType : byte
{
    // ---- Empty / structural ----
    /// <summary>
    ///     An empty partition.
    /// </summary>
    Empty = 0x00,
    Extended_CHS = 0x05,
    Extended_LBA = 0x0F,
    ExtendedLinux = 0x85,
    NonFsData = 0xDA,
    BadBlockTable = 0xFF,

    // ---- FAT family ----
    Fat12 = 0x01,
    Fat16_Small = 0x04, // < 32 MB, CHS
    Fat16_CHS = 0x06, // >= 32 MB, CHS
    Fat32_CHS = 0x0B,
    Fat32_LBA = 0x0C,
    Fat16_LBA = 0x0E,

    // Hidden variants (mirrors the visible type, "hidden" attribute bit set)
    HiddenFat12 = 0x11,
    HiddenFat16_Small = 0x14,
    HiddenFat16_CHS = 0x16,
    HiddenFat32_CHS = 0x1B,
    HiddenFat32_LBA = 0x1C,
    HiddenFat16_LBA = 0x1E,

    // ---- IFS: HPFS / NTFS / exFAT share this single type byte ----
    IfsHpfsNtfsExFat = 0x07,
    HiddenNtfs = 0x17, // typically Windows recovery partitions
    NtfsVolumeSet = 0x86, // legacy FT (fault-tolerant) mirror/stripe set
    NtfsVolumeSet2 = 0x87,

    // ---- OS/2 ----
    Os2BootManager = 0x0A,
    Os2HiddenDrive = 0x84, // also used for hibernation on some laptops

    // ---- Extended/vendor DOS variants ----
    NecDos = 0x24,
    PartitionMagicRecovery = 0x3C,
    OnTrackDiskManager = 0x50,
    OnTrackDiskManagerRW = 0x51,
    OnTrackDiskManagerDDO = 0x54,
    EzDrive = 0x55,
    GoldenBowVFeature = 0x56,
    DrDosFat12 = 0xC1,
    DrDosFat16_Small = 0xC4,
    DrDosFat16 = 0xC6,
    SpeedStor = 0x61,
    SpeedStor2 = 0xE4,
    SpeedStor3 = 0xF1,
    SpeedStor4 = 0xF4,
    DosAccess = 0xE1,
    DosReadOnly = 0xE3,
    DosSecondary = 0xF2,
    DiskSecureMultiBoot = 0x70,
    PriamEDisk = 0x5C,

    // ---- Xenix ----
    XenixRoot = 0x02,
    XenixUsr = 0x03,

    // ---- Unix / Unix-like ----
    UnixSystemV = 0x63,
    PcIx = 0x75,
    Plan9 = 0x39,

    // ---- AIX ----
    Aix = 0x08,
    AixBootable = 0x09,

    // ---- Linux ----
    LinuxOldMinix = 0x80,
    LinuxMinixOldLinux = 0x81,
    LinuxSwap_SolarisX86 = 0x82, // shared type; disambiguate by inspecting content
    LinuxNative = 0x83,
    LinuxLvm = 0x8E,
    LinuxRaidAutodetect = 0xFD,
    LinuxPlaintextPartitionTable = 0x88,

    // ---- Novell / Netware ----
    Netware286 = 0x64,
    Netware386 = 0x65,

    // ---- BSD family ----
    FreeBsd = 0xA5,
    OpenBsd = 0xA6,
    NetBsd = 0xA9,
    Bsdi = 0xB7,
    BsdiSwap = 0xB8,
    BsdOs = 0x9F,

    // ---- NeXT / Darwin / macOS ----
    NextStep = 0xA7,
    DarwinUfs = 0xA8,
    DarwinBoot = 0xAB,
    MacOsHfsPlus = 0xAF,

    // ---- Amoeba ----
    Amoeba = 0x93,
    AmoebaBadBlockTable = 0x94,

    // ---- Laptop hibernation ----
    LaptopHibernation = 0xA0,
    LaptopHibernation2 = 0xA1,

    // ---- QNX ----
    Qnx4x = 0x4D,
    Qnx4xPart2 = 0x4E,
    Qnx4xPart3 = 0x4F,

    // ---- Solaris ----
    Solaris8Boot = 0xBE,
    SolarisX86 = 0xBF,

    // ---- Windows LDM / SFS ----
    SfsOrWindowsLdm = 0x42,

    // ---- Recovery / backup tools ----
    BootWizardHidden = 0xBB,
    AcronisBackup = 0xBC,
    DellDiagnostic = 0xDE,
    BootIt = 0xDF,
    CompaqDiagnostics = 0x12,

    // ---- CP/M and misc legacy ----
    CpmOrMicroportSysV = 0x52,
    CpmOrCtos = 0xDB,
    Venix80286 = 0x40,
    PowerPcPRepBoot = 0x41,
    Opus = 0x10,
    LanStepOrOldHiddenNtfs = 0xFE,

    // ---- BeOS ----
    BeOsFileSystem = 0xEB,

    // ---- VMware ----
    VmwareVmfs = 0xFB,
    VmwareSwap = 0xFC,

    // ---- Linux/PA-RISC ----
    LinuxPaRiscBoot = 0xF0,

    // ---- GPT-related ----
    /// <summary>Protective MBR entry -- indicates the disk is actually GPT-partitioned.</summary>
    GptProtective = 0xEE,

    /// <summary>EFI System Partition, when accessed via an MBR-style entry.</summary>
    EfiSystemPartition = 0xEF
}

public static class MbrPartitionTypeExtensions
{
    /// <summary>
    ///     True for the two type bytes that denote an extended partition - (a pointer to a chained EBR, not a real data
    ///     partition).
    /// </summary>
    public static bool IsExtended(this MbrPartitionType type)
    {
        return type is MbrPartitionType.Extended_CHS or MbrPartitionType.Extended_LBA or MbrPartitionType.ExtendedLinux;
    }

    /// <summary>
    ///     True if this entry's presence indicates the disk's scheme should be interpreted as GPT instead of MBR.
    /// </summary>
    public static bool IsGptProtective(this MbrPartitionType type)
    {
        return type == MbrPartitionType.GptProtective;
    }
}