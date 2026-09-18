using Nternal.Streams;
using Nternal.Core.Streams;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.Intrinsics.X86;
using System.Runtime.Serialization;
using Nternal.Attributes;
using Nternal.Extensions;
using Nternal.Helpers;
using Nternal.Ntfs;
using Nternal.Ntfs.Attributes;
using Nternal.Ntfs.Checksum;
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
            var upcaseStream = UpcaseFile.GetRequiredAttributeData<DataAttribute>(NtfsAttributeType.Data, "")
                .GetStream();

            if (upcaseStream.Length != Utf16CharCount * sizeof(char))
                throw new InvalidDataException(
                    $"$UpCase should be exactly {Utf16ByteCount} bytes long, but actually is of length {upcaseStream.Length} - this should never happen on a healthy volume!");

            CollationHandler.UpCaseTable = new char[65536];
            Span<byte> tableBuffer = MemoryMarshal.Cast<char, byte>(CollationHandler.UpCaseTable.AsSpan());
            upcaseStream.ReadExactly(0, tableBuffer);

            var upcaseInfoStream = UpcaseFile.GetAttributeData<DataAttribute>(NtfsAttributeType.Data, "$Info")?
                .GetStream();

            if (upcaseInfoStream != null
                && NtfsPolicyConfig.PolicyFlags.HasFlag(NtfsPolicyConfig.Flags.ValidateUpcase))
            {
                if (!upcaseInfoStream.TryGetMemory(out var upcaseInfo))
                    throw new NotImplementedException($"$UpCase:$Info is expected to always be resident!");

                if (upcaseInfoStream.Length != 32)
                    throw new Exception(
                        $"$UpCase:$Info should be  32 bytes long, but was {upcaseInfoStream.Length}!");

                ulong writtenChecksum = upcaseInfo.ReadUInt64(8);
                ulong calculatedChecksum = RtlCrc64Nvme.Compute(tableBuffer);

                if (writtenChecksum != calculatedChecksum)
                    throw new InvalidDataContractException(
                        $"$UpCase validation failed - written checksum {writtenChecksum.ToString("X16")}" +
                        $" does not match computed checksum {calculatedChecksum.ToString("X16")}!");
            }
        }

        internal IDataStream GetUnderlyingStream() => _provider;

        private bool _hasParsedMftYet = false;
        private IDataStream? _mftStream = null;

        public NtfsFile? MftFile { get; private set; }
        public NtfsFile? VolumeBitmapFile { get; private set; }
        public NtfsFile? RootFolder { get; private set; }
        public NtfsFile? UpcaseFile { get; private set; }
    }
}