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
    /// <summary>
    /// I use this for testing my library, you should not rely on this code.
    /// </summary>
    /// <param name="args">Arguments passed via cli interface.</param>
    private static void Main(string[] args)
    {
        using var rawStream = new RawDriveStream("/dev/nvme0n1p3");
        using var drive = new NtfsVolume(rawStream);
        drive.Mount();

        foreach (var entry in drive.EnumerateIndexedSymLinks())
        {
            if (entry.Tag.HasFlag(ReparseTag.SymbolicLink))
            {
                var file = drive.GetNtfsFile(entry.Reference);
                var reparseInfo = file.GetAttributeData<ReparseAttribute>();
                if (reparseInfo?.Data is SymbolicLinkReparseData sym)
                    Console.WriteLine(sym.PrintName);
            }
        }
        return;
        
        char[] letters = ['C', 'D', 'E', 'F', 'G', 'H', 'I', 'N'];

        Parallel.ForEach(letters, new ParallelOptions() { MaxDegreeOfParallelism = 8 }, letter =>
        {
            CheckDriveLetter(letter);
        });
    }

    static void CheckDriveLetter(char? letter)
    {
        Console.WriteLine($"Starting with {letter}...");
        RawDriveStream stream;

        if (letter != null)
            stream = new RawDriveStream($@"\\.\{letter}:");
        else
            stream = new RawDriveStream("/dev/nvme0n1p3");

        using NtfsVolume v = new(stream);
        v.Mount();

        //NtfsPolicyConfig.PolicyFlags ^= NtfsPolicyConfig.Flags.ValidateSequenceNumbers;


        Console.WriteLine($"Name={v.VolumeLabel} Version={v.NtfsVersion} Flags={v.VolumeFlags}");
        long processed = 0;

        Stopwatch s = new();
        s.Start();

        v.EnumerateDirectory("/").ToList();

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
