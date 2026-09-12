using System.Text;
using Nternal.Attributes;
using Nternal.Extensions;
using Nternal.Ntfs.Helpers;
using Nternal.Structs;

namespace Nternal.Ntfs.NtfsExtensions.Journal;

public class BasicUsnEntry : IUsnEntry
{
    public ushort MajorVersion { get; init; }
    public ushort MinorVersion { get; init; }
    public FileReference FileReference { get; init; }
    public FileReference ParentFileReference { get; init; }
    public ulong Usn { get; init; }
    public DateTime TimeStamp { get; init; }
    public UsnReason Reason { get; init; }
    public UsnSourceInfo SourceInfo { get; init; }
    public uint SecurityId { get; init; }
    public FileFlags Flags { get; init; }
    public string FileName { get; init; } = "";

    /// <summary>
    /// Parses a USN_RECORD_V2 or USN_RECORD_V3 entry. Both share an
    /// identical field layout after FileReferenceNumber/
    /// ParentFileReferenceNumber -- only the reference width (8 vs 16
    /// bytes) differs, so every subsequent offset is computed from that
    /// single width rather than hardcoded per version.
    /// </summary>
    public static BasicUsnEntry Parse(ReadOnlySpan<byte> data, bool paranoid = true)
    {
        if (paranoid && data.Length < 0x3C)
            throw new InvalidDataException($"USN entry requires at least 0x3C bytes before its filename, got {data.Length}!");

        ushort majorVersion = data.ReadUInt16(4);
        ushort minorVersion = data.ReadUInt16(6);

        if (paranoid && majorVersion is not (2 or 3))
            throw new InvalidDataException(
                $"BasicUsnEntry only supports MajorVersion 2 or 3, got {majorVersion}.{minorVersion} -- use ExtentUsnEntry for V4.");

        int refSize = majorVersion == 2 ? 8 : 16;
        int fileRefOffset = 8;
        int parentRefOffset = fileRefOffset + refSize;
        int tail = parentRefOffset + refSize; // where Usn begins for either version

        FileReference fileRef = UsnHelpers.ParseFileReference(data, fileRefOffset, refSize, paranoid);
        FileReference parentRef = UsnHelpers.ParseFileReference(data, parentRefOffset, refSize, paranoid);

        ulong usn = data.ReadUInt64(tail);
        long timestampRaw = data.ReadInt64(tail + 8); // FILETIME -- signed 64-bit, matches DateTime.FromFileTimeUtc's parameter directly
        UsnReason reason = (UsnReason)data.ReadUInt32(tail + 16);
        UsnSourceInfo sourceInfo = (UsnSourceInfo)data.ReadUInt32(tail + 20);
        uint securityId = data.ReadUInt32(tail + 24);
        FileFlags flags = (FileFlags)data.ReadUInt32(tail + 28);
        ushort fileNameLength = data.ReadUInt16(tail + 32);
        ushort fileNameOffset = data.ReadUInt16(tail + 34);

        if (paranoid && fileNameOffset + fileNameLength > data.Length)
            throw new InvalidDataException(
                $"FileName (offset={fileNameOffset}, length={fileNameLength}) runs past the end of the record ({data.Length} bytes)!");

        string fileName = Encoding.Unicode.GetString(data.Slice(fileNameOffset, fileNameLength));

        return new BasicUsnEntry
        {
            MajorVersion = majorVersion,
            MinorVersion = minorVersion,
            FileReference = fileRef,
            ParentFileReference = parentRef,
            Usn = usn,
            TimeStamp = DateTime.FromFileTimeUtc(timestampRaw),
            Reason = reason,
            SourceInfo = sourceInfo,
            SecurityId = securityId,
            Flags = flags,
            FileName = fileName,
        };
    }
}
