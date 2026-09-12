using Nternal.Streams;
using Nternal.Core.Streams;
using System.Diagnostics;
using System.Runtime.InteropServices;
using Nternal.Attributes;
using Nternal.Helpers;
using Nternal.Ntfs;
using Nternal.Ntfs.Attributes;
using Nternal.Ntfs.Index;
using Nternal.Ntfs.NtfsExtensions.Journal;
using Nternal.Structs;

namespace Nternal
{
    public partial class NtfsVolume
    {
        private IDataStream _provider;

        public NtfsBootSector BootSector { get; private set; }
        
        public FixupHelper FixupHelper { get; private set; }
        public CollationHandler CollationHandler { get; private set; }

        public NtfsVolume(IDataStream dataProvider)
        {
            _provider = dataProvider;
        }

        public void Mount()
        {
            CollationHandler = new();
            ParseBootSector();
            ParseMft();

            VolumeBitmapFile = GetKnownNtfsFile(KnownMftRecord.Volume);
            RootFolder = GetKnownNtfsFile(KnownMftRecord.Root);
            SetupUpcaseTable();
        }

        public UsnJournalReader GetJournalReader() =>
            new UsnJournalReader(GetNtfsFile("/$Extend/$UsnJrnl"), BootSector.BytesPerCluster);

        private void ParseBootSector()
        {
            Span<byte> bootSector = stackalloc byte[NtfsBootSector.BootSectorSize];
            _provider.ReadExactly(0, bootSector);
            
            BootSector = new(bootSector);
            FixupHelper = new(BootSector.BytesPerSector);
        }

        private void SetupUpcaseTable()
        {
            const int Utf16CharCount = 65536, Utf16ByteCount = Utf16CharCount * 2;
            
            UpcaseFile = GetKnownNtfsFile(KnownMftRecord.UpCase);
            var stream = UpcaseFile.GetRequiredAttributeData<DataAttribute>(NtfsAttributeType.Data, "").GetStream();

            if (stream.Length != Utf16CharCount * sizeof(char))
                throw new InvalidDataException($"$UpCase should be exactly {Utf16ByteCount} bytes long, but actually is of length {stream.Length} - this should never happen on a healthy volume!");
            
            CollationHandler.UpCaseTable = new char[65536];
            Span<byte> tableBuffer = MemoryMarshal.Cast<char, byte>(CollationHandler.UpCaseTable.AsSpan());
            stream.ReadExactly(0, tableBuffer);
            
        }

        internal IDataStream GetUnderlyingStream() => _provider;

        private bool _hasParsedMftYet = false;
        private IDataStream? _mftStream = null;
        
        public NtfsFile? MftFile { get; private set; }
        public NtfsFile? VolumeBitmapFile { get; private set; }
        public NtfsFile? RootFolder  { get; private set; }
        public NtfsFile? UpcaseFile { get; private set; }
    }
}
