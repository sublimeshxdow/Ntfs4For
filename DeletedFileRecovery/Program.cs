using System.Buffers.Binary;
using Nternal;
using Nternal.Attributes;
using Nternal.Core;
using Nternal.Core.Streams;
using Nternal.Ntfs;
using Nternal.Ntfs.Attributes;
using Nternal.Streams;
using Nternal.Structs;

namespace DeletedFileRecovery
{
    /// <summary>
    /// How much of a deleted file's content can still be trusted.
    /// </summary>
    internal enum Recoverability
    {
        /// <summary>Content lives inside the MFT record itself - always recoverable while the record survives.</summary>
        Resident,
        /// <summary>None of the file's clusters have been handed to another file since.</summary>
        Intact,
        /// <summary>Some clusters are in use again - parts of the output will contain other data.</summary>
        Partial,
        /// <summary>Every cluster is in use again - the content is gone.</summary>
        Overwritten,
        /// <summary>The record could not be parsed (e.g. its extension records were reused).</summary>
        Unreadable
    }

    internal sealed record DeletedEntry(
        long RecordNumber,
        ushort SequenceNumber,
        string Name,
        FileReference Parent,
        bool IsDirectory,
        long Size,
        DateTime Modified,
        Recoverability State,
        string? Error);

    internal class Program
    {
        static int Main(string[] args)
        {
            string? source = null, outputFolder = null, nameFilter = null;
            long? onlyRecord = null;
            bool includePartial = false, showUnrecoverable = false, invalid = false;

            for (int i = 0; i < args.Length; i++)
            {
                switch (args[i])
                {
                    case "--recover" when i + 1 < args.Length: outputFolder = args[++i]; break;
                    case "--name" when i + 1 < args.Length: nameFilter = args[++i]; break;
                    case "--record" when i + 1 < args.Length: onlyRecord = long.Parse(args[++i].TrimStart('#')); break;
                    case "--include-partial": includePartial = true; break;
                    case "--all": showUnrecoverable = true; break;
                    default:
                        invalid |= source != null;
                        source = args[i];
                        break;
                }
            }

            if (source == null || invalid)
            {
                Console.WriteLine("Usage: DeletedFileRecovery <source> [--name text] [--record N] [--all]");
                Console.WriteLine("                           [--recover <folder>] [--include-partial]");
                Console.WriteLine("  <source>           a drive letter on Windows (e.g. D:), or a device / image path");
                Console.WriteLine("  --name text        only consider deleted files whose name contains this text");
                Console.WriteLine("  --record N         only consider MFT record N");
                Console.WriteLine("  --all              also list overwritten / unreadable records");
                Console.WriteLine("  --recover folder   write recoverable files into this folder");
                Console.WriteLine("  --include-partial  also recover files whose clusters were partly reused");
                Console.WriteLine();
                Console.WriteLine("Without --recover, deleted files are only listed. Never recover onto the volume you are");
                Console.WriteLine("scanning - writing there can overwrite exactly the clusters you are trying to save.");
                return 1;
            }

            return Run(source, nameFilter, onlyRecord, showUnrecoverable, outputFolder, includePartial) ? 0 : 1;
        }

        public static bool Run(string source, string? nameFilter, long? onlyRecord, bool showUnrecoverable,
            string? outputFolder, bool includePartial)
        {
            // Deleted files live in MFT records marked as unused - by default the library refuses those.
            NtfsPolicyConfig.PolicyFlags &= ~NtfsPolicyConfig.Flags.DenyUnusedMftRecords;

            try
            {
                using var stream = new RawDriveStream(ResolveDevicePath(source));
                using var volume = new NtfsVolume(stream);
                volume.Mount(false);

                if (outputFolder != null && IsSameVolume(source, outputFolder))
                {
                    Console.WriteLine($"Refusing to recover onto {source} itself - choose an output folder on another drive.");
                    return false;
                }

                var scanner = new DeletedRecordScanner(volume);
                var entries = scanner.Scan(onlyRecord);
                var paths = new DeletedPathResolver(volume, entries);

                var files = entries
                    .Where(e => !e.IsDirectory)
                    .Where(e => nameFilter == null || e.Name.Contains(nameFilter, StringComparison.OrdinalIgnoreCase))
                    .Where(e => showUnrecoverable || e.State is not (Recoverability.Overwritten or Recoverability.Unreadable))
                    .OrderBy(e => paths.GetFolder(e), StringComparer.OrdinalIgnoreCase)
                    .ThenBy(e => e.Name, StringComparer.OrdinalIgnoreCase)
                    .ToList();

                Console.WriteLine($"Scanned {scanner.RecordsScanned:N0} MFT records: {entries.Count(e => !e.IsDirectory):N0} deleted files, " +
                                  $"{entries.Count(e => e.IsDirectory):N0} deleted folders" +
                                  (scanner.UnreadableRecords > 0 ? $", {scanner.UnreadableRecords:N0} unreadable records" : "") + ".");
                Console.WriteLine();

                Console.WriteLine($"{"Record",-10}{"State",-13}{"Size",12}  {"Modified (UTC)",-18}Path");
                foreach (var entry in files)
                {
                    string state = entry.State.ToString();
                    Console.WriteLine($"#{entry.RecordNumber,-9}{state,-13}{FormatSize(entry.Size),12}  {entry.Modified,-18:yyyy-MM-dd HH:mm}" +
                                      $"{paths.GetFolder(entry)}/{entry.Name}" +
                                      (entry.Error != null ? $"  ({entry.Error})" : ""));
                }

                Console.WriteLine();
                Console.WriteLine($"{files.Count:N0} deleted files listed.");

                if (outputFolder != null)
                    Recover(volume, scanner, files, paths, outputFolder, includePartial);

                return true;
            }

            catch (UnauthorizedAccessException ex)
            {
                Console.WriteLine($"Failed to open {source}: {ex.Message}");
                Console.WriteLine("Make sure you are running this program as administrator / root!");
            }

            catch (Exception ex)
            {
                Console.WriteLine($"Failed to scan for deleted files: {ex.Message}");
            }

            return false;
        }

        private static void Recover(NtfsVolume volume, DeletedRecordScanner scanner, List<DeletedEntry> files,
            DeletedPathResolver paths, string outputFolder, bool includePartial)
        {
            Console.WriteLine();
            Directory.CreateDirectory(outputFolder);

            int recovered = 0, skipped = 0, failed = 0;
            foreach (var entry in files)
            {
                bool wanted = entry.State is Recoverability.Resident or Recoverability.Intact
                              || (includePartial && entry.State == Recoverability.Partial);
                if (!wanted)
                {
                    skipped++;
                    continue;
                }

                // Mirror the original folder structure below the output folder; deleted folders are kept as named.
                string relativeFolder = string.Join(Path.DirectorySeparatorChar,
                    paths.GetFolder(entry).Split('/', StringSplitOptions.RemoveEmptyEntries)
                        .Select(part => SanitizeFileName(part.Trim('<', '>'))));
                string targetFolder = Path.Combine(outputFolder, relativeFolder);
                string targetPath = Path.Combine(targetFolder, $"{entry.RecordNumber}_{SanitizeFileName(entry.Name)}");

                try
                {
                    Directory.CreateDirectory(targetFolder);
                    var file = scanner.ReadDeletedFile(entry.RecordNumber)
                               ?? throw new InvalidDataException("record changed since the scan");
                    using var data = file.GetDataStream();
                    CopyStream(data, targetPath);

                    var info = file.GetAttributeData<StandardInformationAttribute>();
                    if (info != null)
                    {
                        File.SetCreationTimeUtc(targetPath, info.FileCreation);
                        File.SetLastWriteTimeUtc(targetPath, info.FileAltered);
                    }

                    Console.WriteLine($"Recovered #{entry.RecordNumber} -> {targetPath}" +
                                      (entry.State == Recoverability.Partial ? "  [partially overwritten]" : ""));
                    recovered++;
                }

                catch (Exception ex)
                {
                    Console.WriteLine($"Failed #{entry.RecordNumber} {entry.Name}: {ex.Message}");
                    failed++;
                }
            }

            Console.WriteLine();
            Console.WriteLine($"Recovered {recovered:N0} files to {outputFolder}" +
                              (skipped > 0 ? $", skipped {skipped:N0} not (fully) recoverable" : "") +
                              (failed > 0 ? $", {failed:N0} failed" : "") + ".");
        }

        private static void CopyStream(IDataStream data, string targetPath)
        {
            const int BufferSize = 1048576;
            byte[] buffer = new byte[BufferSize];
            using FileStream output = new(targetPath, FileMode.Create, FileAccess.Write, FileShare.None, BufferSize);

            long position = 0, length = data.Length;
            while (position < length)
            {
                int toRead = (int)Math.Min(BufferSize, length - position);
                int read = data.Read(position, buffer.AsSpan(0, toRead));
                if (read <= 0)
                    throw new IOException($"Unexpected end of stream at offset {position} of {length}.");

                output.Write(buffer, 0, read);
                position += read;
            }
        }

        private static bool IsSameVolume(string source, string outputFolder)
        {
            if (!OperatingSystem.IsWindows() || source.TrimEnd(':').Length != 1)
                return false;

            string? root = Path.GetPathRoot(Path.GetFullPath(outputFolder));
            return root != null && char.ToUpperInvariant(root[0]) == char.ToUpperInvariant(source[0]);
        }

        private static string SanitizeFileName(string name)
        {
            var invalid = Path.GetInvalidFileNameChars().Concat(['<', '>', ':', '"', '|', '?', '*', '\\', '/']).ToHashSet();
            return new string(name.Select(c => invalid.Contains(c) || char.IsControl(c) ? '_' : c).ToArray());
        }

        private static string ResolveDevicePath(string source)
        {
            if (OperatingSystem.IsWindows() && source.TrimEnd(':').Length == 1 && char.IsLetter(source[0]))
                return $@"\\.\{char.ToUpperInvariant(source[0])}:";

            return source;
        }

        private static string FormatSize(long bytes)
        {
            string[] units = ["B", "KB", "MB", "GB", "TB"];
            double size = bytes;
            int unit = 0;
            while (size >= 1024 && unit < units.Length - 1)
            {
                size /= 1024;
                unit++;
            }

            return unit == 0 ? $"{bytes} B" : $"{size:F1} {units[unit]}";
        }
    }

    /// <summary>
    /// Walks $MFT record by record looking for deleted base records. The raw record headers are only used
    /// to cheaply skip everything that isn't deleted; the actual parsing goes through NtfsVolume.GetNtfsFile,
    /// which accepts unused records once DenyUnusedMftRecords is cleared.
    /// </summary>
    internal sealed class DeletedRecordScanner(NtfsVolume volume)
    {
        private const uint FileMagic = 0x454C4946; // "FILE"

        private readonly int _recordSize = volume.BootSector.FileRecordSize;
        private readonly IDataStream _mft = volume.GetKnownNtfsFile(KnownMftRecord.Mft).GetDataStream();

        public long RecordsScanned { get; private set; }
        public long UnreadableRecords { get; private set; }

        public List<DeletedEntry> Scan(long? onlyRecord)
        {
            var result = new List<DeletedEntry>();
            long recordCount = _mft.Length / _recordSize;
            long first = onlyRecord ?? 0, last = onlyRecord ?? recordCount - 1;

            // Read many record headers per call - one read per record would be very slow on a real disk.
            int recordsPerChunk = Math.Max(1, 1048576 / _recordSize);
            byte[] chunk = new byte[recordsPerChunk * _recordSize];

            for (long start = first; start <= last; start += recordsPerChunk)
            {
                int count = (int)Math.Min(recordsPerChunk, last - start + 1);
                _mft.ReadExactly(start * _recordSize, chunk.AsSpan(0, count * _recordSize));

                for (int i = 0; i < count; i++)
                {
                    RecordsScanned++;
                    if (!IsDeletedBaseRecord(chunk.AsSpan(i * _recordSize, _recordSize), out ushort sequence, out bool isDirectory))
                        continue;

                    var entry = Inspect(start + i, sequence, isDirectory);
                    if (entry != null)
                        result.Add(entry);
                }
            }

            return result;
        }

        /// <summary>
        /// Re-reads a record that was found during the scan and parses it for recovery.
        /// Returns null if the record has been reused by a new file in the meantime.
        /// </summary>
        public NtfsFile? ReadDeletedFile(long recordNumber)
        {
            var file = volume.GetNtfsFile(new FileReference((ulong)recordNumber, 0), compareSequence: false);
            return file.RecordHeader.Flags.HasFlag(FileRecordFlags.IsInUse) ? null : file;
        }

        /// <summary>
        /// Cheap pre-filter on the record header. These fields all sit in the first 512 bytes, before the
        /// first update-sequence fixup position, so they can be read without applying the fixups.
        /// </summary>
        private static bool IsDeletedBaseRecord(ReadOnlySpan<byte> record, out ushort sequence, out bool isDirectory)
        {
            sequence = 0;
            isDirectory = false;

            if (BinaryPrimitives.ReadUInt32LittleEndian(record) != FileMagic)
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
                file = volume.GetNtfsFile(new FileReference((ulong)recordNumber, sequence), compareSequence: false);
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

            var state = Assess(data, out long size);
            return new DeletedEntry(recordNumber, sequence, name.Name, name.ParentFolder, false,
                size, info?.FileAltered ?? name.FileAltered, state, null);
        }

        /// <summary>
        /// A deleted file's clusters are marked free in $Bitmap. If some of them are allocated again,
        /// another file now owns them and their content has most likely been replaced.
        /// (The reverse isn't a guarantee: a cluster can be reused and freed again in between.)
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
            {
                for (long cluster = run.StartingCluster!.Value; cluster < run.StartingCluster + run.Length; cluster++)
                {
                    total++;
                    if (cluster >= volume.BootSector.TotalClusters || volume.IsClusterUsed(cluster))
                        reused++;
                }
            }

            if (total == 0 || reused == 0)
                return Recoverability.Intact;
            return reused == total ? Recoverability.Overwritten : Recoverability.Partial;
        }
    }

    /// <summary>
    /// Rebuilds the folder a deleted file lived in. The parent may still exist (normal path lookup),
    /// or may itself be deleted - in which case its name is taken from the deleted folder records we scanned.
    /// </summary>
    internal sealed class DeletedPathResolver
    {
        private readonly NtfsVolume _volume;
        private readonly Dictionary<long, DeletedEntry> _deletedFolders;
        private readonly Dictionary<FileReference, string> _cache = new();

        public DeletedPathResolver(NtfsVolume volume, List<DeletedEntry> entries)
        {
            _volume = volume;
            _deletedFolders = entries.Where(e => e.IsDirectory).ToDictionary(e => e.RecordNumber);
        }

        public string GetFolder(DeletedEntry entry) => entry.State == Recoverability.Unreadable
            ? "<unknown>"
            : Resolve(entry.Parent, 0);

        private string Resolve(FileReference folder, int depth)
        {
            if (_cache.TryGetValue(folder, out var cached))
                return cached;

            string path;
            if (depth > 64)
            {
                path = "<too deep>";
            }

            else if (TryLiveFolder(folder, out var livePath))
            {
                path = livePath;
            }

            // Deleting bumps the record's sequence number by one, so the parent reference stored in
            // its children is exactly one behind.
            else if (_deletedFolders.TryGetValue((long)folder.FileRecordId, out var deleted)
                     && (ushort)(folder.SequenceNumber + 1) == deleted.SequenceNumber)
            {
                path = $"{Resolve(deleted.Parent, depth + 1)}/{deleted.Name} [deleted]";
            }

            else
            {
                path = $"<lost folder #{folder.FileRecordId}>";
            }

            _cache[folder] = path;
            return path;
        }

        private bool TryLiveFolder(FileReference folder, out string path)
        {
            path = "";
            try
            {
                if (folder.FileRecordId == (ulong)KnownMftRecord.Root)
                    return true; // path stays "" so children become "/name"

                // Sequence numbers must match - otherwise the record now belongs to a different folder.
                var file = _volume.GetNtfsFile(folder, compareSequence: true);
                if (!file.RecordHeader.Flags.HasFlag(FileRecordFlags.IsInUse | FileRecordFlags.IsDirectory))
                    return false;

                path = _volume.GetPrimaryAbsolutePath(folder);
                return !path.StartsWith("??");
            }

            catch
            {
                return false;
            }
        }
    }
}
