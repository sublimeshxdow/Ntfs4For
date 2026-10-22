using Nternal.Core.Extensions;
using Nternal.Ntfs.Generic;
using Nternal.Ntfs.Helpers;

namespace Nternal.Ntfs.NtfsExtensions.Journal;

public class ExtentUsnEntry : IUsnEntry
{
    public uint RemainingExtents { get; init; }

    public Extent[] Extents { get; init; } = Array.Empty<Extent>();

    public ushort MajorVersion { get; init; }
    public ushort MinorVersion { get; init; }
    public FileReference FileReference { get; init; }
    public FileReference ParentFileReference { get; init; }
    public ulong Usn { get; init; }
    public UsnReason Reason { get; init; }
    public UsnSourceInfo SourceInfo { get; init; }

    public static ExtentUsnEntry Parse(ReadOnlySpan<byte> data, bool paranoid = true)
    {
        if (paranoid && data.Length < 0x40)
            throw new InvalidDataException(
                $"USN_RECORD_V4 requires at least 0x40 bytes before its extents, got {data.Length}!");

        var majorVersion = data.ReadUInt16(4);
        var minorVersion = data.ReadUInt16(6);

        if (paranoid && majorVersion != 4)
            throw new InvalidDataException(
                $"ExtentUsnEntry only supports MajorVersion 4, got {majorVersion}.{minorVersion} -- use BasicUsnEntry for V2/V3.");

        // FileReferenceNumber/ParentFileReferenceNumber are always
        // FILE_ID_128 (16 bytes) for V4 -- no size ambiguity like V2/V3.
        var fileRef = UsnHelpers.ParseFileReference(data, 0x08, 16, paranoid);
        var parentRef = UsnHelpers.ParseFileReference(data, 0x18, 16, paranoid);

        var usn = data.ReadUInt64(0x28);
        var reason = (UsnReason)data.ReadUInt32(0x30);
        var sourceInfo = (UsnSourceInfo)data.ReadUInt32(0x34);
        var remainingExtents = data.ReadUInt32(0x38);
        var numberOfExtents = data.ReadUInt16(0x3C);
        var extentSize = data.ReadUInt16(0x3E);

        if (paranoid && 0x40 + numberOfExtents * extentSize > data.Length)
            throw new InvalidDataException(
                $"Declares {numberOfExtents} extents of {extentSize} bytes each, but only {data.Length - 0x40} bytes remain!");

        var extents = new Extent[numberOfExtents];
        for (var i = 0; i < numberOfExtents; i++)
        {
            var offset = 0x40 + i * extentSize;
            extents[i] = new Extent(data.ReadInt64(offset), data.ReadInt64(offset + 8));
        }

        return new ExtentUsnEntry
        {
            MajorVersion = majorVersion,
            MinorVersion = minorVersion,
            FileReference = fileRef,
            ParentFileReference = parentRef,
            Usn = usn,
            Reason = reason,
            SourceInfo = sourceInfo,
            RemainingExtents = remainingExtents,
            Extents = extents
        };
    }

    public readonly record struct Extent
    {
        public readonly long Length;
        public readonly long Offset;

        public Extent(long offset, long length)
        {
            Offset = offset;
            Length = length;
        }
    }
}