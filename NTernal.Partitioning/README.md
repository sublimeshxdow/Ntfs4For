# NTernal.Partitioning

Parses **MBR** and **GPT** partition tables and turns a partition entry into a stream you can hand to `NtfsVolume`. Target framework: **.NET 10**. Depends on [`NTernal.Core`](../NTernal.Core/README.md).

## Types

| Type | Purpose |
| --- | --- |
| `MasterBootRecord` | The four primary MBR entries (`PartitionEntries`), disk signature, boot signature check, `IsGptProtective`. |
| `MbrPartitionEntry`, `MbrPartitionType` | Boot flag, type (with an `IsExtended()` helper), CHS and LBA start, size. |
| `GptPartitioning` | `ParseFromStream(disk)`. Main and backup headers, `GptPartitionEntries`. |
| `GptHeader` | Revision, CRC-32 values, usable LBA range, disk GUID, partition array location. |
| `GptPartitionEntry`, `GptPartitionFlags` | Type and partition GUIDs, LBA range, flags, name, `IsUnused`. |
| `IPartitionEntry` | The common `StartingLba` / `InclusiveEndingLba` view. |
| `PartitionStream` | An `IDataStream` limited to one partition. |

The partitioning classes parse in strict ("paranoid") mode by default (signatures, header CRCs); pass `paranoid: false` for damaged disks.

## Usage

Find the NTFS volumes on a disk:

```csharp
using Nternal.Core;
using Nternal.Ntfs;
using Nternal.Partitioning;
using Nternal.Partitioning.Gpt;
using Nternal.Partitioning.Mbr;

using var disk = new RawDriveStream("/dev/sda");
var mbr = new MasterBootRecord(disk);

IEnumerable<PartitionStream> partitions;
if (mbr.IsGptProtective)
{
    var gpt = new GptPartitioning();
    gpt.ParseFromStream(disk);
    partitions = gpt.GptPartitionEntries.Where(p => !p.IsUnused).Select(p => p.CreatePartitionStream(disk));
}
else
{
    partitions = mbr.PartitionEntries
        .Where(p => p.SizeInSectors > 0 && !p.PartitionType.IsExtended())
        .Select(p => p.CreatePartitionStream(disk));
}

foreach (var partition in partitions)
{
    // PartitionStream only accepts sector-aligned reads; NTFS parsing does smaller ones.
    using var unaligned = new UnalignedDataStream(partition);
    using var volume = new NtfsVolume(unaligned);
    try { volume.Mount(false); Console.WriteLine(volume.VolumeLabel); }
    catch (Exception) { /* not NTFS */ }
}
```

For a complete tool that also reads logical partitions, checks alignment and probes file systems, see [`Examples/PartitionViewer`](../Examples/PartitionViewer/README.md).

## Limitations

- **Logical partitions inside an MBR extended partition (EBR chains) are not followed by the library.** `MasterBootRecord` exposes the four primary entries. `Examples/PartitionViewer` shows how to walk the extended partition yourself.
- The sector size must match the disk. A disk image does not report its own, so use a known value (the GPT parser takes it from the stream's `Alignment`); a 4Kn image read as 512-byte sectors gives nonsense.
- Read-only: no creating, resizing or repairing partition tables. Hybrid and unusual layouts (Apple, BSD disklabels, dynamic disks) are not handled.
- Validation is limited to the disks and images exercised by the examples; this project has seen far less testing than `Nternal`.
