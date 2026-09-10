using System;
using System.Collections.Generic;
using System.Text;
using Nternal.Attributes;
using Nternal.Core.Streams;
using Nternal.Helpers;
using Nternal.Ntfs.Attributes;
using Nternal.Structs;

namespace Nternal
{
    public partial class NtfsVolume
    {
        public RawFileRecord GetRawRecordData(FileReference reference, bool paranoid = true, bool fixup = true, IDataStream? mft = null)
        {
            if (_hasParsedMftYet)
            {
                var bitmapAttr = Mft.GetAttributeData<BitmapAttribute>(NtfsAttributeType.Bitmap, "");
                if (bitmapAttr == null)
                    throw new InvalidOperationException($"$Mft does not seem to have an unnamed bitmap-attribute - corruption detected.");
                
                if (!bitmapAttr.IsSet((long)reference.FileRecordId))
                    throw new Exception($"Attempted to parse an unused $Mft-Record!");
            }

            var mftStream = mft ?? Mft.GetAttributeStream<DataAttribute>(NtfsAttributeType.Data, "").GetStream();

            if (mftStream == null)
                throw new InvalidOperationException($"$Mft does not seem to have an unnamed data-attribute - corruption detected.");

            long clusterWithRecord = (long)reference.FileRecordId / 4;
            int index = (int)(reference.FileRecordId % 4);
            int recordOffset = index * BootSector.FileRecordSize;
            
            Span<byte> fileRecordCluster = new byte[BootSector.BytesPerCluster];
            mftStream.ReadExactly(clusterWithRecord * BootSector.BytesPerCluster, fileRecordCluster);
            Span<byte> fileRecord = fileRecordCluster[recordOffset..(recordOffset + BootSector.FileRecordSize)];
            
            if(fixup)
                FixupHelper.FixupRecord(fileRecord);

            var rawRecord = new RawFileRecord(fileRecord);
            if (_hasParsedMftYet && rawRecord.SequenceNumber != reference.SequenceNumber)
                throw new ArgumentException($"Attempted to parse record for file-reference {reference}, but that doesn't match with the record's sequence-number {reference.SequenceNumber}!");
                
            return rawRecord;
        }

        public NtfsFile ParseFile(FileReference reference)
        {
            var raw = GetRawRecordData(reference);
            return new NtfsFile(raw, _provider, this);
        }
        
        public void ParseMft(bool paranoid = true, bool fixup = true, bool postXp = true)
        {
            var raw = GetMftRecord(paranoid, fixup);
            Mft = new NtfsFile(raw, _provider, this, postXp);
            _hasParsedMftYet = true;
        }
        
        private RawFileRecord GetMftRecord(bool paranoid = true, bool fixup = true)
        {
            Span<byte> mftRecord = BootSector.FileRecordSize <= 1024
                ? stackalloc byte[BootSector.FileRecordSize]
                : new byte[BootSector.FileRecordSize];
            
            _provider.ReadExactly(BootSector.MftClusterNumber * BootSector.BytesPerCluster, mftRecord);
            
            if (fixup)
                FixupHelper.FixupRecord(mftRecord);

            return new RawFileRecord(mftRecord);
        }
    }
}
