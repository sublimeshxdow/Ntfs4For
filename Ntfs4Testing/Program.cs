using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Security.AccessControl;
using System.Text;
using System.Threading.Tasks.Sources;
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
        char[] letters = ['C', 'D', 'E', 'F', 'G', 'H', 'I', 'N'];

        Parallel.ForEach(letters, letter =>
        {
            CheckDriveLetter(letter);
        });
    }

    static void CheckDriveLetter(char letter)
    {
        var stream = new Win32PartitionStream($@"\\.\{letter}:");
        //var stream = new Win32PartitionStream("/dev/nvme0n1p3", long.MaxValue);

        using NtfsVolume v = new(stream);
        v.Mount();

        //NtfsPolicyConfig.PolicyFlags ^= NtfsPolicyConfig.Flags.ValidateSequenceNumbers;

        long processed = 0;

        Stopwatch s = new();
        s.Start();

        long errors = 0;

        for (long l = 0; l < v.GetMftRecordCount(); l++)
        {
            try
            {
                var test = v.GetNtfsFile(new FileReference((ulong)l, 0), false);

                //Interlocked.Add(ref processed, 1);

                //if (processed % 100000 == 0)
                //    Console.WriteLine(processed);
            }

            catch (Exception ex)
            {
                if (!ex.Message.Contains("unused"))
                {
                    Console.WriteLine($@"\\.\{letter}: {l} - {ex.Message}");
                    errors++;
                }
            }
        }

        s.Stop();
        Console.WriteLine($@"\\.\{letter}: Completed with {v.GetMftRecordCount()} records, after {s.Elapsed.TotalMilliseconds}ms, with {errors} unexpected errors!");

        var file = v.GetNtfsFile($"/$Mft");
        var sdh = v.GetSecurityDescriptor(file);
    }
}
