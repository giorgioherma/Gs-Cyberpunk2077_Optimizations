param(
    [Parameter(Mandatory=$true)][string]$FixtureZip,
    [Parameter(Mandatory=$true)][string]$PackageRoot
)
$ErrorActionPreference='Stop'
Set-StrictMode -Version Latest
Add-Type -AssemblyName System.IO.Compression
Add-Type -AssemblyName System.IO.Compression.FileSystem

# Permanent development regression evidence, NEVER a release payload.
# Read archive in place; inspect only a disposable extraction. The original
# GCO collection.json contains absolute paths from its original installation:
# relocation MUST NOT silently rewrite that sealed evidence.
$FixtureZip=(Resolve-Path -LiteralPath $FixtureZip -ErrorAction Stop).Path
$PackageRoot=(Resolve-Path -LiteralPath $PackageRoot -ErrorAction Stop).Path
$zipHashBefore=(Get-FileHash -LiteralPath $FixtureZip -Algorithm SHA256).Hash
$expectedName='GCO-Capture-15112026_021913.zip'
if ((Split-Path $FixtureZip -Leaf) -ne $expectedName) {
    throw "Wrong real-capture fixture selected: expected $expectedName"
}
if (@(Get-ChildItem -LiteralPath $PackageRoot -Recurse -File -Filter $expectedName).Count -ne 0 -or
    (Test-Path -LiteralPath (Join-Path $PackageRoot 'tests/fixtures'))) {
    throw 'Real test fixture leaked into the GCO portable release.'
}
$limitBytes=2GB
$archive=[IO.Compression.ZipFile]::OpenRead($FixtureZip)
try {
    $total=[long]0
    $seen=[Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
    foreach ($entry in $archive.Entries) {
        $relative=$entry.FullName.Replace('\','/')
        $parts=$relative.TrimEnd('/').Split('/')
        if ([string]::IsNullOrWhiteSpace($relative) -or
            $relative.StartsWith('/') -or $relative.Contains(':') -or
            @($parts | Where-Object { $_ -in @('', '.', '..') }).Count -ne 0) {
            throw "Capture ZIP contains unsafe path: $relative"
        }
        if (!( $seen.Add($relative.TrimEnd('/')) )) {
            throw "Capture ZIP duplicates an entry after path normalization: $relative"
        }
        $unixKind=($entry.ExternalAttributes -shr 16) -band 0xF000
        if ($unixKind -eq 0xA000) { throw "Capture ZIP contains a symbolic link: $relative" }
        if ($entry.Length -lt 0 -or $entry.Length -gt $limitBytes) {
            throw "Capture ZIP entry uncompressed size outside safety cap: $relative"
        }
        $total += $entry.Length
        if ($total -gt $limitBytes) { throw 'Capture ZIP exceeds 2 GiB expanded size safety cap.' }
    }
    if ($seen.Count -lt 4) { throw 'Capture ZIP has too few entries to be a completed session.' }
} finally { $archive.Dispose() }

$stage=Join-Path $env:RUNNER_TEMP ('GCO-Capture-Regression-' + [Guid]::NewGuid().ToString('N'))
try {
    New-Item -ItemType Directory -Path $stage -Force | Out-Null
    [IO.Compression.ZipFile]::ExtractToDirectory($FixtureZip,$stage)
    $indexes=@(Get-ChildItem -LiteralPath $stage -Filter 'collection.json' -File -Recurse)
    if ($indexes.Count -ne 1) {
        throw "Expected exactly one real GCO collection.json in fixture, found $($indexes.Count)"
    }
    $session=$indexes[0].DirectoryName
    if ((Split-Path $session -Leaf) -notlike 'GCO-Capture-*' -and
        $session -ne $stage) {
        throw 'Unexpected real capture root: collection.json must identify one GCO capture directory.'
    }
    $index=Get-Content -LiteralPath $indexes[0].FullName -Raw | ConvertFrom-Json -Depth 70
    if ([string]$index.SessionId -notmatch '^[0-9a-fA-F]{32}$') {
        throw 'Real capture index has no valid GCO session identity.'
    }
    if ($null -eq $index.Cet -or $null -eq $index.Redscript -or
        $null -eq $index.FrameTimeEvidence -or
        $index.CollectionAttempted -ne $true) {
        throw 'Real fixture lacks completed CET + GRSP + indexed PresentMon collection evidence.'
    }
    # Independent evidence verification; no rebase or hand editing of original
    # GameRoot/FrameTimeCsv absolute paths and no false installed-mod inventory.
    function CheckPath([string]$Folder,[string]$Relative) {
        $path=$Relative.Replace('\','/')
        $parts=$path.Split('/')
        if (!$path -or $path.Contains(':') -or $path.StartsWith('/') -or
            @($parts | Where-Object { $_ -in @('', '.', '..') }).Count -gt 0) {
            throw "Unsafe indexed capture relative file: $Relative"
        }
        $candidate=[IO.Path]::GetFullPath((Join-Path $Folder $path))
        $base=[IO.Path]::GetFullPath($Folder).TrimEnd('\') + '\'
        if (!$candidate.StartsWith($base,[StringComparison]::OrdinalIgnoreCase)) {
            throw "Indexed evidence path escapes extraction: $Relative"
        }
        if (!(Test-Path -LiteralPath $candidate -PathType Leaf)) {
            throw "Indexed capture evidence missing: $Relative"
        }
        $info=Get-Item -LiteralPath $candidate
        if (($info.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) {
            throw "Linked indexed evidence is not permitted: $Relative"
        }
        return $candidate
    }
    function CheckOwner([object]$Owner,[string]$Domain,[string[]]$Directories,[string]$SummaryFile) {
        $directory=@($Directories | Where-Object {
            Test-Path -LiteralPath (Join-Path $session $_) -PathType Container
        })
        if ($directory.Count -ne 1) { throw "$Domain capture domain folder missing/ambiguous." }
        $folder=Join-Path $session $directory[0]
        $files=@($Owner.Files)
        if ($files.Count -lt 1 -or
            @($files | Where-Object { $_.Path -eq $SummaryFile }).Count -ne 1) {
            throw "$Domain real capture summary or file inventory missing."
        }
        $seenFiles=[Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
        foreach ($entry in $files) {
            $path=[string]$entry.Path
            if (!$seenFiles.Add($path)) { throw "Duplicate $Domain indexed file: $path" }
            $file=CheckPath $folder $path
            $actual=(Get-Item $file).Length
            if ($actual -ne [long]$entry.Bytes -or
                [string]$entry.Sha256 -notmatch '^[0-9a-fA-F]{64}$' -or
                (Get-FileHash -LiteralPath $file -Algorithm SHA256).Hash -ne [string]$entry.Sha256) {
                throw "Changed $Domain collected bytes/hash: $path"
            }
        }
        return $files.Count
    }
    $cetCount=CheckOwner $index.Cet 'CET' @('G-CET','CET') 'CET_Summary.json'
    $redCount=CheckOwner $index.Redscript 'GRSP' @('G-REDscript','G-RED','REDscript') 'GRSP_Summary.json'
    $pm=$index.FrameTimeEvidence
    $pmCsv=CheckPath $session ([string]$pm.Path)
    if ((Get-Item $pmCsv).Length -ne [long]$pm.Bytes -or
        [string]$pm.Sha256 -notmatch '^[0-9a-fA-F]{64}$' -or
        (Get-FileHash -LiteralPath $pmCsv -Algorithm SHA256).Hash -ne [string]$pm.Sha256) {
        throw 'Original F11-aligned PresentMon CSV differs from collection.json hash/length.'
    }
    $reader=[IO.StreamReader]::new($pmCsv)
    try {
        $header=$reader.ReadLine().TrimStart([char]0xFEFF)
        $columns=@($header.Split(',') | ForEach-Object { $_.Trim().Trim('"') })
        $app=[Array]::IndexOf($columns,'Application')
        $qpc=[Array]::IndexOf($columns,'CPUStartQPC')
        $localTime=[Array]::IndexOf($columns,'CPUStartDateTime')
        # PresentMon 2.6.0 emits MsBetweenPresents (milliseconds between
        # submitted Presents), not a synthetic FrameTime column. Do not
        # confuse this interval with displayed-frame duration or GPU time.
        $frameInterval=[Array]::IndexOf($columns,'MsBetweenPresents')
        $intervalField='MsBetweenPresents'
        # The sealed user capture can predate GCO's switch to --qpc_time.
        # Mirror the actual GCO collection consumer: QPC is preferred, but
        # valid historical --date_time CSVs retain local wall-clock evidence.
        # This check NEVER converts timezones or changes the original CSV.
        $clockIndex=if ($qpc -ge 0) { $qpc } else { $localTime }
        $clockKind=if ($qpc -ge 0) { 'QPC' } else { 'LocalDateTime' }
        if ($app -lt 0 -or $clockIndex -lt 0 -or $frameInterval -lt 0) {
            throw ('PresentMon fixture is missing Application, MsBetweenPresents or a supported ' +
                'CPUStartQPC/CPUStartDateTime clock; actual columns: ' + ($columns -join ', '))
        }
        $frames=0
        $finiteFrameTimes=0
        $invalidClocks=0
        $line=$null
        while ($null -ne ($line=$reader.ReadLine())) {
            $fields=$line.Split(',')
            if ($fields.Length -ne $columns.Count) {
                throw "PresentMon CSV row has $($fields.Length) columns, expected $($columns.Count)."
            }
            if ($fields[$app].Trim('"') -ine 'Cyberpunk2077.exe') { continue }
            $frames++
            $clockText=$fields[$clockIndex].Trim().Trim('"')
            if ($clockKind -eq 'QPC') {
                $clock=0L
                if (![long]::TryParse($clockText,
                    [Globalization.NumberStyles]::None,
                    [Globalization.CultureInfo]::InvariantCulture,[ref]$clock) -or
                    $clock -le 0) {
                    $invalidClocks++
                }
            } else {
                # Match the historical date-time format accepted by GCO's
                # PresentMonCaptureAlignment.TryLocalFrameUnixMs, but avoid
                # interpreting local timestamps in the CI runner's timezone.
                $match=[regex]::Match($clockText,
                    '^(?<day>\d{4}-\d{1,2}-\d{1,2})[ T](?<time>\d{1,2}:\d{2}:\d{2})(?:\.(?<fraction>\d{1,9}))?$')
                if (!$match.Success) {
                    $invalidClocks++
                } else {
                    $baseTime=$match.Groups['day'].Value + ' ' +
                        $match.Groups['time'].Value
                    $parsed=[datetime]::MinValue
                    if (![datetime]::TryParseExact($baseTime,
                        'yyyy-M-d H:mm:ss',
                        [Globalization.CultureInfo]::InvariantCulture,
                        [Globalization.DateTimeStyles]::None,[ref]$parsed)) {
                        $invalidClocks++
                    }
                }
            }
            $value=0.0
            if ([double]::TryParse($fields[$frameInterval].Trim('"'),
                  [Globalization.NumberStyles]::Float,
                  [Globalization.CultureInfo]::InvariantCulture,[ref]$value) -and
                  [double]::IsFinite($value) -and $value -ge 0) {
                $finiteFrameTimes++
            }
        }
        if ($frames -lt 2 -or $invalidClocks -ne 0 -or $finiteFrameTimes -lt 2) {
            throw "PresentMon invalid: frames=$frames invalid$clockKind=$invalidClocks finiteMs=$finiteFrameTimes"
        }
        Write-Host "PASS real PresentMon clock=$clockKind, interval=$intervalField, frames=$frames, valid clocks=$($frames-$invalidClocks), finite intervals=$finiteFrameTimes"
        if ($null -ne $index.PresentMonAlignedFrames -and
            $frames -ne [int]$index.PresentMonAlignedFrames) {
            throw "Aligned PresentMon frame index disagrees with actual retained Cyberpunk rows: index=$($index.PresentMonAlignedFrames), rows=$frames"
        }
    } finally { $reader.Dispose() }

    $handoffs=@(Get-ChildItem -LiteralPath $session -Filter 'CET_Resolver_Input.json' -File -Recurse -ErrorAction SilentlyContinue)
    if ($handoffs.Count -gt 1) { throw 'Real fixture contains ambiguous owner Resolver handoff data.' }
    $resolverInput='ABSENT'
    if ($handoffs.Count -eq 1) {
        $data=Get-Content -LiteralPath $handoffs[0].FullName -Raw | ConvertFrom-Json -Depth 40
        if ([string]::IsNullOrWhiteSpace([string]$data.schemaVersion) -or $null -eq $data.callbacks) {
            throw 'Existing CET owner Resolver input is malformed in real fixture.'
        }
        $resolverInput='PRESENT'
    }
    $zipHashAfter=(Get-FileHash -LiteralPath $FixtureZip -Algorithm SHA256).Hash
    if ($zipHashAfter -ne $zipHashBefore) {
        throw 'Committed real-capture fixture archive was modified during inspection.'
    }
    Write-Host "PASS real original GCO capture: indexed CET files=$cetCount, GRSP files=$redCount, retained PresentMon frames=$frames, finite frametimes=$finiteFrameTimes, CET owner handoff=$resolverInput, ZIP SHA256=$zipHashBefore"
    Write-Host 'PASS fixture never copied into GCO release directory, absolute index paths preserved unmodified, extracted only into runner temporary workspace'
} finally {
    if (Test-Path -LiteralPath $stage) {
        Remove-Item -LiteralPath $stage -Recurse -Force
    }
}
$global:LASTEXITCODE=0
