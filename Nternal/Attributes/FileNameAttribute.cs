namespace Nternal.Ntfs.Attributes;

public enum FileNameNamespace : byte
{
    Posix = 0,
    Win32 = 1,
    Dos = 2,
    Win32AndDos = Win32 | Dos
}

public class FileNameAttribute : IParsableAttribute
{
    public FileNameData FileNameData { get; private set; }
    public static NtfsAttributeType AttributeType => NtfsAttributeType.FileName;

    public NtfsAttribute Underlying { get; init; } = null!;

    public void ParseFromAccessor(bool paranoid)
    {
        Span<byte> data = new byte[Underlying.Accessor.AllocatedLength];
        Underlying.Accessor.ReadExactly(0, data);

        FileNameData = new FileNameData();
        FileNameData.Parse(data, paranoid);
    }
}