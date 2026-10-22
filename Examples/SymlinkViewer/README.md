# SymlinkViewer

Lists every symbolic link and junction on an NTFS volume without walking the whole directory tree. It reads the `$Extend\$Reparse:$R` index, then optionally checks where each link points and whether that target exists.

## Usage

```
SymlinkViewer <source> [options]
```

`<source>` is a drive letter on Windows (for example `D:`), or a device or image path (`/dev/sdb1`, `disk.img`). Reading a raw volume needs administrator rights on Windows; on Linux use `sudo` or join the `disk` group.

| Option | Meaning |
| --- | --- |
| `--tree` | Show the links as a directory tree. |
| `--csv` | Print comma-separated values. |
| `--dot` | Print a Graphviz graph. Render with `dot -Tsvg links.dot -o links.svg`. |
| `--symlinks-only` | Leave out junctions and mount points. |
| `--all` | Include every reparse point (WOF, cloud files, app execution aliases, ...). |
| `--letter <X>` | Drive letter the volume is mounted as, used to resolve absolute targets (default: the letter of `<source>` if one was given). |
| `--no-check` | Do not check whether targets exist. |
| `-h, --help` | Show help. |

```bash
dotnet run -c Release --project Examples/SymlinkViewer -- D: --tree
```

## API used

`EnumerateIndexedSymLinks`, `GetNtfsFile`, `ReparseAttribute`, `SymbolicLinkReparseData`, `MountPointReparseData`, `GetPrimaryAbsolutePath`.

## Notes

- Requires the volume to have `$Extend\$Reparse` (NTFS 3.0 and later); otherwise the program stops with a clear error.
- Targets that point to other volumes cannot be checked here and are reported as such.
