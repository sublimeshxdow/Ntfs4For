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
}
