using Nternal.Core;
using Nternal.Ntfs;
using Nternal.Ntfs.NtfsExtensions.Journal;

namespace UsnJournalViewer;

internal class Program
{
    private static int Main(string[] args)
    {
        string? source = null, nameFilter = null;
        UsnReason reasonFilter = 0;
        var last = 0;
        bool closeOnly = false, invalid = false;

        for (var i = 0; i < args.Length; i++)
            switch (args[i])
            {
                case "--name" when i + 1 < args.Length: nameFilter = args[++i]; break;
                case "--last" when i + 1 < args.Length: last = int.Parse(args[++i]); break;
                case "--close": closeOnly = true; break;
                case "--reason" when i + 1 < args.Length:
                    foreach (var part in args[++i].Split(','))
                        reasonFilter |= Enum.Parse<UsnReason>(part.Trim(), true);
                    break;
                default:
                    invalid |= source != null;
                    source = args[i];
                    break;
            }

        if (source == null || invalid)
        {
            Console.WriteLine(
                "Usage: UsnJournalViewer <source> [--name text] [--reason FileCreate,FileDelete,...] [--close] [--last N]");
            Console.WriteLine("  <source>   a drive letter on Windows (e.g. C:), or a device / image path");
            Console.WriteLine("  --name     only show entries whose file name contains this text");
            Console.WriteLine("  --reason   only show entries with any of these reasons");
            Console.WriteLine($"             ({string.Join(", ", Enum.GetNames<UsnReason>())})");
            Console.WriteLine(
                "  --close    only show the final record of each change (USN_REASON_CLOSE) - much less noise");
            Console.WriteLine("  --last N   only show the newest N matching entries");
            return 1;
        }

        return DumpJournal(source, nameFilter, reasonFilter, closeOnly, last) ? 0 : 1;
    }

    /// <summary>
    ///     Prints the change journal ($Extend\$UsnJrnl:$J) - a log of every create, delete, rename and write
    ///     on the volume, which survives long after the files themselves are gone.
    /// </summary>
    public static bool DumpJournal(string source, string? nameFilter, UsnReason reasonFilter, bool closeOnly, int last)
    {
        try
        {
            using var stream = new RawDriveStream(ParentPathCache.ResolveDevicePath(source));
            using var volume = new NtfsVolume(stream);
            volume.Mount();

            UsnJournalReader journal;
            try
            {
                journal = volume.GetJournalReader();
            }

            catch (FileNotFoundException)
            {
                Console.WriteLine("This volume has no change journal ($Extend/$UsnJrnl). Windows creates it on system");
                Console.WriteLine(
                    "drives automatically; on others, run: fsutil usn createjournal m=33554432 a=4194304 D:");
                return false;
            }

            Console.WriteLine($"Journal id        {journal.UsnId:X16}");
            Console.WriteLine(
                $"Maximum size      {journal.MaximumSize / 1048576.0:F1} MB (+{journal.AllocationDelta / 1048576.0:F1} MB delta)");
            Console.WriteLine($"Lowest valid USN  {journal.LowestValidUsn:N0}");
            Console.WriteLine();

            var paths = new ParentPathCache(volume);
            var entries = journal.GetJournalEntries()
                .Where(e => reasonFilter == 0 || (e.Reason & reasonFilter) != 0)
                .Where(e => !closeOnly || e.Reason.HasFlag(UsnReason.Close))
                .Where(e => nameFilter == null ||
                            (e is BasicUsnEntry b &&
                             b.FileName.Contains(nameFilter, StringComparison.OrdinalIgnoreCase)));

            if (last > 0)
                entries = entries.TakeLast(last);

            var shown = 0;
            Console.WriteLine($"{"Timestamp (UTC)",-24}{"USN",14}  {"Record",-12}{"Reason",-40}Path");
            foreach (var entry in entries)
            {
                shown++;
                switch (entry)
                {
                    case BasicUsnEntry basic:
                        var path = $"{paths.Get(basic.ParentFileReference)}/{basic.FileName}";
                        Console.WriteLine($"{basic.TimeStamp,-24:yyyy-MM-dd HH:mm:ss.fff}{basic.Usn,14:N0}  " +
                                          $"#{basic.FileReference.FileRecordId,-11}{ParentPathCache.FormatReason(basic.Reason),-40}{path}");
                        break;

                    case ExtentUsnEntry extent:
                        // V4 records track which byte ranges changed; they carry no name or timestamp.
                        var ranges = string.Join(", ",
                            extent.Extents.Take(3).Select(x => $"{x.Offset:N0}+{x.Length:N0}"));
                        Console.WriteLine($"{"(range record)",-24}{extent.Usn,14:N0}  " +
                                          $"#{extent.FileReference.FileRecordId,-11}{ParentPathCache.FormatReason(extent.Reason),-40}{ranges}" +
                                          (extent.Extents.Length > 3 ? ", ..." : ""));
                        break;
                }
            }

            Console.WriteLine();
            Console.WriteLine($"{shown:N0} entries shown.");
            return true;
        }

        catch (UnauthorizedAccessException ex)
        {
            Console.WriteLine($"Failed to open {source}: {ex.Message}");
            Console.WriteLine("Make sure you are running this program as administrator / root!");
        }

        catch (Exception ex)
        {
            Console.WriteLine($"Failed to read the change journal: {ex.Message}");
        }

        return false;
    }
}