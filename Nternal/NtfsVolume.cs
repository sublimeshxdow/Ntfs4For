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
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;

namespace Nternal
{
    public sealed partial class NtfsVolume : IDisposable
    {
        private IDataStream _provider;

        public NtfsBootSector BootSector { get; private set; }
        public CollationHandler CollationHandler { get; private set; }

        private DataRun[] _cachedBadRanges;
        private BitmapAttribute _rawClusterMap;

        public NtfsVolume(IDataStream dataProvider)
        {
            _provider = dataProvider;
        }

        public void Dispose()
        {
            if (_isMounted)
            {
                var mftStream = MftFile!.GetAttributeData<DataAttribute>("")!.GetStream();
                mftStream.Dispose();
            }
        }

        public void Mount(bool loadMftIntoMemory = true)
        {
            CollationHandler = new();
            ParseBootSector();
            ParseMft();

            if (MftFile == null)
                throw new Exception("Failed to parse the Mft..?");

            if (loadMftIntoMemory)
            {
                var mftStream = MftFile.GetAttributeData<DataAttribute>("")!.GetStream();
                mftStream.LoadIntoMemory();
            }

            VolumeBitmapFile = GetKnownNtfsFileUnsafe(KnownMftRecord.Volume);
            RootFolder = GetKnownNtfsFileUnsafe(KnownMftRecord.Root);
            SetupUpcaseTable();

            VolumeClusterFile = GetKnownNtfsFileUnsafe(KnownMftRecord.BadClus);
            VolumeBitmapFile = GetKnownNtfsFileUnsafe(KnownMftRecord.Bitmap);
            SecureFile = GetKnownNtfsFileUnsafe(KnownMftRecord.Secure);

            VolumeFile = GetKnownNtfsFileUnsafe(KnownMftRecord.Volume);
            _isMounted = true;
        }

        public UsnJournalReader GetJournalReader() => new UsnJournalReader(GetNtfsFile("/$Extend/$UsnJrnl"));

        private void ParseBootSector()
        {
            Span<byte> bootSector = stackalloc byte[NtfsBootSector.BootSectorSize];
            _provider.ReadExactly(0, bootSector);

            BootSector = new(bootSector);
        }

        private void SetupUpcaseTable()
        {
            const int Utf16CharCount = 65536, Utf16ByteCount = Utf16CharCount * 2;

            UpcaseFile = GetKnownNtfsFileUnsafe(KnownMftRecord.UpCase);
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
        
        public IEnumerable<FolderEntry> EnumerateDirectory(string path)
        {
            EnsureIsMounted();
            return EnumerateDirectory(GetNtfsFile(path).GetReference());
        }

        public IEnumerable<FolderEntry> EnumerateDirectory(FileReference folder)
        {
            EnsureIsMounted();
            var file = GetNtfsFile(folder);
            if (!file.RecordHeader.Flags.HasFlag(FileRecordFlags.IsDirectory))
                throw new ArgumentException($"\"{GetPrimaryAbsolutePath(folder)}\" is not a directory!");

            var index = CreateIndexParser(file, "$I30");
            foreach (var entry in index.EnumerateEntries())
                yield return new FolderEntry(entry.GetI30MftReference()!.Value, entry.GetKey());
        }

        public DataRun[] GetBadClusterRanges()
        {
            EnsureIsMounted();
            List<DataRun> badClusters = new();
            var data = VolumeClusterFile.GetRequiredAttributeData<DataAttribute>(NtfsAttributeType.Data, "$Bad");
            if (data.Underlying.Accessor is not NonResidentDataStream stream)
                throw new InvalidDataException(
                    $"$BadClus:$Bad is resident — this should never happen on a healthy volume!");

            foreach (var run in stream.Extents.SelectMany(x => x.DataRuns))
            {
                if (run.Sparse)
                    continue;

                badClusters.Add(run);
            }

            return badClusters.ToArray();
        }

        public ClusterState GetClusterState(long cluster)
        {
            EnsureIsMounted();
            if (cluster < 0 || cluster >= BootSector.TotalClusters)
                throw new ArgumentOutOfRangeException(nameof(cluster),
                    $"Cluster {cluster} is out of range - must be within 0 and {BootSector.TotalClusters}!");

            ClusterState state = ClusterState.Free;
            if (BadClusterRanges.Any(run =>
                    !(cluster < run.StartingCluster) && !(cluster > run.StartingCluster + run.Length - 1)))
            {
                state |= ClusterState.Bad;
            }

            if (ClusterMap.IsSet(cluster))
                state |= ClusterState.Allocated;

            return state;
        }

        public bool IsClusterUsed(long cluster)
        {
            EnsureIsMounted();
            if (cluster < 0 || cluster >= BootSector.TotalClusters)
                throw new ArgumentOutOfRangeException(nameof(cluster),
                    $"Cluster {cluster} is out of range - must be within 0 and {BootSector.TotalClusters}!");

            return ClusterMap.IsSet(cluster);
        }

        internal IDataStream GetUnderlyingStream() => _provider;

        private bool _hasParsedMftYet = false;
        private bool _isMounted = false;

        internal NtfsFile? MftFile { get; set; }
        
        private NtfsFile VolumeClusterFile { get; set; }
        private NtfsFile RootFolder { get; set; }
        private NtfsFile SecureFile { get; set; }
        private NtfsFile? UpcaseFile { get; set; }
        private NtfsFile? VolumeFile { get; set; }
        private NtfsFile? VolumeBitmapFile { get; set; }

        // private Dictionary<KnownMftRecord, NtfsFile> NtfsMetaFiles { get; set; } = new(12);

        private BitmapIndex? MftBitmap { get; set; }

        private DataRun[] BadClusterRanges
        {
            get
            {
                EnsureIsMounted();

                _cachedBadRanges ??= GetBadClusterRanges();
                return _cachedBadRanges;
            }
        }

        private BitmapAttribute? ClusterMap
        {
            get
            {
                EnsureIsMounted();

                if (_rawClusterMap == null)
                {
                    var bitmap = VolumeBitmapFile.GetRequiredAttributeData<DataAttribute>(NtfsAttributeType.Data, "");
                    _rawClusterMap = new BitmapAttribute()
                    {
                        Underlying = bitmap.Underlying
                    };
                    _rawClusterMap.ParseFromAccessor(true);
                }

                return _rawClusterMap;
            }
        }

        private void EnsureIsMounted()
        {
            if (!_isMounted)
                throw new InvalidOperationException($"The volume has not been mounted yet!");
        }
    }
}