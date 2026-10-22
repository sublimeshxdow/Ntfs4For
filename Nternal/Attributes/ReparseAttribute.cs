using System.Buffers.Binary;
using System.Text;
using Nternal.Core;

namespace Nternal.Ntfs.Attributes;

/// <summary>Reparse point tags as stored in $REPARSE_POINT and in the $Reparse:$R index.</summary>
public enum ReparseTag : uint
{
    ReservedZero = 0x00000000,
    ReservedOne = 0x00000001,
    ReservedTwo = 0x00000002,

    MountPoint = 0xA0000003, // junctions and volume mount points
    Hsm = 0xC0000004, // legacy HSM (uses the obsolete high-latency bit)
    DriveExtender = 0x80000005, // Windows Home Server Drive Extender
    Hsm2 = 0x80000006,
    Sis = 0x80000007, // Single Instance Storage
    Wim = 0x80000008, // WIM mount
    Csv = 0x80000009, // Cluster Shared Volumes
    Dfs = 0x8000000A,
    FilterManager = 0x8000000B,
    SymbolicLink = 0xA000000C,
    IisCache = 0xA0000010,
    Dfsr = 0x80000012,
    Dedup = 0x80000013, // Data Deduplication
    Nfs = 0x80000014, // NFS symlinks and special files
    FilePlaceholder = 0x80000015, // Windows 8.1 OneDrive placeholders
    Wof = 0x80000017, // Windows Overlay Filter (CompactOS, WIMBoot)
    Wci = 0x80000018, // Windows Container Isolation
    Wci1 = 0x90001018,
    GlobalReparse = 0xA0000019, // NPFS server silo named-pipe symlinks
    Cloud = 0x9000001A, // Cloud Files (OneDrive etc.); variants 0x9000x01A, use IsCloud()
    AppExecLink = 0x8000001B, // App execution aliases (WindowsApps\*.exe)
    ProjFs = 0x9000001C, // Projected File System; also known as GVFS
    LxSymlink = 0xA000001D, // WSL symlinks
    StorageSync = 0x8000001E, // Azure File Sync
    WciTombstone = 0xA000001F,
    Unhandled = 0x80000020,
    OneDrive = 0x80000021,
    ProjFsTombstone = 0xA0000022, // also known as GVFS tombstone
    AfUnix = 0x80000023, // AF_UNIX sockets
    LxFifo = 0x80000024, // WSL FIFOs
    LxChr = 0x80000025, // WSL character devices
    LxBlk = 0x80000026, // WSL block devices
    StorageSyncFolder = 0x90000027,
    WciLink = 0xA0000027,
    WciLink1 = 0xA0001027,
    DatalessCim = 0xA0000028
}

public static class ReparseTagBits
{
    public const uint Microsoft = 0x80000000; // owned by Microsoft; non-Microsoft tags carry a GUID
    public const uint HighLatency = 0x40000000; // obsolete; reserved since Vista
    public const uint NameSurrogate = 0x20000000; // points to another file or directory
    public const uint Directory = 0x10000000; // may be set on directories with children
    public const uint ReservedMask = 0x0FFF0000; // must be zero
    public const uint CloudMask = 0x0000F000; // variant bits of Cloud tags

    public static bool IsMicrosoft(this ReparseTag t)
    {
        return ((uint)t & Microsoft) != 0;
    }

    public static bool IsNameSurrogate(this ReparseTag t)
    {
        return ((uint)t & NameSurrogate) != 0;
    }

    public static bool IsDirectory(this ReparseTag t)
    {
        return ((uint)t & Directory) != 0;
    }

    public static bool HasReservedBits(this ReparseTag t)
    {
        return ((uint)t & ReservedMask) != 0;
    }

    public static ushort Number(this ReparseTag t)
    {
        return (ushort)t;
    }

    /// <summary>Matches all 16 Cloud variants (0x9000001A .. 0x9000F01A).</summary>
    public static bool IsCloud(this ReparseTag t)
    {
        return ((uint)t & ~CloudMask) == (uint)ReparseTag.Cloud;
    }

    /// <summary>Hex form, comparable with the IO_REPARSE_TAG_* constants.</summary>
    public static string ToHex(this ReparseTag t)
    {
        return ((uint)t).ToString("X8");
    }
}

// ---------------------------------------------------------------------------------------------
// Parsed reparse data
// ---------------------------------------------------------------------------------------------

public class ReparseData
{
    public byte[] RawData { get; set; } = [];
}

public class NameSurrogateReparseData : ReparseData
{
    /// <summary>The NT path the reparse point resolves to, e.g. \??\C:\ProgramData.</summary>
    public string SubstituteName { get; set; } = "";

    /// <summary>The user-facing form of the target, e.g. C:\ProgramData.</summary>
    public string PrintName { get; set; } = "";
}

public class SymbolicLinkReparseData : NameSurrogateReparseData
{
    [Flags]
    public enum SymlinkFlags : uint
    {
        None = 0,
        IsRelative = 1 // SYMLINK_FLAG_RELATIVE
    }

    public SymlinkFlags Flags { get; set; }
    public bool IsRelative => Flags.HasFlag(SymlinkFlags.IsRelative);
}

public class MountPointReparseData : NameSurrogateReparseData
{
}

/// <summary>
///     Windows Overlay Filter. The file's unnamed $DATA does NOT hold its content:
///     for the File provider the compressed data is in the named stream "WofCompressedData",
///     for the WIM provider it lives in an external WIM file.
/// </summary>
public class WofReparseData : ReparseData
{
    public enum WofAlgorithm : uint
    {
        Xpress4K = 0,
        Lzx = 1,
        Xpress8K = 2,
        Xpress16K = 3
    }

    public enum WofProvider : uint
    {
        Wim = 1,
        File = 2
    }

    public const string CompressedStreamName = "WofCompressedData";

    public uint WofVersion { get; set; }
    public WofProvider Provider { get; set; }
    public uint ProviderVersion { get; set; }

    // File provider (FILE_PROVIDER_EXTERNAL_INFO_V0 on disk)
    public WofAlgorithm? Algorithm { get; set; }

    /// <summary>Only present if a writer stored the V1 struct; Windows itself doesn't.</summary>
    public uint? FileProviderFlags { get; set; }

    // WIM provider (WIM_PROVIDER_EXTERNAL_INFO)
    public uint? WimFlags { get; set; }
    public long? WimDataSourceId { get; set; }
    public byte[]? WimResourceHash { get; set; }

    /// <summary>Uncompressed chunk size of the File provider's algorithm, or null if unknown.</summary>
    public int? ChunkSize => Algorithm switch
    {
        WofAlgorithm.Xpress4K => 4096,
        WofAlgorithm.Xpress8K => 8192,
        WofAlgorithm.Xpress16K => 16384,
        WofAlgorithm.Lzx => 32768,
        _ => null
    };
}

/// <summary>App execution alias (e.g. %LOCALAPPDATA%\Microsoft\WindowsApps\winget.exe).</summary>
public class AppExecLinkReparseData : ReparseData
{
    public uint Version { get; set; }

    /// <summary>All NUL-separated strings, in order. Newer Windows versions may append more.</summary>
    public IReadOnlyList<string> Strings { get; set; } = [];

    public string? PackageId => Strings.Count > 0 ? Strings[0] : null;
    public string? AppUserModelId => Strings.Count > 1 ? Strings[1] : null;
    public string? TargetPath => Strings.Count > 2 ? Strings[2] : null;
}

/// <summary>WSL symlink. The target is a POSIX path in UTF-8, usually relative.</summary>
public class LxSymlinkReparseData : ReparseData
{
    public uint Version { get; set; }
    public string Target { get; set; } = "";
}

// ---------------------------------------------------------------------------------------------
// Attribute
// ---------------------------------------------------------------------------------------------

public class ReparseAttribute : IParsableAttribute
{
    private const int MountPointHeaderSize = 8; // 4 x u16
    private const int SymlinkHeaderSize = 12; // 4 x u16 + u32 flags

    public ReparseTag Tag { get; private set; }
    public Guid? Guid { get; private set; }
    public ReparseData Data { get; private set; } = new();
    public static NtfsAttributeType AttributeType => NtfsAttributeType.Reparse;

    public NtfsAttribute Underlying { get; init; } = null!;

    public void ParseFromAccessor(bool paranoid)
    {
        var length = Underlying.Accessor.Length;
        if (length > 16 * 1024 + 24) // MAXIMUM_REPARSE_DATA_BUFFER_SIZE plus GUID header
            throw new InvalidDataException($"Reparse attribute is implausibly large ({length} bytes)!");

        Span<byte> buffer = new byte[length];
        using var unalignedReader = new UnalignedDataStream(Underlying.Accessor);
        unalignedReader.ReadExactly(0, buffer);

        if (buffer.Length < 8)
            throw new InvalidDataException($"Reparse attribute is too short ({buffer.Length} bytes)!");

        Tag = (ReparseTag)BinaryPrimitives.ReadUInt32LittleEndian(buffer);
        var reparseLength = BinaryPrimitives.ReadUInt16LittleEndian(buffer[4..]);
        // buffer[6..8] is reserved

        if (paranoid && Tag.HasReservedBits())
            throw new InvalidDataException($"Reparse tag {Tag.ToHex()} has reserved bits set!");

        var offset = 8;
        if (!Tag.IsMicrosoft())
        {
            if (buffer.Length < 24)
                throw new InvalidDataException($"Non-Microsoft reparse tag {Tag.ToHex()} without a GUID!");

            Guid = new Guid(buffer.Slice(8, 16));
            offset += 16;
        }

        if (offset + reparseLength > buffer.Length)
            throw new InvalidDataException($"Reparse data length {reparseLength} exceeds the attribute size " +
                                           $"({buffer.Length - offset} bytes available)!");

        ReadOnlySpan<byte> reparseData = buffer.Slice(offset, reparseLength);
        Data = ParseReparseData(reparseData, paranoid);
        Data.RawData = reparseData.ToArray();
    }

    private ReparseData ParseReparseData(ReadOnlySpan<byte> data, bool paranoid)
    {
        try
        {
            switch (Tag)
            {
                case ReparseTag.MountPoint:
                    return ParseMountPoint(data);
                case ReparseTag.SymbolicLink:
                    return ParseSymbolicLink(data);
                case ReparseTag.Wof:
                    return ParseWof(data);
                case ReparseTag.AppExecLink:
                    return ParseAppExecLink(data);
                case ReparseTag.LxSymlink:
                    return ParseLxSymlink(data);
                default:
                    return new ReparseData();
            }
        }
        catch (InvalidDataException) when (!paranoid)
        {
            // Keep the raw bytes available even if the structured parse failed.
            return new ReparseData();
        }
    }

    // MOUNT_POINT_REPARSE_BUFFER: SubstituteNameOffset, SubstituteNameLength,
    // PrintNameOffset, PrintNameLength, PathBuffer[]
    private static MountPointReparseData ParseMountPoint(ReadOnlySpan<byte> data)
    {
        RequireLength(data, MountPointHeaderSize, "mount point");
        return new MountPointReparseData
        {
            SubstituteName = ReadName(data, MountPointHeaderSize, ReadU16(data, 0), ReadU16(data, 2)),
            PrintName = ReadName(data, MountPointHeaderSize, ReadU16(data, 4), ReadU16(data, 6))
        };
    }

    // SYMBOLIC_LINK_REPARSE_BUFFER: same as above plus a u32 Flags before PathBuffer[]
    private static SymbolicLinkReparseData ParseSymbolicLink(ReadOnlySpan<byte> data)
    {
        RequireLength(data, SymlinkHeaderSize, "symbolic link");
        return new SymbolicLinkReparseData
        {
            SubstituteName = ReadName(data, SymlinkHeaderSize, ReadU16(data, 0), ReadU16(data, 2)),
            PrintName = ReadName(data, SymlinkHeaderSize, ReadU16(data, 4), ReadU16(data, 6)),
            Flags = (SymbolicLinkReparseData.SymlinkFlags)ReadU32(data, 8)
        };
    }

    // WOF_EXTERNAL_INFO { u32 Version; u32 Provider; } followed by the provider's info:
    //   File: FILE_PROVIDER_EXTERNAL_INFO_V0 { u32 Version; u32 Algorithm; }  (no Flags on disk)
    //   WIM:  WIM_PROVIDER_EXTERNAL_INFO { u32 Version; u32 Flags; i64 DataSourceId; u8 ResourceHash[20]; }
    private static WofReparseData ParseWof(ReadOnlySpan<byte> data)
    {
        RequireLength(data, 8, "WOF");
        var result = new WofReparseData
        {
            WofVersion = ReadU32(data, 0),
            Provider = (WofReparseData.WofProvider)ReadU32(data, 4)
        };

        var provider = data[8..];
        switch (result.Provider)
        {
            case WofReparseData.WofProvider.File:
                // On disk this is FILE_PROVIDER_EXTERNAL_INFO_V0 (Version, Algorithm) = 8 bytes.
                // The V1 Flags field only exists in the API struct; read it if a writer stored it anyway.
                RequireLength(provider, 8, "WOF file provider");
                result.ProviderVersion = ReadU32(provider, 0);
                result.Algorithm = (WofReparseData.WofAlgorithm)ReadU32(provider, 4);
                if (provider.Length >= 12)
                    result.FileProviderFlags = ReadU32(provider, 8);
                break;

            case WofReparseData.WofProvider.Wim:
                RequireLength(provider, 36, "WOF WIM provider");
                result.ProviderVersion = ReadU32(provider, 0);
                result.WimFlags = ReadU32(provider, 4);
                result.WimDataSourceId = BinaryPrimitives.ReadInt64LittleEndian(provider[8..]);
                result.WimResourceHash = provider.Slice(16, 20).ToArray();
                break;

            // Unknown providers: keep version and provider, the raw data has the rest.
        }

        return result;
    }

    // u32 Version (3), followed by NUL-terminated UTF-16 strings:
    // package ID, app user model ID, target executable path, then version-dependent extras.
    private static AppExecLinkReparseData ParseAppExecLink(ReadOnlySpan<byte> data)
    {
        RequireLength(data, 4, "app execution link");
        var strings = new List<string>();

        var rest = data[4..];
        while (rest.Length >= 2)
        {
            var end = 0;
            while (end + 1 < rest.Length && (rest[end] | rest[end + 1]) != 0)
                end += 2;

            if (end + 1 >= rest.Length) // unterminated trailing string
            {
                strings.Add(Encoding.Unicode.GetString(rest[..(rest.Length & ~1)]));
                break;
            }

            strings.Add(Encoding.Unicode.GetString(rest[..end]));
            rest = rest[(end + 2)..];
        }

        return new AppExecLinkReparseData { Version = ReadU32(data, 0), Strings = strings };
    }

    // u32 Version (2), followed by the target path in UTF-8 (not NUL-terminated).
    private static LxSymlinkReparseData ParseLxSymlink(ReadOnlySpan<byte> data)
    {
        RequireLength(data, 4, "WSL symlink");
        return new LxSymlinkReparseData
        {
            Version = ReadU32(data, 0),
            Target = Encoding.UTF8.GetString(data[4..])
        };
    }

    // -----------------------------------------------------------------------------------------

    private static string ReadName(ReadOnlySpan<byte> data, int headerSize, ushort offset, ushort length)
    {
        var start = headerSize + offset;
        if ((length & 1) != 0 || start + length > data.Length)
            throw new InvalidDataException($"Reparse name (offset {offset}, length {length}) is out of range " +
                                           $"of the {data.Length}-byte reparse data!");

        return Encoding.Unicode.GetString(data.Slice(start, length));
    }

    private static void RequireLength(ReadOnlySpan<byte> data, int minimum, string what)
    {
        if (data.Length < minimum)
            throw new InvalidDataException($"The {what} reparse data is too short ({data.Length} < {minimum} bytes)!");
    }

    private static ushort ReadU16(ReadOnlySpan<byte> data, int offset)
    {
        return BinaryPrimitives.ReadUInt16LittleEndian(data[offset..]);
    }

    private static uint ReadU32(ReadOnlySpan<byte> data, int offset)
    {
        return BinaryPrimitives.ReadUInt32LittleEndian(data[offset..]);
    }
}