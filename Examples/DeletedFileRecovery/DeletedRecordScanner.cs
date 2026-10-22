using System.Buffers.Binary;
using System.Text;
using Nternal.Core;
using Nternal.Ntfs;
using Nternal.Ntfs.Attributes;
using Nternal.Ntfs.Generic;
using Nternal.Ntfs.Streams;

namespace DeletedFileRecovery;

/// <summary>
///     Walks $MFT record by record looking for deleted base records. The raw record headers are only used
///     to cheaply skip everything that isn't deleted; the actual parsing goes through NtfsVolume.GetNtfsFile,
///     which accepts unused records once DenyUnusedMftRecords is cleared.
/// </summary>
internal sealed class DeletedRecordScanner(NtfsVolume volume)
{
    private readonly IDataStream _mft = volume.GetKnownNtfsFile(KnownMftRecord.Mft).GetDataStream();
    private readonly int _recordSize = volume.BootSector.FileRecordSize;

    public long RecordsScanned { get; private set; }
    public long UnreadableRecords { get; private set; }

    public List<DeletedEntry> Scan(long? onlyRecord)
    {
        var result = new List<DeletedEntry>();
        var recordCount = _mft.Length / _recordSize;
        long first = onlyRecord ?? 0, last = onlyRecord ?? recordCount - 1;

        // Read many record headers per call - one read per record would be very slow on a real disk.
        var recordsPerChunk = Math.Max(1, 1048576 / _recordSize);
        var chunk = new byte[recordsPerChunk * _recordSize];

        for (var start = first; start <= last; start += recordsPerChunk)
        {
            var count = (int)Math.Min(recordsPerChunk, last - start + 1);
            _mft.ReadExactly(start * _recordSize, chunk.AsSpan(0, count * _recordSize));

            for (var i = 0; i < count; i++)
            {
                RecordsScanned++;
                if (!IsDeletedBaseRecord(chunk.AsSpan(i * _recordSize, _recordSize), out var sequence,
                        out var isDirectory))
                    continue;

                var entry = Inspect(start + i, sequence, isDirectory);
                if (entry != null)
                    result.Add(entry);
            }
        }

        return result;
    }

    /// <summary>
    ///     Re-reads a record that was found during the scan and parses it for recovery.
    ///     Returns null if the record has been reused by a new file in the meantime.
    /// </summary>
    public NtfsFile? ReadDeletedFile(long recordNumber)
    {
        var file = volume.GetNtfsFile(new FileReference((ulong)recordNumber, 0), false);
        return file.RecordHeader.Flags.HasFlag(FileRecordFlags.IsInUse) ? null : file;
    }

    /// <summary>
    ///     Cheap pre-filter on the record header. These fields all sit in the first 512 bytes, before the
    ///     first update-sequence fixup position, so they can be read without applying the fixups.
    /// </summary>
    private static bool IsDeletedBaseRecord(ReadOnlySpan<byte> record, out ushort sequence, out bool isDirectory)
    {
        sequence = 0;
        isDirectory = false;

        if (Encoding.ASCII.GetString(record[..4]) != GenericFileRecord.ExpectedMagicNumber)
            return false; // never used, or not a record at all

        var flags = (FileRecordFlags)BinaryPrimitives.ReadUInt16LittleEndian(record[0x16..]);
        if (flags.HasFlag(FileRecordFlags.IsInUse))
            return false;

        // Extension records belong to some base record - they are not files on their own.
        if (BinaryPrimitives.ReadUInt64LittleEndian(record[0x20..]) != 0)
            return false;

        sequence = BinaryPrimitives.ReadUInt16LittleEndian(record[0x10..]);
        isDirectory = flags.HasFlag(FileRecordFlags.IsDirectory);
        return true;
    }

    private DeletedEntry? Inspect(long recordNumber, ushort sequence, bool isDirectory)
    {
        NtfsFile file;
        try
        {
            file = volume.GetNtfsFile(new FileReference((ulong)recordNumber, sequence), false);
        }

        catch (Exception ex)
        {
            // Torn record, or its extension records have been reused since - report it, but it can't be recovered.
            UnreadableRecords++;
            return new DeletedEntry(recordNumber, sequence, $"<record #{recordNumber}>", default,
                isDirectory, 0, default, Recoverability.Unreadable, ex.Message);
        }

        var name = file.GetBestFittingName();
        if (name == null)
            return null; // e.g. a reserved MFT record without a name

        var info = file.GetAttributeData<StandardInformationAttribute>();
        if (isDirectory)
            return new DeletedEntry(recordNumber, sequence, name.Name, name.ParentFolder, true,
                0, info?.FileAltered ?? name.FileAltered, Recoverability.Resident, null);

        var data = file.NtfsAttributes.FirstOrDefault(a => a.Type == NtfsAttributeType.Data && a.Name.Length == 0);
        if (data == null)
            return null;

        var state = Assess(data, out var size);
        return new DeletedEntry(recordNumber, sequence, name.Name, name.ParentFolder, false,
            size, info?.FileAltered ?? name.FileAltered, state, null);
    }

    /// <summary>
    ///     A deleted file's clusters are marked free in $Bitmap. If some of them are allocated again,
    ///     another file now owns them and their content has most likely been replaced.
    ///     (The reverse isn't a guarantee: a cluster can be reused and freed again in between.)
    /// </summary>
    private Recoverability Assess(NtfsAttribute data, out long size)
    {
        if (!data.IsNonResident)
        {
            size = data.Accessor.Length;
            return Recoverability.Resident;
        }

        var stream = (NonResidentDataStream)data.Accessor;
        size = stream.Length;

        long total = 0, reused = 0;
        foreach (var run in stream.Extents.SelectMany(e => e.DataRuns).Where(r => !r.Sparse))
            for (var cluster = run.StartingCluster!.Value; cluster < run.StartingCluster + run.Length; cluster++)
            {
                total++;
                if (cluster >= volume.BootSector.TotalClusters || volume.IsClusterUsed(cluster))
                    reused++;
            }

        if (total == 0 || reused == 0)
            return Recoverability.Intact;
        return reused == total ? Recoverability.Overwritten : Recoverability.Partial;
    }
}