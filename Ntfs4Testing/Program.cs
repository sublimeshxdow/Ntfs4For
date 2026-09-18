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
    private static long f = 0, m = 0;

    static void EnumerateFolder(NtfsVolume vol, FileReference folderRef, int depth)
    {
        if (depth > 3) return;

        foreach (var file in vol.EnumerateDirectory(folderRef))
        {
            if (file.Item2.Flags.HasFlag(FileFlags.Directory))
            {
                try
                {
                    EnumerateFolder(vol, file.Item1, depth + 1);
                }
                catch (Exception e)
                {
                    Console.WriteLine(e.Message);
                }
            }

            f++;
        }
    }

    private static void Main(string[] args)
    {
        // var stream = new Win32PartitionStream(@"\\.\C:", long.MaxValue);
        var stream = new Win32PartitionStream("/dev/nvme0n1p3", long.MaxValue);

        NtfsVolume v = new(stream);
        v.Mount();

        Stopwatch s = new Stopwatch();
        s.Start();
        for (int i = 0; i < 1; i++)
            EnumerateFolder(v, new FileReference(5, 5), 0);
        s.Stop();
        ;
        Console.WriteLine(s.Elapsed.TotalMilliseconds / 1);
        Console.WriteLine(f);
    }
}