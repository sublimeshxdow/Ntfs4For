using Nternal;
using Nternal.Attributes;
using Nternal.Helpers;
using Nternal.Partitioning.Mbr;
using Nternal.Streams;

internal class Program
{
    private static void Main(string[] args)
    {
        var hdd = new Win32PartitionStream(@"\\.\PHYSICALDRIVE6", long.MaxValue);

        Span<byte> mbr = stackalloc byte[512];
        hdd.ReadExactly(0, mbr);
        MasterBootRecord mbrRecord = new(mbr);

        var stream = mbrRecord.PartitionEntries.First().CreateDataStream(hdd);

        byte[] data = new byte[512];
        stream.ReadExactly((long)0, data);
    }
}