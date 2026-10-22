# FileExtractor

Copies a file, or one of its alternate data streams, out of an NTFS volume by reading the raw volume. Because it bypasses the operating system's file layer, it works on files that are locked or in use.

## Usage

```
FileExtractor <source> <path[:stream]> <output>
```

`<source>` is a drive letter on Windows (for example `D:`), or a device or image path (`/dev/sdb1`, `disk.img`). Reading a raw volume needs administrator rights on Windows; on Linux use `sudo` or join the `disk` group.

| Argument | Meaning |
| --- | --- |
| `<path[:stream]>` | File on the volume, for example `/Windows/System32/config/SAM`. Append `:name` for an alternate data stream, for example `/file.exe:Zone.Identifier`. |
| `<output>` | Where to write the copy. Parent folders are created. |

```bash
dotnet run -c Release --project Examples/FileExtractor -- D: "/Users/me/notes.txt" ./notes.txt
```

## Behaviour

- Compressed and sparse files are expanded transparently.
- The original creation, modification and access times are copied onto the output file.
- Directories are refused.

## API used

`GetNtfsFile`, `NtfsFile.GetDataStream(name)`, `IDataStream.Read`, `StandardInformationAttribute`.

## Notes

- Encrypted (EFS) files are not supported.
- Files that are being written while you copy them can be inconsistent, the raw read has no locking.
