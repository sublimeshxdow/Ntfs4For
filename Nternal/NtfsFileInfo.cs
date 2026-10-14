using Nternal.Attributes;
using Nternal.Core.Streams;
using System;
using System.Collections.Generic;
using System.Text;

namespace Nternal.Ntfs
{
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

        public IDataStream OpenRead() => UnderlyingStream;

        public DateTimeOffset CreationTime { get; internal set; }
        public DateTimeOffset CreationTimeUtc { get; internal set; }
        public DateTimeOffset LastAccessTime { get; internal set; }
        public DateTimeOffset LastAccessTimeUtc { get; internal set; }
        public DateTimeOffset LastWriteTime { get; internal set; }
        public DateTimeOffset LastWriteTimeUtc { get; internal set; }
        public DateTimeOffset LastReadTime { get; internal set; }
        public DateTimeOffset LastReadTimeUtc { get; internal set; }

        public UnixFileMode UnixFileMode => (UnixFileMode)(-1);
    }
}
