using Nternal.Extensions;
using Nternal.Ntfs.NtfsExtensions.Journal;
using Nternal.Structs;

namespace Nternal.Ntfs.Helpers;

internal static class UsnHelpers
{
    /// <summary>
    /// Reads either an 8-byte NTFS file reference (V2) or a 16-byte
    /// FILE_ID_128 (V3/V4) at the given offset. For the 16-byte form,
    /// only the low 8 bytes carry the real NTFS reference -- the high 8
    /// bytes MUST be zero, per [MS-FSCC]. A nonzero high half means this
    /// wasn't actually produced by NTFS (e.g. a genuine ReFS 128-bit ID).
    /// </summary>
    public static FileReference ParseFileReference(ReadOnlySpan<byte> data, int offset, int size, bool paranoid)
    {
        if (size == 8)
            return new FileReference(data.ReadUInt64(offset));
 
        ulong low = data.ReadUInt64(offset);
        ulong high = data.ReadUInt64(offset + 8);
 
        if (paranoid && high != 0)
            throw new InvalidDataException(
                $"FILE_ID_128 high 64 bits are non-zero (0x{high:X16}) -- not a valid NTFS-produced reference!");
 
        return new FileReference(low);
    }
 
    /// <summary>
    /// Dispatches a single raw record's bytes to BasicUsnEntry.Parse or
    /// ExtentUsnEntry.Parse based on MajorVersion, returning both as the
    /// shared IUsnEntry contract.
    /// </summary>
    public static IUsnEntry ParseAny(ReadOnlySpan<byte> data, bool paranoid = true)
    {
        ushort majorVersion = data.ReadUInt16(4);
        return majorVersion switch
        {
            2 or 3 => BasicUsnEntry.Parse(data, paranoid),
            4 => ExtentUsnEntry.Parse(data, paranoid),
            _ => paranoid
                ? throw new InvalidDataException($"Unknown USN_RECORD MajorVersion {majorVersion}!")
                : throw new NotSupportedException($"Unknown USN_RECORD MajorVersion {majorVersion} (non-paranoid callers should catch this and skip).")
        };
    }
}
