# Ntfs4For

A managed (C#) toolkit for **reading NTFS** from raw volumes and disk images, for forensics, tooling and learning. It is not a filesystem driver: it parses the on-disk structures itself, in user space and read-only, so it works on locked files, disk images and volumes the operating system cannot or should not mount.

> **Status: early.** Validated for reading healthy, cleanly dismounted Windows NTFS volumes (see [Testing](#testing)). Not validated on damaged, hostile or in-use volumes. Read the [limitations](Nternal/README.md#limitations) before relying on it.

## Repository layout

| Project | What it is |
| --- | --- |
| [`Nternal`](Nternal/README.md) | **The NTFS parser** (`Nternal.Ntfs`): MFT, attributes, indexes, file data, security, USN journal. |
| [`NTernal.Core`](NTernal.Core/README.md) | Raw device access, sector-size detection, the `IDataStream` abstraction and unaligned reads. |
| [`NTernal.Partitioning`](NTernal.Partitioning/README.md) | MBR and GPT parsing, and streams for single partitions. |
| [`Examples`](Examples/README.md) | Nine small command-line tools built on the library (tree, extractor, inspector, deleted-file recovery, USN viewer, ...). |
| [`Ntfs4Testing`](Ntfs4Testing/README.md) | Development harness and the `verify` mode used by the test matrix. Not a public API. |

Dependencies point one way: `Nternal` and `NTernal.Partitioning` depend on `NTernal.Core`; everything else depends on those.

## Quick start

Requires the **.NET 10 SDK**.

```bash
git clone https://github.com/sublimeshxdow/Ntfs4For
cd Ntfs4For
dotnet build Ntfs4For.slnx -c Release

dotnet run -c Release --project Examples/VolumeInfo -- D:                # Windows (admin)
sudo dotnet run -c Release --project Examples/VolumeInfo -- /dev/sdb1    # Linux
```

And in code:

```csharp
using Nternal.Core;
using Nternal.Ntfs;

using var raw = new RawDriveStream(@"\\.\D:");     // or "/dev/sdb1", or an image file
using var volume = new NtfsVolume(raw);
volume.Mount();

foreach (var entry in volume.EnumerateDirectory("/Windows"))
    Console.WriteLine($"{entry.Name}  {entry.RealSize:N0} bytes");

using var data = volume.GetNtfsFile("/Windows/notepad.exe").GetDataStream();
var buffer = new byte[4096];
int read = data.Read(0, buffer);
```

Raw device access needs administrator rights on Windows. On Linux use `sudo`, or add yourself to the `disk` group and log in again. A volume inside a partitioned disk needs a partition stream first, see [`NTernal.Partitioning`](NTernal.Partitioning/README.md).

## Highlights

- Complete read path: boot sector, `$MFT` (fixups, `$ATTRIBUTE_LIST`, extension records), resident, non-resident, **sparse** and **LZNT1-compressed** data, alternate data streams.
- **`$I30` B+ trees**: lookup and in-order enumeration, correct for all cluster sizes up to 64 KiB, `$UpCase`-based collation.
- **Security**: `$Secure` (`$SDS`, `$SDH`, `$SII`), full security descriptor decoding, descriptor hash validation.
- **USN journal** reading (V2, V3, V4 records).
- Path resolution with relative symbolic links, hop limits and loop protection.
- Cluster allocation state and bad-cluster ranges.
- Strictness is configurable through `NtfsPolicyConfig`.

The complete lists live in [`Nternal/README.md`](Nternal/README.md): [available features](Nternal/README.md#available-features), [limitations](Nternal/README.md#limitations) and [planned features](Nternal/README.md#planned-features).

## Limitations at a glance

- **Read-only.** No writing, repair or mounting.
- **No encryption (EFS)**, no `$LogFile` replay, no absolute or cross-volume symlink resolution in path lookup.
- Tested on **NTFS 3.1 volumes written by Windows**, on Windows and Linux hosts. Corrupt and hostile input has not been fuzzed.
- Validation compares listings, sizes and file content hashes. Alternate streams, link targets, timestamps, security descriptors and USN contents are not yet checked automatically.

## Testing

The parser is checked against volumes Windows itself created, with the operating system as the oracle:

1. A script builds VHDX files for a matrix of logical sector size (512 / 4096), physical sector size (512e), cluster size (512 B to 64 KiB) and file record size (1 KiB / 4 KiB), then reads the real geometry back from `fsutil`.
2. Each volume is filled with stress content (a 3000-entry directory, unicode names, MFT churn, size boundaries, fragmented, sparse and compressed files, links, junctions, alternate streams).
3. Windows writes a SHA-256 manifest and `chkdsk` must report the volume clean.
4. [`Ntfs4Testing verify`](Ntfs4Testing/README.md) mounts the dismounted volume with Nternal and compares its listing, sizes and content hashes with the manifest.

Current result: **46 geometry combinations pass.** Details, scripts and how to run them: [`Ntfs4Testing/README.md`](Ntfs4Testing/README.md).

## Contributing

Issues and pull requests are welcome. A useful bug report includes the volume geometry (`fsutil fsinfo ntfsinfo`), the NTFS version, the file path or MFT record number, and the exception text. Please do not attach images with private data.

## AI assistance

Most programs in `Examples/` were written with AI assistance (Anthropic's Claude) against the public API, then reviewed and tested by the author. See [`Examples/Disclaimer.txt`](Examples/Disclaimer.txt) for the exact wording.

## License

MIT. See [LICENSE.txt](LICENSE.txt).
