# DirectoryTree

Prints a recursive tree of a folder on an NTFS volume, read straight from the `$I30` directory indexes.

## Usage

```
DirectoryTree <source> [path] [--depth N] [--exact] [--all]
```

`<source>` is a drive letter on Windows (for example `D:`), or a device or image path (`/dev/sdb1`, `disk.img`). Reading a raw volume needs administrator rights on Windows; on Linux use `sudo` or join the `disk` group.

| Option | Meaning |
| --- | --- |
| `[path]` | Folder to start at, for example `/Users/Public` (default: `/`). |
| `--depth N` | Stop descending after N levels. |
| `--exact` | Read each file's own record for its size instead of trusting the (possibly stale) size cached in the index. Slower. |
| `--all` | Also show NTFS metafiles (`$MFT`, `$Bitmap`, ...). |

```bash
dotnet run -c Release --project Examples/DirectoryTree -- D: /Users --depth 2
```

## What it shows

Folders first, then files, sorted case-insensitively, with sizes. Compressed and sparse files are tagged, and links show their target (`->`). A summary line gives the folder, file and byte totals.

## API used

`EnumerateDirectory`, `GetNtfsFile`, `FolderEntry` flags, `ReparseAttribute` (`SymbolicLinkReparseData`, `MountPointReparseData`).

## Notes

- Symbolic links and junctions are listed but never followed, so a link back up the tree cannot cause a loop.
- Index sizes can lag behind the real size, use `--exact` when numbers matter.
