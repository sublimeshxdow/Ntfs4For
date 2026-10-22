# PartitionViewer

Lists the partitions of a disk or disk image (MBR, including logical partitions, or GPT) using `Nternal.Partitioning`, shows the file system found in each partition, and runs a few sanity checks such as 4 KiB alignment.

## Usage

```
PartitionViewer [disk] [options]
```

`[disk]` is a disk number or `\\.\PhysicalDriveN` on Windows, a device such as `/dev/sda` on Linux, or a disk image file. Without it, all disks are listed. Raw disk access needs administrator rights on Windows; on Linux use `sudo` or join the `disk` group.

| Option | Meaning |
| --- | --- |
| `-v, --verbose` | Show GUIDs, raw attributes and CHS addresses for every partition. |
| `--no-fs` | Do not look inside partitions for file systems. |
| `--sector-size <n>` | Override the logical sector size (512 or 4096). Useful for images, which cannot report one. By default it is detected from the GPT header. |
| `-h, --help` | Show help. |

```bash
dotnet run -c Release --project Examples/PartitionViewer -- /dev/sda -v
```

## API used

`Nternal.Partitioning.Mbr`, `Nternal.Partitioning.Gpt`, `PartitionStream`, and `NtfsVolume` to identify NTFS partitions.

## Notes

- Sector size matters. A 4Kn disk image read as 512-byte sectors shows nonsense, pass `--sector-size 4096`.
- Exit code is non-zero if any disk could not be read.
