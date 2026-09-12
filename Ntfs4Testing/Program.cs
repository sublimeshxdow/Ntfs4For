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

        var entry = v.GetNtfsFile($"/$MfT");
        entry = v.GetNtfsFile($"/$MfT");
        var attr = entry.GetAttributeData<DataAttribute>(NtfsAttributeType.Data, "");

        using FileStream fs = new("$MFT_NEW", FileMode.Create);
        Span<byte> buffer = new byte[4096];
        
        for (long offset = 0; offset < attr.GetStream().AllocatedLength - 4096; offset += 4096)
        {
            attr.GetStream().ReadExactly(offset, buffer); 
            fs.Write(buffer);
        }
    }
}