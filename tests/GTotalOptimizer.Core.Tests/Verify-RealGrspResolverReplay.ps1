param(
    [Parameter(Mandatory=$true)][string]$FixtureZip,
    [Parameter(Mandatory=$true)][string]$PrivateSourceRoot,
    [Parameter(Mandatory=$true)][string]$ResolverDll,
    [Parameter(Mandatory=$true)][string]$SourceAuditJson,
    [Parameter(Mandatory=$true)][string]$OwnerCompilerScript,
    [Parameter(Mandatory=$true)][string]$CompilerArchive,
    [Parameter(Mandatory=$true)][string]$CleanBaselineArchive,
    [Parameter(Mandatory=$true)][string]$EffectiveCacheArchive,
    [Parameter(Mandatory=$true)][string]$PluginsArchive
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

    # R4/R5 full installed-stack discovery uses GRSP's exact structural
    # matcher, not a GCO parser. Paths stay private in runner temp.
    $scanOutput = Join-Path $work 'private-grsp-generic-opportunities.json'
    $scanLog = Join-Path $work 'private-grsp-generic-scan.log'
    & dotnet $dll scan-generic --game $game --analysis $result --out $scanOutput *> $scanLog
    if ($LASTEXITCODE -ne 0 -or !(Test-Path -LiteralPath $scanOutput -PathType Leaf)) {
        throw 'GRSP_REAL_SOURCE_WIDE_SCAN_FAILED'
    }
    $scan = Get-Content -LiteralPath $scanOutput -Raw | ConvertFrom-Json -Depth 80
    $measuredCandidates = @($scan.candidates | Where-Object {
        $_.state -eq 'ELIGIBLE_MEASURED'
    })
    if ($scan.schemaVersion -ne 'grsp.generic.opportunities.v1' -or
        $scan.rule -ne 'CNAME_ARRAY_MEMBERSHIP_V1' -or
        $scan.sourceStackSha256 -ne $expectedSourceHash -or
        [int]$scan.sourceFiles -ne [int]$sourceAudit.sourceFiles -or
        $scan.evidenceKind -ne 'GRSP_SOURCE_MATCHED_OPERATOR_EDGES' -or
        [int]$scan.eligibleMeasured -lt 1 -or
        [int]$scan.eligibleMeasured -ne $measuredCandidates.Count -or
        [int]$scan.candidateLoopsExamined -ne
            ([int]$scan.eligibleMeasured + [int]$scan.eligibleUnmeasured +
             [int]$scan.rejectedUnproven) -or
        $scan.generationAuthorized -ne $false -or
        $scan.deployable -ne $false -or
        $scan.sourceModified -ne $false -or
        [int]$scan.generatedFiles -ne 0) {
        throw 'GRSP_REAL_SOURCE_WIDE_SCAN_CONTRACT_FAILED'
    }
    # First failed prerequisite comes from GRSP's authoritative matcher.
    # Public CI prints only aggregated reason codes, never private paths.
    $failureBuckets = @($scan.firstFailedPrerequisiteCounts.PSObject.Properties |
        Sort-Object Name)
    $failureSum = 0
    foreach ($failure in $failureBuckets) {
        if ($failure.Name -cnotmatch '^[A-Z_]+$' -or
            [int]$failure.Value -lt 1) {
            throw 'GRSP_GENERIC_INVALID_REJECTION_REASON'
        }
        $failureSum += [int]$failure.Value
        if (@($scan.candidates | Where-Object {
            $_.state -eq 'REJECTED_UNPROVEN' -and
            $_.reasonCode -ceq $failure.Name
        }).Count -ne [int]$failure.Value) {
            throw 'GRSP_GENERIC_REJECTION_COUNT_MISMATCH'
        }
    }
    if ($failureSum -ne [int]$scan.rejectedUnproven -or
        @($scan.candidates | Where-Object {
            $_.state -eq 'REJECTED_UNPROVEN'
        }).Count -ne $failureSum) {
        throw 'GRSP_GENERIC_REJECTION_ACCOUNTING_INCOMPLETE'
    }
    foreach($candidate in $applied) {
        $expectedPath = 'r6/scripts/' + [string]$candidate.path
        if (@($measuredCandidates | Where-Object { $_.path -ceq $expectedPath }).Count -eq 0) {
            throw 'GRSP_REAL_SOURCE_WIDE_MEASURED_PREVIEW_MISMATCH'
        }
    }
    $unmeasuredScan = Join-Path $work 'private-grsp-unmeasured-opportunities.json'
    $unmeasuredLog = Join-Path $work 'private-grsp-unmeasured-scan.log'
    & dotnet $dll scan-generic --game $game --out $unmeasuredScan *> $unmeasuredLog
    if ($LASTEXITCODE -ne 0 -or !(Test-Path -LiteralPath $unmeasuredScan -PathType Leaf)) {
        throw 'GRSP_REAL_SOURCE_WIDE_SOURCE_ONLY_SCAN_FAILED'
    }
    $plain = Get-Content -LiteralPath $unmeasuredScan -Raw | ConvertFrom-Json -Depth 80
    if ($plain.schemaVersion -ne 'grsp.generic.opportunities.v1' -or
        $plain.evidenceKind -ne 'NOT_CAPTURED' -or
        $plain.sourceStackSha256 -ne $expectedSourceHash -or
        (($plain.firstFailedPrerequisiteCounts | ConvertTo-Json -Compress) -cne
         ($scan.firstFailedPrerequisiteCounts | ConvertTo-Json -Compress)) -or
        [int]$plain.eligibleMeasured -ne 0 -or
        [int]$plain.structuralMatches -ne [int]$scan.structuralMatches -or
        [int]$plain.rejectedUnproven -ne [int]$scan.rejectedUnproven -or
        $plain.deployable -ne $false -or
        [int]$plain.generatedFiles -ne 0) {
        throw 'GRSP_REAL_SOURCE_WIDE_SOURCE_ONLY_INCONSISTENT'
    }
    $breakdown = ($failureBuckets | ForEach-Object {
        $_.Name + '=' + [int]$_.Value
    }) -join ','
    Write-Host ('PASS GRSP generic first-failed prerequisites: ' + $breakdown)
    Write-Host ('PASS GRSP source-wide generic scanner: files=' +
        $scan.sourceFiles + ' examined=' + $scan.candidateLoopsExamined +
        ' measured=' + $scan.eligibleMeasured + ' unmeasured=' +
        $scan.eligibleUnmeasured + ' rejected=' + $scan.rejectedUnproven +
        ' sourceOnlyEquivalent=true generated=0 liveSourcesUntouched=true')

    # CET's generic-first classification equivalent for REDscript must run
    # across the ENTIRE current stack, not only the 5 literal-array cases.
    # This additionally joins verified callsite evidence by source and method
    # without converting outgoing edges into callback entries or savings.
    $semanticReport = Join-Path $work 'gco-callback-semantics.json'
    $semanticLog = Join-Path $work 'gco-callback-semantics.log'
    & dotnet $dll resolve-callbacks --game $game --analysis $result --out $semanticReport *> $semanticLog
    if ($LASTEXITCODE -ne 0 -or !(Test-Path -LiteralPath $semanticReport -PathType Leaf)) {
        throw 'GCO_FULL_CALLBACK_SEMANTIC_REPLAY_FAILED'
    }
    $semantic = Get-Content -LiteralPath $semanticReport -Raw | ConvertFrom-Json -Depth 75
    if ($semantic.schemaVersion -ne 'gco.redscript.callback.semantics.v1' -or
        $semantic.sourceStackSha256 -ne $expectedSourceHash -or
        $semantic.sourceFiles -ne $sourceAudit.sourceFiles -or
        [int]$semantic.classifiedCallbacks -le 45 -or
        [int]$semantic.playerPuppetOnActionWrappers -lt 1 -or
        [int]$semantic.callbacksWithForwardedHelpers -lt 1 -or
        [int]$semantic.sourceMatchedMeasuredCallbacks -lt 1 -or
        $semantic.callbackEntryCountsKnown -ne $false -or
        $semantic.actionSelectorCompletenessProven -ne $false -or
        $semantic.generationAuthorized -ne $false -or
        $semantic.sourceWrites -ne $false) {
        throw 'GCO_FULL_CALLBACK_SEMANTIC_REPLAY_CONTRACT_FAILED'
    }
    Write-Host ('PASS GCO full REDscript callback/effect analysis: callbacks=' +
        $semantic.classifiedCallbacks + ' input=' + $semantic.onActionMethods +
        ' PlayerPuppetWraps=' + $semantic.playerPuppetOnActionWrappers +
        ' sourceMatchedMeasured=' + $semantic.sourceMatchedMeasuredCallbacks +
        ' helperForwarders=' + $semantic.callbacksWithForwardedHelpers +
        ' noUnprovenRouting=true noGeneratedFiles=true')

    # GCO-local CET-inspired callback generic: scan every private source,
    # stage only the exact proven fixed CName index shape, then reuse the SAME
    # official isolated SCC runner used by the established membership pass.
    $callbackScan = Join-Path $work 'gco-callback-scan.json'
    $callbackScanLog = Join-Path $work 'gco-callback-scan.log'
    & dotnet $dll scan-callbacks --game $game --out $callbackScan *> $callbackScanLog
    if ($LASTEXITCODE -ne 0 -or !(Test-Path -LiteralPath $callbackScan -PathType Leaf)) {
        throw 'GCO_CALLBACK_REAL_SOURCE_SCAN_FAILED'
    }
    $callbackReport = Get-Content -LiteralPath $callbackScan -Raw | ConvertFrom-Json -Depth 70
    if ($callbackReport.schemaVersion -ne 'gco.redscript.callback.scan.v1' -or
        $callbackReport.sourceStackSha256 -ne $expectedSourceHash -or
        $callbackReport.sourceFiles -ne $sourceAudit.sourceFiles -or
        $callbackReport.generationAuthorized -ne $false -or
        $callbackReport.sourceWrites -ne $false) {
        throw 'GCO_CALLBACK_REAL_SOURCE_SCAN_CONTRACT_FAILED'
    }
    $callbackCount = [int]$callbackReport.fixedCnameIndexEligible
    $callbackFolder = Join-Path $work 'gco-callback-fixed-index-preview'
    if ($callbackCount -gt 0) {
        $callbackPreviewLog = Join-Path $work 'gco-callback-preview.log'
        & dotnet $dll preview-callback-index --game $game --analysis $result --out $callbackFolder *> $callbackPreviewLog
        if ($LASTEXITCODE -ne 0) {
            throw 'GCO_CALLBACK_REAL_SOURCE_PREVIEW_FAILED'
        }
        $callbackScripts = Join-Path $callbackFolder 'r6/scripts'
        if (!(Test-Path -LiteralPath $callbackScripts -PathType Container)) {
            throw 'GCO_CALLBACK_REAL_SOURCE_PREVIEW_MISSING'
        }
        $callbackFiles = @(Get-ChildItem -LiteralPath $callbackScripts -Recurse -File)
        if ($callbackFiles.Count -lt 1 -or $callbackFiles.Count -gt $callbackCount) {
            throw 'GCO_CALLBACK_REAL_SOURCE_PREVIEW_COUNT_UNPROVEN'
        }
        foreach ($file in $callbackFiles) {
            if ($file.Extension -ne '.reds') { throw 'GCO_CALLBACK_PREVIEW_EXTENSION_UNSAFE' }
            $relative = [System.IO.Path]::GetRelativePath($callbackScripts, $file.FullName)
            if ([System.IO.Path]::IsPathRooted($relative) -or $relative.StartsWith('..')) {
                throw 'GCO_CALLBACK_PREVIEW_PATH_ESCAPE'
            }
            $originalPath = Join-Path (Join-Path $game 'r6/scripts') $relative
            if (!(Test-Path -LiteralPath $originalPath -PathType Leaf) -or
                (Get-FileHash -LiteralPath $originalPath -Algorithm SHA256).Hash -eq
                (Get-FileHash -LiteralPath $file.FullName -Algorithm SHA256).Hash) {
                throw 'GCO_CALLBACK_PREVIEW_ORIGINAL_OWNERSHIP_OR_DIFFERENCE_UNPROVEN'
            }
        }
        Write-Host ('PASS GCO generic callback source preview: candidateMethods=' +
            $callbackCount + ' stagedFiles=' + $callbackFiles.Count +
            ' compilerValidated=false deployable=false originalSourceUnchanged=true')
    }
    else {
        Write-Host 'GCO generic callback index shape absent from real stack; no source staged.'
    }

    # Generic pass 2: a cross-module, exact static literal array provider,
    # immediately consumed by the first OnAction ArrayContains guard. Scan
    # the full real source, preview only independently evidenced candidates.
    # Merge disjoint source overlays with pass 1 only inside disposable CI.
    $actionScanFile = Join-Path $work 'gco-action-filter-scan.json'
    $actionScanLog = Join-Path $work 'gco-action-filter-scan.log'
    & dotnet $dll scan-action-filter --game $game --out $actionScanFile *> $actionScanLog
    if ($LASTEXITCODE -ne 0 -or !(Test-Path -LiteralPath $actionScanFile -PathType Leaf)) {
        throw 'GCO_ACTION_FILTER_REAL_SOURCE_SCAN_FAILED'
    }
    $actionScan = Get-Content -LiteralPath $actionScanFile -Raw | ConvertFrom-Json -Depth 70
    if ($actionScan.schemaVersion -ne 'gco.redscript.action-filter.scan.v1' -or
        $actionScan.sourceStackSha256 -ne $expectedSourceHash -or
        $actionScan.sourceFiles -ne $sourceAudit.sourceFiles -or
        $actionScan.generationAuthorized -ne $false -or
        $actionScan.compilerValidated -ne $false -or
        $actionScan.sourceModified -ne $false -or
        $actionScan.measuredSavingsClaimed -ne $false) {
        throw 'GCO_ACTION_FILTER_REAL_SOURCE_SCAN_CONTRACT_FAILED'
    }
    $actionCount = [int]$actionScan.eligible
    if ($actionCount -gt 0) {
        $actionFolder = Join-Path $work 'gco-action-filter-preview'
        $actionLog = Join-Path $work 'gco-action-filter-preview.log'
        & dotnet $dll preview-action-filter --game $game --analysis $result --out $actionFolder *> $actionLog
        if ($LASTEXITCODE -ne 0) {
            throw 'GCO_ACTION_FILTER_REAL_SOURCE_PREVIEW_FAILED'
        }
        $actionScripts = Join-Path $actionFolder 'r6/scripts'
        if (!(Test-Path -LiteralPath $actionScripts -PathType Container)) {
            throw 'GCO_ACTION_FILTER_REAL_SOURCE_OVERLAY_MISSING'
        }
        $actionFiles = @(Get-ChildItem -LiteralPath $actionScripts -Recurse -File)
        if ($actionFiles.Count -lt 1 -or $actionFiles.Count -gt $actionCount) {
            throw 'GCO_ACTION_FILTER_REAL_SOURCE_COUNT_UNPROVEN'
        }
        $callbackScripts = Join-Path $callbackFolder 'r6/scripts'
        foreach ($file in $actionFiles) {
            $relative = [System.IO.Path]::GetRelativePath($actionScripts, $file.FullName)
            if ($file.Extension -ne '.reds' -or
                [System.IO.Path]::IsPathRooted($relative) -or
                $relative.StartsWith('..') -or $relative.Contains(':')) {
                throw 'GCO_ACTION_FILTER_UNSAFE_SOURCE'
            }
            $originalPath = Join-Path (Join-Path $game 'r6/scripts') $relative
            $target = Join-Path $callbackScripts $relative
            if (!(Test-Path -LiteralPath $originalPath -PathType Leaf) -or
                (Test-Path -LiteralPath $target) -or
                (Get-FileHash -LiteralPath $originalPath -Algorithm SHA256).Hash -eq
                (Get-FileHash -LiteralPath $file.FullName -Algorithm SHA256).Hash) {
                throw 'GCO_ACTION_FILTER_SOURCE_OWNERSHIP_OR_OVERLAY_COLLISION'
            }
            if ((Get-Content -LiteralPath $file.FullName -Raw) -cnotmatch
                'GCO-GENERIC:ACTION_LITERAL_MEMBERSHIP_V1') {
                throw 'GCO_ACTION_FILTER_EXPECTED_MARKER_MISSING'
            }
            New-Item -ItemType Directory -Force -Path (Split-Path $target -Parent) | Out-Null
            Copy-Item -LiteralPath $file.FullName -Destination $target -ErrorAction Stop
        }
        Write-Host ('PASS GCO action-filter real-source preview: admitted=' +
            $actionCount + ' stagedFiles=' + $actionFiles.Count +
            ' mutuallyDisjoint=true sourcesUntouched=true compilerValidated=false')
    }
    else {
        Write-Host 'GCO action-filter: no source-proven match in current stack; not rewritten.'
    }

    # GRSP owns the actual isolated SCC 0.5.31 dual-compile contract.
    # The GCO harness must not duplicate its compiler logic or publish output.
    $ownerScript = (Resolve-Path -LiteralPath $OwnerCompilerScript -ErrorAction Stop).Path
    $extra = @{}
    if ($callbackCount -gt 0 -or $actionCount -gt 0) {
        $extra.CallbackPreviewRoot = $callbackFolder
    }
    & $ownerScript -GameRoot $game -PreviewRoot $previewFolder -ResolverDll $dll -CompilerArchive $CompilerArchive -CleanBaselineArchive $CleanBaselineArchive -EffectiveCacheArchive $EffectiveCacheArchive -PluginsArchive $PluginsArchive @extra
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
