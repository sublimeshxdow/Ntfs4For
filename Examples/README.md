# Examples

Small command-line tools built on the public API of `Nternal.Ntfs` and `Nternal.Partitioning`. Each one is a single-purpose program you can read in a few minutes and copy from.

| Example | What it does |
| --- | --- |
| [VolumeInfo](VolumeInfo/README.md) | Geometry, metadata, used/free space and bad clusters of a volume. |
| [DirectoryTree](DirectoryTree/README.md) | Recursive folder tree from the `$I30` indexes. |
| [FileExtractor](FileExtractor/README.md) | Copy a file or alternate stream out of a volume, even if locked. |
| [FileInspector](FileInspector/README.md) | Full dump of one file's record, attributes, runs and security. |
| [MftExporter](MftExporter/README.md) | Export `$MFT` to a file (Windows only). |
| [SymlinkViewer](SymlinkViewer/README.md) | List symlinks and junctions via the `$Reparse` index. |
| [UsnJournalViewer](UsnJournalViewer/README.md) | Read the change journal with resolved paths. |
| [DeletedFileRecovery](DeletedFileRecovery/README.md) | Find, judge and recover deleted files. |
| [PartitionViewer](PartitionViewer/README.md) | MBR/GPT partition layout and file systems. |

## Common conventions

- `<source>` is a drive letter on Windows (for example `D:`) or a device / image path (`/dev/sdb1`, `disk.img`).
- Raw access needs administrator rights on Windows. On Linux use `sudo` or add yourself to the `disk` group and log in again.
- All examples are **read-only**, except where they write an output file you name (`FileExtractor`, `MftExporter`, `DeletedFileRecovery --recover`).
- Run any of them with `dotnet run -c Release --project Examples/<Name> -- <args>` from the repository root, or build the solution with `dotnet build Ntfs4For.slnx`.
- Prefer working on a dismounted volume or an image: a live volume changes while you read it.

## About these samples

Most examples were generated with AI assistance, based on the public API, and then reviewed and tested by the author (see [Disclaimer.txt](Disclaimer.txt)). They demonstrate the library, they are not hardened tools. If you find a bug, please open an issue.
