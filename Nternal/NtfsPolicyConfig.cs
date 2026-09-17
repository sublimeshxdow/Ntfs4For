namespace Nternal.Ntfs;

public class NtfsPolicyConfig
{
    [Flags]
    public enum Flags
    {
        ParanoidParsing = 0x00000001,
        ParanoidParsingUrnJrnl = 0x00000002
    }

    public static Flags PolicyFlags { get; set; } = Flags.ParanoidParsing;

    // Default Limit for Windows - https://learn.microsoft.com/de-de/windows/win32/fileio/symbolic-link-programming-considerations
    public static int HopLimit { get; set; } = 63;

    // Default value -1: .NET decides itself
    public static int CompressionExtentParallelismThreshold { get; set; } = 4;
    public static int CompressionMaxDegreeOfParallelism { get; set; } = -1;
}