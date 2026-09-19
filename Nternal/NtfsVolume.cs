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
            if (MftFile == null)
                throw new Exception("Failed to parse the Mft..?");

            VolumeBitmapFile = GetKnownNtfsFile(KnownMftRecord.Volume);
            RootFolder = GetKnownNtfsFile(KnownMftRecord.Root);
            SetupUpcaseTable();

            VolumeClusterFile = GetKnownNtfsFile(KnownMftRecord.BadClus);
            VolumeBitmapFile = GetKnownNtfsFile(KnownMftRecord.Bitmap);
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

        public IEnumerable<(FileReference, FileNameData)> EnumerateDirectory(FileReference folder)
        {
            var file = GetNtfsFile(folder);
            if (!file.FileRecord.Flags.HasFlag(FileRecordFlags.IsDirectory))
                throw new ArgumentException($"\"{GetPrimaryAbsolutePath(folder)}\" is not a directory!");

            var index = CreateIndexParser(file, "$I30");
            foreach (var entry in index.EnumerateEntries())
            {
                var parsedEntry = new FileNameData();
                parsedEntry.Parse(entry.GetKey(), true);
                yield return (entry.GetI30MftReference()!.Value, parsedEntry);
            }
        }

        public DataRun[] GetBadClusterRanges()
        {
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
            if (cluster < 0 || cluster >= BootSector.TotalClusters)
                throw new ArgumentOutOfRangeException(nameof(cluster),
                    $"Cluster {cluster} is out of range - must be within 0 and {BootSector.TotalClusters}!");

            return ClusterMap.IsSet(cluster);
        }

        internal IDataStream GetUnderlyingStream() => _provider;

        private bool _hasParsedMftYet = false;
        private IDataStream? _mftStream = null;

        public NtfsFile? MftFile { get; private set; }
        public NtfsFile? UpcaseFile { get; private set; }

        public NtfsFile? VolumeBitmapFile { get; private set; }

        private DataRun[] BadClusterRanges
        {
            get
            {
                _cachedBadRanges ??= GetBadClusterRanges();
                return _cachedBadRanges;
            }
        }

        private BitmapAttribute? ClusterMap
        {
            get
            {
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

        private DataRun[]? _cachedBadRanges;
        private BitmapAttribute? _rawClusterMap;

        public NtfsFile? VolumeClusterFile { get; private set; }

        public NtfsFile? RootFolder { get; private set; }
    }
}