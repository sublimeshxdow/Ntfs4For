# UsnJournalViewer

Prints the NTFS change journal (`$Extend\$UsnJrnl:$J`): a log of file creations, deletions, renames and writes that outlives the files themselves, with parent folders resolved to full paths.

## Usage

```
UsnJournalViewer <source> [--name text] [--reason FileCreate,FileDelete,...] [--close] [--last N]
```

`<source>` is a drive letter on Windows (for example `D:`), or a device or image path (`/dev/sdb1`, `disk.img`). Reading a raw volume needs administrator rights on Windows; on Linux use `sudo` or join the `disk` group.

| Option | Meaning |
| --- | --- |
| `--name text` | Only show entries whose file name contains this text. |
| `--reason a,b,...` | Only show entries with any of these reasons (`UsnReason` names, case-insensitive). |
| `--close` | Only show the final record of each change (`USN_REASON_CLOSE`). Far less noise. |
| `--last N` | Only show the newest N matching entries. |

```bash
dotnet run -c Release --project Examples/UsnJournalViewer -- D: --close --last 50
```

The program first prints the journal id, size limits and lowest valid USN, then the entries.

## API used

`NtfsVolume.GetJournalReader`, `UsnJournalReader.GetJournalEntries`, `IUsnEntry` (V2, V3 and V4 records), `UsnReason`, `UsnSource`.

## Notes

- A volume without a journal reports how to create one (`fsutil usn createjournal`) and exits.
- Journals larger than 2 GB are not supported yet.
- Old entries disappear as the journal wraps, so absence of an entry proves nothing.
