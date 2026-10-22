# DeletedFileRecovery

Scans an NTFS volume for deleted files, rebuilds their original paths and tells you how much of each one can still be trusted. Optionally recovers the files that survive.

## Usage

```
DeletedFileRecovery <source> [--name text] [--record N] [--all]
                            [--recover <folder>] [--include-partial]
```

`<source>` is a drive letter on Windows (for example `D:`), or a device or image path (`/dev/sdb1`, `disk.img`). Reading a raw volume needs administrator rights on Windows; on Linux use `sudo` or join the `disk` group.

| Option | Meaning |
| --- | --- |
| `--name text` | Only consider deleted files whose name contains this text. |
| `--record N` | Only consider MFT record N (a leading `#` is accepted). |
| `--all` | Also list overwritten and unreadable records. |
| `--recover <folder>` | Write recoverable files into this folder, mirroring their original folder structure. |
| `--include-partial` | Also recover files whose clusters were partly reused. |

```bash
dotnet run -c Release --project Examples/DeletedFileRecovery -- D: --name report --recover E:/recovered
```

## Recoverability states

| State | Meaning |
| --- | --- |
| `Resident` | The content lives inside the MFT record itself. |
| `Intact` | None of the file's clusters have been reused. |
| `Partial` | Some clusters are in use again, so parts of the output will contain other data. |
| `Overwritten` | Every cluster is in use again. The content is gone. |
| `Unreadable` | The record could not be parsed (for example its extension records were reused). |

Without `--recover`, files are only listed.

## Important

- **Never recover onto the volume you are scanning.** Writing there can overwrite exactly the clusters you are trying to save. The program refuses an output folder on the same volume.
- Run it on a dismounted volume or an image, and stop using the volume as soon as you notice a deletion.
- Recoverability is judged from the cluster bitmap at scan time, it is not a guarantee that the bytes are the original file.

## API used

`NtfsPolicyConfig.Flags.DenyUnusedMftRecords` (cleared so unused records can be read), `NtfsVolume.Mount`, `GetNtfsFile`, `IsClusterUsed`, data runs of non-resident attributes.
