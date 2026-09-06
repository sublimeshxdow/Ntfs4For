using System;
using System.Collections.Generic;
using System.Text;
using Nternal.Attributes;
using Nternal.Helpers;
using Nternal.Structs;

namespace Nternal
{
    public partial class NtfsVolume
    {
        public RawFileRecord GetRawRecord(FileReference reference, bool fixup = true)
        {
            var dataAttr = Mft.NtfsAttributes
                .Where(x => x.Type == NtfsAttributeType.Data)
                .FirstOrDefault(x => x.Name == string.Empty);

            if (dataAttr == null)
                throw new InvalidOperationException($"$Mft does not seem to have an unnamed data-stream - corruption detected.");

            Span<byte> fileRecord = new byte[BootSector.FileRecordSize];
            dataAttr.Accessor.ReadExactly(reference.SequenceNumber * BootSector.FileRecordSize, fileRecord);
            
            if(fixup)
                FixupHelpers.FixupRecord(fileRecord, BootSector.BytesPerSector);

            return new RawFileRecord(fileRecord);
        }
    }
}
