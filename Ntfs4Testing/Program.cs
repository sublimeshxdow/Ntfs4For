using Nternal;
using Nternal.Attributes;
using Nternal.Helpers;
using Nternal.Partitioning.Gpt;
using Nternal.Partitioning.Mbr;
using Nternal.Streams;

internal class Program
{
    private static void Main(string[] args)
    {
        var hdd = new Win32PartitionStream(@"\\.\PHYSICALDRIVE0", long.MaxValue);

        GptPartitioning p = new(true);
        p.ParseFromStream(hdd);
    }
}