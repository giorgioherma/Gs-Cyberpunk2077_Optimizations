param(
    [Parameter(Mandatory=$true)][string]$FixtureZip,
    [Parameter(Mandatory=$true)][string]$PrivateSourceRoot,
    [Parameter(Mandatory=$true)][string]$ResolverDll,
    [Parameter(Mandatory=$true)][string]$SourceAuditJson,
    [Parameter(Mandatory=$true)][string]$OwnerCompilerScript,
    [Parameter(Mandatory=$true)][string]$CompilerArchive,
    [Parameter(Mandatory=$true)][string]$CleanBaselineArchive,
    [Parameter(Mandatory=$true)][string]$EffectiveCacheFile
)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$fixture = (Resolve-Path -LiteralPath $FixtureZip -ErrorAction Stop).Path
$game = (Resolve-Path -LiteralPath $PrivateSourceRoot -ErrorAction Stop).Path
$dll = (Resolve-Path -LiteralPath $ResolverDll -ErrorAction Stop).Path
$auditFile = (Resolve-Path -LiteralPath $SourceAuditJson -ErrorAction Stop).Path
$sourceAudit = Get-Content -LiteralPath $auditFile -Raw | ConvertFrom-Json
$expectedSourceHash = [string]$sourceAudit.sourceStackSha256
if ($expectedSourceHash -notmatch '^[A-Fa-f0-9]{64}$' -or
    [int]$sourceAudit.sourceFiles -le 0 -or
    $sourceAudit.codeRewritten -ne $false) {
    throw 'Real GRSP replay requires a verified read-only current-source admission.'
}
if (!(Test-Path -LiteralPath (Join-Path $game 'r6/scripts') -PathType Container)) {
    throw 'The private source checkout has no live r6/scripts tree.'
}

# Capture is pinned to an immutable GCO fixture commit, not merged into this
# feature branch. Never put private source or analysis in the release artifact.
$work = Join-Path $env:RUNNER_TEMP ('grsp-r3-real-' + [Guid]::NewGuid().ToString('N'))
$unpack = Join-Path $work 'capture'
$result = Join-Path $work 'private-grsp-r3-analysis.json'
$diagnostic = Join-Path $work 'private-grsp-r3-cli.log'
New-Item -ItemType Directory -Force -Path $unpack | Out-Null
try {
    $zipBefore = (Get-FileHash -LiteralPath $fixture -Algorithm SHA256).Hash
    Expand-Archive -LiteralPath $fixture -DestinationPath $unpack -ErrorAction Stop
    $indexes = @(Get-ChildItem -LiteralPath $unpack -Recurse -File -Filter 'collection.json')
    if ($indexes.Count -ne 1) {
        throw 'Real fixture must contain exactly one sealed GCO collection index.'
    }
    $capture = $indexes[0].Directory.FullName
    $index = Get-Content -LiteralPath $indexes[0].FullName -Raw |
        ConvertFrom-Json -Depth 50
    if ($null -eq $index.Redscript -or $null -eq $index.FrameTimeEvidence -or
        [int]$index.PresentMonAlignedFrames -le 0) {
        throw 'Real capture lacks indexed REDscript + genuine PresentMon evidence.'
    }
    $chronology = Join-Path $capture 'Reports/CET/PresentMon/GCO-PresentMon-Frames.v1.json'
    $calls = Join-Path $capture 'G-REDscript/Data/Developer/RSP_CallSites.csv'
    $targets = Join-Path $capture 'G-REDscript/Data/Developer/RSP_CallbackTargets.csv'
    $pressure = Join-Path $capture 'G-REDscript/Data/Developer/RSP_CallbackPressure.csv'
    foreach ($file in @($chronology, $calls, $targets, $pressure)) {
        if (!(Test-Path -LiteralPath $file -PathType Leaf)) {
            throw 'Real fixture is missing a required native evidence source.'
        }
    }
    $callRows = @(Import-Csv -LiteralPath $calls)
    $expectedRows = $callRows.Count
    $uniqueIds = [System.Collections.Generic.HashSet[string]]::new(
        [System.StringComparer]::Ordinal)
    foreach ($row in $callRows) {
        if ([string]::IsNullOrWhiteSpace([string]$row.stable_callsite_id)) {
            throw 'Native fixture callsite identity is missing.'
        }
        [void]$uniqueIds.Add([string]$row.stable_callsite_id)
    }
    if ($expectedRows -eq 0 -or $uniqueIds.Count -eq 0) {
        throw 'Real GRSP fixture has no callsite observations.'
    }

    # Invoke GRSP's actual .NET10 CLI, not a shadow parser. The original
    # indexed files, current source identity and QPC chronology are proven
    # inside the owner code. Diagnostics stay runner-private.
    $arguments = @(
        'analyze', '--capture', $capture, '--game', $game,
        '--out', $result, '--frame-evidence', $chronology,
        '--expected-source-sha256', $expectedSourceHash
    )
    & dotnet $dll @arguments *> $diagnostic
    if ($LASTEXITCODE -ne 0 -or !(Test-Path -LiteralPath $result -PathType Leaf)) {
        $last = @(Get-Content -LiteralPath $diagnostic -ErrorAction SilentlyContinue |
            Where-Object { [string]$_ -match 'GRSP Resolver refused:' } |
            Select-Object -Last 1)
        $detail = if ($last.Count -eq 1) { [string]$last[0] } else { 'no diagnostic' }
        foreach ($private in @($work, $unpack, $capture, $game, $fixture)) {
            if (![string]::IsNullOrEmpty($private)) {
                $detail = $detail.Replace($private, '[PATH]')
            }
        }
        $detail = [regex]::Replace($detail, '(?i)[a-z]:\\[^ \r\n]*', '[PATH]')
        if ($detail.Length -gt 240) { $detail = $detail.Substring(0, 240) }
        throw ('GRSP_REAL_CAPTURE_ANALYZE_FAILED: ' + $detail)
    }

    $stream = [System.IO.File]::OpenRead($result)
    try {
        $doc = [System.Text.Json.JsonDocument]::Parse($stream)
        try {
            $root = $doc.RootElement
            $counts = $root.GetProperty('counts')
            $frame = $root.GetProperty('frameEvidence')
            $source = $root.GetProperty('source')
            $native = $root.GetProperty('capture')
            $raw = $root.GetProperty('rawTables')

            $rows = $counts.GetProperty('rawCallsiteRows').GetInt32()
            $distinct = $counts.GetProperty('distinctCallsiteIds').GetInt32()
            $repeated = $counts.GetProperty('repeatedCallsiteIdRows').GetInt32()
            $frames = $frame.GetProperty('sourceRowsVerifiedAgainstOriginalCsv').GetInt32()
            $rawCallsiteRows = $raw.GetProperty(
                'Developer/RSP_CallSites.csv').GetProperty('rows').GetArrayLength()
            $rawTargets = $raw.GetProperty(
                'Developer/RSP_CallbackTargets.csv').GetProperty('rows').GetArrayLength()
            $rawPressure = $raw.GetProperty(
                'Developer/RSP_CallbackPressure.csv').GetProperty('rows').GetArrayLength()

            if ($root.GetProperty('schemaVersion').GetString() -ne 'grsp.resolver.input.v1' -or
                $root.GetProperty('generationAuthorized').GetBoolean() -ne $false -or
                $native.GetProperty('collectionProvenance').GetString() -ne 'GCO_INDEXED_COLLECTED_BYTES_VERIFIED' -or
                $frame.GetProperty('provider').GetString() -ne 'PRESENTMON_2_6' -or
                $frame.GetProperty('nativeF11QpcUnixAnchorsVerified').GetBoolean() -ne $true -or
                $frames -ne [int]$index.PresentMonAlignedFrames -or
                $source.GetProperty('sourceStackSha256').GetString() -ne $expectedSourceHash -or
                $source.GetProperty('fileCount').GetInt32() -ne [int]$sourceAudit.sourceFiles -or
                $rows -ne $expectedRows -or
                $rows -ne $rawCallsiteRows -or
                $distinct -ne $uniqueIds.Count -or
                $repeated -ne ($rows - $distinct) -or
                $rawTargets -le 0 -or $rawPressure -le 0 -or
                $root.GetProperty('sourceObservations').GetArrayLength() -ne $rows) {
                throw 'GRSP_REAL_CAPTURE_ANALYSIS_CONTRACT_FAILED: inconsistent native evidence or unsafe admission.'
            }
            $statuses = $counts.GetProperty('sourceMatchStatus')
            if ($statuses.GetProperty('UNRESOLVED_WRAPPER').GetInt32() -lt 1 -or
                $statuses.GetProperty(
                    'LINE_METHOD_NAME_MATCH_RECEIVER_UNVERIFIED').GetInt32() -lt 1) {
                throw 'GRSP_REAL_CAPTURE_SOURCE_MAPPING_MISSING: synthetic wrappers or live mapping not distinguished.'
            }
            if ($root.GetProperty('safety').GetProperty('cpuDomainsAdditive').GetBoolean()) {
                throw 'GRSP measurements must never be summed with CET CPU.'
            }

            Write-Host ("PASS GRSP real .NET10 capture replay: nativeRows=" + $rows +
                " distinctCallsites=" + $distinct + " repeatedRows=" + $repeated +
                " originalPresentMonRows=" + $frames +
                " indexedSourceFiles=" + $source.GetProperty('fileCount').GetInt32() +
                " callbackTargets=" + $rawTargets +
                " callbackPressure=" + $rawPressure +
                " sourceDriftRefused=true generationAuthorized=false")
        }
        finally { $doc.Dispose() }
    }
    finally { $stream.Dispose() }

    # R4/R5 real measured candidate preview: the GRSP owner handles
    # source matching and rewriting. A GCO test checks only structural,
    # read-only and provenance contracts, never produces a deployable ZIP.
    $previewFolder = Join-Path $work 'grsp-generic-preview'
    $previewLog = Join-Path $work 'grsp-generic-preview.log'
    & dotnet $dll preview-generic --game $game --analysis $result --out $previewFolder *> $previewLog
    if ($LASTEXITCODE -ne 0 -or
        !(Test-Path -LiteralPath (Join-Path $previewFolder 'preview.json') -PathType Leaf)) {
        throw 'GRSP_GENERIC_REAL_SOURCE_PREVIEW_FAILED: owner CLI refused the real fixture.'
    }
    $preview = Get-Content -LiteralPath (Join-Path $previewFolder 'preview.json') -Raw |
        ConvertFrom-Json -Depth 80
    $applied = @($preview.candidates | Where-Object {
        $_.state -eq 'PRISTINE_APPLICABLE' -or $_.state -eq 'PARTIALLY_APPLICABLE'
    })
    if ($preview.schemaVersion -ne 'grsp.generic.preview.v1' -or
        $preview.rule -ne 'CNAME_ARRAY_MEMBERSHIP_V1' -or
        $preview.sourceStackSha256 -ne $expectedSourceHash -or
        $preview.applicableFileCount -lt 1 -or
        $preview.stagedFiles -ne $preview.applicableFileCount -or
        $preview.stagedFiles -ne $applied.Count -or
        $preview.measuredOperatorEdgesInApplicableLoops -le 0 -or
        $preview.compilerValidated -ne $false -or
        $preview.generationAuthorized -ne $false -or
        $preview.deployable -ne $false) {
        throw 'GRSP_GENERIC_REAL_SOURCE_PREVIEW_CONTRACT_FAILED'
    }
    foreach ($candidate in $applied) {
        $relative = [string]$candidate.path
        if ([string]::IsNullOrWhiteSpace($relative) -or
            $relative.Contains('..') -or $relative.Contains(':') -or
            $relative.Contains('\\')) {
            throw 'GRSP_GENERIC_INVALID_SOURCE_OWNERSHIP'
        }
        $originalFile = Join-Path $game ('r6/scripts/' + $relative)
        $stagedFile = Join-Path $previewFolder ('overlay/r6/scripts/' + $relative)
        if (!(Test-Path -LiteralPath $originalFile -PathType Leaf) -or
            !(Test-Path -LiteralPath $stagedFile -PathType Leaf)) {
            throw 'GRSP_GENERIC_EXPECTED_PREVIEW_OVERLAY_MISSING'
        }
        $originalText = Get-Content -LiteralPath $originalFile -Raw
        $stagedText = Get-Content -LiteralPath $stagedFile -Raw
        if ($stagedText -notmatch 'GRSP-GENERIC:CNAME_ARRAY_MEMBERSHIP_V1' -or
            $stagedText -notmatch 'ArrayContains' -or
            (Get-FileHash -LiteralPath $originalFile -Algorithm SHA256).Hash -eq
            (Get-FileHash -LiteralPath $stagedFile -Algorithm SHA256).Hash) {
            throw 'GRSP_GENERIC_PREVIEW_OUTPUT_NOT_STRUCTURALLY_DISTINCT'
        }
    }
    Write-Host ('PASS GRSP generic measured source preview: files=' +
                $preview.stagedFiles + ' observedLoopOperatorEdges=' +
                $preview.measuredOperatorEdgesInApplicableLoops +
                ' compilerValidated=false deployable=false originalSourceUnchanged=true')

    # GRSP owns the actual isolated SCC 0.5.31 dual-compile contract.
    # The GCO harness must not duplicate its compiler logic or publish output.
    $ownerScript = (Resolve-Path -LiteralPath $OwnerCompilerScript -ErrorAction Stop).Path
    & $ownerScript -GameRoot $game -PreviewRoot $previewFolder -ResolverDll $dll -CompilerArchive $CompilerArchive -CleanBaselineArchive $CleanBaselineArchive -EffectiveCacheFile $EffectiveCacheFile
    if ($LASTEXITCODE -ne 0) {
        throw 'GRSP_OWNER_R6_SCC_FULL_SOURCE_COMPILATION_FAILED'
    }

    if ((Get-FileHash -LiteralPath $fixture -Algorithm SHA256).Hash -ne $zipBefore) {
        throw 'GRSP_REAL_CAPTURE_ARCHIVE_MUTATED'
    }
}
finally {
    if (Test-Path -LiteralPath $work) {
        Remove-Item -LiteralPath $work -Recurse -Force -ErrorAction Stop
    }
}
$global:LASTEXITCODE = 0
