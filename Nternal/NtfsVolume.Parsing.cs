using System;
using System.Collections.Generic;
using System.Text;
using Nternal.Attributes;
using Nternal.Helpers;
using Nternal.Ntfs.Attributes;
using Nternal.Structs;

namespace Nternal
{
    public partial class NtfsVolume
    {
        public RawFileRecord GetRawRecord(FileReference reference, bool paranoid = true, bool fixup = true)
        {
            var bitmapAttr = Mft.NtfsAttributes
                .Where(x => x.Type == NtfsAttributeType.Bitmap)
                .Cast<BitmapAttribute>()
                .FirstOrDefault(x => x.Name == string.Empty);
         
            if (bitmapAttr == null)
                throw new InvalidOperationException($"$Mft does not seem to have an unnamed bitmap-attribute - corruption detected.");
            
            bitmapAttr.ParseFromAccessor(paranoid);
            if (!bitmapAttr.IsSet(reference.SequenceNumber))
                throw new Exception($"Attempted to parse an unused $Mft-Record!");
            
            var dataAttr = Mft.NtfsAttributes
                .Where(x => x.Type == NtfsAttributeType.Data)
                .FirstOrDefault(x => x.Name == string.Empty);

            if (dataAttr == null)
                throw new InvalidOperationException($"$Mft does not seem to have an unnamed data-attribute - corruption detected.");
         
            Span<byte> fileRecord = new byte[BootSector.FileRecordSize];
            dataAttr.Accessor.ReadExactly(reference.SequenceNumber * BootSector.FileRecordSize, fileRecord);
            
            if(fixup)
                FixupHelper.FixupRecord(fileRecord);

            return new RawFileRecord(fileRecord);
        }
    }
}
