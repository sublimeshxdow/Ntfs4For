using Nternal;
using Nternal.Attributes;
using Nternal.Core;
using Nternal.Ntfs;
using Nternal.Structs;

namespace DirectoryTree
{
    internal class Program
    {
        private static bool _exactSizes;
        private static bool _showMetaFiles;
        private static int _maxDepth = int.MaxValue;

        private static long _fileCount, _folderCount, _totalBytes;

        static int Main(string[] args)
        {
            var positional = new List<string>();
            for (int i = 0; i < args.Length; i++)
            {
                switch (args[i])
                {
                    case "--exact": _exactSizes = true; break;
                    case "--all": _showMetaFiles = true; break;
                    case "--depth" when i + 1 < args.Length: _maxDepth = int.Parse(args[++i]); break;
                    default: positional.Add(args[i]); break;
                }
            }

            if (positional.Count is < 1 or > 2)
            {
                Console.WriteLine("Usage: DirectoryTree <source> [path] [--depth N] [--exact] [--all]");
                Console.WriteLine("  <source>   a drive letter on Windows (e.g. D:), or a device / image path");
                Console.WriteLine("  [path]     folder to start at, e.g. /Users/Public (default: /)");
                Console.WriteLine("  --depth N  stop descending after N levels");
                Console.WriteLine("  --exact    read each file's record for its size instead of trusting the $I30 index");
                Console.WriteLine("  --all      also show NTFS metafiles ($MFT, $Bitmap, ...)");
                return 1;
            }

            return PrintTree(positional[0], positional.Count > 1 ? positional[1] : "/") ? 0 : 1;
        }

        public static bool PrintTree(string source, string path)
        {
            try
            {
                using var stream = new Win32PartitionStream(ResolveDevicePath(source));
                using var volume = new NtfsVolume(stream);

                // We are going to touch many records - keeping $MFT in memory pays off quickly.
                volume.Mount(loadMftIntoMemory: true);

                var root = volume.GetNtfsFile(path);
                Console.WriteLine(path);
                PrintFolder(volume, root.GetReference(), "", 1);

                Console.WriteLine();
                Console.WriteLine($"{_folderCount:N0} folders, {_fileCount:N0} files, {FormatSize(_totalBytes)}");
                return true;
            }

            catch (UnauthorizedAccessException ex)
            {
                Console.WriteLine($"Failed to open {source}: {ex.Message}");
                Console.WriteLine("Make sure you are running this program as administrator / root!");
            }

            catch (Exception ex)
            {
                Console.WriteLine($"Failed to list {path}: {ex.Message}");
            }

            return false;
        }

        private static void PrintFolder(NtfsVolume volume, FileReference folder, string indent, int depth)
        {
            List<FolderEntry> entries;
            try
            {
                entries = volume.EnumerateDirectory(folder)
                    // The root folder lists itself as "."
                    .Where(e => e.Reference.FileRecordId != folder.FileRecordId)
                    .Where(e => _showMetaFiles || !IsMetaFile(e))
                    .OrderByDescending(e => e.Flags.HasFlag(FileFlags.Directory))
                    .ThenBy(e => e.Name, StringComparer.OrdinalIgnoreCase)
                    .ToList();
            }

            catch (Exception ex)
            {
                Console.WriteLine($"{indent}└── <error: {ex.Message}>");
                return;
            }

            for (int i = 0; i < entries.Count; i++)
            {
                var entry = entries[i];
                bool last = i == entries.Count - 1;
                bool isFolder = entry.Flags.HasFlag(FileFlags.Directory);
                bool isReparse = entry.Flags.HasFlag(FileFlags.ReparsePoint);

                string line = $"{indent}{(last ? "└── " : "├── ")}{entry.Name}";
                if (isFolder)
                {
                    _folderCount++;
                    line += "/";
                }
                else
                {
                    _fileCount++;
                    long size = _exactSizes ? GetExactSize(volume, entry) : entry.RealSize;
                    _totalBytes += size;
                    line += $"  ({FormatSize(size)})";
                }

                if (isReparse)
                    line += $"  -> {DescribeReparseTarget(volume, entry)}";
                if (entry.Flags.HasFlag(FileFlags.Compressed))
                    line += "  [compressed]";
                if (entry.Flags.HasFlag(FileFlags.SparseFile))
                    line += "  [sparse]";

                Console.WriteLine(line);

                // Don't follow junctions / symlinks - they can point anywhere, including back up the tree.
                if (isFolder && !isReparse && depth < _maxDepth)
                    PrintFolder(volume, entry.Reference, indent + (last ? "    " : "│   "), depth + 1);
            }
        }

        private static long GetExactSize(NtfsVolume volume, FolderEntry entry)
        {
            try
            {
                var file = volume.GetNtfsFile(entry.Reference);
                using var data = file.GetDataStream();
                return data.Length;
            }

            catch
            {
                return entry.RealSize;
            }
        }

        private static string DescribeReparseTarget(NtfsVolume volume, FolderEntry entry)
        {
            try
            {
                var reparse = volume.GetNtfsFile(entry.Reference).GetAttributeData<ReparseAttribute>();
                return reparse?.Data switch
                {
                    SymbolicLinkReparseData link => link.PrintName.Length > 0 ? link.PrintName : link.SubstituteName,
                    MountPointReparseData mount => mount.PrintName.Length > 0 ? mount.PrintName : mount.SubstituteName,
                    _ => $"reparse point ({reparse?.TypeAndFlags})"
                };
            }

            catch (Exception ex)
            {
                return $"<unreadable reparse point: {ex.Message}>";
            }
        }

        private static bool IsMetaFile(FolderEntry entry)
            => entry.Reference.FileRecordId < 16 && entry.Name.StartsWith('$');

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
