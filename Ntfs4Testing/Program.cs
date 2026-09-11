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

        var entry = v.GetNtfsFile($"/$AttrDef");
        var attr = entry.GetAttributeData<DataAttribute>(NtfsAttributeType.Data, "");
        Span<byte> buffer = new byte[attr.GetStream().Length];
        attr.GetStream().ReadExactly(0, buffer);
        File.WriteAllBytes("$Attrdef", buffer.Slice(0, (int)attr.GetStream().Length));
    }
}