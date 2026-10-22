namespace Nternal.Ntfs.Generic;

public enum CollationRules : uint
{
    CollationBinary = 0x00,
    CollationFileName = 0x01,
    CollationUnicodeString = 0x02,
    CollationNtofsUlong = 0x10,
    CollationNtofsSid = 0x11,
    CollationNtofsSecurityHash = 0x12,
    CollationNtofsUlongs = 0x13
}