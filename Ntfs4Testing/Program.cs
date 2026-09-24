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
        var stream = new Win32PartitionStream(@"\\.\N:", long.MaxValue);
        //var stream = new Win32PartitionStream("/dev/nvme0n1p3", long.MaxValue);

        NtfsVolume v = new(stream);
        v.Mount();

        //NtfsPolicyConfig.PolicyFlags ^= NtfsPolicyConfig.Flags.ValidateSequenceNumbers;

        long processed = 0;

        Stopwatch s = new();
        s.Start();

        for (long l = 0; l < v.GetMftRecordCount(); l++)
        {
            try
            {
                var test = v.GetNtfsFile(new FileReference((ulong)l, 0), false);

                Interlocked.Add(ref processed, 1);

                if (processed % 100000 == 0)
                    Console.WriteLine(processed);
            }

            catch (Exception ex)
            {
                if (!ex.Message.Contains("unused"))
                    Console.WriteLine($"{l} - {ex.Message}");
            }
        }

        s.Stop();
        Console.WriteLine(s.Elapsed.TotalMilliseconds);

        //var file = v.GetNtfsFile($"/Users/xxluk/AppData/Local/Microsoft/IdentityCache/1/UD/u_B9UEQH0F44PUHL59/e_C2GK9UTC67FSUCG3/ID/r_74DB6FURNR2TGPBK/c_6HM880EG8H6C0PCE.bin");
        //var sdh = v.GetSecurityDescriptor(file);
    }
}
