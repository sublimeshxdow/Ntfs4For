using System.Text;
using Nternal.Core;
using Nternal.Ntfs;
using Nternal.Partitioning;
using Nternal.Partitioning.Gpt;
using Nternal.Partitioning.Mbr;

namespace PartitionViewer;

/// <summary>
///     Lists the MBR or GPT partitions of a disk (or disk image) using Nternal.Partitioning,
///     together with the file system found in each partition and a few sanity checks.
/// </summary>
internal class Program
{
    private const string Usage =
        """
        Usage: PartitionViewer [disk] [options]

        Lists the partitions of a disk or disk image (MBR, including logical partitions, or GPT).

          [disk]               a disk number or \\.\PhysicalDriveN on Windows, a device such as /dev/sda
                               on Linux, or a disk image file. Without it, all disks are listed.

        Options:
          -v, --verbose        show GUIDs, raw attributes and CHS addresses for every partition
          --no-fs              don't look inside partitions for file systems
          --sector-size <n>    override the logical sector size (512 or 4096); useful for disk images,
                               which can't report one. By default it's detected from the GPT header.
          -h, --help           show this help
        """;

    private static bool _verbose, _probeFileSystems = true;

    private static int Main(string[] args)
    {
        string? disk = null;
        int? sectorSize = null;

        for (var i = 0; i < args.Length; i++)
            switch (args[i])
            {
                case "-h" or "--help":
                    Console.WriteLine(Usage);
                    return 0;
                case "-v" or "--verbose": _verbose = true; break;
                case "--no-fs": _probeFileSystems = false; break;
                case "--sector-size" when i + 1 < args.Length && int.TryParse(args[i + 1], out var size)
                                                              && size is >= 512 and <= 65536 &&
                                                              (size & (size - 1)) == 0:
                    sectorSize = size;
                    i++;
                    break;
                default:
                    if (args[i].StartsWith('-') || disk != null)
                        return Fail($"Unexpected argument \"{args[i]}\".");
                    disk = args[i];
                    break;
            }

        var disks = disk != null ? [ResolveDiskPath(disk)] : EnumerateDisks();
        if (disks.Count == 0)
        {
            Console.Error.WriteLine("No disks found. Pass a device or image path explicitly.");
            return 1;
        }

        var allOk = true;
        foreach (var path in disks)
        {
            allOk &= PrintDisk(path, sectorSize);
            Console.WriteLine();
        }

        return allOk ? 0 : 1;
    }

    // -----------------------------------------------------------------------------------------
    // Disk level
    // -----------------------------------------------------------------------------------------

    private static bool PrintDisk(string path, int? sectorSizeOverride)
    {
        IDataStream? raw = null;

        try
        {
            raw = new RawDriveStream(path);
            var disk = raw;
            var sectorSource = "reported by the device";

            if (sectorSizeOverride is int forced && forced != raw.Alignment)
            {
                disk = new SectorSizeStream(raw, forced);
                sectorSource = "--sector-size";
            }
            else if (sectorSizeOverride == null && DetectGptSectorSize(raw) is int detected &&
                     detected != raw.Alignment)
            {
                disk = new SectorSizeStream(raw, detected);
                sectorSource = "detected from the GPT header";
            }

            var sectorSize = disk.Alignment;
            var diskSectors = (ulong)(disk.Length / sectorSize);

            Console.WriteLine($"Disk {path}");
            Console.WriteLine($"  Size              {FormatSize(disk.Length)} ({diskSectors:N0} sectors)");
            Console.WriteLine($"  Sector size       {sectorSize} bytes ({sectorSource})");

            var mbr = new MasterBootRecord(disk, false);

            // NTFS, FAT and exFAT boot sectors also end in 0xAA55, so a file system written directly to the disk
            // (a "superfloppy", common on USB sticks) would otherwise be misread as an MBR with garbage entries.
            var sector0 = new byte[Math.Max(disk.Alignment, 512)];
            disk.ReadExactly(0, sector0);
            var isSuperfloppy = !mbr.IsGptProtective && FileSystemProbe.Identify(sector0) is not null;

            if (!mbr.IsValid || isSuperfloppy)
            {
                Console.WriteLine(isSuperfloppy
                    ? "  Partition table   none (sector 0 is a file system boot sector)"
                    : "  Partition table   none (no 0xAA55 signature in sector 0)");
                // A "superfloppy": a file system directly on the disk, without any partition table.
                var whole = new Partition("-", 0, diskSectors - 1, "whole disk");
                if (_probeFileSystems)
                    Probe(disk, whole);
                if (whole.FileSystem != null)
                    Console.WriteLine($"  File system       {DescribeFileSystem(whole)} (no partition table)");
                return true;
            }

            var warnings = new List<string>();
            List<Partition> partitions;
            ulong firstUsable, lastUsable;

            if (mbr.IsGptProtective)
                (partitions, firstUsable, lastUsable) = ReadGpt(disk, mbr, diskSectors, warnings);
            else
                (partitions, firstUsable, lastUsable) = ReadMbr(disk, mbr, diskSectors, warnings);

            if (_probeFileSystems)
                foreach (var partition in partitions.Where(p => !p.IsContainer))
                    Probe(disk, partition);

            Check(partitions, firstUsable, lastUsable, sectorSize, warnings);

            Console.WriteLine();
            PrintTable(partitions, sectorSize);
            Console.WriteLine();
            PrintMap(partitions, diskSectors, firstUsable, lastUsable);
            PrintFreeSpace(partitions, firstUsable, lastUsable, sectorSize);

            if (warnings.Count > 0)
            {
                Console.WriteLine();
                foreach (var warning in warnings)
                    WriteColored($"  ! {warning}", ConsoleColor.Yellow);
            }

            return true;
        }

        catch (UnauthorizedAccessException ex)
        {
            Console.WriteLine($"Disk {path}: {ex.Message}");
            Console.WriteLine("  Make sure you are running this program as administrator / root!");
        }

        catch (Exception ex)
        {
            Console.WriteLine($"Disk {path}: failed to read the partition table: {ex.Message}");
        }

        finally
        {
            raw?.Dispose();
        }

        return false;
    }

    /// <summary>
    ///     Disk images always report 512-byte sectors. A GPT written for 4096-byte sectors has its header at
    ///     byte 4096 instead of 512, so looking for "EFI PART" in both places tells us the real sector size.
    /// </summary>
    private static int? DetectGptSectorSize(IDataStream disk)
    {
        Span<byte> buffer = stackalloc byte[8];
        foreach (var candidate in new[] { disk.Alignment, 512, 4096 }.Distinct())
            try
            {
                var sector = new byte[Math.Max(candidate, disk.Alignment)];
                disk.ReadExactly(candidate, sector);
                if (Encoding.ASCII.GetString(sector, 0, 8) == GptHeader.ExpectedSignature)
                    return candidate;
            }

            catch
            {
                // Too small to contain that offset, or unreadable: not this one.
            }

        return null;
    }

    // -----------------------------------------------------------------------------------------
    // GPT
    // -----------------------------------------------------------------------------------------

    private static (List<Partition>, ulong FirstUsable, ulong LastUsable) ReadGpt(
        IDataStream disk, MasterBootRecord mbr, ulong diskSectors, List<string> warnings)
    {
        // Parse strictly first: that validates both header CRCs and the partition array CRC.
        // If that fails, report why and fall back to a lenient parse so the table can still be shown.
        GptPartitioning gpt;
        string? validationError = null;

        try
        {
            gpt = new GptPartitioning();
            gpt.ParseFromStream(disk);
        }

        catch (Exception ex)
        {
            validationError = ex.Message;
            gpt = new GptPartitioning(false);
            gpt.ParseFromStream(disk);
        }

        var header = gpt.Header;

        // When the primary header is damaged, Header is the backup one, whose own LBA is the disk's last sector
        // and whose "copy" LBA points back to LBA 1 - so take each location from the header that describes it.
        var backupLba = gpt.BackupHeader?.LbaOfHeader ?? gpt.MainHeader?.LbaOfHeaderCopy ?? header.LbaOfHeaderCopy;
        var allValid = validationError == null && gpt.MainHeader != null && gpt.BackupHeader != null;

        Console.WriteLine($"  Partition table   GPT, revision {gpt.RevisionString}");
        Console.WriteLine($"  Disk GUID         {header.DiskGuid.ToString().ToUpperInvariant()}");
        Console.WriteLine($"  Usable sectors    {header.FirstUsableLba:N0} - {header.LastUsableLba:N0}");
        Console.WriteLine(
            $"  Partition array   {header.NumberOfPartitionEntries} entries x {header.SizeOfPartitionEntry} bytes " +
            $"at LBA {header.LbaStartOfPartitionEntries:N0}{(gpt.MainHeader == null ? " (backup copy)" : "")}");
        Console.WriteLine($"  Primary header    {(gpt.MainHeader != null ? "ok" : "INVALID")} (LBA 1)");
        Console.WriteLine(
            $"  Backup header     {(gpt.BackupHeader != null ? "ok" : "INVALID or missing")} (LBA {backupLba:N0})");
        Console.WriteLine($"  Checksums         {(allValid ? "ok" : "FAILED")}");

        if (validationError != null)
            warnings.Add($"Strict GPT validation failed: {validationError}");
        if (gpt.MainHeader == null)
            warnings.Add("The primary GPT header is damaged; the table was read from the backup header.");
        if (gpt.BackupHeader == null)
            warnings.Add("The backup GPT header is damaged or missing.");
        if (backupLba != diskSectors - 1)
            warnings.Add($"The backup header is expected at the last sector ({diskSectors - 1:N0}), but is at " +
                         $"{backupLba:N0}. The disk was probably resized without updating the GPT.");

        var others = mbr.PartitionEntries
            .Where(e => e.PartitionType is not (MbrPartitionType.Empty or MbrPartitionType.GptProtective)).ToList();
        if (others.Count > 0)
            warnings.Add(
                $"Hybrid MBR: besides the protective entry, the MBR also defines {others.Count} partition(s), " +
                "which BIOS-based systems will use instead of the GPT.");

        var partitions = new List<Partition>();
        var number = 0;
        foreach (var entry in gpt.GptPartitionEntries)
        {
            var partition = new Partition((++number).ToString(), entry.StartingLba, entry.InclusiveEndingLba,
                KnownTypes.GptTypeName(entry.TypeGuid))
            {
                Name = entry.Name,
                Flags = DescribeGptFlags((ulong)entry.Flags, KnownTypes.UsesMicrosoftAttributes(entry.TypeGuid))
            };

            partition.Details.Add($"Partition GUID  {entry.PartitionGuid.ToString().ToUpperInvariant()}");
            partition.Details.Add($"Type GUID       {entry.TypeGuid.ToString().ToUpperInvariant()}");
            partition.Details.Add($"Attributes      0x{(ulong)entry.Flags:X16}");
            partitions.Add(partition);
        }

        return (partitions, header.FirstUsableLba, header.LastUsableLba);
    }

    private static string DescribeGptFlags(ulong flags, bool microsoftAttributes)
    {
        var names = new List<string>();
        if ((flags & (1UL << 0)) != 0) names.Add("required");
        if ((flags & (1UL << 1)) != 0) names.Add("no block I/O");
        if ((flags & (1UL << 2)) != 0) names.Add("legacy BIOS bootable");

        // Bits 48-63 belong to the partition type. These meanings are Microsoft's, for basic data
        // partitions; Windows also sets them on recovery partitions (e.g. "no drive letter").
        var typeSpecific = flags >> 48;
        if (microsoftAttributes)
        {
            if ((flags & (1UL << 60)) != 0) names.Add("read-only");
            if ((flags & (1UL << 61)) != 0) names.Add("shadow copy");
            if ((flags & (1UL << 62)) != 0) names.Add("hidden");
            if ((flags & (1UL << 63)) != 0) names.Add("no drive letter");
            typeSpecific &= ~0xF000UL;
        }

        if (typeSpecific != 0)
            names.Add($"type bits 0x{typeSpecific:X4}");

        var reserved = flags & 0x0000_FFFF_FFFF_FFF8;
        if (reserved != 0)
            names.Add($"reserved bits 0x{reserved:X}");

        return string.Join(", ", names);
    }

    // -----------------------------------------------------------------------------------------
    // MBR
    // -----------------------------------------------------------------------------------------

    private static (List<Partition>, ulong FirstUsable, ulong LastUsable) ReadMbr(
        IDataStream disk, MasterBootRecord mbr, ulong diskSectors, List<string> warnings)
    {
        Console.WriteLine("  Partition table   MBR");
        Console.WriteLine($"  Disk signature    {mbr.DiskSignature:X8}");

        var partitions = new List<Partition>();
        var active = 0;

        for (var i = 0; i < MasterBootRecord.NumberOfBootEntries; i++)
        {
            var entry = mbr.PartitionEntries[i];
            if (entry.PartitionType == MbrPartitionType.Empty || entry.SizeInSectors == 0)
                continue;

            if (entry.Bootable == BootState.Bootable)
                active++;
            if (entry.Bootable == BootState.Invalid)
                warnings.Add($"Partition {i + 1} has an invalid boot indicator (neither 0x00 nor 0x80).");

            var partition = new Partition((i + 1).ToString(), entry.StartingLba,
                (ulong)entry.StartingLba + entry.SizeInSectors - 1, KnownTypes.MbrTypeName(entry.PartitionType))
            {
                Flags = entry.Bootable == BootState.Bootable ? "active" : "",
                IsContainer = entry.PartitionType.IsExtended()
            };
            AddChsDetails(partition, entry);
            partitions.Add(partition);

            if (partition.IsContainer)
                ReadLogicalPartitions(disk, partition, partitions, warnings);
        }

        if (active > 1)
            warnings.Add($"{active} partitions are marked active; BIOS boot code expects at most one.");

        // MBR has no "usable area" header; everything after the MBR sector is usable.
        return (partitions, 1, diskSectors - 1);
    }

    /// <summary>
    ///     Logical partitions live in a chain of extended boot records (EBRs) inside the extended partition.
    ///     Each EBR has the MBR layout: entry 1 is the logical partition (relative to this EBR),
    ///     entry 2 points to the next EBR (relative to the start of the extended partition).
    /// </summary>
    private static void ReadLogicalPartitions(IDataStream disk, Partition extended, List<Partition> partitions,
        List<string> warnings)
    {
        var ebrLba = extended.StartingLba;
        var visited = new HashSet<ulong>();
        var number = 5;
        var sector = new byte[disk.Alignment];

        while (visited.Add(ebrLba))
        {
            if (visited.Count > 256)
            {
                warnings.Add("The EBR chain is suspiciously long; stopped after 256 entries.");
                return;
            }

            if (ebrLba > extended.InclusiveEndingLba)
            {
                warnings.Add($"An EBR at LBA {ebrLba:N0} lies outside its extended partition.");
                return;
            }

            disk.ReadExactly((long)ebrLba * disk.Alignment, sector);
            var ebr = new MasterBootRecord(sector.AsSpan(0, MasterBootRecord.MbrSize), false);
            if (!ebr.IsValid)
            {
                warnings.Add(
                    $"The EBR at LBA {ebrLba:N0} has no 0xAA55 signature; the logical partition chain ends there.");
                return;
            }

            var logical = ebr.PartitionEntries[0];
            if (logical.PartitionType != MbrPartitionType.Empty && logical.SizeInSectors > 0)
            {
                var start = ebrLba + logical.StartingLba;
                var partition = new Partition((number++).ToString(), start, start + logical.SizeInSectors - 1,
                    KnownTypes.MbrTypeName(logical.PartitionType))
                {
                    Flags = logical.Bootable == BootState.Bootable ? "active" : "",
                    IsLogical = true
                };
                partition.Details.Add($"Logical, EBR at LBA {ebrLba:N0}");
                AddChsDetails(partition, logical);
                partitions.Add(partition);
            }

            var next = ebr.PartitionEntries[1];
            if (next.PartitionType == MbrPartitionType.Empty || !next.PartitionType.IsExtended())
                return;

            ebrLba = extended.StartingLba + next.StartingLba;
        }

        warnings.Add($"The EBR chain loops back to LBA {ebrLba:N0}.");
    }

    private static void AddChsDetails(Partition partition, MbrPartitionEntry entry)
    {
        partition.Details.Add(
            $"CHS start       {entry.ChsStart.Cylinder}/{entry.ChsStart.Head}/{entry.ChsStart.Sector}");
        partition.Details.Add($"CHS end         {entry.ChsEnd.Cylinder}/{entry.ChsEnd.Head}/{entry.ChsEnd.Sector}");
    }

    // -----------------------------------------------------------------------------------------
    // File systems and checks
    // -----------------------------------------------------------------------------------------

    private static void Probe(IDataStream disk, Partition partition)
    {
        try
        {
            var sectorSize = disk.Alignment;
            var length = (long)Math.Min(FileSystemProbe.ProbeSize, partition.Sectors * (ulong)sectorSize);
            length -= length % sectorSize;
            if (length == 0)
                return;

            var start = new byte[length];
            disk.ReadExactly((long)partition.StartingLba * sectorSize, start);
            partition.FileSystem = FileSystemProbe.Identify(start);

            if (partition.FileSystem == "NTFS")
                partition.VolumeLabel = TryReadNtfsLabel(disk, partition);
        }

        catch (Exception ex)
        {
            partition.FileSystem = "(unreadable)";
            partition.Details.Add($"Probe failed    {ex.Message}");
        }
    }

    /// <summary>Mounts the partition with Nternal.Ntfs to read its volume label.</summary>
    private static string? TryReadNtfsLabel(IDataStream disk, Partition partition)
    {
        try
        {
            var partitionStream = new PartitionStream(disk, partition);
            // PartitionStream only accepts sector-aligned reads; NTFS parsing does smaller ones.
            using var unaligned = new UnalignedDataStream(partitionStream);
            using var volume = new NtfsVolume(unaligned);
            volume.Mount(false);
            return volume.VolumeLabel;
        }

        catch (Exception ex)
        {
            partition.Details.Add($"NTFS mount      failed: {ex.Message}");
            return null;
        }
    }

    private static void Check(List<Partition> partitions, ulong firstUsable, ulong lastUsable, int sectorSize,
        List<string> warnings)
    {
        var real = partitions.Where(p => !p.IsContainer).OrderBy(p => p.StartingLba).ToList();

        foreach (var p in partitions)
        {
            if (p.StartingLba < firstUsable || p.InclusiveEndingLba > lastUsable)
                warnings.Add($"Partition {p.Number} ({p.StartingLba:N0} - {p.InclusiveEndingLba:N0}) extends outside " +
                             $"the usable area ({firstUsable:N0} - {lastUsable:N0}).");

            if (!p.IsContainer && p.StartingLba * (ulong)sectorSize % 4096 != 0)
                warnings.Add(
                    $"Partition {p.Number} isn't 4 KiB aligned, which slows down disks with 4 KiB physical sectors.");
        }

        for (var i = 1; i < real.Count; i++)
        for (var j = 0; j < i; j++)
            if (real[i].StartingLba <= real[j].InclusiveEndingLba && real[j].StartingLba <= real[i].InclusiveEndingLba)
                warnings.Add($"Partitions {real[j].Number} and {real[i].Number} overlap.");
    }

    // -----------------------------------------------------------------------------------------
    // Output
    // -----------------------------------------------------------------------------------------

    private static void PrintTable(List<Partition> partitions, int sectorSize)
    {
        if (partitions.Count == 0)
        {
            Console.WriteLine("  No partitions.");
            return;
        }

        string[] header = ["#", "Start LBA", "End LBA", "Size", "Align", "Type", "File system", "Name", "Flags"];
        var rows = partitions.Select(p => new[]
        {
            p.IsLogical ? "  " + p.Number : p.Number,
            p.StartingLba.ToString("N0"),
            p.InclusiveEndingLba.ToString("N0"),
            FormatSize((long)(p.Sectors * (ulong)sectorSize)),
            p.IsContainer ? "" : Alignment(p.StartingLba * (ulong)sectorSize),
            p.Type,
            p.IsContainer ? "(logical partitions)" : DescribeFileSystem(p),
            p.Name ?? "",
            p.Flags
        }).ToList();

        var widths = header.Select((h, i) => Math.Max(h.Length, rows.Max(r => r[i].Length))).ToArray();
        bool[] rightAligned = [false, true, true, true, false, false, false, false, false];

        // Leave out columns that are empty for every row (e.g. Name on MBR disks).
        var visible = Enumerable.Range(0, header.Length).Where(i => i < 4 || rows.Any(r => r[i].Length > 0)).ToArray();

        string Format(string[] cells)
        {
            return "  " + string.Join("  ", visible.Select(i =>
                rightAligned[i] ? cells[i].PadLeft(widths[i]) : cells[i].PadRight(widths[i]))).TrimEnd();
        }

        Console.WriteLine(Format(header));
        Console.WriteLine("  " + string.Join("  ", visible.Select(i => new string('-', widths[i]))));

        for (var r = 0; r < rows.Count; r++)
        {
            Console.WriteLine(Format(rows[r]));
            if (_verbose)
                foreach (var detail in partitions[r].Details)
                    Console.WriteLine($"       {detail}");
        }
    }

    private static string Alignment(ulong byteOffset)
    {
        if (byteOffset % (1024 * 1024) == 0) return "1 MiB";
        if (byteOffset % 4096 == 0) return "4 KiB";
        return $"{byteOffset % 4096} B off";
    }

    private static string DescribeFileSystem(Partition p)
    {
        return p.FileSystem == null ? "" :
            p.VolumeLabel is { Length: > 0 } label ? $"{p.FileSystem} \"{label}\"" : p.FileSystem;
    }

    /// <summary>
    ///     A proportional bar of the disk: digits/letters are partitions, '.' is free space,
    ///     '#' is space outside the usable area (partition table structures).
    /// </summary>
    private static void PrintMap(List<Partition> partitions, ulong diskSectors, ulong firstUsable, ulong lastUsable)
    {
        const int width = 64;
        if (diskSectors == 0)
            return;

        var real = partitions.Where(p => !p.IsContainer).ToList();

        // The partition's own number where it fits in one character, then letters (a = 10, b = 11, ...).
        const string symbols = "0123456789abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ";

        char Symbol(int index)
        {
            return int.TryParse(real[index].Number, out var n) && n < symbols.Length ? symbols[n] : '+';
        }

        var map = new char[width];
        for (var c = 0; c < width; c++)
        {
            var sector = (ulong)((c + 0.5) / width * diskSectors);
            var owner = real.FindIndex(p => sector >= p.StartingLba && sector <= p.InclusiveEndingLba);
            map[c] = owner >= 0 ? Symbol(owner)
                : sector < firstUsable || sector > lastUsable ? '#'
                : '.';
        }

        // Small partitions would otherwise disappear between two cells; give each at least its start cell.
        for (var i = 0; i < real.Count; i++)
        {
            var cell = (int)Math.Min(width - 1, (double)real[i].StartingLba / diskSectors * width);
            if (!map.Contains(Symbol(i)))
                map[cell] = Symbol(i);
        }

        Console.WriteLine($"  [{new string(map)}]");
        var legend = real.Select((p, i) => (Symbol: Symbol(i), p.Number)).Where(x => x.Symbol.ToString() != x.Number)
            .Select(x => $"{x.Symbol}={x.Number}");
        Console.WriteLine(
            $"   digits = partition numbers  {string.Join("  ", legend.Append(".=free").Append("#=reserved"))}");
    }

    private static void PrintFreeSpace(List<Partition> partitions, ulong firstUsable, ulong lastUsable, int sectorSize)
    {
        const ulong minimumBytes = 1024 * 1024; // ignore the usual 1 MiB alignment gaps
        var gaps = new List<(ulong Start, ulong End)>();
        var cursor = firstUsable;

        foreach (var p in partitions.Where(p => !p.IsContainer).OrderBy(p => p.StartingLba))
        {
            if (p.StartingLba > cursor)
                gaps.Add((cursor, p.StartingLba - 1));
            cursor = Math.Max(cursor, p.InclusiveEndingLba + 1);
        }

        if (cursor <= lastUsable)
            gaps.Add((cursor, lastUsable));

        var large = gaps.Where(g => (g.End - g.Start + 1) * (ulong)sectorSize > minimumBytes).ToList();
        if (large.Count == 0)
            return;

        Console.WriteLine();
        Console.WriteLine("  Unpartitioned space:");
        foreach (var (start, end) in large)
            Console.WriteLine(
                $"    {start,15:N0} - {end,15:N0}  {FormatSize((long)((end - start + 1) * (ulong)sectorSize)),10}");
    }

    // -----------------------------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------------------------

    private static List<string> EnumerateDisks()
    {
        var disks = new List<string>();

        if (OperatingSystem.IsWindows())
        {
            for (var i = 0; i < 32; i++)
            {
                var path = $@"\\.\PhysicalDrive{i}";
                try
                {
                    using var handle = File.OpenHandle(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                    disks.Add(path);
                }

                catch (UnauthorizedAccessException)
                {
                    disks.Add(path); // exists; PrintDisk will explain the missing rights
                }

                catch
                {
                    // No such disk.
                }
            }
        }
        else if (OperatingSystem.IsLinux() && Directory.Exists("/sys/block"))
        {
            string[] skip = ["loop", "ram", "zram", "dm-", "md", "sr", "fd", "nbd"];
            foreach (var dir in Directory.GetDirectories("/sys/block").Order())
            {
                var name = Path.GetFileName(dir);
                if (!skip.Any(name.StartsWith) && File.Exists($"/dev/{name}"))
                    disks.Add($"/dev/{name}");
            }
        }

        return disks;
    }

    private static string ResolveDiskPath(string disk)
    {
        if (OperatingSystem.IsWindows() && int.TryParse(disk, out var number))
            return $@"\\.\PhysicalDrive{number}";

        return disk;
    }

    private static string FormatSize(long bytes)
    {
        string[] units = ["B", "KiB", "MiB", "GiB", "TiB", "PiB"];
        double size = bytes;
        var unit = 0;
        while (size >= 1024 && unit < units.Length - 1)
        {
            size /= 1024;
            unit++;
        }

        return unit == 0 ? $"{bytes} B" : $"{size:0.#} {units[unit]}";
    }

    private static void WriteColored(string text, ConsoleColor color)
    {
        if (Console.IsOutputRedirected)
        {
            Console.WriteLine(text);
            return;
        }

        Console.ForegroundColor = color;
        Console.WriteLine(text);
        Console.ResetColor();
    }

    private static int Fail(string message)
    {
        Console.Error.WriteLine(message);
        Console.Error.WriteLine("Use --help for usage.");
        return 2;
    }
}