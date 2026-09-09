using System.Runtime.InteropServices;
using System.Text;
using Nternal.Extensions;
using Nternal.Structs;

namespace Nternal.Ntfs.Index;

public static class ComparisonUtils
{
    public static int Compare(ReadOnlySpan<byte> span1, ReadOnlySpan<byte> span2, CollationRules rule)
    {
        ReadOnlySpan<byte> firstKey = GetKeyData(span1), secondKey = GetKeyData(span2);

        switch (rule)
        {
            case CollationRules.CollationBinary: return firstKey.SequenceCompareTo(secondKey);
            case CollationRules.CollationFileName:
                return string.Compare(GetFileName(span1), GetFileName(span2),
                    StringComparison.InvariantCultureIgnoreCase);
            case CollationRules.CollationUnicodeString:
                return string.Compare(DecodeUtf16(firstKey), DecodeUtf16(secondKey), StringComparison.InvariantCulture);
            case CollationRules.CollationNtofsUlong: return firstKey.ReadUInt32(0).CompareTo(secondKey.ReadUInt32(0));
            case CollationRules.CollationNtofsSid: return firstKey[2..].SequenceCompareTo(secondKey[2..]);
            case CollationRules.CollationNtofsSecurityHash:
                int securityHash = firstKey.ReadUInt32(0).CompareTo(secondKey.ReadUInt32(0));
                return securityHash != 0 ? securityHash : firstKey.ReadUInt32(4).CompareTo(secondKey.ReadUInt32(4));
            case CollationRules.CollationNtofsUlongs:
                if (firstKey.Length != secondKey.Length)
                    return firstKey.Length < secondKey.Length ? -1 : 1;
                    
                for (int i = 0; i < firstKey.Length; i += 4)
                {
                    uint u1 = firstKey.ReadUInt32(i), u2 = secondKey.ReadUInt32(i);
                    if (u1 != u2)
                        return u1.CompareTo(u2);
                }

                return 0;
            default: throw new ArgumentException(nameof(rule), $"Invalid collation rule ({rule})!");
        }
    }
    
    public static int CompareToTarget(ReadOnlySpan<byte> fullRawEntry, ReadOnlySpan<byte> target, CollationRules rule)
    {
        ReadOnlySpan<byte> key = GetKeyData(fullRawEntry);

        switch (rule)
        {
            case CollationRules.CollationBinary:
                return key.SequenceCompareTo(target);

            case CollationRules.CollationFileName:
                return string.Compare(GetFileName(fullRawEntry), target.ReadUnicodeString(0, target.Length), StringComparison.InvariantCultureIgnoreCase);

            case CollationRules.CollationUnicodeString:
                return string.Compare(DecodeUtf16(key), key.ReadUnicodeString(0, key.Length), StringComparison.InvariantCulture);

            case CollationRules.CollationNtofsUlong:
                return key.ReadUInt32(0).CompareTo(target.ReadUInt32(0));

            case CollationRules.CollationNtofsSid:
                return key[2..].SequenceCompareTo(target[2..]);

            case CollationRules.CollationNtofsSecurityHash:
                int cmp = key.ReadUInt32(0).CompareTo(target.ReadUInt32(0));
                return cmp != 0 ? cmp : key.ReadUInt32(4).CompareTo(target.ReadUInt32(4));
            
            case CollationRules.CollationNtofsUlongs:
                if (key.Length != target.Length)
                    return key.Length < target.Length ? -1 : 1;
                    
                for (int i = 0; i < key.Length; i += 4)
                {
                    uint u1 = key.ReadUInt32(i), u2 = target.ReadUInt32(i);
                    if (u1 != u2)
                        return u1.CompareTo(u2);
                }

                return 0;

            default: throw new ArgumentException(nameof(rule), $"Invalid collation rule ({rule})!");
        }
    }

    public static byte[] EncodeSearchString(string value) => Encoding.Unicode.GetBytes(value);

    private static ReadOnlySpan<byte> GetKeyData(ReadOnlySpan<byte> entry)
    {
        ushort keyLength = entry.ReadUInt16(10);
        return entry[16..(16 + keyLength)];
    }

    private static string GetFileName(ReadOnlySpan<byte> entry)
    {
        int rawNameLength = entry[80] * 2;
        return Encoding.Unicode.GetString(entry.Slice(82, rawNameLength));
    }
    
    private static string DecodeUtf16(ReadOnlySpan<byte> key) => new string(MemoryMarshal.Cast<byte, char>(key));
}
