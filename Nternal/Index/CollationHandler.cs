using System.Runtime.InteropServices;
using System.Text;
using Nternal.Core.Extensions;
using Nternal.Ntfs.Generic;

namespace Nternal.Ntfs.Index;

public class CollationHandler
{
    internal char[]? UpCaseTable { get; set; }

    public int Compare(ReadOnlySpan<byte> span1, ReadOnlySpan<byte> span2, CollationRules rule)
    {
        ReadOnlySpan<byte> firstKey = GetKeyData(span1), secondKey = GetKeyData(span2);

        switch (rule)
        {
            case CollationRules.CollationBinary: return firstKey.SequenceCompareTo(secondKey);
            case CollationRules.CollationFileName:
                return CompareUpcased(GetFileName(span1), GetFileName(span2));
            case CollationRules.CollationUnicodeString:
                return CompareUpcased(AsChars(firstKey), AsChars(secondKey));
            case CollationRules.CollationNtofsUlong: return firstKey.ReadUInt32().CompareTo(secondKey.ReadUInt32());
            case CollationRules.CollationNtofsSid: return firstKey[2..].SequenceCompareTo(secondKey[2..]);
            case CollationRules.CollationNtofsSecurityHash:
                var securityHash = firstKey.ReadUInt32().CompareTo(secondKey.ReadUInt32());
                return securityHash != 0 ? securityHash : firstKey.ReadUInt32(4).CompareTo(secondKey.ReadUInt32(4));
            case CollationRules.CollationNtofsUlongs:
                if (firstKey.Length != secondKey.Length)
                    return firstKey.Length < secondKey.Length ? -1 : 1;

                for (var i = 0; i < firstKey.Length; i += 4)
                {
                    uint u1 = firstKey.ReadUInt32(i), u2 = secondKey.ReadUInt32(i);
                    if (u1 != u2)
                        return u1.CompareTo(u2);
                }

                return 0;
            default: throw new ArgumentException(nameof(rule), $"Invalid collation rule ({rule})!");
        }
    }

    public int CompareToTarget(ReadOnlySpan<byte> fullRawEntry, ReadOnlySpan<byte> target, CollationRules rule)
    {
        var key = GetKeyData(fullRawEntry);

        switch (rule)
        {
            case CollationRules.CollationBinary:
                return target.SequenceCompareTo(key);

            case CollationRules.CollationFileName:
                return CompareUpcased(AsChars(target), GetFileName(fullRawEntry));

            case CollationRules.CollationUnicodeString:
                return CompareUpcased(AsChars(target), AsChars(key));

            case CollationRules.CollationNtofsUlong:
                return target.ReadUInt32().CompareTo(key.ReadUInt32());

            case CollationRules.CollationNtofsSid:
                return target[2..].SequenceCompareTo(key[2..]);

            case CollationRules.CollationNtofsSecurityHash:
                var cmp = target.ReadUInt32().CompareTo(key.ReadUInt32());
                return cmp != 0 ? cmp : target.ReadUInt32(4).CompareTo(key.ReadUInt32(4));

            case CollationRules.CollationNtofsUlongs:
                if (key.Length != target.Length)
                    return target.Length < key.Length ? -1 : 1; // also flipped

                for (var i = 0; i < key.Length; i += 4)
                {
                    uint u1 = target.ReadUInt32(i), u2 = key.ReadUInt32(i);
                    if (u1 != u2)
                        return u1.CompareTo(u2);
                }

                return 0;

            default: throw new ArgumentException(nameof(rule), $"Invalid collation rule ({rule})!");
        }
    }

    public int CompareUpcased(ReadOnlySpan<char> a, ReadOnlySpan<char> b)
    {
        var upA = a.Length <= 260 ? stackalloc char[a.Length] : new char[a.Length];
        var upB = b.Length <= 260 ? stackalloc char[b.Length] : new char[b.Length];
        UpcaseInto(a, upA);
        UpcaseInto(b, upB);
        return upA.SequenceCompareTo(upB);
    }

    public void UpcaseInto(ReadOnlySpan<char> source, Span<char> destination)
    {
        if (UpCaseTable == null)
            throw new InvalidOperationException("Cannot compare filenames before $UpCase has been parsed!");

        for (var i = 0; i < source.Length; i++)
            destination[i] = UpCaseTable[source[i]];
    }

    public static byte[] EncodeSearchString(string value)
    {
        return Encoding.Unicode.GetBytes(value);
    }

    private static ReadOnlySpan<byte> GetKeyData(ReadOnlySpan<byte> entry)
    {
        var keyLength = entry.ReadUInt16(10);
        return entry[16..(16 + keyLength)];
    }

    private static string GetFileName(ReadOnlySpan<byte> entry)
    {
        var rawNameLength = entry[80] * 2;
        return Encoding.Unicode.GetString(entry.Slice(82, rawNameLength));
    }

    // Reinterprets UTF-16LE bytes as chars, ignoring a trailing odd byte (never valid UTF-16).
    private static ReadOnlySpan<char> AsChars(ReadOnlySpan<byte> bytes)
    {
        return MemoryMarshal.Cast<byte, char>(bytes[..(bytes.Length & ~1)]);
    }
}