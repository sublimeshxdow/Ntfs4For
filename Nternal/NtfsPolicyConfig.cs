namespace Nternal.Ntfs;

public class NtfsPolicyConfig
{
    [Flags]
    public enum Flags
    {
        ParanoidParsing = 0x00000001
    }

    public static Flags PolicyFlags { get; set; } = Flags.ParanoidParsing;
}