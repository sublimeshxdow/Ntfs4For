using Nternal.Core;
using Nternal.Ntfs;
using Nternal.Ntfs.Attributes;
using Nternal.Ntfs.Generic;
using Nternal.Ntfs.Security;
using Nternal.Ntfs.Streams;

namespace FileInspector;

internal class Program
{
    // ---- helpers ----

    private static readonly Dictionary<string, string> WellKnownSids = new()
    {
        ["S-1-1-0"] = "Everyone",
        ["S-1-3-0"] = "CREATOR OWNER",
        ["S-1-3-1"] = "CREATOR GROUP",
        ["S-1-5-11"] = "Authenticated Users",
        ["S-1-5-18"] = "SYSTEM",
        ["S-1-5-19"] = "LOCAL SERVICE",
        ["S-1-5-20"] = "NETWORK SERVICE",
        ["S-1-5-32-544"] = "Administrators",
        ["S-1-5-32-545"] = "Users",
        ["S-1-5-32-546"] = "Guests",
        ["S-1-15-2-1"] = "ALL APPLICATION PACKAGES"
    };

    private static int Main(string[] args)
    {
        if (args.Length != 2)
        {
            Console.WriteLine("Usage: FileInspector <source> <path | #record>");
            Console.WriteLine("  <source>  a drive letter on Windows (e.g. C:), or a device / image path");
            Console.WriteLine("  <path>    file or folder on the volume, e.g. /Windows/notepad.exe");
            Console.WriteLine("  #record   or an MFT record number, e.g. #5 for the root folder");
            return 1;
        }

        return Inspect(args[0], args[1]) ? 0 : 1;
    }

    /// <summary>
    ///     Prints everything the MFT knows about one file: record header, both sets of timestamps,
    ///     every attribute (with cluster runs for non-resident ones), hard links and the security descriptor.
    /// </summary>
    public static bool Inspect(string source, string target)
    {
        try
        {
            using var stream = new RawDriveStream(ResolveDevicePath(source));
            using var volume = new NtfsVolume(stream);
            volume.Mount(false);

            var file = target.StartsWith('#')
                ? volume.GetNtfsFile(new FileReference(ulong.Parse(target[1..]), 0), false)
                : volume.GetNtfsFile(target.Replace('\\', '/'));

            var record = file.RecordHeader;
            var reference = file.GetReference();

            Section("Record");
            Row("Path", SafePath(volume, reference));
            Row("Record number", $"{reference.FileRecordId:N0}");
            Row("Sequence number", $"{record.SequenceNumber}");
            Row("Flags", DescribeRecordFlags(record.Flags));
            Row("Hard links", $"{record.NumberOfHardlinks}");
            Row("$LogFile LSN", $"{record.Lsn}");
            Row("Record usage", $"{record.RealSizeOfRecord} / {record.AllocatedSizeOfRecord} bytes");

            PrintTimestamps(file);
            PrintAttributes(file);
            PrintSecurity(volume, file);
            PrintEAs(volume, file);
            return true;
        }

        catch (UnauthorizedAccessException ex)
        {
            Console.WriteLine($"Failed to open {source}: {ex.Message}");
            Console.WriteLine("Make sure you are running this program as administrator / root!");
        }

        catch (Exception ex)
        {
            Console.WriteLine($"Failed to inspect {target}: {ex.Message}");
        }

        return false;
    }

    private static void PrintTimestamps(NtfsFile file)
    {
        var si = file.GetAttributeData<StandardInformationAttribute>();
        var names = file.GetAttributeDataArray<FileNameAttribute>()
            .Select(x => x.FileNameData)
            .ToArray();

        Section("Timestamps (UTC)");
        Console.WriteLine($"  {"",-20}{"Created",-24}{"Modified",-24}{"MFT changed",-24}{"Accessed",-24}");
        if (si != null)
            TimeRow("$STANDARD_INFO", si.FileCreation, si.FileAltered, si.MftChanged, si.FileRead);
        foreach (var fn in names)
            TimeRow($"$FILE_NAME ({ShortNamespace(fn.FileNamespace)})", fn.FileCreation, fn.FileAltered, fn.MftChanged,
                fn.FileRead);

        // $STANDARD_INFORMATION can be changed from user mode (SetFileTime), $FILE_NAME can't.
        // A creation time in $SI that is *earlier* than in $FN is a classic sign of timestomping.
        if (si != null && names.Any(fn => si.FileCreation < fn.FileCreation))
            Console.WriteLine(
                "  ! $STANDARD_INFO creation time predates $FILE_NAME - timestamps may have been altered.");

        if (si != null)
        {
            Section("Standard information");
            Row("File attributes", $"{si.Attributes}");
            if (si.SecurityId != null) Row("Security id", $"{si.SecurityId}");
            if (si.OwnerId != null) Row("Owner id", $"{si.OwnerId}");
            if (si.UpdateSequenceNumber != null) Row("Last USN", $"{si.UpdateSequenceNumber}");
        }

        if (names.Length > 0)
        {
            Section("Names");
            foreach (var fn in names)
                Console.WriteLine($"  [{fn.FileNamespace,-11}] {fn.Name}  (parent #{fn.ParentFolder.FileRecordId})");
        }
    }

    private static void PrintAttributes(NtfsFile file)
    {
        Section("Attributes");
        Console.WriteLine($"  {"Type",-22}{"Name",-20}{"Where",-14}{"Size",14}  Flags");

        foreach (var attribute in file.NtfsAttributes)
        {
            var name = attribute.Name.Length > 0 ? attribute.Name : "-";
            var where = attribute.IsNonResident ? "non-resident" : "resident";
            var size = TryGetLength(attribute);
            var flags = attribute.Flags == NtfsAttributeFlags.None ? "" : attribute.Flags.ToString();

            Console.WriteLine($"  {attribute.Type,-22}{Truncate(name, 19),-20}{where,-14}{size,14}  {flags}");

            if (attribute.Accessor is NonResidentDataStream nonResident)
                PrintRuns(nonResident, attribute.Flags.HasFlag(NtfsAttributeFlags.Compressed));
        }
    }

    private static void PrintRuns(NonResidentDataStream stream, bool compressed)
    {
        // In a compressed stream, the sparse runs are the unused tail of each compression unit, not holes.
        var sparseLabel = compressed ? "(compression unit padding)" : "sparse (reads as zeros)";

        var runs = stream.Extents.SelectMany(e => e.DataRuns).ToList();
        var fragments = runs.Count(r => !r.Sparse);
        Console.WriteLine($"  {"",-22}{runs.Count} run(s), {fragments} fragment(s) on disk:");

        const int MaxRunsShown = 8;
        long vcn = 0;
        foreach (var run in runs.Take(MaxRunsShown))
        {
            var location = run.Sparse
                ? sparseLabel
                : $"LCN {run.StartingCluster:N0} - {run.StartingCluster + run.Length - 1:N0}";
            Console.WriteLine($"  {"",-24}VCN {vcn,-10:N0} {run.Length,10:N0} cluster(s)  {location}");
            vcn += run.Length;
        }

        if (runs.Count > MaxRunsShown)
            Console.WriteLine($"  {"",-24}... {runs.Count - MaxRunsShown} more");
    }

    private static void PrintSecurity(NtfsVolume volume, NtfsFile file)
    {
        SecurityDescriptor? descriptor;
        try
        {
            descriptor = volume.GetSecurityDescriptor(file);
        }

        catch (Exception ex)
        {
            Section("Security");
            Console.WriteLine($"  <could not read security descriptor: {ex.Message}>");
            return;
        }

        Section("Security");
        if (descriptor == null)
        {
            Console.WriteLine("  (no security descriptor)");
            return;
        }

        Row("Owner", DescribeSid(descriptor.Owner));
        Row("Group", DescribeSid(descriptor.Group));
        Row("Control", $"{descriptor.Control}");

        if (descriptor.Dacl == null)
        {
            Console.WriteLine("  DACL: none (everyone has full access)");
            return;
        }

        Console.WriteLine($"  DACL ({descriptor.Dacl.Aces.Count} entries):");
        foreach (var ace in descriptor.Dacl.Aces)
        {
            var inherited = ace.Flags.HasFlag(AceFlags.Inherited) ? " (inherited)" : "";
            Console.WriteLine($"    {ace.Type,-14} {DescribeSid(ace.Sid),-40} {ace.AccessMask}{inherited}");
        }
    }

    private static void PrintEAs(NtfsVolume volume, NtfsFile file)
    {
        Section("EAs");
        var ea = file.GetAttributeData<EaAttribute>();
        if (ea == null)
        {
            Console.WriteLine("<No EAs present>");
            return;
        }

        foreach (var entry in ea.EaEntries)
            Console.WriteLine($"  EA: {entry.Name} [Flags: {entry.Flags}]: {entry.Value.Length} bytes");
    }

    private static string DescribeSid(Sid? sid)
    {
        if (sid == null)
            return "(none)";

        var text = sid.ToString();
        if (WellKnownSids.TryGetValue(text, out var name))
            return $"{name} ({text})";
        if (text.StartsWith("S-1-5-80-"))
            return $"NT SERVICE\\... ({text})";
        return text;
    }

    private static string SafePath(NtfsVolume volume, FileReference reference)
    {
        try
        {
            var path = volume.GetPrimaryAbsolutePath(reference);
            return path.Length > 0 ? path : "/";
        }

        catch (Exception ex)
        {
            return $"<unresolvable: {ex.Message}>";
        }
    }

    private static string DescribeRecordFlags(FileRecordFlags flags)
    {
        var parts = new List<string>
        {
            flags.HasFlag(FileRecordFlags.IsInUse) ? "in use" : "DELETED (record is free)"
        };

        if (flags.HasFlag(FileRecordFlags.IsDirectory))
            parts.Add("directory");
        return string.Join(", ", parts);
    }

    private static string TryGetLength(NtfsAttribute attribute)
    {
        try
        {
            return $"{attribute.Accessor.Length:N0}";
        }
        catch
        {
            return "?";
        }
    }

    private static string ShortNamespace(FileNameNamespace ns)
    {
        return ns switch
        {
            FileNameNamespace.Win32AndDos => "Win32+DOS",
            _ => ns.ToString()
        };
    }

    private static string Truncate(string text, int max)
    {
        return text.Length <= max ? text : text[..(max - 1)] + "…";
    }

    private static void Section(string title)
    {
        Console.WriteLine();
        Console.WriteLine(title);
    }

    private static void Row(string label, string value)
    {
        Console.WriteLine($"  {label,-20}{value}");
    }

    private static void TimeRow(string label, params DateTime[] times)
    {
        Console.WriteLine($"  {label,-20}" + string.Concat(times.Select(t => $"{t,-24:yyyy-MM-dd HH:mm:ss.fff}")));
    }

    private static string ResolveDevicePath(string source)
    {
        if (OperatingSystem.IsWindows() && source.TrimEnd(':').Length == 1 && char.IsLetter(source[0]))
            return $@"\\.\{char.ToUpperInvariant(source[0])}:";

        return source;
    }
}