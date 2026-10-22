# VolumeInfo

Prints the geometry, metadata and space usage of an NTFS volume.

## Usage

```
VolumeInfo <source>
```

`<source>` is a drive letter on Windows (for example `D:`), or a device or image path (`/dev/sdb1`, `disk.img`). Reading a raw volume needs administrator rights on Windows; on Linux use `sudo` or join the `disk` group.

```bash
dotnet run -c Release --project Examples/VolumeInfo -- D:
```

## What it shows

- Label, NTFS version, volume flags and serial number.
- Bytes per sector and cluster, file record size, index buffer size.
- Total size and cluster count, `$MFT` and `$MFTMirr` locations, number of MFT records.
- Used and free space, counted from `$Bitmap`.
- Bad clusters and their ranges, read from `$BadClus:$Bad`.

## API used

`NtfsVolume.Mount`, `BootSector`, `VolumeLabel`, `NtfsVersion`, `VolumeFlags`, `GetMftRecordCount`, `GetBadClusterRanges`, `IsClusterUsed` / `$Bitmap`.

## Notes

Read-only. The used/free numbers come from the on-disk bitmap, so on a mounted, busy volume they are a moment-in-time estimate.
