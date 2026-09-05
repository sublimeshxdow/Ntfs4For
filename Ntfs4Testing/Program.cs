using Nternal;
using Nternal.Attributes;
using Nternal.Core;
using Nternal.Helpers;
using Nternal.Partitioning.Gpt;
using Nternal.Partitioning.Mbr;
using Nternal.Streams;

internal class Program
{
    private static void Main(string[] args)
    {
        // var hdd = new Win32PartitionStream(@"\\.\PHYSICALDRIVE0", long.MaxValue);
        var hdd = new Win32PartitionStream(@"/dev/nvme0n1", long.MaxValue);

        GptPartitioning p = new(true);
        p.ParseFromStream(hdd);

        var stream = p.GptPartitionEntries[0].CreatePartitionStream(hdd);

        byte[] data = new byte[512];
        stream.ReadExactly(0x02D09000, data);
    }
}