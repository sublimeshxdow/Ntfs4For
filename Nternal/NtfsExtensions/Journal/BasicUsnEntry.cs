using System.Text;
using Nternal.Core.Extensions;
using Nternal.Ntfs.Attributes;
using Nternal.Ntfs.Generic;
using Nternal.Ntfs.Helpers;

namespace Nternal.Ntfs.NtfsExtensions.Journal;

public class BasicUsnEntry : IUsnEntry
{
    public DateTime TimeStamp { get; init; }
    public uint SecurityId { get; init; }
    public FileFlags Flags { get; init; }
    public string FileName { get; init; } = "";
    public ushort MajorVersion { get; init; }
    public ushort MinorVersion { get; init; }
    public FileReference FileReference { get; init; }
    public FileReference ParentFileReference { get; init; }
    public ulong Usn { get; init; }
    public UsnReason Reason { get; init; }
    public UsnSourceInfo SourceInfo { get; init; }

    public static BasicUsnEntry Parse(ReadOnlySpan<byte> data, bool paranoid = true)
    {
        if (paranoid && data.Length < 0x3C)
            throw new InvalidDataException(
                $"USN entry requires at least 0x3C bytes before its filename, got {data.Length}!");

        var majorVersion = data.ReadUInt16(4);
        var minorVersion = data.ReadUInt16(6);

        if (paranoid && majorVersion is not (2 or 3))
            throw new InvalidDataException(
                $"BasicUsnEntry only supports MajorVersion 2 or 3, got {majorVersion}.{minorVersion} -- use ExtentUsnEntry for V4.");

        var refSize = majorVersion == 2 ? 8 : 16;
        var fileRefOffset = 8;
        var parentRefOffset = fileRefOffset + refSize;
        var tail = parentRefOffset + refSize; // where Usn begins for either version

        var fileRef = UsnHelpers.ParseFileReference(data, fileRefOffset, refSize, paranoid);
        var parentRef = UsnHelpers.ParseFileReference(data, parentRefOffset, refSize, paranoid);

        var usn = data.ReadUInt64(tail);
        var timestampRaw =
            data.ReadInt64(tail +
                           8); // FILETIME -- signed 64-bit, matches DateTime.FromFileTimeUtc's parameter directly
        var reason = (UsnReason)data.ReadUInt32(tail + 16);
        var sourceInfo = (UsnSourceInfo)data.ReadUInt32(tail + 20);
        var securityId = data.ReadUInt32(tail + 24);
        var flags = (FileFlags)data.ReadUInt32(tail + 28);
        var fileNameLength = data.ReadUInt16(tail + 32);
        var fileNameOffset = data.ReadUInt16(tail + 34);

        if (paranoid && fileNameOffset + fileNameLength > data.Length)
            throw new InvalidDataException(
                $"FileName (offset={fileNameOffset}, length={fileNameLength}) runs past the end of the record ({data.Length} bytes)!");

        var fileName = Encoding.Unicode.GetString(data.Slice(fileNameOffset, fileNameLength));

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
            FileName = fileName
        };
    }
}