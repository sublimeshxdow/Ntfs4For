using System.Runtime.InteropServices;
using Nternal.Core;
using Nternal.Core.Extensions;
using Nternal.Ntfs.Attributes;
using Nternal.Ntfs.Checksum;
using Nternal.Ntfs.Exceptions;
using Nternal.Ntfs.Generic;
using Nternal.Ntfs.Index;
using Nternal.Ntfs.NtfsExtensions.Journal;
using Nternal.Ntfs.Streams;

namespace Nternal.Ntfs;

/// <summary>
///     Represents a logical Ntfs Volume.
///     This class provides functionality for interacting with the NTFS file system.
///     It supports enumerating files, querying raw Ntfs Attributes, reading from files, and a lot more.
/// </summary>
public sealed partial class NtfsVolume : IDisposable
{
    private readonly IDataStream _provider;

    /// <summary>
    ///     Contains ranges of bad clusters, if cached.
    /// </summary>
    private DataRun[]? _cachedBadRanges;

    /// <summary>
    ///     Whether the $Mft has been parsed yet.
    /// </summary>
    private bool _hasParsedMftYet;

    /// <summary>
    ///     Whether the volume has been mounted already (Mount() has been called).
    /// </summary>
    private bool _isMounted;

    /// <summary>
    ///     Contains the cluster map of this volume, if cached.
    /// </summary>
    private BitmapAttribute? _rawClusterMap;

    /// <summary>
    ///     Creates a new instance of an NtfsVolume.
    /// </summary>
    /// <param name="dataProvider">The stream representing the underlying partition.</param>
    public NtfsVolume(IDataStream dataProvider)
    {
        _provider = dataProvider;
    }

    /// <summary>
    ///     Represents the Boot Sector of the volume.
    ///     Contains important filesystem-relevant data such as sector/cluster sizes, offset to the $Mft, etc.
    /// </summary>
    public NtfsBootSector BootSector { get; private set; }

    /// <summary>
    ///     Represents the collation-handler used for comparing when interacting with Index Attributes.
    /// </summary>
    public CollationHandler CollationHandler { get; private set; }

    // Begin of Ntfs Meta Files
    internal NtfsFile? MftFile { get; set; }

    private NtfsFile VolumeClusterFile { get; set; }
    private NtfsFile RootFolder { get; set; }
    private NtfsFile SecureFile { get; set; }
    private NtfsFile? UpcaseFile { get; set; }
    private NtfsFile? VolumeFile { get; set; }

    private NtfsFile? VolumeBitmapFile { get; set; }
    // End of Ntfs Meta Files

    /// <summary>
    ///     Holds the bitmap index of $Mft.
    /// </summary>
    private BitmapIndex? MftBitmap { get; set; }

    /// <summary>
    ///     An array of runs representing bad cluster ranges.
    /// </summary>
    private DataRun[] BadClusterRanges
    {
        get
        {
            EnsureIsMounted();

            _cachedBadRanges ??= GetBadClusterRanges();
            return _cachedBadRanges;
        }
    }

    /// <summary>
    ///     The cluster map of this volume.
    /// </summary>
    private BitmapAttribute? ClusterMap
    {
        get
        {
            EnsureIsMounted();

            if (_rawClusterMap == null)
            {
                var bitmap = VolumeBitmapFile!.GetRequiredAttributeData<DataAttribute>(NtfsAttributeType.Data, "");
                _rawClusterMap = new BitmapAttribute
                {
                    Underlying = bitmap.Underlying
                };
                _rawClusterMap.ParseFromAccessor(true);
            }

            return _rawClusterMap;
        }
    }

    /// <summary>
    ///     Releases all unmanaged resources used by this instance.
    /// </summary>
    public void Dispose()
    {
        if (_isMounted)
        {
            var mftStream = MftFile!.GetAttributeData<DataAttribute>()!.GetStream();
            mftStream.Dispose();
        }
    }

    /// <summary>
    ///     Attempts to mount the volume, enabling this instance to interpret the filesystem of the underlying partition.
    /// </summary>
    /// <param name="loadMftIntoMemory">
    ///     Whether the Master-File-Table ($Mft) should be loaded into memory. This will cause the
    ///     mounting times to increase drastically, but querying individual files may become faster at a large scale.
    /// </param>
    /// <exception cref="Exception">
    ///     Thrown when the $Mft could not be loaded correctly. This should never be the case on a
    ///     healthy Ntfs-Volume.
    /// </exception>
    public void Mount(bool loadMftIntoMemory = true)
    {
        CollationHandler = new CollationHandler();
        ParseBootSector();
        ParseMft();

        if (MftFile == null)
            throw new Exception("Failed to parse the Mft..?");

        if (loadMftIntoMemory)
        {
            var mftStream = MftFile.GetAttributeData<DataAttribute>()!.GetStream();
            mftStream.LoadIntoMemory();
        }

        VolumeBitmapFile = GetKnownNtfsFileUnsafe(KnownMftRecord.Volume);
        RootFolder = GetKnownNtfsFileUnsafe(KnownMftRecord.Root);
        SetupUpCaseTable();

        VolumeClusterFile = GetKnownNtfsFileUnsafe(KnownMftRecord.BadClus);
        VolumeBitmapFile = GetKnownNtfsFileUnsafe(KnownMftRecord.Bitmap);
        SecureFile = GetKnownNtfsFileUnsafe(KnownMftRecord.Secure);

        VolumeFile = GetKnownNtfsFileUnsafe(KnownMftRecord.Volume);
        _isMounted = true;
    }

    /// <summary>
    ///     Returns the UsnJournalReader for this volume.
    /// </summary>
    /// <returns>The UsnJournalReader, or null, if the journal could not be found.</returns>
    public UsnJournalReader? GetJournalReader()
    {
        try
        {
            return new UsnJournalReader(GetNtfsFile("/$Extend/$UsnJrnl"));
        }
        catch (FileNotFoundException ex)
        {
            return null;
        }
    }

    /// <summary>
    ///     Parses the boot sector.
    /// </summary>
    private void ParseBootSector()
    {
        Span<byte> bootSector = stackalloc byte[NtfsBootSector.BootSectorSize];
        _provider.ReadExactly(0, bootSector);

        BootSector = new NtfsBootSector(bootSector);
    }

    /// <summary>
    ///     Parses $UpCase so the collation handler can compare file-names correctly.
    /// </summary>
    /// <exception cref="InvalidDataException">
    ///     Thrown if the main stream of $UpCase is of invalid length. This usually entails
    ///     corruption.
    /// </exception>
    /// <exception cref="NotImplementedException">
    ///     [Iff NtfsPolicyConfig:ValidateUpCase] Thrown if the corresponding $Info
    ///     stream is non-resident. This function will be updated once this is no longer the case.
    /// </exception>
    /// <exception cref="Exception">
    ///     [Iff NtfsPolicyConfig:ValidateUpCase] Thrown if the $Info stream is of unexpected length.
    ///     Again, this function will be updated if this stream's size changes.
    /// </exception>
    /// <exception cref="ChecksumVerificationFailedException">
    ///     [Iff NtfsPolicyConfig:ValidateUpCase] Thrown if the checksum of
    ///     the UpCase-Table does not match the one stored within the $Info stream.
    /// </exception>
    private void SetupUpCaseTable()
    {
        const int Utf16CharCount = 65536, Utf16ByteCount = Utf16CharCount * 2;

        UpcaseFile = GetKnownNtfsFileUnsafe(KnownMftRecord.UpCase);
        var upcaseStream = UpcaseFile.GetRequiredAttributeData<DataAttribute>(NtfsAttributeType.Data, "")
            .GetStream();

        if (upcaseStream.Length != Utf16CharCount * sizeof(char))
            throw new InvalidDataException(
                $"$UpCase should be exactly {Utf16ByteCount} bytes long, but actually is of length {upcaseStream.Length} - this should never happen on a healthy volume!");

        CollationHandler.UpCaseTable = new char[65536];
        var tableBuffer = MemoryMarshal.Cast<char, byte>(CollationHandler.UpCaseTable.AsSpan());
        upcaseStream.ReadExactly(0, tableBuffer);

        var upcaseInfoStream = UpcaseFile.GetAttributeData<DataAttribute>(NtfsAttributeType.Data, "$Info")?
            .GetStream();

        if (upcaseInfoStream != null
            && NtfsPolicyConfig.PolicyFlags.HasFlag(NtfsPolicyConfig.Flags.ValidateUpCase))
        {
            if (!upcaseInfoStream.TryGetMemory(out var upcaseInfo))
                throw new NotImplementedException("$UpCase:$Info is expected to always be resident!");

            if (upcaseInfoStream.Length != 32)
                throw new Exception(
                    $"$UpCase:$Info should be  32 bytes long, but was {upcaseInfoStream.Length}!");

            var writtenChecksum = upcaseInfo.ReadUInt64(8);
            var calculatedChecksum = RtlCrc64Nvme.Compute(tableBuffer);

            if (writtenChecksum != calculatedChecksum)
                throw new ChecksumVerificationFailedException(
                    $"$UpCase validation failed - written checksum {writtenChecksum.ToString("X16")}" +
                    $" does not match computed checksum {calculatedChecksum.ToString("X16")}!");
        }
    }

    /// <summary>
    ///     Yields an enumerable object that allows enumerating entries of the specified directory.
    /// </summary>
    /// <param name="path">The path of the folder to enumerate.</param>
    /// <returns>The enumerable object.</returns>
    /// ///
    /// <exception cref="ArgumentException">Thrown if the specified file reference does not reference a directory.</exception>
    public IEnumerable<FolderEntry> EnumerateDirectory(string path)
    {
        EnsureIsMounted();
        return EnumerateDirectory(GetNtfsFile(path).GetReference());
    }

    /// <summary>
    ///     Yields an enumerable object that allows enumerating entries of the specified directory.
    /// </summary>
    /// <param name="folder">A file reference to the folder to enumerate.</param>
    /// <returns>The enumerable object.</returns>
    /// ///
    /// <exception cref="ArgumentException">Thrown if the specified file reference does not reference a directory.</exception>
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

    /// <summary>
    ///     Returns ranges of bad (damaged) clusters as data runs.
    /// </summary>
    /// <returns>The data runs representing bad clusters.</returns>
    /// <exception cref="InvalidDataException">
    ///     Thrown if $BadClus:$Bad [the stream used for finding bad clusters] is
    ///     non-resident. This may never happend, and usually entails corruption.
    /// </exception>
    public DataRun[] GetBadClusterRanges()
    {
        EnsureIsMounted();
        List<DataRun> badClusters = new();
        var data = VolumeClusterFile.GetRequiredAttributeData<DataAttribute>(NtfsAttributeType.Data, "$Bad");
        if (data.Underlying.Accessor is not NonResidentDataStream stream)
            throw new InvalidDataException(
                "$BadClus:$Bad is resident — this should never happen on a healthy volume!");

        foreach (var run in stream.Extents.SelectMany(x => x.DataRuns))
        {
            if (run.Sparse)
                continue;

            badClusters.Add(run);
        }

        return badClusters.ToArray();
    }

    /// <summary>
    ///     Queries the state of the specified cluster.
    /// </summary>
    /// <param name="cluster">The cluster to query the state for.</param>
    /// <returns>The state of the queried cluster.</returns>
    /// <exception cref="ArgumentOutOfRangeException">The specified cluster does not reside within this volume.</exception>
    public ClusterState GetClusterState(long cluster)
    {
        EnsureIsMounted();
        if (cluster < 0 || cluster >= BootSector.TotalClusters)
            throw new ArgumentOutOfRangeException(nameof(cluster),
                $"Cluster {cluster} is out of range - must be within 0 and {BootSector.TotalClusters}!");

        var state = ClusterState.Free;
        if (BadClusterRanges.Any(run =>
                !(cluster < run.StartingCluster) && !(cluster > run.StartingCluster + run.Length - 1)))
            state |= ClusterState.Bad;

        if (ClusterMap.IsSet(cluster))
            state |= ClusterState.Allocated;

        return state;
    }

    /// <summary>
    ///     Returns whether the given cluster is currently used/reserved for any file.
    /// </summary>
    /// <param name="cluster">The cluster to query the allocation status for.</param>
    /// <returns>Whether the specified cluster is in use.</returns>
    /// <exception cref="ArgumentOutOfRangeException">The specified cluster does not reside within this volume.</exception>
    public bool IsClusterUsed(long cluster)
    {
        EnsureIsMounted();
        if (cluster < 0 || cluster >= BootSector.TotalClusters)
            throw new ArgumentOutOfRangeException(nameof(cluster),
                $"Cluster {cluster} is out of range - must be within 0 and {BootSector.TotalClusters}!");

        return ClusterMap.IsSet(cluster);
    }

    /// <summary>
    ///     Yields the underlying data stream of the partition.
    /// </summary>
    /// <returns></returns>
    internal IDataStream GetUnderlyingStream()
    {
        return _provider;
    }

    /// <summary>
    ///     Safety check for ensuring the volume is mounted.
    /// </summary>
    /// <exception cref="InvalidOperationException">The volume has not been mounted yet.</exception>
    private void EnsureIsMounted()
    {
        if (!_isMounted)
            throw new InvalidOperationException("The volume has not been mounted yet!");
    }
}