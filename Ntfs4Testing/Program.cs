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
        byte[] Lznt1Example =
        {
            0x18, 0xB0, 0x00, 0x41, 0x42, 0x43, 0x44, 0x45, 0x46, 0x47, 0x48,
            0x00, 0x49, 0x4A, 0x4B, 0x4C, 0x4D, 0x4E, 0x4F, 0x50,
            0x0A, 0x51, 0x02, 0x80, 0x21, 0x04, 0x10,
            0x00, 0x00
        };
        Span<byte> output = stackalloc byte[1024];
        var r = NtfsDecompressor.DecompressChunk(Lznt1Example, output);
        Console.WriteLine(Encoding.UTF8.GetString(r));
        return;

        //var stream = new Win32PartitionStream(@"\\.\C:", long.MaxValue);
        var stream = new Win32PartitionStream("/dev/nvme0n1p3", long.MaxValue);

        NtfsVolume v = new(stream);
        v.Mount();

        var entry = v.GetNtfsFile("/$Secure");
        var index = v.CreateIndexParser(entry, "$SII");

        var secureEntry = index.TryFindRootEntry(BitConverter.GetBytes((uint)256));

        return;
    }
}