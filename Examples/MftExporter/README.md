# MftExporter

Dumps the `$MFT` of an NTFS volume to a file called `$Mft`. The result can be opened in other MFT parsers.

**Windows only**: the program takes a drive letter and uses `DriveInfo` to check for NTFS first. Run it as administrator.

## Usage

```
MftExporter [options] [drive] [output-directory]
```

| Argument / option | Meaning |
| --- | --- |
| `drive` | Drive letter, for example `C`, `C:` or `C:\` (default: `C`). |
| `output-directory` | Where to write `$Mft` (default: your desktop). Created if missing. |
| `-d, --drive <drive>` | Same as the drive argument. |
| `-o, --output <dir>` | Same as the output directory argument. |
| `-h, --help` | Show help. |

```
MftExporter D
MftExporter D: "C:\Dumps"
MftExporter --drive E --output "%USERPROFILE%\Dumps"
```

## API used

`NtfsVolume.GetKnownMftReference(KnownMftRecord.Mft)`, `GetNtfsFile`, `GetDataStream`, copied in 1 MiB blocks.

## Notes

- The export is a raw copy of the live `$MFT`. Records can change while it is being read, so treat it as a snapshot of a moving target, or export from a dismounted volume or image.
- A non-NTFS drive is rejected with an error.
