# Nternal (`Nternal.Ntfs`)

The NTFS parser. Given any readable `IDataStream` that starts at an NTFS boot sector (a raw volume, an image, or a partition stream), it exposes the file system read-only: files, directories, attributes, data, security and the change journal.

- Target framework: **.NET 10**
- Depends on: [`NTernal.Core`](../NTernal.Core/README.md)
- Main entry point: `NtfsVolume`

## Contents

- [Quick start](#quick-start)
- [Key types](#key-types)
- [Available features](#available-features)
- [Limitations](#limitations)
- [Planned features](#planned-features)
- [Parsing policy](#parsing-policy)

## Quick start

```csharp
using Nternal.Core;
using Nternal.Ntfs;

using var raw = new RawDriveStream(@"\\.\D:");
using var volume = new NtfsVolume(raw);
volume.Mount();                                  // Mount(false) to skip preloading $MFT into memory

Console.WriteLine($"{volume.VolumeLabel}  NTFS {volume.NtfsVersion}  {volume.BootSector.BytesPerCluster} B clusters");

// Directory listing (reads the $I30 B+ tree)
foreach (var e in volume.EnumerateDirectory("/Users"))
    Console.WriteLine($"{e.Name}  {e.RealSize:N0}");

// File data; pass a stream name for an alternate data stream
var file = volume.GetNtfsFile("/Users/me/notes.txt");
using var data = file.GetDataStream();
var buf = new byte[(int)data.Length];
data.ReadExactly(0, buf);

// By MFT record instead of path
var byRecord = volume.GetNtfsFile(new FileReference(5, 0), compareSequence: false);   // root folder
```

## Key types

| Type | Role |
| --- | --- |
| `NtfsVolume` | Mounts a volume. Path and record lookup, directory enumeration, cluster state, bad clusters, security descriptors, journal reader. |
| `NtfsFile` | One file record with all its attributes (merged across `$ATTRIBUTE_LIST` extension records). `GetDataStream`, `GetAttributeData<T>`. |
| `NtfsBootSector` | Geometry: sector, cluster, file record and index buffer sizes, MFT location, serial number. |
| `FileReference` | MFT record number plus sequence number. |
| `FolderEntry` | One directory index entry (name, reference, flags, cached sizes and times). |
| `IndexParser`, `CollationHandler` | Generic B+ tree index reading and the collation rules; used for `$I30` and `$Secure`. |
| `NonResidentDataStream`, `CompressedDataStream`, `ResidentDataStream` | The `IDataStream` implementations behind `$DATA`. |
| `SecurityDescriptor` (`Sid`, `Acl`, `Ace`) | Decoded security descriptors. |
| `UsnJournalReader`, `IUsnEntry` | Change journal records (V2, V3, V4). |
| `NtfsPolicyConfig` | Global strictness and behaviour switches. |

## Available features

### Volume and low-level structures
- Boot sector (BPB) parsing: sector size, cluster size, MFT location, MFT record size and index buffer size.
- `$MFT` parsing with fixup (update sequence array) handling, record-number and sequence-number validation.
- Optional in-memory preload of the whole `$MFT`, a large speed-up for full scans.
- Fixed system files by record number (`$MFT`, `$Bitmap`, `$Secure`, `$UpCase`, `$Volume`, `$BadClus`, `$Extend`, ...).
- Volume label, NTFS version, volume flags.
- `$UpCase` loading with optional CRC-64 validation against `$UpCase:$Info`.
- Cluster allocation state from `$Bitmap` and bad-cluster ranges from `$BadClus:$Bad`.

### Files and attributes
- Resident and non-resident attributes, named and unnamed.
- Data runs with signed deltas, sparse runs and multiple extents.
- `$ATTRIBUTE_LIST` with extension records, including sequence validation and extent consistency checks.
- Typed decoding of `$STANDARD_INFORMATION`, `$FILE_NAME`, `$OBJECT_ID`, `$SECURITY_DESCRIPTOR`, `$VOLUME_NAME`, `$VOLUME_INFORMATION`, `$DATA`, `$INDEX_ROOT`, `$INDEX_ALLOCATION`, `$BITMAP`, `$REPARSE_POINT`, `$EA` / `$EA_INFORMATION` and `$LOGGED_UTILITY_STREAM`.
- Alternate data streams.

### File contents
- Resident, non-resident, **sparse** and **LZNT1-compressed** streams behind one `IDataStream` interface.
- Unaligned reads on top of sector-aligned devices.
- Optional parallel decompression and full in-memory loading for compressed files.

### Directories and indexes
- Full `$I30` B+ tree support: lookup by name and in-order enumeration across `$INDEX_ROOT` and `$INDEX_ALLOCATION`.
- Correct index block size versus cluster size handling (including 64 KiB clusters), bitmap checks and fixups.
- Collation rules: filename (via the volume's own `$UpCase`), binary, unicode string, ULONG, ULONGs, SID, security hash.
- Generic index parsing, also used for the `$Secure` indexes.
- Path resolution (`GetNtfsFile("/a/b/c")`) with `.` / `..` and **relative symbolic links**, a 63-hop limit as on Windows, and loop protection.
- Primary path from a file reference, and enumeration of symbolic links via `$Extend\$Reparse`.
- Win32 and Win32+DOS names by default, POSIX names switchable.

### Security
- `$Secure`: `$SDS` stream, `$SDH` and `$SII` indexes, lookup by security id or by file.
- Full security descriptor decoding (owner, group, DACL, SACL, SIDs, ACEs, flags) with C# enums.
- Security descriptor hash computation and validation.

### USN change journal
- `$Extend\$UsnJrnl` (`$J`, `$Max`), including sparse regions.
- Record versions V2, V3 and V4 with reason and source flags.

## Limitations

**Scope.** Read-only. It does not write, repair or mount volumes, and it is not a filesystem driver. Raw speed is not a design goal: the `$MFT` preload helps, but a driver will be faster at bulk enumeration.

**Not supported (yet)**
- **Encryption (EFS).** Encrypted files are not decrypted, and compression combined with encryption throws `NotImplementedException`.
- **`$LogFile` replay.** A volume that was not cleanly dismounted can differ from what Windows shows after recovery. Dismount it or work on an image.
- **Absolute and cross-volume symbolic links** (`\??\Volume{GUID}\...`) throw `NotSupportedException` during path lookup. Junction and mount-point targets are not resolved during lookup.
- **Streams over 2 GB:** compressed streams cannot be loaded into memory, and USN journals above 2 GB are not parsed.
- **Quota, transactional NTFS (`$TxfLog`, `$RmMetadata`), `$LogFile` contents and shadow copies** are not interpreted.
- **NTFS versions below 3.0** (no `$Secure`, no journal) are untested; testing covered NTFS 3.1 volumes written by Windows.
- Windows and Linux only for raw device access; ReFS, exFAT, FAT and big-endian hosts are out of scope.

**Validation gaps**
- Verified against what Windows reports: directory listings, entry types, file sizes and SHA-256 of file contents.
- Not yet verified automatically: alternate stream contents, link targets, timestamps, per-file security descriptors, USN contents, deleted-file recovery, and `$ATTRIBUTE_LIST` layouts beyond what the test content produces.
- Test volumes were freshly created. Aged volumes with a fragmented `$MFT` or multi-extent indexes have seen less coverage.
- Corrupt or hostile input is bounds-checked and raises `InvalidDataException`, but it has not been fuzzed.
- Thread safety of a shared `NtfsVolume` is neither documented nor tested. Use one volume per thread unless you have checked.

**Behaviour to know**
- A live, mounted volume is a moving target. Use a dismounted volume, an image or a snapshot.
- Directory index entries cache sizes and times that Windows updates lazily. For authoritative values use the file record (`NtfsFile`).
- Unused MFT records throw by default (`DenyUnusedMftRecords`); clear that flag for recovery work.

## Planned features

Goals, not promises; the order may change.

**Correctness and validation**
- Extend the automated verifier to alternate streams, link targets, timestamps, security descriptors and USN contents.
- Test on aged real-world volumes and on volumes with fragmented `$MFT` and multi-extent indexes.
- Fuzz the record, attribute, data-run and index parsers.
- CI that runs the test matrix on generated volumes.

**Format coverage**
- `$LogFile` parsing and, later, replay for dirty volumes.
- EFS: `$EFS` metadata first, decryption with a supplied key later.
- Absolute and cross-volume symbolic links, junctions and mount points through a caller-supplied volume resolver.
- `$Quota`, `$ObjectId` and `$Reparse` index helpers, and `$Boot` versus backup boot sector comparison.
- USN journals above 2 GB and streaming reads of very large compressed files.

**API and ergonomics**
- A friendlier file-system style facade over the attribute-level API (`NtfsFileInfo`, `OpenRead`, ...).
- Documented thread-safety guarantees and async APIs.
- Lenient modes for damaged volumes with per-record error reporting instead of exceptions alone.
- NuGet packages and generated API documentation.

## Parsing policy

`NtfsPolicyConfig` (static) controls how strict the parser is. The defaults are strict.

| Setting | Effect |
| --- | --- |
| `Flags.ParanoidParsing` | Extra structural checks; mismatches throw. |
| `Flags.ParanoidParsingUrnJrnl` | The same for the USN journal. |
| `Flags.ValidateUpCase` | Check `$UpCase` against its stored CRC-64. |
| `Flags.ValidateSdh` | Check `$Secure` hash entries. |
| `Flags.ValidateSequenceNumbers`, `ValidateSequenceNumbersInternal` | Reject references whose sequence number no longer matches. |
| `Flags.DenyUnusedMftRecords` | Refuse to parse records not marked in use. Clear for deleted-file work. |
| `ListPosixFileNames` | Include POSIX-namespace names in directory listings. |
| `HopLimit` | Maximum symbolic link hops during path lookup (default 63). |
| `CompressionExtentParallelismThreshold`, `CompressionMaxDegreeOfParallelism` | Tuning for parallel decompression. |

```csharp
// Relax checks for a damaged volume or for recovery work
NtfsPolicyConfig.PolicyFlags &= ~NtfsPolicyConfig.Flags.DenyUnusedMftRecords;
NtfsPolicyConfig.PolicyFlags &= ~NtfsPolicyConfig.Flags.ParanoidParsing;
```
