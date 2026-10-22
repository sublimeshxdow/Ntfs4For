using System.Buffers.Binary;
using System.Numerics;
using System.Text;
using Nternal.Ntfs.Utils.DecodingUtils;

namespace Nternal.Ntfs;

/// <summary>
///     Parses the mandatory fields of an NTFS Volume Boot Record (VBR / "boot sector")
///     directly from a raw 512-byte buffer.
/// </summary>
/// <remarks>
///     Field layout reference: NTFS BIOS Parameter Block (BPB), offsets 0x00-0x53.
///     All multi-byte integers on disk are little-endian.
/// </remarks>
public sealed class NtfsBootSector
{
    /// <summary>An NTFS boot sector is always exactly one sector: 512 bytes.</summary>
    public const int BootSectorSize = 512;

    private const ushort ExpectedBootSignature = 0xAA55;
    private const string ExpectedOemId = "NTFS    "; // "NTFS" + 4 trailing spaces

    /// <summary>
    ///     Parses an NTFS boot sector from a raw 512-byte buffer.
    /// </summary>
    /// <param name="sector">Exactly 512 bytes read from the start of the volume.</param>
    /// <exception cref="ArgumentException"><paramref name="sector" /> is not exactly <see cref="BootSectorSize" /> bytes.</exception>
    public NtfsBootSector(ReadOnlySpan<byte> sector)
    {
        if (sector.Length != BootSectorSize)
            throw new ArgumentException(
                $"NTFS boot sector must be exactly {BootSectorSize} bytes, got {sector.Length}.", nameof(sector));

        OemId = Encoding.ASCII.GetString(sector[0x03..0x0B]); // 8 bytes

        BytesPerSector = BinaryPrimitives.ReadUInt16LittleEndian(sector[0x0B..]); // 2 bytes

        var rawSectorsPerCluster = sector[0x0D]; // 1 byte
        SectorsPerCluster =
            rawSectorsPerCluster <= 0x80 ? rawSectorsPerCluster : 1 << (256 - rawSectorsPerCluster); // make it an int
        if (!BitOperations.IsPow2((uint)SectorsPerCluster))
            throw new InvalidDataException(
                $"SectorsPerCluster must be positive, and a power of two, but got {SectorsPerCluster}!");

        ReservedSectors = BinaryPrimitives.ReadUInt16LittleEndian(sector[0x0E..]); // 2 bytes (always 0 for NTFS)
        MediaDescriptor = sector[0x15]; // 1 byte
        SectorsPerTrack = BinaryPrimitives.ReadUInt16LittleEndian(sector[0x18..]); // 2 bytes
        NumberOfHeads = BinaryPrimitives.ReadUInt16LittleEndian(sector[0x1A..]); // 2 bytes
        HiddenSectors = BinaryPrimitives.ReadUInt32LittleEndian(sector[0x1C..]); // 4 bytes

        TotalSectors = BinaryPrimitives.ReadInt64LittleEndian(sector[0x28..]); // 8 bytes (signed)
        TotalClusters = TotalSectors / SectorsPerCluster;

        MftClusterNumber = BinaryPrimitives.ReadInt64LittleEndian(sector[0x30..]); // 8 bytes (signed)
        MftMirrClusterNumber = BinaryPrimitives.ReadInt64LittleEndian(sector[0x38..]); // 8 bytes (signed)

        RawClustersPerFileRecord = unchecked((sbyte)sector[0x40]); // 1 byte (signed)
        RawClustersPerIndexBuffer = unchecked((sbyte)sector[0x44]); // 1 byte (signed)

        VolumeSerialNumber = BinaryPrimitives.ReadUInt64LittleEndian(sector[0x48..]); // 8 bytes
        BootSignature = BinaryPrimitives.ReadUInt16LittleEndian(sector[0x1FE..]); // 2 bytes: 0xAA55 marker

        if (!BitOperations.IsPow2(FileRecordSize) || FileRecordSize < 1024 || FileRecordSize > 65536)
            throw new InvalidDataException($"Unsupported file record size ({FileRecordSize})!");
        if (!BitOperations.IsPow2(IndexBufferSize) || IndexBufferSize < 1024 || IndexBufferSize > 65536)
            throw new InvalidDataException($"Unsupported index buffer size ({IndexBufferSize})!");
    }

    /// <summary>OEM identifier. Should read "NTFS    " for a genuine NTFS volume.</summary>
    public string OemId { get; }

    /// <summary>Bytes per sector, normally 512.</summary>
    public ushort BytesPerSector { get; }

    /// <summary>Sectors per cluster (power of two: 1, 2, 4, ... 128).</summary>
    public int SectorsPerCluster { get; }

    /// <summary>Reserved sectors before the volume proper begins. Always 0 for NTFS.</summary>
    public ushort ReservedSectors { get; }

    /// <summary>BIOS media descriptor byte (0xF8 for fixed disks).</summary>
    public byte MediaDescriptor { get; }

    /// <summary>Legacy CHS geometry field, sectors per track.</summary>
    public ushort SectorsPerTrack { get; }

    /// <summary>Legacy CHS geometry field, number of heads.</summary>
    public ushort NumberOfHeads { get; }

    /// <summary>Sectors preceding the partition, i.e. the partition's starting LBA.</summary>
    public uint HiddenSectors { get; }

    /// <summary>Total number of sectors in the volume.</summary>
    public long TotalSectors { get; }

    /// <summary>Total number of clusters in the volume.</summary>
    public long TotalClusters { get; }

    /// <summary>Starting cluster number (LCN) of the $MFT.</summary>
    public long MftClusterNumber { get; }

    /// <summary>Starting cluster number (LCN) of the $MFTMirr.</summary>
    public long MftMirrClusterNumber { get; }

    /// <summary>Raw signed byte controlling MFT file-record-segment size (see <see cref="FileRecordSize" />).</summary>
    public sbyte RawClustersPerFileRecord { get; }

    /// <summary>Raw signed byte controlling index-buffer size (see <see cref="IndexBufferSize" />).</summary>
    public sbyte RawClustersPerIndexBuffer { get; }

    /// <summary>64-bit volume serial number, shown by tools like `vol` or `fsutil`.</summary>
    public ulong VolumeSerialNumber { get; }

    /// <summary>End-of-sector marker; must equal 0xAA55 on a valid boot sector.</summary>
    public ushort BootSignature { get; }

    /// <summary>Bytes per cluster, derived from <see cref="BytesPerSector" /> x <see cref="SectorsPerCluster" />.</summary>
    public int BytesPerCluster => BytesPerSector * SectorsPerCluster;

    /// <summary>Total volume size in bytes.</summary>
    public long TotalBytes => TotalSectors * BytesPerSector;

    /// <summary>
    ///     Size in bytes of a single MFT file record segment. The on-disk byte is signed:
    ///     a positive value is a cluster count (multiply by <see cref="BytesPerCluster" />);
    ///     a negative value N means the size is 2^|N| bytes directly.
    /// </summary>
    public int FileRecordSize => RawOrLogEncoding.ResolveSize(RawClustersPerFileRecord, BytesPerCluster);

    /// <summary>Size in bytes of a single index-record buffer, resolved the same way as <see cref="FileRecordSize" />.</summary>
    public int IndexBufferSize => RawOrLogEncoding.ResolveSize(RawClustersPerIndexBuffer, BytesPerCluster);

    /// <summary>True when the OEM ID and end-of-sector marker both match a genuine NTFS boot sector.</summary>
    public bool IsValid => OemId == ExpectedOemId && BootSignature == ExpectedBootSignature;

    /// <summary>
    ///     Attempts to parse <paramref name="sector" />, returning <see langword="false" /> instead of throwing
    ///     when the buffer is the wrong size or fails the NTFS signature check. Preferred in hot paths that
    ///     scan many candidate sectors and want to avoid exception overhead.
    /// </summary>
    public static bool TryParse(ReadOnlySpan<byte> sector, out NtfsBootSector? bootSector)
    {
        if (sector.Length != BootSectorSize)
        {
            bootSector = null;
            return false;
        }

        var candidate = new NtfsBootSector(sector);
        if (!candidate.IsValid)
        {
            bootSector = null;
            return false;
        }

        bootSector = candidate;
        return true;
    }

    public override string ToString()
    {
        return $"NTFS Volume [{OemId.TrimEnd()}] {TotalBytes / (1024.0 * 1024 * 1024):F2} GiB, "
               + $"{BytesPerSector}B/sector x {SectorsPerCluster} sectors/cluster, "
               + $"MFT@LCN {MftClusterNumber}, Serial 0x{VolumeSerialNumber:X16}";
    }
}