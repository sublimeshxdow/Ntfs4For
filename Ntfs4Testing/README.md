# Ntfs4Testing

The author's development harness for Nternal. It is not part of the public API and its `Main` contains throwaway experiments, but it hosts the **`verify`** mode that powers the test matrix, which is the part worth reading.

## `verify`: compare a volume against a manifest

```
Ntfs4Testing verify --device <path> --manifest <manifest.csv> [--offset <bytes>] [--no-hash] [--max-report N]
```

| Option | Meaning |
| --- | --- |
| `--device` | Raw volume or disk: `/dev/loopN` (offset 0), `\\.\PhysicalDriveN` plus `--offset`, or an image path. |
| `--offset` | Byte offset of the NTFS volume inside `--device` (default 0). Must be a multiple of the sector size. |
| `--manifest` | `manifest.csv` written by the test scripts: `Path,IsDirectory,Length,Sha256`. |
| `--no-hash` | Compare only listing, types and sizes. |
| `--max-report N` | Print at most N problems (default 50). |

It mounts the volume with Nternal and then:

1. Walks the whole tree through `EnumerateDirectory`, without entering reparse points.
2. Compares the listing with the manifest and reports files that are missing from the listing or extra in it.
3. For every regular file compares the entry type, the data stream length and the SHA-256 of the contents.

Skipped on purpose: metafiles beginning with `$`, `System Volume Information`, `lost+found`, symbolic links and junctions (and manifest rows reached through a followed link), and the content of encrypted files.

Exit codes: **0** everything matched, **1** a mismatch or the volume failed to mount, **2** usage or I/O error.

## The test matrix

Two scripts create volumes, fill them, write the manifest and optionally call `verify` for each one. They are in `tests/`.

### Windows: `New-NtfsTestMatrix.ps1` (authoritative)

Creates VHDX files for every combination of logical sector size (512 / 4096), physical sector size (512 / 4096, with `-Include512e`), cluster size (512 B to 64 KiB) and file record size (1 KiB, plus 4 KiB with `-IncludeLargeFrs`). It reads the real geometry back with `fsutil`, fills each volume, requires a clean read-only `chkdsk`, writes the SHA-256 manifest, dismounts the volume and runs your parser.

```powershell
dotnet publish Ntfs4Testing -c Release -o C:\nt

Set-ExecutionPolicy -Scope Process -ExecutionPolicy Bypass -Force   # this session only

.\New-NtfsTestMatrix.ps1 -ListOnly
.\New-NtfsTestMatrix.ps1 -IncludeLargeFrs -Include512e -KeepVhds `
    -ParserCommand 'dotnet C:\nt\Ntfs4Testing.dll verify --device {PhysicalDrive} --offset {PartitionOffset} --manifest "{Manifest}"'
```

Run it from an elevated PowerShell with the Hyper-V module available. Useful parameters:

| Parameter | Meaning |
| --- | --- |
| `-VhdDirectory` | Where VHDX files and logs go (default `.\ntfs-tests`). |
| `-ExistingVhdPath` | Test one existing VHD/VHDX read-only, without formatting or deleting it. |
| `-LogicalSectorSizes`, `-ClusterSizes` | Restrict the matrix. |
| `-Include512e`, `-IncludeLargeFrs` | Add 512e disks and 4 KiB file records. |
| `-NameFilter '*cl65536*'` | Run only matching combinations. |
| `-ParserCommand` | Command per volume. Placeholders: `{RawVolume} {PhysicalDrive} {PartitionOffset} {DriveLetter} {VhdPath} {Manifest} {LogDir} {Name} {SectorSize} {ClusterSize} {FrsSize}`. Exit code 0 means pass. |
| `-KeepVhds` | Keep the VHDX files of passing runs too. Failing ones are always kept. |
| `-IncludeEfs` | Also create EFS-encrypted files (off by default, Nternal does not support encryption). |
| `-SkipChkdsk`, `-SkipDismount`, `-SizeBytes`, `-Fixed`, `-Populate:$false`, `-ListOnly` | As named. |

Combinations with a cluster smaller than the physical sector size are skipped, because Windows does not produce usable volumes for them.

The content it creates: a 3000-entry directory, unicode and collation-sensitive names, MFT churn with reused records, sizes around the resident limit, interleaved fragmented files, sparse files, LZNT1-compressed files (clusters up to 4 KiB), hard links, many hard links and streams on one file, symbolic links, junctions, alternate streams and attribute/timestamp variants.

Output goes to `<VhdDirectory>\report.csv` and `<VhdDirectory>\logs\<name>\` (`ntfsinfo.txt`, `chkdsk.txt`, `manifest.csv`, `parser.txt`, `run.log`, `content.log`).

### Linux: `ntfs-test-matrix.sh`

Needs root, `mkntfs` and `ntfs-3g`. Builds sparse images with `losetup -b <sector size>`, formats them with `mkntfs -c <cluster> -s <sector>`, fills them through ntfs-3g, writes the same manifest format and optionally runs the parser (`{Device}`, `{Image}`, `{Offset}`, `{Manifest}`, ...).

```bash
sudo ./ntfs-test-matrix.sh --gpt \
  --parser 'dotnet ~/nt/Ntfs4Testing.dll verify --device {Device} --manifest {Manifest}'
```

Options include `--sector-sizes`, `--cluster-sizes`, `--size`, `--filter`, `--list-only`, `--keep`, `--image` and `--offset`.

The volumes are formatted by mkntfs and written by ntfs-3g, not by Windows: there is no EFS, no USN journal and no 4 KiB file records, and security, compression and reparse data differ in flavour. Use it for fast geometry coverage and CI, not as a replacement for the Windows run.

## Current results

46 geometry combinations pass on Windows (all with a clean `chkdsk` and a matching listing, size and content hash for every file). The combinations that do not appear are 512e disks with clusters below 4096 bytes, which Windows does not format usefully.

## What the matrix does not prove

It compares listings, sizes and file hashes only. Alternate stream contents, link targets, timestamps, security descriptors, USN contents and deleted-file behaviour are not compared, and all volumes are freshly created. See the [limitations](../Nternal/README.md#limitations).
