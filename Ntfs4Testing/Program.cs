using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Security.AccessControl;
using System.Text;
using Nternal;
using Nternal.Attributes;
using Nternal.Core;
using Nternal.Helpers;
using Nternal.Ntfs;
using Nternal.Ntfs.Attributes;
using Nternal.Ntfs.Compression;
using Nternal.Ntfs.Hashing;
using Nternal.Ntfs.Index;
using Nternal.Ntfs.NtfsExtensions;
using Nternal.Ntfs.NtfsExtensions.Journal;
using Nternal.Ntfs.Security;
using Nternal.Partitioning.Gpt;
using Nternal.Partitioning.Mbr;
using Nternal.Streams;
using Nternal.Structs;

internal class Program
{
    private static void Main(string[] args)
    {
        // var stream = new Win32PartitionStream(@"\\.\C:", long.MaxValue);
        var stream = new Win32PartitionStream("/dev/nvme0n1p3", long.MaxValue);

        NtfsVolume v = new(stream);
        v.Mount();

        var file = v.GetNtfsFile($"/$Mft");
        var sdh = v.GetSecurityDescriptor(file);
    }
}