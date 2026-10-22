using System.Security.Cryptography;
using System.Text;
using Nternal.Core;
using Nternal.Ntfs;
using Nternal.Ntfs.Attributes;
using Nternal.Ntfs.Generic;

namespace Ntfs4Testing;

/// <summary>
///     "verify" mode: mounts a volume with Nternal and compares what the library sees (directory
///     enumeration, file sizes, SHA-256 of file contents) against a manifest.csv written by
///     New-NtfsTestMatrix.ps1 / ntfs-test-matrix.sh - i.e. against what the operating system saw.
///     Exit codes: 0 = everything matched, 1 = mismatches (or the volume failed to mount), 2 = usage / I/O error.
/// </summary>
internal static class Verify
{
    private const int HashBufferSize = 1 << 20;

    public static int Run(string[] args)
    {
        string? device = null, manifestPath = null;
        long offset = 0;
        var maxReport = 50;
        var skipHash = false;

        for (var i = 0; i < args.Length; i++)
            switch (args[i])
            {
                case "verify" when i == 0: break; // tolerate being forwarded the whole command line
                case "--device" when i + 1 < args.Length: device = args[++i]; break;
                case "--offset" when i + 1 < args.Length: offset = long.Parse(args[++i]); break;
                case "--manifest" when i + 1 < args.Length: manifestPath = args[++i]; break;
                case "--max-report" when i + 1 < args.Length: maxReport = int.Parse(args[++i]); break;
                case "--no-hash": skipHash = true; break;
                default:
                    Console.Error.WriteLine($"Unknown or incomplete argument: {args[i]}");
                    return Usage();
            }

        if (device == null || manifestPath == null)
            return Usage();

        List<ManifestRow> manifest;
        try
        {
            manifest = LoadManifest(manifestPath);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Could not read manifest \"{manifestPath}\": {ex.Message}");
            return 2;
        }

        var problems = new List<string>();
        void Problem(string message)
        {
            problems.Add(message);
        }

        long hashed = 0, lengthChecked = 0, skipped = 0, viaLink = 0;
        var enumerated = 0;

        try
        {
            using var raw = new RawDriveStream(device);
            using IDataStream source = offset > 0 ? new OffsetStream(raw, offset) : new PassThrough(raw);
            using var volume = new NtfsVolume(source);

            try
            {
                volume.Mount();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"FAIL: volume failed to mount: {ex.GetType().Name}: {ex.Message}");
                return 1;
            }

            // ---- 1. enumerate the whole tree through the library (never descending into reparse points)
            var found = new Dictionary<string, FolderEntry>(StringComparer.Ordinal);
            var reparseDirs = new List<string>();
            var stack = new Stack<(FileReference Folder, string Path)>();
            stack.Push((volume.GetNtfsFile("/").GetReference(), ""));

            while (stack.Count > 0)
            {
                var (folder, folderPath) = stack.Pop();
                List<FolderEntry> entries;
                try
                {
                    entries = volume.EnumerateDirectory(folder).ToList();
                }
                catch (Exception ex)
                {
                    Problem($"cannot enumerate \"{(folderPath.Length == 0 ? "/" : folderPath)}\": {ex.GetType().Name}: {ex.Message}");
                    continue;
                }

                foreach (var entry in entries)
                {
                    if (entry.Reference.FileRecordId == folder.FileRecordId)
                        continue; // the "." entry of the root

                    if (folderPath.Length == 0 && IsIgnoredAtRoot(entry.Name))
                        continue;

                    var path = folderPath + "/" + entry.Name;
                    if (!found.TryAdd(path, entry))
                    {
                        Problem($"listed twice: {path}");
                        continue;
                    }

                    var isDirectory = entry.Flags.HasFlag(FileFlags.Directory);
                    var isReparse = entry.Flags.HasFlag(FileFlags.ReparsePoint);
                    if (isDirectory && isReparse)
                        reparseDirs.Add(path + "/");
                    else if (isDirectory)
                        stack.Push((entry.Reference, path));
                }
            }

            enumerated = found.Count;

            // ---- 2. compare against the manifest
            var inManifest = new HashSet<string>(StringComparer.Ordinal);
            foreach (var row in manifest)
            {
                if (reparseDirs.Any(prefix => row.Path.StartsWith(prefix, StringComparison.Ordinal)))
                {
                    // The OS followed a link here; the library intentionally does not.
                    viaLink++;
                    continue;
                }

                inManifest.Add(row.Path);
                if (!found.TryGetValue(row.Path, out var entry))
                {
                    Problem($"missing from listing: {row.Path}");
                    continue;
                }

                var isDirectory = entry.Flags.HasFlag(FileFlags.Directory);
                var isReparse = entry.Flags.HasFlag(FileFlags.ReparsePoint);
                var linkRow = row.Sha256 == "SYMLINK";

                if (!isReparse && !linkRow && isDirectory != row.IsDirectory)
                    Problem($"type mismatch: {row.Path} (library: {(isDirectory ? "directory" : "file")}, manifest: {(row.IsDirectory ? "directory" : "file")})");

                if (row.IsDirectory || isDirectory || isReparse || linkRow ||
                    row.Sha256.Length == 0 || row.Sha256 == "UNREADABLE")
                {
                    skipped++;
                    continue;
                }

                try
                {
                    var file = volume.GetNtfsFile(entry.Reference);
                    using var data = file.GetDataStream();

                    lengthChecked++;
                    if (data.Length != row.Length)
                    {
                        Problem($"length mismatch: {row.Path} (library {data.Length}, manifest {row.Length})");
                        continue;
                    }

                    // Encrypted data is ciphertext without the user's key, the manifest holds plaintext hashes.
                    if (skipHash || entry.Flags.HasFlag(FileFlags.Encrypted))
                        continue;

                    var hash = Sha256Of(data);
                    hashed++;
                    if (!string.Equals(hash, row.Sha256, StringComparison.OrdinalIgnoreCase))
                        Problem($"content mismatch: {row.Path} (library {hash[..12]}..., manifest {row.Sha256[..Math.Min(12, row.Sha256.Length)]}...)");
                }
                catch (Exception ex)
                {
                    Problem($"cannot read {row.Path}: {ex.GetType().Name}: {ex.Message}");
                }
            }

            foreach (var path in found.Keys)
                if (!inManifest.Contains(path))
                    Problem($"extra in listing (not in manifest): {path}");
        }
        catch (UnauthorizedAccessException ex)
        {
            Console.Error.WriteLine($"Cannot open {device}: {ex.Message} (run as administrator / root)");
            return 2;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"FAIL: unexpected {ex.GetType().Name}: {ex.Message}");
            return 1;
        }

        Console.WriteLine(
            $"Manifest entries: {manifest.Count}, listed by library: {enumerated}, sizes checked: {lengthChecked}, " +
            $"hashed: {hashed}, skipped (dirs/links/EFS/unhashable): {skipped}, under followed links: {viaLink}");

        if (problems.Count == 0)
        {
            Console.WriteLine("PASS");
            return 0;
        }

        foreach (var p in problems.Take(maxReport))
            Console.WriteLine("  " + p);
        if (problems.Count > maxReport)
            Console.WriteLine($"  ... and {problems.Count - maxReport} more");

        Console.WriteLine($"FAIL: {problems.Count} problem(s)");
        return 1;
    }

    private static int Usage()
    {
        Console.Error.WriteLine("""
            Usage: Ntfs4Testing verify --device <path> --manifest <manifest.csv> [--offset <bytes>] [--no-hash] [--max-report N]

              --device    raw volume or disk: /dev/loopN (offset 0), \\.\PhysicalDriveN + --offset, or an image path
              --offset    byte offset of the NTFS volume inside --device (default 0)
              --manifest  manifest.csv written by New-NtfsTestMatrix.ps1 / ntfs-test-matrix.sh
              --no-hash   only compare listing, types and sizes
            Exit code: 0 = match, 1 = mismatch, 2 = usage / I/O error
            """);
        return 2;
    }

    private static bool IsIgnoredAtRoot(string name)
    {
        return name.StartsWith('$') || name == "System Volume Information" || name == "lost+found";
    }

    private static string Sha256Of(IDataStream stream)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var buffer = new byte[HashBufferSize];
        long position = 0;
        var length = stream.Length;
        while (position < length)
        {
            var toRead = (int)Math.Min(buffer.Length, length - position);
            var read = stream.Read(position, buffer.AsSpan(0, toRead));
            if (read <= 0)
                throw new IOException($"Unexpected end of stream at {position} of {length}");

            hash.AppendData(buffer, 0, read);
            position += read;
        }

        return Convert.ToHexString(hash.GetHashAndReset());
    }

    // ------------------------------------------------------------------ manifest

    private sealed record ManifestRow(string Path, bool IsDirectory, long Length, string Sha256);

    private static List<ManifestRow> LoadManifest(string file)
    {
        var records = ParseCsv(File.ReadAllText(file));
        if (records.Count == 0)
            throw new InvalidDataException("manifest is empty");

        var header = records[0];
        int Col(string name)
        {
            var i = header.FindIndex(h => string.Equals(h.Trim(), name, StringComparison.OrdinalIgnoreCase));
            return i >= 0 ? i : throw new InvalidDataException($"manifest has no \"{name}\" column");
        }

        int cPath = Col("Path"), cDir = Col("IsDirectory"), cLen = Col("Length"), cHash = Col("Sha256");

        // Windows manifests use backslashes, Linux ones use slashes.
        var windowsStyle = records.Skip(1).Any(r => r.Count > cPath && r[cPath].StartsWith('\\'));

        var rows = new List<ManifestRow>();
        foreach (var r in records.Skip(1))
        {
            if (r.Count <= Math.Max(Math.Max(cPath, cDir), Math.Max(cLen, cHash)))
                continue;

            var path = windowsStyle ? r[cPath].Replace('\\', '/') : r[cPath];
            rows.Add(new ManifestRow(path, bool.Parse(r[cDir]), long.Parse(r[cLen]), r[cHash]));
        }

        return rows;
    }

    /// <summary>Minimal RFC-4180 parser (quoted fields, doubled quotes, CRLF or LF).</summary>
    private static List<List<string>> ParseCsv(string text)
    {
        var rows = new List<List<string>>();
        var row = new List<string>();
        var field = new StringBuilder();
        var inQuotes = false;

        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];
            if (inQuotes)
            {
                if (c == '"')
                {
                    if (i + 1 < text.Length && text[i + 1] == '"')
                    {
                        field.Append('"');
                        i++;
                    }
                    else
                    {
                        inQuotes = false;
                    }
                }
                else
                {
                    field.Append(c);
                }

                continue;
            }

            switch (c)
            {
                case '"': inQuotes = true; break;
                case ',':
                    row.Add(field.ToString());
                    field.Clear();
                    break;
                case '\r': break;
                case '\n':
                    row.Add(field.ToString());
                    field.Clear();
                    rows.Add(row);
                    row = new List<string>();
                    break;
                default: field.Append(c); break;
            }
        }

        if (field.Length > 0 || row.Count > 0)
        {
            row.Add(field.ToString());
            rows.Add(row);
        }

        return rows;
    }

    // ------------------------------------------------------------------ stream helpers

    /// <summary>Does not own the wrapped stream - lets the caller control its lifetime.</summary>
    private sealed class PassThrough : IDataStream
    {
        private readonly IDataStream _inner;

        public PassThrough(IDataStream inner)
        {
            _inner = inner;
        }

        public long Length => _inner.Length;
        public long AllocatedLength => _inner.AllocatedLength;
        public int Alignment => _inner.Alignment;

        public void LoadIntoMemory()
        {
            _inner.LoadIntoMemory();
        }

        public int Read(long position, Span<byte> buffer)
        {
            return _inner.Read(position, buffer);
        }

        public void ReadExactly(long position, Span<byte> buffer)
        {
            _inner.ReadExactly(position, buffer);
        }

        public bool TryGetMemory(out Span<byte> memory)
        {
            return _inner.TryGetMemory(out memory);
        }

        public void Dispose()
        {
        }
    }

    /// <summary>A window into an underlying device that starts at a fixed byte offset (e.g. a partition).</summary>
    private sealed class OffsetStream : IDataStream
    {
        private readonly IDataStream _inner;
        private readonly long _offset;

        public OffsetStream(IDataStream inner, long offset)
        {
            if (offset < 0 || offset % inner.Alignment != 0)
                throw new ArgumentException(
                    $"Offset {offset} must be non-negative and a multiple of the sector size ({inner.Alignment}).",
                    nameof(offset));

            _inner = inner;
            _offset = offset;
        }

        public long Length => _inner.Length - _offset;
        public long AllocatedLength => Length;
        public int Alignment => _inner.Alignment;

        public void LoadIntoMemory()
        {
            throw new NotSupportedException();
        }

        public int Read(long position, Span<byte> buffer)
        {
            ArgumentOutOfRangeException.ThrowIfNegative(position);
            if (position >= Length || buffer.Length == 0)
                return 0;

            var count = (int)Math.Min(buffer.Length, Length - position);
            return _inner.Read(_offset + position, buffer[..count]);
        }

        public void ReadExactly(long position, Span<byte> buffer)
        {
            var total = 0;
            while (total < buffer.Length)
            {
                var read = Read(position + total, buffer[total..]);
                if (read == 0)
                    throw new EndOfStreamException(
                        $"Could not read {buffer.Length} bytes at {position}; only {total} were available.");

                total += read;
            }
        }

        public bool TryGetMemory(out Span<byte> memory)
        {
            memory = default;
            return false;
        }

        public void Dispose()
        {
        }
    }
}
