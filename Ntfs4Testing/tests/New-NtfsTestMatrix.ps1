#Requires -RunAsAdministrator
#Requires -Modules Hyper-V
<#
.SYNOPSIS
    Builds a matrix of NTFS test volumes (VHDX) with different sector / cluster / file-record
    sizes, fills them with stress content, records a SHA-256 manifest (the "oracle"), and
    optionally runs your parser against each one.

.DESCRIPTION
    Per combination:
      New-VHD -> Mount -> GPT -> partition -> Format-Volume -> read back ACTUAL geometry
      -> populate -> flush -> read-only chkdsk -> manifest.csv -> dismount volume
      -> run -ParserCommand against the raw volume -> dismount VHDX.
    Passing VHDX files are deleted unless -KeepVhds; failing ones are always kept.
    Everything is written to -VhdDirectory\logs\<name>\ and summarised in report.csv.

.PARAMETER VhdDirectory
    Where VHDX files and logs go. Default: .\ntfs-tests

.PARAMETER ExistingVhdPath
    Test ONE existing VHD/VHDX instead of generating the matrix. It is mounted read-only,
    never formatted or populated, and never deleted.

.PARAMETER LogicalSectorSizes
    Logical sector sizes to test. Default 512, 4096.

.PARAMETER ClusterSizes
    Cluster sizes (bytes). Combos with cluster < logical sector size are skipped.

.PARAMETER Include512e
    Also create 512-logical / 4096-physical (512e) disks.

.PARAMETER IncludeLargeFrs
    Also format with 4096-byte file record segments (Format-Volume -UseLargeFRS).

.PARAMETER IncludeEfs
    Also create EFS-encrypted files (off by default: Nternal does not support encryption).

.PARAMETER SizeBytes
    VHDX size. Default 4GB.

.PARAMETER Fixed
    Use fixed instead of dynamic VHDX.

.PARAMETER Populate
    Fill the volume with test content. Default on; use -Populate:$false for empty volumes.

.PARAMETER LargeDirFileCount / SmallFileCount / FragmentedFileCount / FragmentedFileSizeMB
    Content size knobs.

.PARAMETER ParserCommand
    Command line run per volume. Placeholders:
      {RawVolume} {PhysicalDrive} {PartitionOffset} {DriveLetter} {VhdPath} {Manifest}
      {LogDir} {Name} {SectorSize} {ClusterSize} {FrsSize}
    Exit code 0 = pass. Example:
      -ParserCommand 'dotnet run --project C:\src\Ntfs4Testing -- --disk {PhysicalDrive} --offset {PartitionOffset} --manifest "{Manifest}"'

.PARAMETER NameFilter
    Only run combos whose name matches this wildcard (e.g. "*cl65536*").

.PARAMETER ListOnly
    Print the planned combinations and exit.

.PARAMETER SkipChkdsk / SkipDismount / KeepVhds
    Self-explanatory.

.PARAMETER ReportPath
    CSV report path. Default <VhdDirectory>\report.csv

.EXAMPLE
    .\New-NtfsTestMatrix.ps1 -ListOnly
.EXAMPLE
    .\New-NtfsTestMatrix.ps1 -VhdDirectory D:\vhd -Include512e -IncludeLargeFrs -ParserCommand 'MyParser.exe {PhysicalDrive} {PartitionOffset} "{Manifest}"'
.EXAMPLE
    .\New-NtfsTestMatrix.ps1 -ExistingVhdPath D:\vhd\mine.vhdx -ParserCommand 'MyParser.exe {PhysicalDrive} {PartitionOffset}'
#>
[CmdletBinding()]
param(
    [string]   $VhdDirectory = (Join-Path (Get-Location) 'ntfs-tests'),
    [string]   $ExistingVhdPath,
    [int[]]    $LogicalSectorSizes = @(512, 4096),
    [int[]]    $ClusterSizes = @(512, 1024, 2048, 4096, 8192, 16384, 32768, 65536),
    [switch]   $Include512e,
    [switch]   $IncludeLargeFrs,
    [switch]   $IncludeEfs,
    [UInt64]   $SizeBytes = 4GB,
    [switch]   $Fixed,
    [bool]     $Populate = $true,
    [int]      $LargeDirFileCount = 3000,
    [int]      $SmallFileCount = 500,
    [int]      $FragmentedFileCount = 12,
    [int]      $FragmentedFileSizeMB = 8,
    [string]   $ParserCommand,
    [string]   $NameFilter = '*',
    [switch]   $ListOnly,
    [switch]   $SkipChkdsk,
    [switch]   $SkipDismount,
    [switch]   $KeepVhds,
    [string]   $ReportPath
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version 2.0

if (-not $ReportPath) { $ReportPath = Join-Path $VhdDirectory 'report.csv' }

function Write-Log([string]$Message, [string]$Level = 'INFO') {
    $line = '{0} [{1}] {2}' -f (Get-Date -Format 'HH:mm:ss'), $Level, $Message
    switch ($Level) {
        'WARN' { Write-Host $line -ForegroundColor Yellow }
        'FAIL' { Write-Host $line -ForegroundColor Red }
        'PASS' { Write-Host $line -ForegroundColor Green }
        default { Write-Host $line }
    }
    if ($script:CurrentLog) { Add-Content -Path $script:CurrentLog -Value $line }
}

function Get-Combinations {
    $list = @()
    foreach ($ls in $LogicalSectorSizes) {
        $physList = @($ls)
        if ($Include512e -and $ls -eq 512) { $physList += 4096 }
        foreach ($ps in $physList) {
            foreach ($cl in $ClusterSizes) {
                # Windows cannot usefully format NTFS with clusters smaller than the sector sizes
                # (512e disks with 512/1024/2048 clusters produced unreadable volumes in testing).
                if ($cl -lt $ls -or $cl -lt $ps) { continue }
                $frsList = @(1024)
                if ($IncludeLargeFrs) { $frsList += 4096 }
                foreach ($frs in $frsList) {
                    $name = 'ls{0}-ps{1}-cl{2}-frs{3}' -f $ls, $ps, $cl, $frs
                    if ($name -notlike $NameFilter) { continue }
                    $list += [pscustomobject]@{
                        Name = $name; Logical = $ls; Physical = $ps; Cluster = $cl; Frs = $frs
                    }
                }
            }
        }
    }
    return $list
}

# ---------------------------------------------------------------- geometry
function Get-NtfsGeometry([string]$Letter) {
    $out = & fsutil.exe fsinfo ntfsinfo ("{0}:" -f $Letter) 2>&1 | Out-String
    $g = @{ Raw = $out }
    foreach ($pair in @(
            @('Sector', 'Bytes Per Sector\s*:\s*(\d+)'),
            @('Physical', 'Bytes Per Physical Sector\s*:\s*(\d+)'),
            @('Cluster', 'Bytes Per Cluster\s*:\s*(\d+)'),
            @('Frs', 'Bytes Per FileRecord Segment\s*:\s*(\d+)'))) {
        $m = [regex]::Match($out, $pair[1])
        $g[$pair[0]] = if ($m.Success) { [int]$m.Groups[1].Value } else { 0 }
    }
    return $g
}

# ---------------------------------------------------------------- content
function New-RandomFile([string]$Path, [long]$Bytes, [int]$Seed) {
    $rng = New-Object System.Random($Seed)
    $buf = New-Object byte[] (1MB)
    $fs = [System.IO.File]::Create($Path)
    try {
        $left = $Bytes
        while ($left -gt 0) {
            $rng.NextBytes($buf)
            $n = [int][Math]::Min($left, $buf.Length)
            $fs.Write($buf, 0, $n)
            $left -= $n
        }
    }
    finally { $fs.Dispose() }
}

function Add-TestContent([string]$Root) {
    $r = $Root.TrimEnd('\')
    $utf8 = New-Object System.Text.UTF8Encoding($false)

    Write-Log "Populate: large directory ($LargeDirFileCount files)"
    $d = Join-Path $r 'largedir'
    [void](New-Item -ItemType Directory -Path $d)
    for ($i = 0; $i -lt $LargeDirFileCount; $i++) {
        [System.IO.File]::WriteAllText((Join-Path $d ('file_{0:D6}_with_a_fairly_long_name_to_fill_index_nodes.txt' -f $i)), "content $i", $utf8)
    }

    Write-Log 'Populate: unicode / collation names'
    $u = Join-Path $r 'unicode'
    [void](New-Item -ItemType Directory -Path $u)
    $names = @(
        'a', 'B', '_underscore', '~tilde', '0digit', 'Zebra', 'zebra2',
        ('caf' + [char]0xE9), ('CAF' + [char]0xC9 + '2'),
        ([string][char]0x3A9 + [char]0x3C9 + '_greek'),
        ([string][char]0x65E5 + [char]0x672C + [char]0x8A9E),
        ([string][char]0x41F + [char]0x440 + [char]0x438 + [char]0x432 + [char]0x435 + [char]0x442),
        ('emoji_' + [char]::ConvertFromUtf32(0x1F600)),
        ('ligature_' + [char]0xFB01),
        'trailing_dot_ok.', 'with space', 'a.b.c.d.e',
        ('x' * 200), ('y' * 255)
    )
    $i = 0
    foreach ($n in $names) {
        try { [System.IO.File]::WriteAllText((Join-Path $u $n), "u$i", $utf8) }
        catch { Write-Log "Could not create unicode name #$i : $($_.Exception.Message)" 'WARN' }
        $i++
    }
    # many short names that differ only by case-folding behaviour of the upcase table
    foreach ($c in 0x61..0x7A) {
        [System.IO.File]::WriteAllText((Join-Path $u ('Mix{0}{1}' -f [char]$c, [char]($c - 32))), 'm', $utf8)
    }

    Write-Log 'Populate: MFT churn (create + delete)'
    $c = Join-Path $r 'churn'
    [void](New-Item -ItemType Directory -Path $c)
    for ($i = 0; $i -lt ($SmallFileCount * 2); $i++) {
        [System.IO.File]::WriteAllText((Join-Path $c ("c$i.tmp")), ('x' * (($i % 700) + 1)), $utf8)
    }
    for ($i = 0; $i -lt ($SmallFileCount * 2); $i += 2) { Remove-Item -LiteralPath (Join-Path $c "c$i.tmp") -Force }
    # reuse freed records
    for ($i = 0; $i -lt $SmallFileCount; $i++) {
        [System.IO.File]::WriteAllText((Join-Path $c ("r$i.tmp")), "reuse $i", $utf8)
    }

    Write-Log 'Populate: resident vs non-resident boundary sizes'
    $b = Join-Path $r 'sizes'
    [void](New-Item -ItemType Directory -Path $b)
    foreach ($sz in @(0, 1, 100, 500, 600, 700, 900, 1000, 1500, 3000, 4095, 4096, 4097, 65535, 65536, 65537, 1048576)) {
        New-RandomFile (Join-Path $b ("size_$sz.bin")) $sz $sz
    }

    Write-Log "Populate: fragmented files ($FragmentedFileCount x $FragmentedFileSizeMB MB, interleaved)"
    $f = Join-Path $r 'fragmented'
    [void](New-Item -ItemType Directory -Path $f)
    $streams = @()
    try {
        for ($i = 0; $i -lt $FragmentedFileCount; $i++) {
            $streams += [System.IO.File]::Create((Join-Path $f ("frag_$i.bin")))
        }
        $rng = New-Object System.Random(1234)
        $chunk = New-Object byte[] 64KB
        $rounds = [int](($FragmentedFileSizeMB * 1MB) / $chunk.Length)
        for ($k = 0; $k -lt $rounds; $k++) {
            foreach ($s in $streams) { $rng.NextBytes($chunk); $s.Write($chunk, 0, $chunk.Length) }
        }
    }
    finally { foreach ($s in $streams) { $s.Dispose() } }

    Write-Log 'Populate: sparse files'
    $sp = Join-Path $r 'sparse'
    [void](New-Item -ItemType Directory -Path $sp)
    $spFile = Join-Path $sp 'sparse_big.bin'
    $fs = [System.IO.File]::Create($spFile); $fs.Dispose()
    & fsutil.exe sparse setflag $spFile | Out-Null
    $fs = [System.IO.File]::Open($spFile, 'Open', 'Write')
    try {
        $fs.SetLength(256MB)
        $fs.Position = 0;       $fs.Write([byte[]](1..200), 0, 200)
        $fs.Position = 100MB;   $fs.Write([byte[]](1..200), 0, 200)
        $fs.Position = 256MB - 200; $fs.Write([byte[]](1..200), 0, 200)
    }
    finally { $fs.Dispose() }
    # a fully sparse file
    $spFile2 = Join-Path $sp 'sparse_all_holes.bin'
    $fs = [System.IO.File]::Create($spFile2); $fs.SetLength(64MB); $fs.Dispose()
    & fsutil.exe sparse setflag $spFile2 | Out-Null
    & fsutil.exe sparse setrange $spFile2 0 (64MB) | Out-Null

    if ($script:ActualCluster -le 4096) {
        Write-Log 'Populate: LZNT1-compressed files'
        $cp = Join-Path $r 'compressed'
        [void](New-Item -ItemType Directory -Path $cp)
        $text = ('The quick brown fox jumps over the lazy dog. ' * 4000)
        for ($i = 0; $i -lt 6; $i++) {
            [System.IO.File]::WriteAllText((Join-Path $cp "text_$i.txt"), ($text * ($i + 1)), $utf8)
        }
        New-RandomFile (Join-Path $cp 'incompressible.bin') 3MB 99
        # mixed: compressible head, random tail, compressible tail
        $mixed = Join-Path $cp 'mixed.bin'
        $fs = [System.IO.File]::Create($mixed)
        try {
            $t = $utf8.GetBytes($text); $fs.Write($t, 0, $t.Length)
            $rb = New-Object byte[] 2MB; (New-Object System.Random(7)).NextBytes($rb); $fs.Write($rb, 0, $rb.Length)
            $fs.Write($t, 0, $t.Length)
        }
        finally { $fs.Dispose() }
        & compact.exe /c /s:$cp /i /q | Out-Null
    }
    else {
        Write-Log 'Skipping compression (NTFS cannot compress when cluster > 4096)' 'WARN'
    }

    if ($IncludeEfs) {
    Write-Log 'Populate: EFS (may be unavailable on Home editions)'
    try {
        $ef = Join-Path $r 'efs'
        [void](New-Item -ItemType Directory -Path $ef)
        New-RandomFile (Join-Path $ef 'secret_small.bin') 2000 5
        New-RandomFile (Join-Path $ef 'secret_big.bin') 5MB 6
        $encSparse = Join-Path $ef 'secret_sparse.bin'
        $fs = [System.IO.File]::Create($encSparse); $fs.SetLength(32MB); $fs.Dispose()
        & fsutil.exe sparse setflag $encSparse | Out-Null
        $fs = [System.IO.File]::Open($encSparse, 'Open', 'Write'); $fs.Position = 10MB; $fs.Write([byte[]](9, 9, 9), 0, 3); $fs.Dispose()
        foreach ($file in Get-ChildItem -LiteralPath $ef -File) {
            & cipher.exe /e /a $file.FullName | Out-Null
        }
    }
    catch { Write-Log "EFS population failed: $($_.Exception.Message)" 'WARN' }
    }

    Write-Log 'Populate: links, reparse points, ADS'
    $l = Join-Path $r 'links'
    [void](New-Item -ItemType Directory -Path $l)
    $target = Join-Path $l 'target.txt'
    [System.IO.File]::WriteAllText($target, 'link target', $utf8)
    $sub = Join-Path $l 'subdir'
    [void](New-Item -ItemType Directory -Path $sub)
    [System.IO.File]::WriteAllText((Join-Path $sub 'inner.txt'), 'inner', $utf8)
    & cmd.exe /c "fsutil hardlink create `"$l\hardlink1.txt`" `"$target`"" | Out-Null
    & cmd.exe /c "fsutil hardlink create `"$l\hardlink2.txt`" `"$target`"" | Out-Null
    & cmd.exe /c "mklink `"$l\sym_rel_file.txt`" target.txt" | Out-Null
    & cmd.exe /c "mklink `"$l\sym_abs_file.txt`" `"$target`"" | Out-Null
    & cmd.exe /c "mklink /D `"$l\sym_rel_dir`" subdir" | Out-Null
    & cmd.exe /c "mklink /D `"$l\sym_up`" ..\sizes" | Out-Null
    & cmd.exe /c "mklink `"$l\sym_chain1.txt`" sym_rel_file.txt" | Out-Null
    & cmd.exe /c "mklink `"$l\sym_chain2.txt`" sym_chain1.txt" | Out-Null
    & cmd.exe /c "mklink /J `"$l\junction`" `"$sub`"" | Out-Null
    Set-Content -LiteralPath $target -Stream 'ads1' -Value 'alternate stream one'
    Set-Content -LiteralPath $target -Stream 'ads_big' -Value ('A' * 5000)
    Set-Content -LiteralPath $sub -Stream 'folder_ads' -Value 'streams on folders too'

    Write-Log 'Populate: many hard links (attribute-list pressure)'
    $hl = Join-Path $r 'manylinks'
    [void](New-Item -ItemType Directory -Path $hl)
    $hlTarget = Join-Path $hl 'base.txt'
    [System.IO.File]::WriteAllText($hlTarget, 'base', $utf8)
    for ($i = 0; $i -lt 150; $i++) {
        & cmd.exe /c "fsutil hardlink create `"$hl\link_with_a_reasonably_long_name_$i.txt`" `"$hlTarget`"" | Out-Null
    }
    # many ADS on one file
    for ($i = 0; $i -lt 60; $i++) {
        Set-Content -LiteralPath $hlTarget -Stream ("stream_{0}" -f $i) -Value ('S' * 300)
    }

    Write-Log 'Populate: attributes / timestamps'
    $at = Join-Path $r 'attrs'
    [void](New-Item -ItemType Directory -Path $at)
    foreach ($n in @('hidden.txt', 'system.txt', 'readonly.txt')) {
        [System.IO.File]::WriteAllText((Join-Path $at $n), $n, $utf8)
    }
    # Timestamps first: a read-only file refuses timestamp changes.
    (Get-Item (Join-Path $at 'readonly.txt')).CreationTimeUtc = [datetime]'1999-12-31T23:59:59Z'
    (Get-Item (Join-Path $at 'hidden.txt')).Attributes = 'Hidden'
    (Get-Item (Join-Path $at 'readonly.txt')).Attributes = 'ReadOnly'
    (Get-Item (Join-Path $at 'system.txt')).Attributes = 'Hidden, System'
}

function Write-Manifest([string]$Root, [string]$OutFile) {
    $sha = [System.Security.Cryptography.SHA256]::Create()
    $rootFull = $Root.TrimEnd('\')
    $rows = New-Object System.Collections.Generic.List[object]
    $items = Get-ChildItem -LiteralPath $Root -Recurse -Force -ErrorAction SilentlyContinue |
        Where-Object { $_.FullName -notlike '*\System Volume Information*' -and $_.FullName -notlike '*\$RECYCLE.BIN*' }
    foreach ($it in $items) {
        $rel = $it.FullName.Substring($rootFull.Length)
        $isDir = $it.PSIsContainer
        $hash = ''
        if (-not $isDir -and -not ($it.Attributes -band [IO.FileAttributes]::ReparsePoint)) {
            try {
                $fs = [System.IO.File]::Open($it.FullName, 'Open', 'Read', 'ReadWrite')
                try { $hash = ([BitConverter]::ToString($sha.ComputeHash($fs))).Replace('-', '') }
                finally { $fs.Dispose() }
            }
            catch { $hash = 'UNREADABLE' }
        }
        $length = if ($isDir) { 0 } else { $it.Length }
        $rows.Add([pscustomobject]@{
                Path = $rel; IsDirectory = $isDir; Length = $length
                Attributes = [int]$it.Attributes; CreationUtc = $it.CreationTimeUtc.ToString('o')
                Sha256 = $hash
            })
    }
    $rows | Export-Csv -Path $OutFile -NoTypeInformation -Encoding UTF8
    return $rows.Count
}

# ---------------------------------------------------------------- run one volume
function Invoke-ParserForVolume($ctx) {
    if (-not $ParserCommand) { return @{ Ran = $false; ExitCode = 0; Output = '' } }
    $cmdline = $ParserCommand
    foreach ($k in $ctx.Keys) { $cmdline = $cmdline.Replace('{' + $k + '}', [string]$ctx[$k]) }
    $cmdFile = Join-Path $ctx['LogDir'] 'run-parser.cmd'
    Set-Content -Path $cmdFile -Value ("@echo off`r`n" + $cmdline + "`r`n") -Encoding ASCII
    Write-Log "Parser: $cmdline"
    $outFile = Join-Path $ctx['LogDir'] 'parser.txt'
    $p = Start-Process -FilePath 'cmd.exe' -ArgumentList @('/c', "`"$cmdFile`"") -NoNewWindow -Wait -PassThru `
        -RedirectStandardOutput $outFile -RedirectStandardError ($outFile + '.err')
    return @{ Ran = $true; ExitCode = $p.ExitCode; Output = $outFile }
}

function Test-OneVolume {
    param($Combo, [string]$VhdPath, [bool]$Generated, [string]$LogDir)

    $result = [ordered]@{
        Name = $Combo.Name; Status = 'FAIL'; Notes = ''
        ReqSector = $Combo.Logical; ReqPhysical = $Combo.Physical; ReqCluster = $Combo.Cluster; ReqFrs = $Combo.Frs
        ActSector = 0; ActPhysical = 0; ActCluster = 0; ActFrs = 0
        Chkdsk = ''; Files = 0; ParserExit = ''; Vhd = $VhdPath
    }
    $notes = New-Object System.Collections.Generic.List[string]
    $mounted = $false
    $letter = $null
    try {
        if ($Generated) {
            if (Test-Path $VhdPath) {
                # A leftover mount (e.g. from an aborted run) keeps the file locked.
                try { Dismount-VHD -Path $VhdPath -ErrorAction SilentlyContinue } catch { }
                $removed = $false
                for ($try = 1; $try -le 5 -and -not $removed; $try++) {
                    try { Remove-Item -LiteralPath $VhdPath -Force -ErrorAction Stop; $removed = $true }
                    catch { Write-Log "VHDX still locked (attempt $try): $($_.Exception.Message)" 'WARN'; Start-Sleep -Seconds 2 }
                }
                if (-not $removed) { throw "Could not delete existing $VhdPath" }
            }
            $vhdParams = @{
                Path = $VhdPath; SizeBytes = $SizeBytes
                LogicalSectorSizeBytes = $Combo.Logical; PhysicalSectorSizeBytes = $Combo.Physical
            }
            if ($Fixed) { $vhdParams['Fixed'] = $true } else { $vhdParams['Dynamic'] = $true }
            Write-Log "New-VHD $VhdPath"
            [void](New-VHD @vhdParams)
            $disk = Mount-VHD -Path $VhdPath -Passthru | Get-Disk
            $mounted = $true
            Initialize-Disk -Number $disk.Number -PartitionStyle GPT
            $part = New-Partition -DiskNumber $disk.Number -UseMaximumSize -AssignDriveLetter
            $fmt = @{
                DriveLetter = $part.DriveLetter; FileSystem = 'NTFS'; AllocationUnitSize = $Combo.Cluster
                NewFileSystemLabel = $Combo.Name; Confirm = $false; Force = $true
            }
            if ($Combo.Frs -eq 4096) { $fmt['UseLargeFRS'] = $true }
            [void](Format-Volume @fmt)
        }
        else {
            $disk = Mount-VHD -Path $VhdPath -ReadOnly -Passthru | Get-Disk
            $mounted = $true
            $part = Get-Partition -DiskNumber $disk.Number | Where-Object { $_.DriveLetter } | Select-Object -First 1
            if (-not $part) { throw 'No partition with a drive letter found in the existing VHD.' }
        }

        $letter = [string]$part.DriveLetter
        $root = $letter + ':\'
        $geo = Get-NtfsGeometry $letter
        Set-Content -Path (Join-Path $LogDir 'ntfsinfo.txt') -Value $geo.Raw
        $result.ActSector = $geo.Sector; $result.ActPhysical = $geo.Physical
        $result.ActCluster = $geo.Cluster; $result.ActFrs = $geo.Frs
        $script:ActualCluster = $geo.Cluster
        Write-Log ("Actual geometry: sector={0} physical={1} cluster={2} frs={3}" -f $geo.Sector, $geo.Physical, $geo.Cluster, $geo.Frs)

        if ($Generated) {
            if ($geo.Cluster -ne $Combo.Cluster) { $notes.Add("cluster mismatch (wanted $($Combo.Cluster), got $($geo.Cluster))"); Write-Log $notes[-1] 'WARN' }
            if ($geo.Sector -ne $Combo.Logical) { $notes.Add("sector mismatch (wanted $($Combo.Logical), got $($geo.Sector))"); Write-Log $notes[-1] 'WARN' }
            if ($geo.Frs -ne $Combo.Frs) { $notes.Add("FRS mismatch (wanted $($Combo.Frs), got $($geo.Frs))"); Write-Log $notes[-1] 'WARN' }
        }

        if ($Generated -and $Populate) {
            $script:CurrentLog = Join-Path $LogDir 'content.log'
            Add-TestContent $root
            $script:CurrentLog = Join-Path $LogDir 'run.log'
            Write-VolumeCache -DriveLetter $letter
        }

        if (-not $SkipChkdsk) {
            Write-Log 'chkdsk (read-only)'
            $ck = & chkdsk.exe ("{0}:" -f $letter) 2>&1 | Out-String
            Set-Content -Path (Join-Path $LogDir 'chkdsk.txt') -Value $ck
            if ($LASTEXITCODE -eq 0) { $result.Chkdsk = 'clean' } else { $result.Chkdsk = "exit $LASTEXITCODE"; $notes.Add("chkdsk exit $LASTEXITCODE"); Write-Log $notes[-1] 'WARN' }
        }

        $manifest = Join-Path $LogDir 'manifest.csv'
        Write-Log 'Writing manifest (SHA-256 of every file)'
        $result.Files = Write-Manifest $root $manifest

        $parserExit = 0
        if (-not $SkipDismount) {
            Write-Log 'Dismounting the filesystem (flush + release for raw reads)'
            & fsutil.exe volume dismount ("{0}:" -f $letter) | Out-Null
        }

        $ctx = @{
            RawVolume = '\\.\' + $letter + ':'; PhysicalDrive = '\\.\PhysicalDrive' + $disk.Number
            PartitionOffset = $part.Offset; DriveLetter = $letter; VhdPath = $VhdPath; Manifest = $manifest
            LogDir = $LogDir; Name = $Combo.Name; SectorSize = $geo.Sector; ClusterSize = $geo.Cluster; FrsSize = $geo.Frs
        }
        $pr = Invoke-ParserForVolume $ctx
        if ($pr.Ran) {
            $parserExit = $pr.ExitCode
            $result.ParserExit = [string]$parserExit
            if ($parserExit -ne 0) { $notes.Add("parser exit $parserExit") }
        }

        $ok = ($notes.Count -eq 0) -or ($pr.Ran -and $parserExit -eq 0 -and -not ($notes | Where-Object { $_ -notlike 'chkdsk*' -and $_ -notlike '*mismatch*' }))
        # geometry mismatches and chkdsk problems never silently pass
        if ($notes.Count -gt 0) { $ok = $false }
        $result.Status = if ($ok) { 'PASS' } else { 'FAIL' }
    }
    catch {
        $notes.Add($_.Exception.Message)
        Write-Log $_.Exception.Message 'FAIL'
        $result.Status = 'ERROR'
    }
    finally {
        if ($mounted) { try { Dismount-VHD -Path $VhdPath -ErrorAction SilentlyContinue } catch { }; Start-Sleep -Milliseconds 500 }
        $result.Notes = ($notes -join '; ')
        if ($Generated -and $result.Status -eq 'PASS' -and -not $KeepVhds) {
            try { Remove-Item -LiteralPath $VhdPath -Force; $result.Vhd = '(deleted)' } catch { }
        }
    }
    return [pscustomobject]$result
}

# ---------------------------------------------------------------- main
$script:CurrentLog = $null
$script:ActualCluster = 4096

if ($ExistingVhdPath) {
    if (-not (Test-Path -LiteralPath $ExistingVhdPath)) { throw "VHD not found: $ExistingVhdPath" }
    $combos = @([pscustomobject]@{ Name = [IO.Path]::GetFileNameWithoutExtension($ExistingVhdPath); Logical = 0; Physical = 0; Cluster = 0; Frs = 0 })
}
else {
    $combos = @(Get-Combinations)
}

Write-Host ("Planned volumes: {0}" -f $combos.Count)
$combos | Format-Table Name, Logical, Physical, Cluster, Frs -AutoSize | Out-Host
if ($ListOnly) { return }
if ($combos.Count -eq 0) { Write-Host 'Nothing to do.'; return }

if (-not $ExistingVhdPath) {
    $needed = $combos.Count * 50MB
    Write-Host ("Note: dynamic VHDX files grow with content; keep a few GB free per volume. Failing volumes are kept.")
}

[void](New-Item -ItemType Directory -Force -Path $VhdDirectory)
$reportDir = Split-Path -Parent $ReportPath
if ($reportDir) { [void](New-Item -ItemType Directory -Force -Path $reportDir) }
$all = @()

foreach ($combo in $combos) {
    $logDir = Join-Path (Join-Path $VhdDirectory 'logs') $combo.Name
    [void](New-Item -ItemType Directory -Force -Path $logDir)
    $script:CurrentLog = Join-Path $logDir 'run.log'
    Write-Log ("===== {0} =====" -f $combo.Name)

    if ($ExistingVhdPath) { $vhd = (Resolve-Path -LiteralPath $ExistingVhdPath).Path; $gen = $false }
    else { $vhd = Join-Path $VhdDirectory ($combo.Name + '.vhdx'); $gen = $true }

    $r = Test-OneVolume -Combo $combo -VhdPath $vhd -Generated $gen -LogDir $logDir
    Write-Log ("{0}: {1} {2}" -f $combo.Name, $r.Status, $r.Notes) $(if ($r.Status -eq 'PASS') { 'PASS' } else { 'FAIL' })
    $all += $r
    $all | Export-Csv -Path $ReportPath -NoTypeInformation -Encoding UTF8
}

$script:CurrentLog = $null
Write-Host ''
$all | Format-Table Name, Status, ActSector, ActCluster, ActFrs, Chkdsk, ParserExit, Notes -AutoSize | Out-Host
$bad = @($all | Where-Object { $_.Status -ne 'PASS' }).Count
Write-Host ("Report: {0}" -f $ReportPath)
Write-Host ("{0} of {1} volumes passed." -f ($all.Count - $bad), $all.Count)
if ($bad -gt 0) { exit 1 }
