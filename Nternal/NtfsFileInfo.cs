using Nternal.Core;

namespace Nternal.Ntfs;

public class NtfsFileInfo
{
    private IDataStream UnderlyingStream { get; set; }

    public string Name { get; internal set; }
    public string FullPath { get; internal set; }
    public string DirectoryName { get; internal set; }

    public string Extension => Path.GetExtension(Name);

    public long Length { get; internal set; }
    public FileAttributes Attributes { get; internal set; }
    public bool IsReadOnly => Attributes.HasFlag(FileAttributes.ReadOnly);

    public DateTimeOffset CreationTime { get; internal set; }
    public DateTimeOffset CreationTimeUtc { get; internal set; }
    public DateTimeOffset LastAccessTime { get; internal set; }
    public DateTimeOffset LastAccessTimeUtc { get; internal set; }
    public DateTimeOffset LastWriteTime { get; internal set; }
    public DateTimeOffset LastWriteTimeUtc { get; internal set; }
    public DateTimeOffset LastReadTime { get; internal set; }
    public DateTimeOffset LastReadTimeUtc { get; internal set; }

    public UnixFileMode UnixFileMode => (UnixFileMode)(-1);

    public IDataStream OpenRead()
    {
        return UnderlyingStream;
    }
}