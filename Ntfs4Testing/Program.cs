using System.Security.AccessControl;
using System.Text;
using Nternal;
using Nternal.Attributes;
using Nternal.Core;
using Nternal.Helpers;
using Nternal.Ntfs.Attributes;
using Nternal.Ntfs.Index;
using Nternal.Ntfs.Security;
using Nternal.Partitioning.Gpt;
using Nternal.Partitioning.Mbr;
using Nternal.Streams;
using Nternal.Structs;

internal class Program
{
    private static void Main(string[] args)
    {
        var stream = new Win32PartitionStream("/dev/nvme0n1p3", long.MaxValue);

        NtfsVolume v = new(stream);
        v.Mount();
        v.ParseMft();

        var data = v.Mft.GetAttributeData<FileNameAttribute>(NtfsAttributeType.FileName, "");
        
        var root = v.ParseFile(new FileReference(5, 5));
        var parser = new IndexParser(
            root.GetAttributeData<IndexRootAttribute>(NtfsAttributeType.IndexRoot, "$I30"),
            root.GetAttributeData<IndexAllocationAttribute>(NtfsAttributeType.IndexAllocation, "$I30"),
            root.GetAttributeData<BitmapAttribute>(NtfsAttributeType.Bitmap, "$I30"),
            true,
            v.FixupHelper);

        parser.TryFindRootEntry(Encoding.Unicode.GetBytes("PROGRA~3"));
    }
}