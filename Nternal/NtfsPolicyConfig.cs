namespace Nternal.Ntfs;

public class NtfsPolicyConfig
{
    [Flags]
    public enum Flags
    {
        ParanoidParsing = 0x00000001,
        ParanoidParsingUrnJrnl = 0x00000002,
        ValidateUpcase = 0x00000004,
        ValidateSdh = 0x00000008,
        ValidateSequenceNumbers = 0x00000010,
        ValidateSequenceNumbersInternal = 0x00000020
    }

    public static bool ListPosixFileNames { get; set; } = true;

    public static Flags PolicyFlags { get; set; } = (Flags)0xffff;

    // Default Limit for Windows - https://learn.microsoft.com/de-de/windows/win32/fileio/symbolic-link-programming-considerations
    public static int HopLimit { get; set; } = 63;

    // Default value -1: .NET decides itself
    public static int CompressionExtentParallelismThreshold { get; set; } = 4;
    public static int CompressionMaxDegreeOfParallelism { get; set; } = -1;
}