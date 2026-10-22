using Nternal.Ntfs;
using Nternal.Ntfs.Generic;
using Nternal.Ntfs.NtfsExtensions.Journal;

namespace UsnJournalViewer;

/// <summary>
///     Journal entries only store the parent folder's reference, so resolve (and remember) folder paths.
///     If the folder was deleted or its record reused since, the path can't be recovered - show the record id instead.
/// </summary>
internal sealed class ParentPathCache(NtfsVolume volume)
{
    private readonly Dictionary<FileReference, string> _cache = new();

    public string Get(FileReference parent)
    {
        if (_cache.TryGetValue(parent, out var path))
            return path;

        try
        {
            path = volume.GetPrimaryAbsolutePath(parent);
            if (path.StartsWith("??"))
                path = $"<#{parent.FileRecordId}>";
        }

        catch
        {
            path = $"<#{parent.FileRecordId}>";
        }

        _cache[parent] = path;
        return path;
    }

    internal static string FormatReason(UsnReason reason)
    {
        // Drop "Close" from the display, it is on nearly every final record and just adds noise
        var withoutClose = reason & ~UsnReason.Close;
        var text = withoutClose == 0 ? "" : withoutClose.ToString().Replace(", ", "|");
        if (reason.HasFlag(UsnReason.Close))
            text += text.Length > 0 ? " (closed)" : "Close";
        return text;
    }

    internal static string ResolveDevicePath(string source)
    {
        if (OperatingSystem.IsWindows() && source.TrimEnd(':').Length == 1 && char.IsLetter(source[0]))
            return $@"\\.\{char.ToUpperInvariant(source[0])}:";

        return source;
    }
}