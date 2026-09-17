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
    private static void Main(string[] args)
    {
        Stopwatch sysTime = new();
        List<string> sysFiles = new();
        sysTime.Start();
        for (int i = 0; i < 100; i++)
        {
            foreach (string file in Directory.EnumerateFileSystemEntries("/mnt/Windows/WinSxS/").ToList())
                sysFiles.Add(file);
        }

        sysTime.Stop();
        Console.WriteLine(sysTime.Elapsed.TotalMilliseconds / 100);
        
        // var stream = new Win32PartitionStream(@"\\.\C:", long.MaxValue);
        var stream = new Win32PartitionStream("/dev/nvme0n1p3", long.MaxValue);

        NtfsVolume v = new(stream);
        v.Mount();

        var entry = v.GetNtfsFile("Windows/WinSxS/");
        var index = v.CreateIndexParser(entry, "$I30");

        List<string> parsedNames = new();
        Console.WriteLine();

        Stopwatch myTime = new();
        List<string> myFiles = new();
        myTime.Start();
        for (int a = 0; a < 100; a++)
        {
            foreach (var i in index.EnumerateEntries())
            {
                var f = new FileNameData();
                f.Parse(i.GetKey(), true);
                myFiles.Add(f.Name);
            }
        }
        myTime.Stop();
        Console.WriteLine(myTime.Elapsed.TotalMilliseconds / 100);
        Console.WriteLine("---");
        Console.WriteLine($"{sysFiles.Count}/{myFiles.Count}");
    }
}