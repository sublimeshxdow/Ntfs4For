# FileInspector

Dumps everything the library knows about one file or folder: record header, timestamps, names, every attribute, where the data lives on disk, the security descriptor and extended attributes.

## Usage

```
FileInspector <source> <path | #record>
```

`<source>` is a drive letter on Windows (for example `D:`), or a device or image path (`/dev/sdb1`, `disk.img`). Reading a raw volume needs administrator rights on Windows; on Linux use `sudo` or join the `disk` group.

`<path | #record>` is a path such as `/Windows/notepad.exe`, or an MFT record number prefixed with `#` (for example `#5` for the root folder). Record numbers also work for deleted or system files that have no path.

```bash
dotnet run -c Release --project Examples/FileInspector -- D: /Windows/notepad.exe
```

## Sections

- **Record**: flags, sequence number, hard link count.
- **Timestamps (UTC)**: from `$STANDARD_INFORMATION` and `$FILE_NAME`.
- **Standard information**: attributes and the other fields of that attribute.
- **Names**: every `$FILE_NAME` with its namespace and parent record.
- **Attributes**: type, name, resident or non-resident, size and flags, plus the data runs (fragmentation) of non-resident attributes.
- **Security**: owner, group and DACL, with well-known SIDs named.
- **EAs**: extended attributes.

## API used

`GetNtfsFile` (by path or by `FileReference`), `NtfsFile.NtfsAttributes`, `FileNameAttribute`, `StandardInformationAttribute`, `NonResidentDataStream.Extents`, `GetSecurityDescriptor`, `EaAttribute`.

## Notes

Good for checking what the library sees when another tool disagrees with it.
