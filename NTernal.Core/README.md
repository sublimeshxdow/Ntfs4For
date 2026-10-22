# NTernal.Core

The foundation under the other projects: a minimal stream abstraction and raw disk access for Windows and Linux. Target framework: **.NET 10**. No dependencies.

## Types

| Type | Purpose |
| --- | --- |
| `IDataStream` | A readable, random-access object: `Length`, `AllocatedLength`, `Alignment`, `Read`, `ReadExactly`, `TryGetMemory`, `LoadIntoMemory`. Positions are absolute offsets, so there is no seek state. |
| `RawDriveStream` | Opens a raw device or image read-only (`\\.\D:`, `\\.\PhysicalDrive1`, `/dev/sdb1`, `disk.img`) with a sector `Alignment`. |
| `UnalignedDataStream` | Wraps an aligned stream and serves reads of any offset and size. |
| `DiskInfo` | `GetSectorSizes(path)` (logical and physical) and `GetLength(path)`, using Win32 IOCTLs on Windows and ioctls on Linux. |

## Usage

```csharp
using Nternal.Core;

using var disk = new RawDriveStream("/dev/sda");
Console.WriteLine($"{disk.Length:N0} bytes, {disk.Alignment}-byte sectors");

var sector = new byte[disk.Alignment];
disk.ReadExactly(0, sector);               // aligned read, goes straight to the device

var odd = new byte[100];
disk.ReadExactly(1000, odd);               // unaligned: RawDriveStream falls back to an internal aligner
```

Implement `IDataStream` yourself to feed the parser from anything else (an in-memory image, a network source, an E01 reader):

```csharp
public sealed class MyImageStream : IDataStream { /* Length, Alignment, Read, ... */ }
using var volume = new NtfsVolume(new MyImageStream());
```

## Notes

- **Alignment.** `Alignment` is the minimum read granularity (the logical sector size for devices). Direct `RawDriveStream` reads that are not aligned are handled for you; other wrappers such as `PartitionStream` accept only aligned reads, so wrap them in `UnalignedDataStream` before handing them to `NtfsVolume`.
- **Read-only.** Devices are opened for reading with shared access; nothing is ever written.
- **Permissions.** Raw device access needs administrator rights on Windows. On Linux use `sudo`, or add yourself to the `disk` group (log out and back in).
- **Platforms.** Windows and Linux. `DiskInfo` throws `PlatformNotSupportedException` elsewhere.
- **Memory.** `RawDriveStream.LoadIntoMemory()` is intentionally unsupported.
