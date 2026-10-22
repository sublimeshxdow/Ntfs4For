using System.Text;
using Nternal.Core;
using Nternal.Ntfs;
using Nternal.Ntfs.Attributes;
using Nternal.Ntfs.Generic;

namespace SymlinkViewer;

/// <summary>
///     Lists every symbolic link (and junction) on an NTFS volume by reading the $Extend\$Reparse:$R index,
///     instead of walking the whole directory tree. Optionally checks where each link points and whether
///     that target exists on the same volume.
/// </summary>
internal class Program
{
    private const string Usage =
        """
        Usage: SymlinkViewer <source> [options]

        Lists all symbolic links and junctions of an NTFS volume, using the $Extend\$Reparse:$R index.

          <source>          a drive letter on Windows (e.g. C:), or a device / image path (e.g. /dev/sdb1, disk.img)

        Options:
          --tree            show the links as a directory tree
          --csv             print comma-separated values
          --dot             print a Graphviz graph (render with: dot -Tsvg links.dot -o links.svg)
          --symlinks-only   leave out junctions / mount points
          --all             include every reparse point (WOF, cloud files, app execution aliases, ...)
          --letter <X>      drive letter this volume is mounted as, used to resolve absolute targets
                            (default: the drive letter of <source>, if one was given)
          --no-check        don't check whether link targets exist
          -h, --help        show this help
        """;

    private static int Main(string[] args)
    {
        var format = OutputFormat.List;
        bool includeJunctions = true, includeAll = false, check = true;
        char? letter = null;
        string? source = null;

        for (var i = 0; i < args.Length; i++)
            switch (args[i])
            {
                case "-h" or "--help":
                    Console.WriteLine(Usage);
                    return 0;
                case "--tree": format = OutputFormat.Tree; break;
                case "--csv": format = OutputFormat.Csv; break;
                case "--dot": format = OutputFormat.Dot; break;
                case "--symlinks-only": includeJunctions = false; break;
                case "--all": includeAll = true; break;
                case "--no-check": check = false; break;
                case "--letter" when i + 1 < args.Length && TryParseDriveLetter(args[i + 1], out var l):
                    letter = l;
                    i++;
                    break;
                default:
                    if (args[i].StartsWith('-') || source != null)
                        return Fail($"Unexpected argument \"{args[i]}\".");
                    source = args[i];
                    break;
            }

        if (source == null)
            return Fail("No source given.");

        if (letter == null && TryParseDriveLetter(source, out var sourceLetter))
            letter = sourceLetter;

        try
        {
            using var stream = new RawDriveStream(ResolveDevicePath(source));
            using var volume = new NtfsVolume(stream);
            volume.Mount(false);

            var collector = new LinkCollector(volume, letter, check);
            var links = new List<LinkInfo>();

            foreach (var (tag, reference) in volume.EnumerateIndexedSymLinks())
            {
                var wanted = tag == ReparseTag.SymbolicLink
                             || (includeJunctions && tag == ReparseTag.MountPoint)
                             || includeAll;
                if (wanted)
                    links.Add(collector.Collect(tag, reference));
            }

            links.Sort((a, b) => StringComparer.OrdinalIgnoreCase.Compare(a.Path, b.Path));

            switch (format)
            {
                case OutputFormat.List: PrintList(links); break;
                case OutputFormat.Tree: PrintTree(links); break;
                case OutputFormat.Csv: PrintCsv(links); break;
                case OutputFormat.Dot: PrintDot(links); break;
            }

            if (format is OutputFormat.List or OutputFormat.Tree)
                PrintSummary(links, letter, check);

            return 0;
        }

        catch (UnauthorizedAccessException ex)
        {
            Console.Error.WriteLine($"Failed to open {source}: {ex.Message}");
            Console.Error.WriteLine("Make sure you are running this program as administrator / root!");
        }

        catch (Exception ex)
        {
            Console.Error.WriteLine($"Failed to list the links of {source}: {ex.Message}");
        }

        return 1;
    }

    /// <summary>A one-line description for reparse points that aren't links.</summary>
    private static string Describe(ReparseAttribute reparse)
    {
        return reparse.Data switch
        {
            AppExecLinkReparseData app => $"app alias for {app.TargetPath ?? app.AppUserModelId ?? "?"}",
            LxSymlinkReparseData lx => $"WSL symlink to {lx.Target}",
            WofReparseData wof when wof.Provider == WofReparseData.WofProvider.File =>
                $"WOF-compressed ({wof.Algorithm}), data in :{WofReparseData.CompressedStreamName}",
            WofReparseData wof => $"WOF ({wof.Provider} provider)",
            _ when reparse.Tag.IsCloud() => "cloud file placeholder",
            _ => $"{reparse.Data.RawData.Length} bytes of reparse data" + (reparse.Guid is { } g ? $", GUID {g}" : "")
        };
    }

    private static string TagName(ReparseTag tag)
    {
        return Enum.IsDefined(tag) ? tag.ToString() : tag.IsCloud() ? "Cloud" : $"0x{tag.ToHex()}";
    }

    // -----------------------------------------------------------------------------------------
    // Output
    // -----------------------------------------------------------------------------------------

    private static bool IsLink(LinkInfo link)
    {
        return link.Tag is ReparseTag.SymbolicLink or ReparseTag.MountPoint;
    }

    private static string Kind(LinkInfo link)
    {
        return link.Tag switch
        {
            ReparseTag.SymbolicLink => link.IsDirectory ? "dir link" : "file link",
            ReparseTag.MountPoint => "junction",
            _ => TagName(link.Tag)
        };
    }

    private static string StatusText(LinkInfo link)
    {
        return link.Status switch
        {
            LinkStatus.Ok => "ok",
            LinkStatus.Dangling => "DANGLING",
            LinkStatus.External => "other volume",
            LinkStatus.Unreadable => "UNREADABLE",
            _ => ""
        };
    }

    private static void PrintList(List<LinkInfo> links)
    {
        if (links.Count == 0)
            return;

        var kindWidth = links.Max(l => Kind(l).Length);
        var pathWidth = Math.Min(links.Max(l => l.Path.Length), 70);

        foreach (var link in links)
        {
            var line = new StringBuilder();
            line.Append(Kind(link).PadRight(kindWidth)).Append("  ");
            line.Append(link.Path.PadRight(pathWidth));

            if (link.Target.Length > 0)
                line.Append(IsLink(link) ? "  →  " : "  :  ").Append(link.Target);

            var details = new[] { StatusText(link), link.Note }.Where(s => !string.IsNullOrEmpty(s));
            var detailText = string.Join(", ", details);
            if (detailText.Length > 0)
                line.Append("   [").Append(detailText).Append(']');

            WriteColored(line.ToString(), link.Status);
        }
    }

    /// <summary>Prints only the folders that lead to links, with each link as a leaf.</summary>
    private static void PrintTree(List<LinkInfo> links)
    {
        var root = new TreeNode("/");
        foreach (var link in links)
        {
            var node = root;
            foreach (var part in link.Path.Split('/', StringSplitOptions.RemoveEmptyEntries))
                node = node.Child(part);
            node.Link = link;
        }

        Console.WriteLine("/");
        PrintTreeNode(root, "");
    }

    private static void PrintTreeNode(TreeNode node, string indent)
    {
        var index = 0;
        foreach (var child in node.Children.Values)
        {
            var last = ++index == node.Children.Count;
            var branch = last ? "└── " : "├── ";

            if (child.Link is { } link)
            {
                var status = StatusText(link);
                var text = $"{indent}{branch}{child.Name}{(IsLink(link) ? "  →  " : "  :  ")}{link.Target}" +
                           (link.Tag == ReparseTag.SymbolicLink ? "" : $"  ({Kind(link)})") +
                           (status.Length > 0 && link.Status != LinkStatus.Ok ? $"  [{status}]" : "");
                WriteColored(text, link.Status);
            }
            else
            {
                Console.WriteLine($"{indent}{branch}{child.Name}/");
            }

            // A link folder can also contain further links when it's a real directory with a reparse point.
            if (child.Children.Count > 0)
                PrintTreeNode(child, indent + (last ? "    " : "│   "));
        }
    }

    private static void PrintCsv(List<LinkInfo> links)
    {
        Console.WriteLine("Tag,Kind,Path,Target,ResolvedOnVolume,Status,Note,FileRecord");
        foreach (var l in links)
            Console.WriteLine(string.Join(',',
                Csv(TagName(l.Tag)), Csv(Kind(l)), Csv(l.Path), Csv(l.Target), Csv(l.VolumeTarget ?? ""),
                Csv(l.Status.ToString()), Csv(l.Note ?? ""), l.Reference.FileRecordId));
    }

    private static string Csv(string value)
    {
        return value.IndexOfAny([',', '"', '\n', '\r']) >= 0 ? $"\"{value.Replace("\"", "\"\"")}\"" : value;
    }

    /// <summary>
    ///     A Graphviz graph: one node per link and per target, edges link → target.
    ///     Dangling targets are red, targets on other volumes grey.
    /// </summary>
    private static void PrintDot(List<LinkInfo> links)
    {
        Console.WriteLine("digraph links {");
        Console.WriteLine("  rankdir=LR;");
        Console.WriteLine("  node [shape=box, style=rounded, fontname=\"Segoe UI,Helvetica\", fontsize=10];");
        Console.WriteLine("  edge [fontname=\"Segoe UI,Helvetica\", fontsize=9];");

        var targetNodes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var link in links)
        {
            var linkShape = link.IsDirectory ? "folder" : "note";
            Console.WriteLine($"  {Dot(link.Path)} [shape={linkShape}, style=filled, fillcolor=\"#dbeafe\"];");

            if (!IsLink(link) || link.Target.Length == 0)
                continue;

            var targetId = link.VolumeTarget ?? link.Target;
            if (targetNodes.Add(targetId))
            {
                var style = link.Status switch
                {
                    LinkStatus.Dangling => "style=\"rounded,dashed\", color=\"#dc2626\", fontcolor=\"#dc2626\"",
                    LinkStatus.External => "style=\"rounded,filled\", fillcolor=\"#f3f4f6\", color=\"#9ca3af\"",
                    _ => ""
                };
                Console.WriteLine(
                    $"  {Dot(targetId)} [label={Dot(link.VolumeTarget ?? link.Target)}{(style.Length > 0 ? ", " + style : "")}];");
            }

            var edgeLabel = link.Tag == ReparseTag.MountPoint ? "junction" : link.Note ?? "";
            Console.WriteLine($"  {Dot(link.Path)} -> {Dot(targetId)}" +
                              (edgeLabel.Length > 0 ? $" [label={Dot(edgeLabel)}]" : "") + ";");
        }

        Console.WriteLine("}");
    }

    private static string Dot(string value)
    {
        return "\"" + value.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";
    }

    private static void PrintSummary(List<LinkInfo> links, char? letter, bool check)
    {
        Console.WriteLine();
        if (links.Count == 0)
        {
            Console.WriteLine("No matching reparse points found.");
            return;
        }

        var byKind = links.GroupBy(Kind).OrderByDescending(g => g.Count()).Select(g => $"{g.Count():N0} {g.Key}");
        Console.WriteLine($"{links.Count:N0} entries: {string.Join(", ", byKind)}");

        var dangling = links.Count(l => l.Status == LinkStatus.Dangling);
        var unreadable = links.Count(l => l.Status == LinkStatus.Unreadable);
        if (dangling > 0)
            Console.WriteLine($"{dangling:N0} dangling link(s)");
        if (unreadable > 0)
            Console.WriteLine($"{unreadable:N0} unreadable entr(y/ies)");

        if (check && letter == null &&
            links.Any(l => l.Status == LinkStatus.Unchecked && l.Target.Length > 1 && l.Target[1] == ':'))
            Console.WriteLine(
                "Absolute targets weren't checked - pass --letter <X> to say which drive letter this volume has.");
    }

    private static void WriteColored(string text, LinkStatus status)
    {
        ConsoleColor? color = status switch
        {
            LinkStatus.Dangling or LinkStatus.Unreadable => ConsoleColor.Red,
            LinkStatus.External => ConsoleColor.DarkGray,
            _ => null
        };

        if (color == null || Console.IsOutputRedirected)
        {
            Console.WriteLine(text);
            return;
        }

        Console.ForegroundColor = color.Value;
        Console.WriteLine(text);
        Console.ResetColor();
    }

    // -----------------------------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------------------------

    private static bool TryParseDriveLetter(string value, out char letter)
    {
        var trimmed = value.TrimEnd('\\', '/').TrimEnd(':');
        letter = trimmed.Length == 1 && char.IsAsciiLetter(trimmed[0]) ? char.ToUpperInvariant(trimmed[0]) : default;
        return letter != default;
    }

    private static string ResolveDevicePath(string source)
    {
        if (OperatingSystem.IsWindows() && TryParseDriveLetter(source, out var letter))
            return $@"\\.\{letter}:";

        return source;
    }

    private static int Fail(string message)
    {
        Console.Error.WriteLine(message);
        Console.Error.WriteLine("Use --help for usage.");
        return 2;
    }

    private enum OutputFormat
    {
        List,
        Tree,
        Csv,
        Dot
    }

    private enum LinkStatus
    {
        Ok, // target found on this volume
        Dangling, // target should be on this volume, but doesn't exist
        External, // target is on another drive, a UNC share or a volume GUID path
        Unchecked, // checking was disabled, or the target couldn't be resolved
        Unreadable // the $R entry points to a record that couldn't be read
    }

    private sealed record LinkInfo(
        ReparseTag Tag,
        FileReference Reference,
        string Path,
        bool IsDirectory,
        string Target,
        string? VolumeTarget,
        LinkStatus Status,
        string? Note);

    // -----------------------------------------------------------------------------------------
    // Collecting
    // -----------------------------------------------------------------------------------------

    private sealed class LinkCollector(NtfsVolume volume, char? volumeLetter, bool check)
    {
        // Many links share parent folders; caching their paths saves a lot of record reads.
        private readonly Dictionary<ulong, string> _folderPaths = new();
        private readonly FileReference _root = volume.GetKnownMftReference(KnownMftRecord.Root);

        public LinkInfo Collect(ReparseTag tag, FileReference reference)
        {
            NtfsFile file;
            string path;

            try
            {
                file = volume.GetNtfsFile(reference);
                path = GetPath(file);
            }

            catch (Exception ex)
            {
                // Index entries can outlive their file on an inconsistent volume.
                return new LinkInfo(tag, reference, $"<record {reference.FileRecordId}>", false, "", null,
                    LinkStatus.Unreadable, ex.Message);
            }

            var isDirectory = file.RecordHeader.Flags.HasFlag(FileRecordFlags.IsDirectory);
            ReparseAttribute? reparse;

            try
            {
                reparse = file.GetAttributeData<ReparseAttribute>();
            }

            catch (Exception ex)
            {
                return new LinkInfo(tag, reference, path, isDirectory, "", null, LinkStatus.Unreadable,
                    $"unreadable reparse data: {ex.Message}");
            }

            if (reparse == null)
                return new LinkInfo(tag, reference, path, isDirectory, "", null, LinkStatus.Unreadable,
                    "listed in $Reparse:$R, but the file has no $REPARSE_POINT attribute");

            if (reparse.Tag != tag)
                return new LinkInfo(tag, reference, path, isDirectory, "", null, LinkStatus.Unreadable,
                    $"$R lists tag {tag.ToHex()}, but the file has {reparse.Tag.ToHex()}");

            return reparse.Data switch
            {
                SymbolicLinkReparseData symlink => Resolve(tag, reference, path, isDirectory, symlink.PrintName,
                    symlink.IsRelative
                        ? ResolveRelative(path, symlink.SubstituteName)
                        : ResolveAbsolute(symlink.SubstituteName),
                    symlink.IsRelative),

                MountPointReparseData junction => Resolve(tag, reference, path, isDirectory, junction.PrintName,
                    ResolveAbsolute(junction.SubstituteName), false),

                _ => new LinkInfo(tag, reference, path, isDirectory, Describe(reparse), null, LinkStatus.Unchecked,
                    null)
            };
        }

        private LinkInfo Resolve(ReparseTag tag, FileReference reference, string path, bool isDirectory,
            string printName, (string? VolumePath, LinkStatus Status) resolved, bool isRelative)
        {
            var note = isRelative ? "relative" : null;
            var (volumePath, status) = resolved;

            if (volumePath != null && status == LinkStatus.Unchecked && check)
                status = Exists(volumePath) ? LinkStatus.Ok : LinkStatus.Dangling;

            return new LinkInfo(tag, reference, path, isDirectory, printName, volumePath, status, note);
        }

        /// <summary>Relative targets are relative to the folder containing the link.</summary>
        private static (string?, LinkStatus) ResolveRelative(string linkPath, string target)
        {
            var parts = new List<string>();
            var normalized = target.Replace('\\', '/');

            // "\foo" is relative to the root of the link's own volume.
            if (!normalized.StartsWith('/'))
                parts.AddRange(linkPath.Split('/', StringSplitOptions.RemoveEmptyEntries)[..^1]);

            foreach (var part in normalized.Split('/', StringSplitOptions.RemoveEmptyEntries))
            {
                if (part == ".")
                    continue;
                if (part == "..")
                {
                    if (parts.Count > 0)
                        parts.RemoveAt(parts.Count - 1);
                    continue;
                }

                parts.Add(part);
            }

            return ("/" + string.Join('/', parts), LinkStatus.Unchecked);
        }

        /// <summary>
        ///     Absolute targets are NT paths like \??\C:\Dir, \??\UNC\server\share or \??\Volume{guid}\Dir.
        ///     Only drive-letter paths matching this volume's letter can be checked.
        /// </summary>
        private (string?, LinkStatus) ResolveAbsolute(string substituteName)
        {
            var target = substituteName.StartsWith(@"\??\") ? substituteName[4..] : substituteName;

            if (target.Length >= 2 && target[1] == ':' && char.IsAsciiLetter(target[0]))
            {
                if (volumeLetter == null)
                    return (null, LinkStatus.Unchecked);

                if (char.ToUpperInvariant(target[0]) != volumeLetter)
                    return (null, LinkStatus.External);

                var rest = target[2..].Replace('\\', '/').TrimEnd('/');
                return (rest.Length == 0 ? "/" : rest.StartsWith('/') ? rest : "/" + rest, LinkStatus.Unchecked);
            }

            return (null, LinkStatus.External);
        }

        private bool Exists(string volumePath)
        {
            try
            {
                volume.GetNtfsFile(volumePath);
                return true;
            }

            catch (FileNotFoundException)
            {
                return false;
            }

            catch (Exception)
            {
                // E.g. the path runs through an absolute symlink, which path lookup doesn't follow.
                // Treat as found rather than claiming the link is dangling.
                return true;
            }
        }

        private string GetPath(NtfsFile file)
        {
            var name = file.GetBestFittingName()
                       ?? throw new InvalidDataException("the record has no $FILE_NAME attribute");

            return CombinePath(GetFolderPath(name.ParentFolder, 0), name.Name);
        }

        private string GetFolderPath(FileReference folder, int depth)
        {
            if (folder.FileRecordId == _root.FileRecordId)
                return "/";

            if (_folderPaths.TryGetValue(folder.FileRecordId, out var cached))
                return cached;

            if (depth > 1024)
                throw new InvalidDataException("folder structure is too deep - possible loop");

            var file = volume.GetNtfsFile(folder);
            var name = file.GetBestFittingName()
                       ?? throw new InvalidDataException($"folder {folder.FileRecordId} has no $FILE_NAME attribute");

            var path = CombinePath(GetFolderPath(name.ParentFolder, depth + 1), name.Name);
            _folderPaths[folder.FileRecordId] = path;
            return path;
        }

        private static string CombinePath(string parent, string name)
        {
            return parent == "/" ? "/" + name : parent + "/" + name;
        }
    }

    private sealed class TreeNode(string name)
    {
        public string Name { get; } = name;
        public LinkInfo? Link { get; set; }
        public SortedDictionary<string, TreeNode> Children { get; } = new(StringComparer.OrdinalIgnoreCase);

        public TreeNode Child(string name)
        {
            if (!Children.TryGetValue(name, out var child))
                Children[name] = child = new TreeNode(name);
            return child;
        }
    }
}