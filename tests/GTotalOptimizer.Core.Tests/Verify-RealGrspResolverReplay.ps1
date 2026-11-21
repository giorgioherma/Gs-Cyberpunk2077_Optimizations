param(
    [Parameter(Mandatory=$true)][string]$FixtureZip,
    [Parameter(Mandatory=$true)][string]$PrivateSourceRoot,
    [Parameter(Mandatory=$true)][string]$ResolverDll,
    [Parameter(Mandatory=$true)][string]$SourceAuditJson,
    [Parameter(Mandatory=$true)][string]$OwnerCompilerScript,
    [Parameter(Mandatory=$true)][string]$CompilerArchive,
    [Parameter(Mandatory=$true)][string]$CleanBaselineArchive,
    [Parameter(Mandatory=$true)][string]$EffectiveCacheArchive,
    [Parameter(Mandatory=$true)][string]$PluginsArchive,
    [string]$FrameworkRoot = '',
    [switch]$IncludeResearchDiagnostics
)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$fixture = (Resolve-Path -LiteralPath $FixtureZip -ErrorAction Stop).Path
$game = (Resolve-Path -LiteralPath $PrivateSourceRoot -ErrorAction Stop).Path
$dll = (Resolve-Path -LiteralPath $ResolverDll -ErrorAction Stop).Path
$auditFile = (Resolve-Path -LiteralPath $SourceAuditJson -ErrorAction Stop).Path
$sourceAudit = Get-Content -LiteralPath $auditFile -Raw | ConvertFrom-Json
# Validate the actual framework OWNER source before compiling it with the
# private game stack. These negative contracts guard detach/reattach ordering,
# generation and original pre-attach fallback, not just a feature-name marker.
if (![string]::IsNullOrWhiteSpace($FrameworkRoot)) {
    $ownerFramework = (Resolve-Path -LiteralPath $FrameworkRoot -ErrorAction Stop).Path
    & python (Join-Path $PSScriptRoot 'test_framework_player_signal.py') --framework $ownerFramework
    if ($LASTEXITCODE -ne 0) {
        throw 'GREDRUNTIME_OWNER_PLAYER_LIFETIME_CONTRACT_FAILED'
    }
    # Quest fact subscription adoption has no approved default source pass.
    # Keep its owner contract opt-in with the archived R5 research suite.
    if ($IncludeResearchDiagnostics) {
        & python (Join-Path $PSScriptRoot 'test_framework_quest_fact_signals.py') --framework $ownerFramework
        if ($LASTEXITCODE -ne 0) {
            throw 'GREDRUNTIME_OWNER_QUEST_FACT_OBSERVATION_CONTRACT_FAILED'
        }
    }
}
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
        $preview.sourceShapeDriven -ne $true -or
        [int]$preview.transformedLoops -lt [int]$preview.stagedFiles -or
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
    Write-Host ('PASS GRSP generic source-admitted preview: files=' +
                $preview.stagedFiles + ' transformedLoops=' +
                $preview.transformedLoops +
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

    # Historical read-only callback, position, quest, UI and shared-work
    # investigations remain in Git but are not executed by default Actions.
    # They generated zero admitted transforms; manual opt-in is preserved.
    if ($IncludeResearchDiagnostics) {
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

    # R5 generic callback-work triage: exclude the seven reserved semantic
    # owners from ranking only; source discovery and generator admission are
    # unaffected. All evidence comes from existing sealed GRSP + tokens.
    $callbackWorkJson = Join-Path $work 'gco-generic-callback-work.json'
    $callbackWorkLog = Join-Path $work 'gco-generic-callback-work.log'
    $reservedSemanticOwners = 'ReinforcementsSystem;Dark Future;FlushingEtiquette;Crowd Voice Unlocked;custom_quickslots;MuchBetterContracts;DigitalVixenCore'
    & dotnet $dll scan-callback-work --game $game --analysis $result --exclude-ranking-owners $reservedSemanticOwners --out $callbackWorkJson *> $callbackWorkLog
    if ($LASTEXITCODE -ne 0 -or !(Test-Path -LiteralPath $callbackWorkJson -PathType Leaf)) {
        throw 'GCO_GENERIC_CALLBACK_WORK_REAL_SOURCE_SCAN_FAILED'
    }
    $callbackWork = Get-Content -LiteralPath $callbackWorkJson -Raw | ConvertFrom-Json -Depth 80
    $rankedCallbackRows = @($callbackWork.callbacks | Where-Object { $_.rankingIncluded })
    $sourceMeasured = @($rankedCallbackRows | Where-Object {
        $null -ne $_.sourceMatchedCallsiteExclusiveMs
    })
    $multiOwnerPatterns = @($callbackWork.patterns | Where-Object { $_.crossOwnerStructure })
    if ($callbackWork.schemaVersion -ne 'gco.redscript.generic-callback-work.v1' -or
        $callbackWork.sourceStackSha256 -ne $expectedSourceHash -or
        [int]$callbackWork.sourceFiles -ne [int]$sourceAudit.sourceFiles -or
        [int]$callbackWork.analyzedOnActionMethods -lt 20 -or
        [int]$callbackWork.rankedOnActionMethods -ne $rankedCallbackRows.Count -or
        [int]$callbackWork.rankedOnActionMethods -ge [int]$callbackWork.analyzedOnActionMethods -or
        [int]$callbackWork.sourceMatchedMeasuredMethods -ne $sourceMeasured.Count -or
        [int]$callbackWork.crossOwnerPatterns -ne $multiOwnerPatterns.Count -or
        [int]$callbackWork.crossOwnerPatterns -lt 1 -or
        $callbackWork.rankingExclusionsAffectGeneratorAdmission -ne $false -or
        $callbackWork.anyFullForwardedCalleeEffectsProven -ne $false -or
        $callbackWork.canSkipCallback -ne $false -or
        $callbackWork.generationAuthorized -ne $false -or
        $callbackWork.originalSourcesModified -ne $false -or
        @($callbackWork.callbacks | Where-Object { $_.generationAuthorized -ne $false }).Count -ne 0) {
        throw 'GCO_GENERIC_CALLBACK_WORK_REAL_SOURCE_CONTRACT_FAILED'
    }
    $patternSummary = @($multiOwnerPatterns | ForEach-Object {
        $_.pattern + ':' + $_.sourceOwnerFolders + 'owners/' +
            $_.callbackMethods + 'methods'
    }) -join ','
    Write-Host ('PASS GCO cross-owner generic callback-work discovery: ' +
        'sourceFiles=' + $callbackWork.sourceFiles +
        ' callbacks=' + $callbackWork.analyzedOnActionMethods +
        ' ranked=' + $callbackWork.rankedOnActionMethods +
        ' sourceMatchedMeasured=' + $callbackWork.sourceMatchedMeasuredMethods +
        ' crossOwnerPatterns=' + $callbackWork.crossOwnerPatterns +
        ' families=' + $patternSummary +
        ' rewrites=0 entriesUnknown=true originalSourcesUnchanged=true')


    # One canonical, source-wide position dependency inventory before
    # native proximity admission. No mod list, no extra parser, no output
    # paths published from this sealed runner.
    $positionReport = Join-Path $work 'gco-player-position-dependencies.json'
    $positionLog = Join-Path $work 'gco-player-position-dependencies.log'
    & dotnet $dll scan-player-position --game $game --out $positionReport *> $positionLog
    if ($LASTEXITCODE -ne 0 -or !(Test-Path -LiteralPath $positionReport -PathType Leaf)) {
        throw 'GCO_PLAYER_POSITION_SOURCE_GRAPH_FAILED'
    }
    $position = Get-Content -LiteralPath $positionReport -Raw | ConvertFrom-Json -Depth 80
    $classified = [int]$position.categoryCounts.LOCAL_PLAYER +
        [int]$position.categoryCounts.NPC + [int]$position.categoryCounts.VEHICLE +
        [int]$position.categoryCounts.OTHER_ENTITY + [int]$position.categoryCounts.UNKNOWN +
        [int]$position.categoryCounts.MIXED
    if ($position.schemaVersion -ne 'gco.redscript.player-position-dependencies.v1' -or
        $position.sourceStackSha256 -ne $expectedSourceHash -or
        [int]$position.sourceFiles -ne [int]$sourceAudit.sourceFiles -or
        [int]$position.worldPositionCallsites -lt 50 -or
        [int]$position.worldPositionCallsites -ne @($position.callsites).Count -or
        ([int]$position.distanceCallsites + [int]$position.distanceSquaredCallsites) -ne @($position.geometries).Count -or
        @($position.geometries | Where-Object { $_.generationAuthorized -ne $false }).Count -ne 0 -or
        [int]$position.sourceFilesWithWorldPosition -le 0 -or
        [int]$position.sourceFilesWithWorldPosition -gt [int]$position.sourceFiles -or
        [int]$position.sourceOwnerFoldersWithWorldPosition -gt [int]$position.sourceFilesWithWorldPosition -or
        [int]$position.provenLocalPlayerFiles -gt [int]$position.sourceFilesWithWorldPosition -or
        [int]$position.provenLocalPlayerOwnerFolders -gt [int]$position.provenLocalPlayerFiles -or
        $classified -ne [int]$position.worldPositionCallsites -or
        $position.generationAuthorized -ne $false -or
        $position.originalSourcesModified -ne $false -or
        $position.productionZipAuthorized -ne $false) {
        throw 'GCO_PLAYER_POSITION_SOURCE_GRAPH_CONTRACT_FAILED'
    }
    Write-Host ('PASS GCO player position source graph: worldPositionReads=' +
        $position.worldPositionCallsites + ' distance=' +
        $position.distanceCallsites + ' squared=' +
        $position.distanceSquaredCallsites + ' files=' +
        $position.sourceFilesWithWorldPosition + ' sourceFolders=' +
        $position.sourceOwnerFoldersWithWorldPosition + ' localFiles=' +
        $position.provenLocalPlayerFiles + ' localFolders=' +
        $position.provenLocalPlayerOwnerFolders + ' provenLocal=' +
        $position.provenLocalPlayerCallsites + ' npc=' +
        [int]$position.categoryCounts.NPC + ' vehicle=' +
        [int]$position.categoryCounts.VEHICLE + ' unknown=' +
        [int]$position.categoryCounts.UNKNOWN + ' mixed=' +
        [int]$position.categoryCounts.MIXED + ' indirectConsumers=' +
        $position.indirectConsumerMethods +
        ' sourceReadOnly=true generated=0 capturedFrequencyClaimed=false')

    # R5: full-source read-only recurring refresh/maintenance work triage.
    # Reuses the existing callback owner parser and same source-verified
    # outgoing callsite join. Never mistakes summed edges for callback
    # entries or a deployable dirty-flag transformation.
    $sharedWorkOutput = Join-Path $work 'gco-shared-work-evidence.json'
    $sharedWorkLog = Join-Path $work 'gco-shared-work-evidence.log'
    & dotnet $dll scan-shared-work --game $game --analysis $result --out $sharedWorkOutput *> $sharedWorkLog
    if ($LASTEXITCODE -ne 0 -or !(Test-Path -LiteralPath $sharedWorkOutput -PathType Leaf)) {
        throw 'GCO_SHARED_WORK_REAL_SOURCE_SCAN_FAILED'
    }
    $shared = Get-Content -LiteralPath $sharedWorkOutput -Raw | ConvertFrom-Json -Depth 80
    $sharedCandidates = @($shared.candidates)
    $measuredShared = @($sharedCandidates | Where-Object {
        $null -ne $_.directInstrumentedMs
    })
    if ($shared.schemaVersion -ne 'gco.redscript.shared-work.scan.v1' -or
        $shared.parser -ne 'CALLBACK_SEMANTIC_RESOLVER_AUTHORITATIVE' -or
        $shared.sourceFiles -ne $sourceAudit.sourceFiles -or
        $shared.sourceStackSha256 -ne $expectedSourceHash -or
        $shared.evidenceKind -ne 'SOURCE_ALIGNED_OUTGOING_VM_CALLSITES_ONLY' -or
        [int]$shared.candidateMethods -ne $sharedCandidates.Count -or
        [int]$shared.sourceMatchedMeasured -ne $measuredShared.Count -or
        [int]$shared.candidateMethods -ne
            ([int]$shared.sourceMatchedMeasured + [int]$shared.unmeasured) -or
        [int]$shared.sourceMatchedMeasured -lt 1 -or
        [double]$shared.sourceMatchedDirectInstrumentedMs -le 0 -or
        $shared.mutatorAndObserverProofIncomplete -ne $true -or
        $shared.captureRowsAreOutgoingEdgesNotMethodEntries -ne $true -or
        $shared.directInstrumentedMsNotPredictedSavingsOrTotalCpu -ne $true -or
        $shared.generationAuthorized -ne $false -or
        $shared.compilerValidated -ne $false -or
        $shared.sourceWrites -ne $false -or
        [int]$shared.generatedFiles -ne 0) {
        throw 'GCO_SHARED_WORK_SOURCE_EVIDENCE_CONTRACT_FAILED'
    }
    $reasonCount = 0
    foreach ($reason in $shared.firstFailedPrerequisiteCounts.PSObject.Properties) {
        $reasonCount += [int]$reason.Value
    }
    if ($reasonCount -ne $sharedCandidates.Count) {
        throw 'GCO_SHARED_WORK_BLOCKER_ACCOUNTING_FAILED'
    }
    foreach ($candidate in $sharedCandidates) {
        if ($candidate.generationAuthorized -ne $false -or
            $candidate.safeToSkipBody -ne $false -or
            $candidate.completeWriterSetProven -ne $false -or
            $null -ne $candidate.expectedSavingsMs -or
            $null -ne $candidate.callbackEntries -or
            [string]::IsNullOrWhiteSpace([string]$candidate.firstFailedPrerequisite)) {
            throw 'GCO_SHARED_WORK_UNPROVEN_OPTIMIZATION_ADMITTED'
        }
    }
    # Every broadly discovered source method must receive an explicitly
    # unproven direct-operand profile, not just the 187 measured methods.
    $direct = $shared.directDependencyCategoryCounts
    $trigger = $shared.candidateSharedTriggerCounts
    $primary = $shared.primaryCandidateSharedTriggerCounts
    $primaryCount = 0
    foreach ($entry in $primary.PSObject.Properties) {
        $primaryCount += [int]$entry.Value
    }
    if ($shared.sourceDependencyCategoriesAreLexicalObservations -ne $true -or
        $shared.sharedTriggersAreHypothesesNotPublishers -ne $true -or
        $shared.directInstanceFieldsAreNotCompleteWriterGraph -ne $true -or
        $shared.helperCallsHaveUnresolvedTransitiveEffects -ne $true -or
        [int]$direct.INSTANCE_FIELDS -lt 1 -or
        [int]$direct.INSTANCE_METHOD_CALLS -lt 1 -or
        [int]$direct.CALLBACK_ARGUMENTS -lt 1 -or
        $null -eq $trigger.OWNER_FIELD_WRITERS_NOT_CLOSED -or
        $primaryCount -ne [int]$shared.candidateMethods) {
        throw 'GCO_SHARED_WORK_DEPENDENCY_PROVENANCE_OR_ACCOUNTING_FAILED'
    }
    foreach ($candidate in $sharedCandidates) {
        $deps = $candidate.directDependencies
        if ($null -eq $deps -or
            $deps.completeCallGraphProven -ne $false -or
            $deps.completeWriterGraphProven -ne $false -or
            $deps.sharedTriggerProven -ne $false -or
            @($deps.possibleSharedTriggers).Count -lt 1 -or
            [string]::IsNullOrWhiteSpace([string]$deps.primaryCandidateSharedTrigger)) {
            throw 'GCO_SHARED_WORK_UNPROVEN_TRIGGER_EXPORTED_AS_PROVEN'
        }
    }
    # The 1,013 methods without exact source-aligned measured callsites
    # are NOT player-state-only. Demand a separate dependency distribution
    # for measured and unmeasured methods with complete accounting.
    $measuredPrimaryCount = 0
    foreach ($entry in $shared.measuredPrimaryTriggerCounts.PSObject.Properties) {
        $measuredPrimaryCount += [int]$entry.Value
    }
    $unmeasuredPrimaryCount = 0
    foreach ($entry in $shared.unmeasuredPrimaryTriggerCounts.PSObject.Properties) {
        $unmeasuredPrimaryCount += [int]$entry.Value
    }
    if ($measuredPrimaryCount -ne [int]$shared.sourceMatchedMeasured -or
        $unmeasuredPrimaryCount -ne [int]$shared.unmeasured -or
        $measuredPrimaryCount + $unmeasuredPrimaryCount -ne $primaryCount) {
        throw 'GCO_SHARED_WORK_MEASURED_UNMEASURED_PRIMARY_SPLIT_FAILED'
    }
    foreach ($entry in $direct.PSObject.Properties) {
        $category = [string]$entry.Name
        $meas = $shared.measuredDependencyCategoryCounts.PSObject.Properties[$category]
        $unmeas = $shared.unmeasuredDependencyCategoryCounts.PSObject.Properties[$category]
        $measuredCategory = if ($null -eq $meas) { 0 } else { [int]$meas.Value }
        $unmeasuredCategory = if ($null -eq $unmeas) { 0 } else { [int]$unmeas.Value }
        if ($measuredCategory + $unmeasuredCategory -ne [int]$entry.Value) {
            throw 'GCO_SHARED_WORK_MEASURED_UNMEASURED_CATEGORY_SPLIT_FAILED'
        }
    }
    # Aggregate-only disclosure: no private original source names, class
    # names, source contents or captured paths are written into CI logs.
    foreach ($entry in $primary.PSObject.Properties) {
        $category = [string]$entry.Name
        $measure = $shared.measuredPrimaryTriggerCounts.PSObject.Properties[$category]
        $noMeasure = $shared.unmeasuredPrimaryTriggerCounts.PSObject.Properties[$category]
        $measuredCount = if ($null -eq $measure) { 0 } else { [int]$measure.Value }
        $unmeasuredCount = if ($null -eq $noMeasure) { 0 } else { [int]$noMeasure.Value }
        Write-Host ('GCO_SHARED_TRIGGER_HYPOTHESIS ' + $category +
            ' total=' + [int]$entry.Value +
            ' sourceMatchedMeasured=' + $measuredCount +
            ' sourceUnmeasured=' + $unmeasuredCount)
    }
    foreach ($entry in $direct.PSObject.Properties) {
        $category = [string]$entry.Name
        $measure = $shared.measuredDependencyCategoryCounts.PSObject.Properties[$category]
        $noMeasure = $shared.unmeasuredDependencyCategoryCounts.PSObject.Properties[$category]
        $measuredCount = if ($null -eq $measure) { 0 } else { [int]$measure.Value }
        $unmeasuredCount = if ($null -eq $noMeasure) { 0 } else { [int]$noMeasure.Value }
        Write-Host ('GCO_SHARED_DIRECT_DEPENDENCY ' + $category +
            ' total=' + [int]$entry.Value +
            ' sourceMatchedMeasured=' + $measuredCount +
            ' sourceUnmeasured=' + $unmeasuredCount)
    }
    Write-Host ('PASS GCO source-only dependency coverage: measuredMethods=' +
        $measuredPrimaryCount + ' unmeasuredMethods=' + $unmeasuredPrimaryCount +
        ' writerClosureProven=false noSharedTriggerAuthorized=true')

    Write-Host ('PASS GCO shared-work direct dependencies: methods=' +
        $shared.candidateMethods + ' directFieldRead=' +
        $direct.INSTANCE_FIELDS + ' ownMethodCalls=' + $direct.INSTANCE_METHOD_CALLS +
        ' callbackArguments=' + $direct.CALLBACK_ARGUMENTS +
        ' primaryTriggers=' + $primaryCount +
        ' writerClosureUnproven=true generationAuthorized=false')

    Write-Host ('PASS GCO generic shared-work evidence: methods=' +
        $shared.candidateMethods + ' sourceMatched=' + $shared.sourceMatchedMeasured +
        ' unmeasured=' + $shared.unmeasured +
        ' sourceOnly=false generationAuthorized=false allReasonsAccounted=true')

    # Generic UI output-slice classifier: inspect the actual caller source,
    # separate live timer/stack inputs from status/record identity, and
    # REFUSE any count-only or Nth-frame setter/refresh suppression.
    $uiOutput = Join-Path $work 'gco-ui-output-slices.json'
    $uiLog = Join-Path $work 'gco-ui-output-slices.log'
    & dotnet $dll scan-ui-outputs --game $game --analysis $result --out $uiOutput *> $uiLog
    if ($LASTEXITCODE -ne 0 -or !(Test-Path -LiteralPath $uiOutput -PathType Leaf)) {
        throw 'GCO_UI_OUTPUT_REAL_SOURCE_SCAN_FAILED'
    }
    $ui = Get-Content -LiteralPath $uiOutput -Raw | ConvertFrom-Json -Depth 70
    $uiRows = @($ui.outputs)
    $mixedUi = @($uiRows | Where-Object { $_.mixedStructuralAndTimeInputs -eq $true })
    $measuredUi = @($uiRows | Where-Object { $_.sourceMatchedMeasuredMethod -eq $true })
    $typedUi = @($uiRows | Where-Object { $null -ne $_.receiverSourceDeclaredType })
    if ($ui.schemaVersion -ne 'gco.redscript.ui-output.slices.v1' -or
        $ui.parser -ne 'CALLBACK_SEMANTIC_RESOLVER_AUTHORITATIVE' -or
        $ui.sourceFiles -ne $sourceAudit.sourceFiles -or
        $ui.sourceStackSha256 -ne $expectedSourceHash -or
        $ui.evidenceKind -ne 'SEALED_F11_METHOD_EDGES_NOT_SETTER_ENTRIES' -or
        [int]$ui.setDataSites -ne $uiRows.Count -or
        [int]$ui.mixedStructuralTimeSites -ne $mixedUi.Count -or
        [int]$ui.sourceMatchedMethodSites -ne $measuredUi.Count -or
        [int]$ui.sourceTypedReceiverSites -ne $typedUi.Count -or
        $uiRows.Count -lt 1 -or $mixedUi.Count -lt 1 -or
        $ui.sourceProofOverridesProfilerRanking -ne $true -or
        $ui.lexicalTimeHintsAreNotActualTimerChangeRates -ne $true -or
        $ui.noCountOnlyOrNthFrameThrottlingAllowed -ne $true -or
        $ui.allSetterEffectsAndUiLifecyclesProven -ne $false -or
        $ui.generationAuthorized -ne $false -or
        $ui.sourceWrites -ne $false -or [int]$ui.generatedFiles -ne 0) {
        throw 'GCO_UI_OUTPUT_SOURCE_OR_SAFETY_CONTRACT_FAILED'
    }
    foreach ($outputRow in $uiRows) {
        if ($outputRow.uiSetterImplementationSourceProven -ne $false -or
            $outputRow.fullValueTupleEqualityProven -ne $false -or
            $outputRow.widgetGenerationAndLifecycleProven -ne $false -or
            $outputRow.timerAndAnimationSemanticsProven -ne $false -or
            $outputRow.sourceChangePublisherComplete -ne $false -or
            $outputRow.sameSourceOriginalEffectsPreserved -ne $false -or
            $outputRow.suppressWholeRefreshAuthorized -ne $false -or
            $outputRow.suppressSetterAuthorized -ne $false -or
            $outputRow.methodTimeIsNotSetterTimeOrSavings -ne $true -or
            $outputRow.generationAuthorized -ne $false -or
            [int]$outputRow.argumentCount -ne @($outputRow.arguments).Count) {
            throw 'GCO_UI_OUTPUT_UNPROVEN_SETTER_OR_REFRESH_SKIPPED'
        }
    }
    Write-Host ('PASS GCO generic UI output slice source proof: setters=' +
        $uiRows.Count + ' mixedStructuralTimeSites=' + $mixedUi.Count +
        ' sourceMatchedMethodSites=' + $measuredUi.Count +
        ' sourceTypedReceiverSites=' + $typedUi.Count +
        ' fullSetterEffectProof=0 throttling=0 rewrites=0')

    # R5 quest-fact producer/readers/registrations, across ALL current mod
    # methods (not just those named Update). This is an external private
    # evidence graph. Unknown/dynamic receivers MUST remain refused.
    $factsOutput = Join-Path $work 'gco-quest-facts.json'
    $factsLog = Join-Path $work 'gco-quest-facts.log'
    & dotnet $dll scan-quest-facts --game $game --analysis $result --out $factsOutput *> $factsLog
    if ($LASTEXITCODE -ne 0 -or !(Test-Path -LiteralPath $factsOutput -PathType Leaf)) {
        throw 'GCO_QUEST_FACT_REAL_SOURCE_GRAPH_FAILED'
    }
    $qFacts = Get-Content -LiteralPath $factsOutput -Raw | ConvertFrom-Json -Depth 70
    if ($qFacts.schemaVersion -ne 'gco.redscript.quest-fact.graph.v1' -or
        $qFacts.sourceFiles -ne $sourceAudit.sourceFiles -or
        $qFacts.sourceStackSha256 -ne $expectedSourceHash -or
        $qFacts.scope -ne 'ALL_LIVE_REDSCRIPT_METHODS_NOT_JUST_1200_REFRESH_NAMES' -or
        [int]$qFacts.factCallsites -lt 100 -or
        [int]$qFacts.literalCallsites -lt 1 -or
        [int]$qFacts.dynamicOrUnresolvedCallsites -lt 1 -or
        [int]$qFacts.factCallsites -ne ([int]$qFacts.literalCallsites +
                                         [int]$qFacts.dynamicOrUnresolvedCallsites) -or
        [int]$qFacts.distinctLiteralFactNames -ne @($qFacts.facts).Count -or
        $qFacts.allDynamicFactNamesRemainUnproven -ne $true -or
        $qFacts.completeProducerAndMutationGraph -ne $false -or
        $qFacts.engineCallbackSemanticsValidated -ne $false -or
        $qFacts.generationAuthorized -ne $false -or
        $qFacts.sourceWrites -ne $false -or
        [int]$qFacts.generatedFiles -ne 0) {
        throw 'GCO_QUEST_FACT_GRAPH_PROVENANCE_OR_SAFETY_FAILED'
    }
    foreach ($fact in @($qFacts.facts)) {
        if ($fact.everyWriterAndAliasProven -ne $false -or
            $fact.receiverTypeProven -ne $false -or
            $fact.listenerCallbackReturnAndOrderProven -ne $false -or
            $fact.sharedSubscriptionAuthorized -ne $false) {
            throw 'GCO_QUEST_FACT_UNPROVEN_SUBSTITUTION_ADMITTED'
        }
    }
    $literal = @($qFacts.callsites | Where-Object { $null -ne $_.factName })
    $unresolvedReceivers = @($qFacts.callsites | Where-Object {
        $_.operation -eq 'RegisterListener' -and
        $_.questReceiverSourceTyped -ne $true
    })
    if ($literal.Count -ne [int]$qFacts.literalCallsites -or
        $unresolvedReceivers.Count -lt 1) {
        throw 'GCO_QUEST_FACT_LITERAL_RECEIVER_ACCOUNTING_FAILED'
    }
    $refreshFactReaders = @($qFacts.directRefreshReaders)
    $refreshMeasured = @($refreshFactReaders | Where-Object {
        $_.hasSourceMatchedOutgoingEdgeEvidence -eq $true
    })
    $refreshDynamic = @($refreshFactReaders | Where-Object {
        [int]$_.dynamicFactReads -gt 0
    })
    if ($qFacts.anyRefreshFullDependencyClosureProven -ne $false -or
        [int]$qFacts.directRefreshFactReaderMethods -ne $refreshFactReaders.Count -or
        [int]$qFacts.sourceMatchedDirectRefreshFactReaderMethods -ne
            $refreshMeasured.Count -or
        [int]$qFacts.dynamicDirectRefreshFactReaderMethods -ne
            $refreshDynamic.Count -or
        $refreshFactReaders.Count -lt 1) {
        throw 'GCO_QUEST_FACT_REFRESH_DEPENDENCY_ACCOUNTING_FAILED'
    }
    foreach ($reader in $refreshFactReaders) {
        if ([int]$reader.directFactReads -lt 1 -or
            [int]$reader.dynamicFactReads -lt 0 -or
            [int]$reader.dynamicFactReads -gt [int]$reader.directFactReads -or
            $reader.completeFactWriterCoverage -ne $false -or
            $reader.completeTransitiveDependencyClosure -ne $false -or
            $reader.originalEntryAndSideEffectSemanticsProven -ne $false -or
            $reader.sharedChangeNotificationEquivalent -ne $false -or
            $reader.safeToReplaceGetterOrSkipMethod -ne $false -or
            $reader.generationAuthorized -ne $false) {
            throw 'GCO_QUEST_FACT_REFRESH_SKIP_WITHOUT_SOURCE_PROOF'
        }
    }
    Write-Host ('PASS GCO quest fact refresh dependency gate: refreshMethods=' +
        $refreshFactReaders.Count + ' sourceMeasured=' +
        $refreshMeasured.Count + ' dynamicReaders=' + $refreshDynamic.Count +
        ' completeWriterGraph=0 fullyCovered=0 generatedFiles=0')

    # Compare BOTH sides of named-fact consumer compatibility: exact-name
    # read shapes with additional state, and simple lexical read shapes.
    # Neither cohort is allowed to delete its original getter/method, since
    # quest engine/native writers and callbacks are not source-complete.
    $subscriberReads = @($qFacts.questSubscriberReads)
    $subscriberCohorts = @($qFacts.questSubscriberCohorts)
    $wrapperReads = @($subscriberReads | Where-Object {
        $_.readShape -eq 'SAME_OWNER_QUEST_WRAPPER_SHAPE'
    })
    $unresolvedHelpers = @($subscriberReads | Where-Object {
        $_.readShape -eq 'UNRESOLVED_SAME_OWNER_HELPER'
    })
    $observedCount = 0
    $crossOwner = 0
    $mixedAndSimple = 0
    $directAndWrapper = 0
    foreach ($c in $subscriberCohorts) {
        $observedCount += [int]$c.literalReadSites
        if ($c.sharesNameAcrossOwners -eq $true) { $crossOwner++ }
        if ($c.sharesNameAcrossMixedAndSimpleMethods -eq $true) { $mixedAndSimple++ }
        if ($c.sharesNameAcrossDirectAndWrapperReads -eq $true) { $directAndWrapper++ }
        if ($c.completeWriterGraphProven -ne $false -or
            $c.callbackCoversAllMutations -ne $false -or
            $c.lifecycleProven -ne $false -or
            $c.sharedSubscriptionAuthorized -ne $false -or
            $c.originalGetterReplacementAuthorized -ne $false -or
            $c.originalMethodSkipAuthorized -ne $false) {
            throw 'GCO_FACT_SUBSCRIBER_COHORT_UNSAFE_ADMISSION'
        }
    }
    if ($qFacts.partialObservationCohortsAreResearchOnly -ne $true -or
        $qFacts.cannotInferOriginalGetterCanBeSkipped -ne $true -or
        $qFacts.anySubscriptionOrPartialRewriteAuthorized -ne $false -or
        [int]$qFacts.observedLiteralFactReadSites -ne $subscriberReads.Count -or
        [int]$qFacts.observedLiteralFactReadCohorts -ne $subscriberCohorts.Count -or
        [int]$qFacts.identifiedSameOwnerQuestWrapperReads -ne $wrapperReads.Count -or
        [int]$qFacts.unresolvedSameOwnerHelperReads -ne $unresolvedHelpers.Count -or
        [int]$qFacts.crossOwnerReadCohorts -ne $crossOwner -or
        [int]$qFacts.mixedAndLexicallySimpleCohorts -ne $mixedAndSimple -or
        [int]$qFacts.directAndWrapperSharedCohorts -ne $directAndWrapper -or
        $observedCount -ne $subscriberReads.Count -or
        $subscriberReads.Count -lt 1) {
        throw 'GCO_FACT_SUBSCRIBER_COHORT_ACCOUNTING_FAILED'
    }
    foreach ($read in $subscriberReads) {
        if ($read.allWriterAndAliasPathsProven -ne $false -or
            $read.isolatedExpressionProven -ne $false -or
            $read.runtimeNotificationEquivalent -ne $false -or
            $read.originalExecutionPreserved -ne $false -or
            $read.canReplaceOriginalGetter -ne $false -or
            $read.canSkipOriginalMethod -ne $false -or
            $read.generationAuthorized -ne $false) {
            throw 'GCO_FACT_SUBSCRIBER_PARTIAL_GETTER_UNSAFE_ADMISSION'
        }
    }
    Write-Host ('PASS GCO bidirectional fact subscriber evidence: literalReadSites=' +
        $subscriberReads.Count + ' exactFactCohorts=' + $subscriberCohorts.Count +
        ' crossOwner=' + $crossOwner +
        ' mixedAndSimple=' + $mixedAndSimple +
        ' directPlusWrapper=' + $directAndWrapper +
        ' sameOwnerWrapperReads=' + $wrapperReads.Count +
        ' unresolvedHelperReads=' + $unresolvedHelpers.Count +
        ' fullSkipAuthorized=0 partialGetterReplaceAuthorized=0 originalPathsPreserved=true')

    # A mixed owner method can be a valid *partial input* research target:
    # account every existing exact-name read, including same-owner wrappers,
    # assignments and locals. Do not require whole-method side-effect purity.
    $partialInputs = @($qFacts.questPartialValueInputs)
    $partialMixed = @($partialInputs | Where-Object { $_.methodContainsUnrelatedInputs -eq $true })
    $partialMeasured = @($partialInputs | Where-Object { $_.sourceMeasuredMethod -eq $true })
    $partialLocals = @($partialInputs | Where-Object { $null -ne $_.localName })
    $partialConditions = @($partialInputs | Where-Object { $_.factReachesCondition -eq $true })
    if ([int]$qFacts.partialSubscriberInputSites -ne $subscriberReads.Count -or
        $partialInputs.Count -ne $subscriberReads.Count -or
        [int]$qFacts.partialInputSitesInMixedMethods -ne $partialMixed.Count -or
        [int]$qFacts.partialInputSitesWithSourceMatchedF11 -ne $partialMeasured.Count -or
        [int]$qFacts.partialInputLocalValueSites -ne $partialLocals.Count -or
        [int]$qFacts.partialInputWithDownstreamCondition -ne $partialConditions.Count -or
        $qFacts.partialInputsDoNotRequireWholeMethodEligibility -ne $true -or
        $qFacts.anyPartialInputOriginalGetterReplacementAuthorized -ne $false) {
        throw 'GCO_FACT_PARTIAL_VALUE_INPUT_ACCOUNTING_FAILED'
    }
    foreach ($input in $partialInputs) {
        if ($input.candidateForSharedFactInputResearch -ne $true -or
            $input.pureDerivedValueProven -ne $false -or
            $input.everyFactProducerWriterObserved -ne $false -or
            $input.subscriberLifecycleProven -ne $false -or
            $input.originalNonfactWorkPreserved -ne $false -or
            $input.originalGetterReplacementAuthorized -ne $false -or
            $input.generationAuthorized -ne $false -or
            $input.measuredMethodTimeIsNotFactSliceSavings -ne $true) {
            throw 'GCO_FACT_PARTIAL_VALUE_SOURCE_REWRITE_UNSAFE'
        }
    }
    Write-Host ('PASS GCO partial fact input subscriber evidence: exactReads=' +
        $partialInputs.Count + ' mixedMethodInputs=' + $partialMixed.Count +
        ' sourceMeasuredInputs=' + $partialMeasured.Count +
        ' localValueInputs=' + $partialLocals.Count +
        ' downstreamConditionalInputs=' + $partialConditions.Count +
        ' partialInputFoundWithoutWholeMethodEligibility=true rewrites=0')

    # Cohort-level two-sided analysis: a shared exact fact input may feed
    # local values and direct predicates in DIFFERENT methods with independent
    # clocks, fields, actions, widgets or helper effects. Never substitute
    # the whole method, and never treat a shared name as verified event parity.
    $partialCohorts = @($qFacts.questSubscriberCohorts)
    $cohortInputSites = 0
    $cohortConditionMixed = 0
    $cohortUseShapesMixed = 0
    foreach ($cohort in $partialCohorts) {
        $sameFactInputs = @($partialInputs | Where-Object { $_.fact -ceq $cohort.fact })
        $cohortInputSites += [int]$cohort.partialValueInputSites
        $hasCond = @($sameFactInputs | Where-Object { $_.factReachesCondition -eq $true }).Count -gt 0
        $hasNonCond = @($sameFactInputs | Where-Object { $_.factReachesCondition -ne $true }).Count -gt 0
        if ($hasCond -and $hasNonCond) { $cohortConditionMixed++ }
        if (@($cohort.partialValueUseShapes).Count -gt 1) { $cohortUseShapesMixed++ }
        if ([int]$cohort.partialValueInputSites -ne $sameFactInputs.Count -or
            [int]$cohort.partialValueInputsInMixedMethods -ne
                @($sameFactInputs | Where-Object { $_.methodContainsUnrelatedInputs -eq $true }).Count -or
            [int]$cohort.partialValueInputsWithConditions -ne
                @($sameFactInputs | Where-Object { $_.factReachesCondition -eq $true }).Count -or
            [int]$cohort.partialValueInputsAssignedToLocals -ne
                @($sameFactInputs | Where-Object { $null -ne $_.localName }).Count -or
            [int]$cohort.partialValueInputsSourceMeasured -ne
                @($sameFactInputs | Where-Object { $_.sourceMeasuredMethod -eq $true }).Count -or
            $cohort.containsConditionalAndOtherValueUses -ne ($hasCond -and $hasNonCond) -or
            $cohort.partialInputRelationIsSourceOnly -ne $true) {
            throw 'GCO_FACT_PARTIAL_BIDIRECTIONAL_COHORT_ACCOUNTING_FAILED'
        }
    }
    if ($cohortInputSites -ne $partialInputs.Count -or
        [int]$qFacts.partialFactCohortsWithDifferentValueUses -ne $cohortUseShapesMixed -or
        [int]$qFacts.partialFactCohortsWithConditionalAndNonconditionalInputs -ne $cohortConditionMixed -or
        $qFacts.partialCohortsAreReadOnlyResearch -ne $true) {
        throw 'GCO_FACT_PARTIAL_BIDIRECTIONAL_COHORT_INCONSISTENT'
    }
    Write-Host ('PASS GCO bidirectional partial-value fact cohorts: factNames=' +
        $partialCohorts.Count + ' sourceInputSites=' + $cohortInputSites +
        ' differentValueUseCohorts=' + $cohortUseShapesMixed +
        ' conditionalAndNonconditionalCohorts=' + $cohortConditionMixed +
        ' partialOnly=true noOriginalMethodSkip=true')

    Write-Host ('PASS GCO source-wide quest fact dependency graph: methods=' +
        $qFacts.sourceMethods + ' literalCallsites=' + $qFacts.literalCallsites +
        ' dynamicCallsites=' + $qFacts.dynamicOrUnresolvedCallsites +
        ' factNames=' + $qFacts.distinctLiteralFactNames +
        ' crossOwnerSourceTypedListeners=' + $qFacts.factsWithCrossOwnerLiteralRegistrations +
        ' unprovenListenerReceivers=' + $qFacts.lexicalListenerCallsitesReceiverUnverified +
        ' runtimeSavingsClaimed=false generationAuthorized=false')

    } else {
        Write-Host 'SKIP inactive R5 research diagnostics; active source generators, SCC and output checks continue.'
    }

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

    # Generic mechanical pass 3: four-vertex angle sorting. Only a complete
    # source-proven comparison/swap shape may hoist the pure AtanF for pts[i],
    # refreshing the hoisted angle after every swap. Source-path independent.
    $angleScanFile = Join-Path $work 'gco-angle-sort-scan.json'
    $angleScanLog = Join-Path $work 'gco-angle-sort-scan.log'
    & dotnet $dll scan-angle-sort --game $game --out $angleScanFile *> $angleScanLog
    if ($LASTEXITCODE -ne 0 -or !(Test-Path -LiteralPath $angleScanFile -PathType Leaf)) {
        throw 'GCO_ANGLE_SORT_REAL_SOURCE_SCAN_FAILED'
    }
    $angleScan = Get-Content -LiteralPath $angleScanFile -Raw | ConvertFrom-Json
    if ($angleScan.schemaVersion -ne 'gco.redscript.angle-sort.scan.v1' -or
        $angleScan.sourceStackSha256 -ne $expectedSourceHash -or
        $angleScan.sourceFiles -ne $sourceAudit.sourceFiles -or
        $angleScan.generationAuthorized -ne $false -or
        $angleScan.compilerValidated -ne $false -or
        $angleScan.measuredSavingsClaimed -ne $false -or
        $angleScan.sourceWrites -ne $false) {
        throw 'GCO_ANGLE_SORT_REAL_SOURCE_SCAN_CONTRACT_FAILED'
    }
    $angleCount = [int]$angleScan.eligible
    if ($angleCount -lt 1) {
        throw 'GCO_ANGLE_SORT_KNOWN_REAL_SOURCE_FIXTURE_UNEXPECTEDLY_UNMATCHED'
    }
    $angleFolder = Join-Path $work 'gco-angle-sort-preview'
    $angleLog = Join-Path $work 'gco-angle-sort-preview.log'
    & dotnet $dll preview-angle-sort --game $game --analysis $result --out $angleFolder *> $angleLog
    if ($LASTEXITCODE -ne 0) {
        throw 'GCO_ANGLE_SORT_REAL_SOURCE_PREVIEW_FAILED'
    }
    $angleScripts = Join-Path $angleFolder 'r6/scripts'
    $angleFiles = @(Get-ChildItem -LiteralPath $angleScripts -File -Recurse)
    # The same original geometry file can now contain both the invariant
    # sorter rewrite and the guarded quad broadphase. Count transformations
    # separately from distinct owner files, as the canonical plan does.
    if ($angleFiles.Count -lt 1 -or $angleFiles.Count -gt $angleCount) {
        throw 'GCO_ANGLE_SORT_REAL_SOURCE_PREVIEW_COUNT_UNPROVEN'
    }
    $callbackScripts = Join-Path $callbackFolder 'r6/scripts'
    foreach ($file in $angleFiles) {
        $relative = [IO.Path]::GetRelativePath($angleScripts, $file.FullName)
        $originalPath = Join-Path (Join-Path $game 'r6/scripts') $relative
        $target = Join-Path $callbackScripts $relative
        if ($file.Extension -ne '.reds' -or
            [IO.Path]::IsPathRooted($relative) -or
            $relative.StartsWith('..') -or $relative.Contains(':') -or
            !(Test-Path -LiteralPath $originalPath -PathType Leaf) -or
            (Test-Path -LiteralPath $target) -or
            (Get-FileHash -LiteralPath $originalPath -Algorithm SHA256).Hash -eq
            (Get-FileHash -LiteralPath $file.FullName -Algorithm SHA256).Hash -or
            (Get-Content -LiteralPath $file.FullName -Raw) -cnotmatch
            'GCO-GENERIC:ANGLE_SORT_INVARIANT_V1') {
            throw 'GCO_ANGLE_SORT_UNSAFE_SOURCE_OR_OVERLAY_COLLISION'
        }
        New-Item -ItemType Directory -Force -Path (Split-Path $target -Parent) | Out-Null
        Copy-Item -LiteralPath $file.FullName -Destination $target -ErrorAction Stop
    }
    Write-Host ('PASS GCO generic angle-sort real-source preview: matches=' +
        $angleCount + ' mergedFiles=' + $angleFiles.Count +
        ' disjoint=true sourceUntouched=true deployable=false')

    # Generic pass 4: Source-proven action type early return must execute
    # before a QuestsSystem lookup; wrappedMethod stays first. We stage
    # only a structurally proven standalone overlay in disposable CI.
    $lateScanFile = Join-Path $work 'gco-late-system-read-scan.json'
    $lateScanLog = Join-Path $work 'gco-late-system-read-scan.log'
    & dotnet $dll scan-late-system-read --game $game --out $lateScanFile *> $lateScanLog
    if ($LASTEXITCODE -ne 0 -or !(Test-Path -LiteralPath $lateScanFile -PathType Leaf)) {
        throw 'GCO_LATE_SYSTEM_READ_REAL_SOURCE_SCAN_FAILED'
    }
    $lateScan = Get-Content -LiteralPath $lateScanFile -Raw | ConvertFrom-Json
    if ($lateScan.schemaVersion -ne 'gco.redscript.late-system-read.scan.v1' -or
        $lateScan.sourceStackSha256 -ne $expectedSourceHash -or
        $lateScan.sourceFiles -ne $sourceAudit.sourceFiles -or
        $lateScan.generationAuthorized -ne $false -or
        $lateScan.compilerValidated -ne $false -or
        $lateScan.measuredSavingsClaimed -ne $false -or
        $lateScan.sourceWrites -ne $false) {
        throw 'GCO_LATE_SYSTEM_READ_REAL_SOURCE_SCAN_CONTRACT_FAILED'
    }
    $lateCount = [int]$lateScan.eligible
    $lateFolder = Join-Path $work 'gco-late-system-read-preview'
    if ($lateCount -gt 0) {
        $lateLog = Join-Path $work 'gco-late-system-read-preview.log'
        & dotnet $dll preview-late-system-read --game $game --analysis $result --out $lateFolder --base-preview $callbackFolder *> $lateLog
        if ($LASTEXITCODE -ne 0) {
            throw 'GCO_LATE_SYSTEM_READ_REAL_SOURCE_PREVIEW_FAILED'
        }
        $lateScripts = Join-Path $lateFolder 'r6/scripts'
        if (!(Test-Path -LiteralPath $lateScripts -PathType Container)) {
            throw 'GCO_LATE_SYSTEM_READ_PREVIEW_MISSING'
        }
        $lateFiles = @(Get-ChildItem -LiteralPath $lateScripts -Recurse -File)
        if ($lateFiles.Count -lt 1 -or $lateFiles.Count -gt $lateCount) {
            throw 'GCO_LATE_SYSTEM_READ_PREVIEW_COUNT_UNPROVEN'
        }
        $callbackScripts = Join-Path $callbackFolder 'r6/scripts'
        foreach ($file in $lateFiles) {
            $relative = [IO.Path]::GetRelativePath($lateScripts, $file.FullName)
            $originalPath = Join-Path (Join-Path $game 'r6/scripts') $relative
            $target = Join-Path $callbackScripts $relative
            if ($file.Extension -ne '.reds' -or
                [IO.Path]::IsPathRooted($relative) -or
                $relative.StartsWith('..') -or $relative.Contains(':') -or
                !(Test-Path -LiteralPath $originalPath -PathType Leaf) -or
                (Test-Path -LiteralPath $target) -or
                (Get-FileHash -LiteralPath $originalPath -Algorithm SHA256).Hash -eq
                (Get-FileHash -LiteralPath $file.FullName -Algorithm SHA256).Hash -or
                (Get-Content -LiteralPath $file.FullName -Raw) -cnotmatch
                'GCO-GENERIC:(?:SYSTEM_READ_AFTER_ACTION_GUARD_V1|LOCAL_SYSTEM_HANDLE_REUSE_V1)') {
                throw 'GCO_LATE_SYSTEM_READ_UNSAFE_SOURCE_OR_OVERLAY_COLLISION'
            }
            if (Test-Path -LiteralPath $target -PathType Leaf) {
                # Earlier ordered source rules may already occupy this file.
                # Verify they persist in the owner-generated composed output.
                $before = Get-Content -LiteralPath $target -Raw
                $after = Get-Content -LiteralPath $file.FullName -Raw
                $priorMarkers = @([regex]::Matches($before,
                    '(?:GCO|GRSP)-GENERIC:[A-Z0-9_]+') |
                    ForEach-Object { $_.Value } | Select-Object -Unique)
                foreach ($marker in $priorMarkers) {
                    if (!$after.Contains($marker, [StringComparison]::Ordinal)) {
                        throw 'GCO_LATE_SYSTEM_REUSE_LOST_PRIOR_RULE'
                    }
                }
            }
            else {
                New-Item -ItemType Directory -Force -Path (Split-Path $target -Parent) | Out-Null
            }
            Copy-Item -LiteralPath $file.FullName -Destination $target -Force -ErrorAction Stop
        }
        Write-Host ('PASS GCO generic late-system read real-source preview: candidates=' +
            $lateCount + ' stagedFiles=' + $lateFiles.Count +
            ' disjoint=true originalSourceUntouched=true deployable=false')
    }
    else {
        Write-Host 'No source-proven late-system action read in current stack; no generated files.'
    }

    # Generic pass 5: statement-local immutable BlackboardDefs group reuse.
    # No cached gameplay values, no new timer, no new framework service.
    # Source states and capture evidence are verified by the SAME GCO code.
    $bbScanFile = Join-Path $work 'gco-blackboard-definition-scan.json'
    $bbScanLog = Join-Path $work 'gco-blackboard-definition-scan.log'
    & dotnet $dll scan-blackboard-defs --game $game --out $bbScanFile *> $bbScanLog
    if ($LASTEXITCODE -ne 0 -or !(Test-Path -LiteralPath $bbScanFile -PathType Leaf)) {
        throw 'GCO_BLACKBOARD_DEFINITION_REAL_SOURCE_SCAN_FAILED'
    }
    $bbScan = Get-Content -LiteralPath $bbScanFile -Raw | ConvertFrom-Json
    if ($bbScan.schemaVersion -ne 'gco.redscript.bbdefs.scan.v1' -or
        $bbScan.sourceStackSha256 -ne $expectedSourceHash -or
        $bbScan.sourceFiles -ne $sourceAudit.sourceFiles -or
        $bbScan.generationAuthorized -ne $false -or
        $bbScan.compilerValidated -ne $false -or
        $bbScan.measuredSavingsClaimed -ne $false -or
        $bbScan.sourceWrites -ne $false) {
        throw 'GCO_BLACKBOARD_DEFINITION_REAL_SOURCE_CONTRACT_FAILED'
    }
    $bbCount = [int]$bbScan.eligible
    # Both variants are owned by the existing immutable-definition
    # generator; never duplicate a blackboard pass or infer type by mod name.
    $bbStatement = @($bbScan.candidates | Where-Object {
        $_.rule -eq 'BLACKBOARD_DEFINITION_STATEMENT_V1'
    })
    $bbSequence = @($bbScan.candidates | Where-Object {
        $_.rule -eq 'BLACKBOARD_DEFINITION_SEQUENCE_V1'
    })
    if ($bbCount -ne ($bbStatement.Count + $bbSequence.Count)) {
        throw 'GCO_BLACKBOARD_DEFINITION_UNKNOWN_GENERIC_VARIANT'
    }
    foreach ($sequence in $bbSequence) {
        if ($sequence.sourceControlledScope -ne $true -or
            $sequence.sameReceiverAndGroup -ne $true -or
            $sequence.originalSetterCountAndOrderRetained -ne $true -or
            $sequence.originalKeyFieldAndLiveValueAccessRetained -ne $true -or
            [int]$sequence.consecutiveStatements -lt 2 -or
            [int]$sequence.repeatedGroupLookupsAvoidable -ne
                ([int]$sequence.consecutiveStatements - 1)) {
            throw 'GCO_BLACKBOARD_SEQUENCE_SOURCE_PROOF_INVALID'
        }
    }
    $bbFolder = Join-Path $work 'gco-blackboard-definition-preview'
    $bbStageCount = 0
    $bbComposedCount = 0
    if ($bbCount -gt 0) {
        $bbLog = Join-Path $work 'gco-blackboard-definition-preview.log'
        & dotnet $dll preview-blackboard-defs --game $game --analysis $result --out $bbFolder --base-preview $callbackFolder *> $bbLog
        if ($LASTEXITCODE -ne 0) {
            throw 'GCO_BLACKBOARD_DEFINITION_REAL_SOURCE_PREVIEW_FAILED'
        }
        $bbScripts = Join-Path $bbFolder 'r6/scripts'
        if (!(Test-Path -LiteralPath $bbScripts -PathType Container)) {
            throw 'GCO_BLACKBOARD_DEFINITION_PREVIEW_MISSING'
        }
        $bbFiles = @(Get-ChildItem -LiteralPath $bbScripts -Recurse -File)
        if ($bbFiles.Count -lt 1 -or $bbFiles.Count -gt $bbCount) {
            throw 'GCO_BLACKBOARD_DEFINITION_PREVIEW_COUNT_UNPROVEN'
        }
        $callbackScripts = Join-Path $callbackFolder 'r6/scripts'
        foreach ($file in $bbFiles) {
            $relative = [IO.Path]::GetRelativePath($bbScripts, $file.FullName)
            $originalPath = Join-Path (Join-Path $game 'r6/scripts') $relative
            $target = Join-Path $callbackScripts $relative
            if ($file.Extension -ne '.reds' -or
                [IO.Path]::IsPathRooted($relative) -or
                $relative.StartsWith('..') -or $relative.Contains(':') -or
                !(Test-Path -LiteralPath $originalPath -PathType Leaf) -or
                (Get-FileHash -LiteralPath $originalPath -Algorithm SHA256).Hash -eq
                (Get-FileHash -LiteralPath $file.FullName -Algorithm SHA256).Hash -or
                (Get-Content -LiteralPath $file.FullName -Raw) -cnotmatch
                'GCO-GENERIC:BLACKBOARD_DEFINITION_(?:STATEMENT|SEQUENCE)_V1') {
                throw 'GCO_BLACKBOARD_DEFINITION_OUTPUT_INVALID'
            }
            if (Test-Path -LiteralPath $target -PathType Leaf) {
                # The owner blackboard matcher has already verified the exact
                # unmodified source statement inside the current effective
                # preview. Its output contains BOTH rewrites. Verify that no
                # existing generic marker disappeared before replacing only
                # this disposable CI stage file.
                $prior = Get-Content -LiteralPath $target -Raw
                $merged = Get-Content -LiteralPath $file.FullName -Raw
                $priorMarkers = @([regex]::Matches($prior,
                    '(?:GCO|GRSP)-GENERIC:[A-Z0-9_]+') |
                    ForEach-Object { $_.Value } | Select-Object -Unique)
                if ($priorMarkers.Count -lt 1) {
                    throw 'GCO_BLACKBOARD_COMPOSITION_UNVERIFIED_PRIOR_OWNER'
                }
                foreach ($marker in $priorMarkers) {
                    if (!$merged.Contains($marker, [StringComparison]::Ordinal)) {
                        throw 'GCO_BLACKBOARD_COMPOSITION_DROPPED_PREVIOUS_RULE'
                    }
                }
                Copy-Item -LiteralPath $file.FullName -Destination $target -Force -ErrorAction Stop
                $bbComposedCount++
                continue
            }
            New-Item -ItemType Directory -Force -Path (Split-Path $target -Parent) | Out-Null
            Copy-Item -LiteralPath $file.FullName -Destination $target -ErrorAction Stop
            $bbStageCount++
        }
        if ($bbStageCount + $bbComposedCount -ne $bbFiles.Count) {
            throw 'GCO_BLACKBOARD_DEFINITION_MERGE_ACCOUNTING_FAILED'
        }
        Write-Host ('PASS GCO blackboard immutable-definition source preview: eligible=' +
            $bbCount + ' statements=' + $bbStatement.Count +
            ' sequences=' + $bbSequence.Count +
            ' files=' + $bbFiles.Count + ' stagedDisjoint=' +
            $bbStageCount + ' composedSameFile=' + $bbComposedCount +
            ' deferredFileCollisions=0 originalSourceUntouched=true deployable=false')
    }
    else {
        Write-Host 'GCO blackboard definitions: no matching source shape; no files staged.'
    }

    # G-PureConstantSlice V1: source-proven zero-argument final-static Bool
    # literal getters only. Reuse the one existing REDscript parser and GCO
    # effective-stage source, then compile actual generated .reds alongside
    # all previous generic previews. No mod name/path allowlist and no runtime
    # observer. Refuse foreign wrappers, invalid imports or altered inputs.
    $pureScanFile = Join-Path $work 'gco-pure-bool-scan.json'
    $pureScanLog = Join-Path $work 'gco-pure-bool-scan.log'
    & dotnet $dll scan-pure-constants --game $game --out $pureScanFile *> $pureScanLog
    if ($LASTEXITCODE -ne 0 -or !(Test-Path -LiteralPath $pureScanFile -PathType Leaf)) {
        throw 'GCO_PURE_BOOL_REAL_SOURCE_SCAN_FAILED'
    }
    $pureScan = Get-Content -LiteralPath $pureScanFile -Raw | ConvertFrom-Json
    if ($pureScan.schemaVersion -ne 'gco.redscript.pure-final-bool.scan.v1' -or
        $pureScan.rule -ne 'PURE_FINAL_STATIC_BOOL_V1' -or
        $pureScan.sourceStackSha256 -ne $expectedSourceHash -or
        $pureScan.sourceFiles -ne $sourceAudit.sourceFiles -or
        $pureScan.noClassOrModNameAllowlist -ne $true -or
        $pureScan.lexicalShapeAloneDoesNotAuthorizeRewrite -ne $true -or
        $pureScan.callerExecutionAndLiveStateUnchanged -ne $true -or
        $pureScan.sourceCallSideEffectsProvenAbsent -ne $true -or
        $pureScan.generationAuthorized -ne $false -or
        $pureScan.compilerValidated -ne $false -or
        $pureScan.performanceSavingsClaimed -ne $false -or
        $pureScan.sourceWrites -ne $false -or
        [int]$pureScan.eligibleSites -ne @($pureScan.sites).Count) {
        throw 'GCO_PURE_BOOL_REAL_SOURCE_CONTRACT_FAILED'
    }
    $pureCount = [int]$pureScan.eligibleSites
    $pureFolder = Join-Path $work 'gco-pure-bool-preview'
    $pureStaged = 0
    $pureComposed = 0
    if ($pureCount -gt 0) {
        $pureLog = Join-Path $work 'gco-pure-bool-preview.log'
        & dotnet $dll preview-pure-constants --game $game --analysis $result --out $pureFolder --base-preview $callbackFolder *> $pureLog
        if ($LASTEXITCODE -ne 0) {
            throw 'GCO_PURE_BOOL_SOURCE_PREVIEW_FAILED'
        }
        $pureScripts = Join-Path $pureFolder 'r6/scripts'
        if (!(Test-Path -LiteralPath $pureScripts -PathType Container)) {
            throw 'GCO_PURE_BOOL_NO_GENERATED_SOURCES'
        }
        $pureFiles = @(Get-ChildItem -LiteralPath $pureScripts -Recurse -File)
        if ($pureFiles.Count -lt 1 -or $pureFiles.Count -gt $pureCount) {
            throw 'GCO_PURE_BOOL_OUTPUT_FILE_COUNT_INVALID'
        }
        $combinedScripts = Join-Path $callbackFolder 'r6/scripts'
        foreach ($file in $pureFiles) {
            $relative = [IO.Path]::GetRelativePath($pureScripts, $file.FullName)
            $originalPath = Join-Path (Join-Path $game 'r6/scripts') $relative
            $target = Join-Path $combinedScripts $relative
            if ($file.Extension -ne '.reds' -or
                [IO.Path]::IsPathRooted($relative) -or
                $relative.StartsWith('..') -or $relative.Contains(':') -or
                !(Test-Path -LiteralPath $originalPath -PathType Leaf) -or
                (Get-FileHash -LiteralPath $originalPath -Algorithm SHA256).Hash -eq
                (Get-FileHash -LiteralPath $file.FullName -Algorithm SHA256).Hash -or
                (Get-Content -LiteralPath $file.FullName -Raw) -cnotmatch
                'GCO-GENERIC:PURE_FINAL_STATIC_BOOL_V1') {
                throw 'GCO_PURE_BOOL_GENERATED_SOURCE_INVALID'
            }
            if (Test-Path -LiteralPath $target -PathType Leaf) {
                $prior = Get-Content -LiteralPath $target -Raw
                $composed = Get-Content -LiteralPath $file.FullName -Raw
                $priorMarkers = @([regex]::Matches($prior,
                    '(?:GCO|GRSP)-GENERIC:[A-Z0-9_]+') |
                    ForEach-Object { $_.Value } | Select-Object -Unique)
                if ($priorMarkers.Count -lt 1) {
                    throw 'GCO_PURE_BOOL_UNKNOWN_PRIOR_GENERATION'
                }
                foreach ($marker in $priorMarkers) {
                    if (!$composed.Contains($marker, [StringComparison]::Ordinal)) {
                        throw 'GCO_PURE_BOOL_COMPOSITION_LOST_PRIOR_GENERATOR'
                    }
                }
                Copy-Item -LiteralPath $file.FullName -Destination $target -Force -ErrorAction Stop
                $pureComposed++
            } else {
                New-Item -ItemType Directory -Force -Path (Split-Path $target -Parent) | Out-Null
                Copy-Item -LiteralPath $file.FullName -Destination $target -ErrorAction Stop
                $pureStaged++
            }
        }
        if ($pureStaged + $pureComposed -ne $pureFiles.Count) {
            throw 'GCO_PURE_BOOL_OUTPUT_ACCOUNTING_FAILED'
        }
        Write-Host ('PASS GCO pure final-static Bool source preview: sites=' +
            $pureCount + ' files=' + $pureFiles.Count +
            ' new=' + $pureStaged + ' composed=' + $pureComposed +
            ' generatedSource=true originalsUntouched=true deployable=false')
    }
    else {
        Write-Host 'GCO pure final-static Bool: no source-proven callsites in current stack.'
    }

    # G-InputEventReuse V1: shared source-wide callback-local immutable
    # ListenerAction decode. Do not change engine listeners, input consumers,
    # return values or wrapped-method dispatch. Original source is untouched.
    $snapshotScanPath = Join-Path $work 'gco-action-snapshot-scan.json'
    $snapshotScanLog = Join-Path $work 'gco-action-snapshot-scan.log'
    & dotnet $dll scan-action-snapshot --game $game --out $snapshotScanPath *> $snapshotScanLog
    if ($LASTEXITCODE -ne 0 -or !(Test-Path -LiteralPath $snapshotScanPath -PathType Leaf)) {
        throw 'GCO_ACTION_SNAPSHOT_REAL_SOURCE_SCAN_FAILED'
    }
    $snapshotScan = Get-Content -LiteralPath $snapshotScanPath -Raw | ConvertFrom-Json
    if ($snapshotScan.schemaVersion -ne 'gco.redscript.action-snapshot.scan.v1' -or
        $snapshotScan.rule -ne 'ACTION_EVENT_SNAPSHOT_V1' -or
        $snapshotScan.sourceStackSha256 -ne $expectedSourceHash -or
        [int]$snapshotScan.sourceFiles -ne [int]$sourceAudit.sourceFiles -or
        $snapshotScan.noModAllowlist -ne $true -or
        $snapshotScan.rawInputConsumerAndListenerUnchanged -ne $true -or
        $snapshotScan.productionZipAuthorized -ne $false -or
        $snapshotScan.sourceWrites -ne $false) {
        throw 'GCO_ACTION_SNAPSHOT_REAL_SOURCE_CONTRACT_FAILED'
    }
    $snapshotCount = [int]$snapshotScan.eligible
    $snapshotMerged = 0
    if ($snapshotCount -gt 0) {
        $snapshotFolder = Join-Path $work 'gco-action-snapshot-preview'
        $snapshotLog = Join-Path $work 'gco-action-snapshot-preview.log'
        & dotnet $dll preview-action-snapshot --game $game --analysis $result --out $snapshotFolder --base-preview $callbackFolder *> $snapshotLog
        if ($LASTEXITCODE -ne 0) {
            throw 'GCO_ACTION_SNAPSHOT_SOURCE_PREVIEW_FAILED'
        }
        $snapshotScripts = Join-Path $snapshotFolder 'r6/scripts'
        if (!(Test-Path -LiteralPath $snapshotScripts -PathType Container)) {
            throw 'GCO_ACTION_SNAPSHOT_NO_GENERATED_SOURCES'
        }
        $snapshotFiles = @(Get-ChildItem -LiteralPath $snapshotScripts -Recurse -Filter *.reds -File)
        if ($snapshotFiles.Count -lt 1 -or $snapshotFiles.Count -gt $snapshotCount) {
            throw 'GCO_ACTION_SNAPSHOT_OUTPUT_COUNT_INVALID'
        }
        $manualScripts = Join-Path $callbackFolder 'r6/scripts'
        foreach ($file in $snapshotFiles) {
            $rel = [IO.Path]::GetRelativePath($snapshotScripts, $file.FullName)
            $original = Join-Path (Join-Path $game 'r6/scripts') $rel
            $destination = Join-Path $manualScripts $rel
            if ([IO.Path]::IsPathRooted($rel) -or $rel.StartsWith('..') -or
                $rel.Contains(':') -or !(Test-Path -LiteralPath $original -PathType Leaf) -or
                (Get-FileHash -LiteralPath $original -Algorithm SHA256).Hash -eq
                (Get-FileHash -LiteralPath $file.FullName -Algorithm SHA256).Hash -or
                (Get-Content -LiteralPath $file.FullName -Raw) -cnotmatch
                'GCO-GENERIC:ACTION_EVENT_SNAPSHOT_V1') {
                throw 'GCO_ACTION_SNAPSHOT_GENERATED_SOURCE_INVALID'
            }
            if (Test-Path -LiteralPath $destination -PathType Leaf) {
                $prior = Get-Content -LiteralPath $destination -Raw
                $after = Get-Content -LiteralPath $file.FullName -Raw
                $priorMarkers = @([regex]::Matches($prior,
                    '(?:GCO|GRSP)-GENERIC:[A-Z0-9_]+') |
                    ForEach-Object { $_.Value } | Select-Object -Unique)
                foreach ($marker in $priorMarkers) {
                    if (!$after.Contains($marker, [StringComparison]::Ordinal)) {
                        throw 'GCO_ACTION_SNAPSHOT_LOST_PREVIOUS_RULE'
                    }
                }
                Copy-Item -LiteralPath $file.FullName -Destination $destination -Force -ErrorAction Stop
            }
            else {
                New-Item -ItemType Directory -Force -Path (Split-Path $destination -Parent) | Out-Null
                Copy-Item -LiteralPath $file.FullName -Destination $destination -ErrorAction Stop
            }
            $snapshotMerged++
        }
        Write-Host ('PASS action-event snapshot source: avoidedGetterSites=' +
            $snapshotCount + ' files=' + $snapshotFiles.Count +
            ' engineRegistrationsUnchanged=true deployable=false')
    }
    else {
        Write-Host 'GCO action-event snapshot: no applicable repeated input getters.'
    }

    # Preserve the full pre-framework generic overlay as its own SCC lane.
    # New shared-player source imports G-REDruntime and belongs only in the
    # separate framework-composed compilation scenario.
    $legacyCallbackFolder = Join-Path $work 'gco-pre-framework-generic-preview'
    $legacyScripts = Join-Path $legacyCallbackFolder 'r6/scripts'
    New-Item -ItemType Directory -Force -Path $legacyScripts | Out-Null
    Get-ChildItem -LiteralPath (Join-Path $callbackFolder 'r6/scripts') -Force |
        Copy-Item -Destination $legacyScripts -Recurse -Force -ErrorAction Stop
    if (@(Get-ChildItem -LiteralPath $legacyScripts -Recurse -Filter *.reds -File).Count -ne
        @(Get-ChildItem -LiteralPath (Join-Path $callbackFolder 'r6/scripts') -Recurse -Filter *.reds -File).Count) {
        throw 'GCO_LEGACY_GENERIC_CHECKPOINT_SNAPSHOT_INCOMPLETE'
    }

    # First cross-author shared player producer adoption: use existing
    # G-REDruntime StateCache attach generation, with the original getter
    # fallback for UNKNOWN. This composes AFTER the A/E source transforms.
    $sharedPlayerScanPath = Join-Path $work 'gco-shared-player-scan.json'
    $sharedPlayerScanLog = Join-Path $work 'gco-shared-player-scan.log'
    & dotnet $dll scan-shared-player --game $game --out $sharedPlayerScanPath *> $sharedPlayerScanLog
    if ($LASTEXITCODE -ne 0 -or
        !(Test-Path -LiteralPath $sharedPlayerScanPath -PathType Leaf)) {
        throw 'GCO_SHARED_PLAYER_SOURCE_SCAN_FAILED'
    }
    $sharedPlayerScan = Get-Content -LiteralPath $sharedPlayerScanPath -Raw | ConvertFrom-Json
    if ($sharedPlayerScan.schemaVersion -ne 'gco.redscript.shared-player.scan.v1' -or
        $sharedPlayerScan.sourceStackSha256 -ne $expectedSourceHash -or
        [int]$sharedPlayerScan.sourceFiles -ne [int]$sourceAudit.sourceFiles -or
        $sharedPlayerScan.productionZipAuthorized -ne $false) {
        throw 'GCO_SHARED_PLAYER_SCAN_CONTRACT_FAILED'
    }
    $sharedPlayerCount = [int]$sharedPlayerScan.eligible
    $sharedPlayerMerged = 0
    if ($sharedPlayerCount -gt 0) {
        $sharedPlayerFolder = Join-Path $work 'gco-shared-player-preview'
        $sharedPlayerLog = Join-Path $work 'gco-shared-player-preview.log'
        & dotnet $dll preview-shared-player --game $game --analysis $result --out $sharedPlayerFolder --base-preview $callbackFolder *> $sharedPlayerLog
        if ($LASTEXITCODE -ne 0) {
            throw 'GCO_SHARED_PLAYER_SOURCE_PREVIEW_FAILED'
        }
        $sharedPlayerScripts = Join-Path $sharedPlayerFolder 'r6/scripts'
        $sharedFiles = @(Get-ChildItem -LiteralPath $sharedPlayerScripts -Filter *.reds -File -Recurse)
        if ($sharedFiles.Count -lt 1 -or $sharedFiles.Count -gt $sharedPlayerCount) {
            throw 'GCO_SHARED_PLAYER_GENERATED_FILE_COUNT_INVALID'
        }
        $manualScripts = Join-Path $callbackFolder 'r6/scripts'
        foreach ($file in $sharedFiles) {
            $rel = [IO.Path]::GetRelativePath($sharedPlayerScripts, $file.FullName)
            $original = Join-Path (Join-Path $game 'r6/scripts') $rel
            $destination = Join-Path $manualScripts $rel
            if ([IO.Path]::IsPathRooted($rel) -or $rel.StartsWith('..') -or
                $rel.Contains(':') -or !(Test-Path -LiteralPath $original -PathType Leaf) -or
                (Get-FileHash -LiteralPath $original -Algorithm SHA256).Hash -eq
                (Get-FileHash -LiteralPath $file.FullName -Algorithm SHA256).Hash -or
                (Get-Content -LiteralPath $file.FullName -Raw) -cnotmatch
                'GCO-GENERIC:SHARED_PLAYER_SIGNAL_V1') {
                throw 'GCO_SHARED_PLAYER_OUTPUT_INVALID'
            }
            if (Test-Path -LiteralPath $destination -PathType Leaf) {
                $prior = Get-Content -LiteralPath $destination -Raw
                $after = Get-Content -LiteralPath $file.FullName -Raw
                $priorMarkers = @([regex]::Matches($prior,
                    '(?:GCO|GRSP)-GENERIC:[A-Z0-9_]+') |
                    ForEach-Object { $_.Value } | Select-Object -Unique)
                foreach ($marker in $priorMarkers) {
                    if (!$after.Contains($marker, [StringComparison]::Ordinal)) {
                        throw 'GCO_SHARED_PLAYER_LOST_PREVIOUS_RULE'
                    }
                }
                Copy-Item -LiteralPath $file.FullName -Destination $destination -Force -ErrorAction Stop
            }
            else {
                New-Item -ItemType Directory -Force -Path (Split-Path $destination -Parent) | Out-Null
                Copy-Item -LiteralPath $file.FullName -Destination $destination -ErrorAction Stop
            }
            $sharedPlayerMerged++
        }
        Write-Host ('PASS GCO shared player provider source: callsites=' +
            $sharedPlayerCount + ' files=' + $sharedFiles.Count +
            ' originalGetterFallback=true noNewObserver=true deployable=false')
    }
    else {
        Write-Host 'GCO shared player provider: no exact typed producer consumers.'
    }

    # GCO Resolver owns one ordered source plan; existing generators
    # consume it. Require identical current-source output to the legacy
    # CI orchestration before considering app generation integration.
    $orderedPlanFile = Join-Path $work 'gco-redscript-generation-plan.json'
    $orderedPlanLog = Join-Path $work 'gco-redscript-generation-plan.log'
    & dotnet $dll plan-redscript --game $game --out $orderedPlanFile *> $orderedPlanLog
    if ($LASTEXITCODE -ne 0 -or !(Test-Path -LiteralPath $orderedPlanFile -PathType Leaf)) {
        throw 'GCO_REDSCRIPT_ORDERED_PLAN_FAILED'
    }
    $orderedPlan = Get-Content -LiteralPath $orderedPlanFile -Raw | ConvertFrom-Json
    if ($orderedPlan.schemaVersion -ne 'gco.redscript.generation-plan.v1' -or
        $orderedPlan.sourceStackSha256 -ne $expectedSourceHash -or
        [int]$orderedPlan.sourceFiles -ne [int]$sourceAudit.sourceFiles -or
        [int]$orderedPlan.implementedRuleCount -ne 10 -or
        [int]$orderedPlan.captureGatedExistingRules -ne 0 -or
        [int]$orderedPlan.deferredExperimentalSystems -ne 5 -or
        $orderedPlan.productionZipAuthorized -ne $false -or
        [int]$orderedPlan.readyRuleCount -lt 1) {
        throw 'GCO_REDSCRIPT_ORDERED_PLAN_CONTRACT_INVALID'
    }
    $scheduled = @($orderedPlan.rules | Where-Object { $null -ne $_.order })
    $deferred = @($orderedPlan.rules | Where-Object { $_.status -eq 'NO_GENERATOR_YET' })
    $gated = @($orderedPlan.rules | Where-Object {
        $_.status -eq 'CAPTURE_GATED_EXISTING_GENERATOR'
    })
    if ($scheduled.Count -ne 10 -or $deferred.Count -ne 5 -or
        $gated.Count -ne 0 -or
        @($scheduled | Where-Object { $_.id -eq 'source-cname-membership' }).Count -ne 1) {
        throw 'GCO_REDSCRIPT_ORDERED_PLAN_RULES_MISSING'
    }
    if (@($scheduled | Where-Object { $_.id -eq 'native-player-location-predicate' }).Count -ne 0) {
        throw 'GCO_RETIRED_LOCATION_RULE_STILL_SCHEDULED'
    }
    $stableRow = @($scheduled | Where-Object { $_.id -eq 'stable-array-bound' })
    if ($stableRow.Count -ne 1 -or $stableRow[0].order -ne 10 -or
        $stableRow[0].rule -ne 'STABLE_ARRAY_BOUND_V1') {
        throw 'GCO_STABLE_ARRAY_BOUND_MISSING_OR_WRONG_ORDER'
    }
    $membershipRow = @($scheduled | Where-Object { $_.id -eq 'source-cname-membership' })
    $membershipPlanned = [int]$membershipRow[0].eligible
    $membershipScan = Join-Path $work 'gco-cname-source-membership-scan.json'
    & dotnet $dll scan-cname-membership --game $game --out $membershipScan
    if ($LASTEXITCODE -ne 0 -or
        !(Test-Path -LiteralPath $membershipScan -PathType Leaf)) {
        throw 'GCO_CNAME_MEMBERSHIP_SOURCE_SCAN_FAILED'
    }
    $memberReport = Get-Content -LiteralPath $membershipScan -Raw | ConvertFrom-Json
    if ([int]$memberReport.eligible -ne $membershipPlanned -or
        $memberReport.sourceStackSha256 -ne $expectedSourceHash) {
        throw 'GCO_CNAME_MEMBERSHIP_PLAN_SOURCE_SCAN_MISMATCH'
    }
    # Inspect the original installed sources with the authoritative token
    # reader; neither mod names nor captured frequencies gate eligibility.
    $stableScanPath = Join-Path $work 'gco-stable-array-bound-scan.json'
    & dotnet $dll scan-stable-array-bound --game $game --out $stableScanPath
    if ($LASTEXITCODE -ne 0 -or
        !(Test-Path -LiteralPath $stableScanPath -PathType Leaf)) {
        throw 'GCO_STABLE_ARRAY_BOUND_REAL_SOURCE_SCAN_FAILED'
    }
    $stableReport = Get-Content -LiteralPath $stableScanPath -Raw | ConvertFrom-Json
    $stablePlanned = [int]$stableRow[0].eligible
    $stableAdmitted = @($stableReport.candidates | Where-Object {
        $_.state -eq 'PRISTINE_APPLICABLE'
    })
    $semanticReserved = @('ReinforcementsSystem','Dark Future','FlushingEtiquette',
        'Crowd Voice Unlocked','custom_quickslots','MuchBetterContracts','DigitalVixenCore')
    $genericOwners = @($stableAdmitted | ForEach-Object {
        $segments = $_.path.Split('/')
        if ($segments.Count -ge 4 -and $segments[2] -notin $semanticReserved) {
            $segments[2]
        }
    } | Sort-Object -Unique)
    if ($stableReport.schemaVersion -ne 'gco.redscript.array-bound.scan.v1' -or
        $stableReport.sourceStackSha256 -ne $expectedSourceHash -or
        [int]$stableReport.sourceFiles -ne [int]$sourceAudit.sourceFiles -or
        [int]$stableReport.eligible -ne $stablePlanned -or
        $stableAdmitted.Count -ne $stablePlanned -or
        $genericOwners.Count -lt 3 -or
        $stableReport.generationAuthorized -ne $false -or
        $stableReport.originalSourcesModified -ne $false) {
        throw 'GCO_STABLE_ARRAY_BOUND_REAL_SOURCE_ADMISSION_FAILED'
    }
    Write-Host ('PASS GCO stable ArraySize-bound source scan: eligible=' +
        $stablePlanned + ' distinctNonSemanticOwners=' + $genericOwners.Count +
        ' sourceUnchanged=true noModRule=true')

    $planEligible = [int](($scheduled | Measure-Object -Property eligible -Sum).Sum)
    if ($planEligible -ne [int]$orderedPlan.eligibleSourceEdits -or
        $planEligible -ne ($callbackCount + $actionCount + $angleCount +
            $lateCount + $bbCount + $pureCount + $snapshotCount + $sharedPlayerCount +
            $membershipPlanned + $stablePlanned)) {
        throw 'GCO_REDSCRIPT_ORDERED_PLAN_SOURCE_COUNTS_DISAGREE'
    }
    # Independently produce the same source-admitted membership edits on the
    # existing fully composed manual overlay. Unlike the old measured-only
    # preview, no individual OperatorLess/AssignAdd capture row is required.
    if ($membershipPlanned -gt 0) {
        $memberManual = Join-Path $work 'gco-manual-cname-membership-composed'
        & dotnet $dll preview-cname-membership --game $game --analysis $result --out $memberManual --base-preview $callbackFolder
        if ($LASTEXITCODE -ne 0) {
            throw 'GCO_CNAME_MEMBERSHIP_MANUAL_COMPOSITION_FAILED'
        }
        $memberManualScripts = Join-Path $memberManual 'r6/scripts'
        $memberGeneratedFiles = @(Get-ChildItem -LiteralPath $memberManualScripts -Filter *.reds -File -Recurse)
        if ($memberGeneratedFiles.Count -lt 1 -or
            $memberGeneratedFiles.Count -gt $membershipPlanned) {
            throw 'GCO_CNAME_MEMBERSHIP_MANUAL_FILE_COUNT_INVALID'
        }
        foreach ($memberFile in $memberGeneratedFiles) {
            $rel = [IO.Path]::GetRelativePath($memberManualScripts, $memberFile.FullName)
            $destination = Join-Path (Join-Path $callbackFolder 'r6/scripts') $rel
            $after = Get-Content -LiteralPath $memberFile.FullName -Raw
            if ($rel.StartsWith('..') -or $rel.Contains(':') -or
                !$after.Contains('GRSP-GENERIC:CNAME_ARRAY_MEMBERSHIP_V1',
                                 [StringComparison]::Ordinal)) {
                throw 'GCO_CNAME_MEMBERSHIP_MANUAL_OUTPUT_INVALID'
            }
            if (Test-Path -LiteralPath $destination -PathType Leaf) {
                $before = Get-Content -LiteralPath $destination -Raw
                $markers = @([regex]::Matches($before,
                    '(?:GCO|GRSP)-GENERIC:[A-Z0-9_]+') |
                    ForEach-Object { $_.Value } | Select-Object -Unique)
                foreach ($marker in $markers) {
                    if (!$after.Contains($marker, [StringComparison]::Ordinal)) {
                        throw 'GCO_CNAME_MEMBERSHIP_MANUAL_LOST_PREVIOUS_RULE'
                    }
                }
            }
            else {
                New-Item -ItemType Directory -Force -Path (Split-Path $destination -Parent) | Out-Null
            }
            Copy-Item -LiteralPath $memberFile.FullName -Destination $destination -Force
        }
    }
    # Generate the new generic pass on the already-composed source, then
    # independently compare the complete output against the canonical
    # ordered-plan Stage() result. Never patch older generated files afterward.
    if ($stablePlanned -gt 0) {
        $stableManual = Join-Path $work 'gco-manual-array-bound-composed'
        & dotnet $dll preview-stable-array-bound --game $game --analysis $result --out $stableManual --base-preview $callbackFolder
        if ($LASTEXITCODE -ne 0) {
            throw 'GCO_STABLE_ARRAY_BOUND_MANUAL_COMPOSITION_FAILED'
        }
        $stableScripts = Join-Path $stableManual 'r6/scripts'
        $stableFiles = @(Get-ChildItem -LiteralPath $stableScripts -Filter *.reds -File -Recurse)
        if ($stableFiles.Count -lt 1 -or $stableFiles.Count -gt $stablePlanned) {
            throw 'GCO_STABLE_ARRAY_BOUND_MANUAL_FILE_COUNT_INVALID'
        }
        foreach ($stableFile in $stableFiles) {
            $rel = [IO.Path]::GetRelativePath($stableScripts, $stableFile.FullName)
            $destination = Join-Path (Join-Path $callbackFolder 'r6/scripts') $rel
            $after = Get-Content -LiteralPath $stableFile.FullName -Raw
            if ($rel.StartsWith('..') -or $rel.Contains(':') -or
                !$after.Contains('GCO-GENERIC:STABLE_ARRAY_BOUND_V1',
                                 [StringComparison]::Ordinal)) {
                throw 'GCO_STABLE_ARRAY_BOUND_MANUAL_SOURCE_INVALID'
            }
            if (Test-Path -LiteralPath $destination -PathType Leaf) {
                $previous = Get-Content -LiteralPath $destination -Raw
                $priorMarkers = @([regex]::Matches($previous,
                    '(?:GCO|GRSP)-GENERIC:[A-Z0-9_]+') |
                    ForEach-Object { $_.Value } | Select-Object -Unique)
                foreach ($priorMarker in $priorMarkers) {
                    if (!$after.Contains($priorMarker, [StringComparison]::Ordinal)) {
                        throw 'GCO_STABLE_ARRAY_BOUND_MANUAL_LOST_EARLIER_RULE'
                    }
                }
            }
            else {
                New-Item -ItemType Directory -Force -Path (Split-Path $destination -Parent) | Out-Null
            }
            Copy-Item -LiteralPath $stableFile.FullName -Destination $destination -Force
        }
    }

    # Reference composition: apply the approved Phase 2 rule directly to the
    # already-composed ten-generic EFFECTIVE source. It must match the same
    # result that Stage() emits in a single GCO Generate invocation.
    $semanticReference = Join-Path $work 'gco-phase2-dvcore-reference'
    $semanticReferenceLog = Join-Path $work 'gco-phase2-dvcore-reference.log'
    & dotnet $dll phase2-footsteps --game $game --base-overlay $callbackFolder --out $semanticReference *> $semanticReferenceLog
    if ($LASTEXITCODE -ne 0) {
      $diagnostic=if (Test-Path -LiteralPath $semanticReferenceLog -PathType Leaf) {
        Get-Content -LiteralPath $semanticReferenceLog -Raw
      } else { '' }
      $code=[regex]::Match([string]$diagnostic,
        '(?:DVCORE|REDSCRIPT|ARRAY_BOUND|CNAME_MEMBERSHIP)_[A-Z0-9_]+')
      $safe=if ($code.Success) { $code.Value } else {
        'NO_SAFE_SOURCE_ERROR_CODE'
      }
      throw ('GCO_DVCORE_REFERENCE_GENERATOR_FAILED: '+$safe)
    }
    $semanticReport = Get-Content -LiteralPath (Join-Path $semanticReference 'phase2-result.json') -Raw | ConvertFrom-Json
    if ($semanticReport.ruleId -ne 'DVCORE_FOOTSTEPS_REFLECTION_SUCCESS_CACHE_V1' -or
        $semanticReport.status -notin @('APPLIED','COMPLETE','NOT_APPLICABLE','REFUSED')) {
      throw 'GCO_DVCORE_REFERENCE_RESULT_INVALID'
    }
    if ($semanticReport.status -eq 'APPLIED') {
      $semanticScripts = Join-Path $semanticReference 'r6/scripts'
      $semanticFiles = @(Get-ChildItem -LiteralPath $semanticScripts -Filter *.reds -File -Recurse)
      if ($semanticFiles.Count -ne 1 -or [int]$semanticReport.appliedEdits -ne 1) {
        throw 'GCO_DVCORE_REFERENCE_EXPECTED_SINGLE_FILE'
      }
      foreach ($file in $semanticFiles) {
        $relative = [IO.Path]::GetRelativePath($semanticScripts, $file.FullName)
        if ($relative.StartsWith('..') -or [IO.Path]::IsPathRooted($relative)) {
          throw 'GCO_DVCORE_REFERENCE_PATH_ESCAPE'
        }
        $dest = Join-Path (Join-Path $callbackFolder 'r6/scripts') $relative
        if (Test-Path -LiteralPath $dest -PathType Leaf) {
          $old = Get-Content -LiteralPath $dest -Raw
          $now = Get-Content -LiteralPath $file.FullName -Raw
          foreach ($m in @([regex]::Matches($old,
                   '(?:GCO|GRSP)-GENERIC:[A-Z0-9_]+') | ForEach-Object Value |
                   Sort-Object -Unique)) {
            if (!$now.Contains($m, [StringComparison]::Ordinal)) {
              throw 'GCO_DVCORE_REFERENCE_DISCARDED_GENERIC_SOURCE'
            }
          }
        }
        else { New-Item -ItemType Directory -Force -Path (Split-Path $dest -Parent) | Out-Null }
        Copy-Item -LiteralPath $file.FullName -Destination $dest -Force
      }
    }
    Write-Host ('PASS Phase 2 DVCore reference composition: status=' +
        $semanticReport.status + ' edits=' + [int]$semanticReport.appliedEdits +
        ' originalTenGenericRulesPreserved=true')

    # The second independently approved semantic operation must compose
    # *after* Footsteps over the SAME effective ten-generic source. The
    # original Dark Future UI and every animation call remain untouched.
    $dfReference = Join-Path $work 'gco-phase2-dark-future-reference'
    $dfReferenceLog = Join-Path $work 'gco-phase2-dark-future-reference.log'
    & dotnet $dll phase2-dark-future --game $game --base-overlay $callbackFolder --out $dfReference *> $dfReferenceLog
    if ($LASTEXITCODE -ne 0) {
      $diagnostic=if (Test-Path -LiteralPath $dfReferenceLog -PathType Leaf) {
        Get-Content -LiteralPath $dfReferenceLog -Raw
      } else { '' }
      $m=[regex]::Match([string]$diagnostic, 'DARK_FUTURE_[A-Z0-9_]+')
      $safe=if ($m.Success) { $m.Value } else { 'NO_SAFE_SOURCE_ERROR_CODE' }
      throw ('GCO_DARK_FUTURE_REFERENCE_GENERATOR_FAILED: '+$safe)
    }
    $dfReport = Get-Content -LiteralPath (Join-Path $dfReference 'phase2-result.json') -Raw | ConvertFrom-Json
    if ($dfReport.ruleId -ne 'DARK_FUTURE_INFINITE_TAG_CACHE_V1' -or
        $dfReport.status -notin @('APPLIED','COMPLETE','NOT_APPLICABLE','REFUSED')) {
      throw 'GCO_DARK_FUTURE_REFERENCE_RESULT_INVALID'
    }
    if ($dfReport.status -eq 'APPLIED') {
      $dfScripts = Join-Path $dfReference 'r6/scripts'
      $dfFiles = @(Get-ChildItem -LiteralPath $dfScripts -Filter '*.reds' -File -Recurse)
      if ($dfFiles.Count -ne 1 -or [int]$dfReport.appliedEdits -ne 1) {
        throw 'GCO_DARK_FUTURE_REFERENCE_EXPECTED_SINGLE_SOURCE'
      }
      foreach ($file in $dfFiles) {
        $rel=[IO.Path]::GetRelativePath($dfScripts, $file.FullName)
        if ([IO.Path]::IsPathRooted($rel) -or $rel.StartsWith('..')) {
          throw 'GCO_DARK_FUTURE_REFERENCE_PATH_ESCAPE'
        }
        $dest = Join-Path (Join-Path $callbackFolder 'r6/scripts') $rel
        if (Test-Path -LiteralPath $dest -PathType Leaf) {
          $old = Get-Content -LiteralPath $dest -Raw
          $now = Get-Content -LiteralPath $file.FullName -Raw
          foreach ($m in @([regex]::Matches($old,
                   '(?:GCO|GRSP)-GENERIC:[A-Z0-9_]+') |
                   ForEach-Object Value | Sort-Object -Unique)) {
            if (!$now.Contains($m, [StringComparison]::Ordinal)) {
              throw 'GCO_DARK_FUTURE_REFERENCE_LOST_GENERIC_SOURCE'
            }
          }
        }
        else {
          New-Item -ItemType Directory -Force -Path (Split-Path $dest -Parent) | Out-Null
        }
        Copy-Item -LiteralPath $file.FullName -Destination $dest -Force
      }
    }
    Write-Host ('PASS Phase 2 Dark Future reference: status='+$dfReport.status+
        ' edits='+[int]$dfReport.appliedEdits+
        ' originalUpdateAndAnimationsPreserved=true')

    # Third semantic operation is independently generated from the same
    # effective source that already contains the Dark Future tag cache.
    # It must not slow other ordinary Cyberpunk effect icons.
    $dfIconReference = Join-Path $work 'gco-phase2-df-icons-reference'
    $dfIconLog = Join-Path $work 'gco-phase2-df-icons-reference.log'
    & dotnet $dll phase2-dark-future-icons --game $game --base-overlay $callbackFolder --out $dfIconReference *> $dfIconLog
    if ($LASTEXITCODE -ne 0) {
      throw 'GCO_DF_ICON_REFERENCE_GENERATOR_FAILED'
    }
    $dfIconReport = Get-Content -LiteralPath (Join-Path $dfIconReference 'phase2-result.json') -Raw | ConvertFrom-Json
    if ($dfIconReport.ruleId -ne 'DARK_FUTURE_STATUS_ICON_400MS_V1' -or
        $dfIconReport.status -notin @('APPLIED','COMPLETE','NOT_APPLICABLE','REFUSED')) {
      throw 'GCO_DF_ICON_REFERENCE_RESULT_INVALID'
    }
    if ($dfIconReport.status -eq 'APPLIED') {
      $dfIconScripts = Join-Path $dfIconReference 'r6/scripts'
      $dfIconFiles = @(Get-ChildItem -LiteralPath $dfIconScripts -Filter '*.reds' -File -Recurse)
      if ($dfIconFiles.Count -ne 1 -or [int]$dfIconReport.appliedEdits -ne 1) {
        throw 'GCO_DF_ICON_REFERENCE_SINGLE_FILE_REQUIRED'
      }
      foreach ($file in $dfIconFiles) {
        $rel=[IO.Path]::GetRelativePath($dfIconScripts,$file.FullName)
        if ([IO.Path]::IsPathRooted($rel) -or $rel.StartsWith('..')) {
          throw 'GCO_DF_ICON_REFERENCE_PATH_ESCAPE'
        }
        $dest=Join-Path (Join-Path $callbackFolder 'r6/scripts') $rel
        if (!(Test-Path -LiteralPath $dest -PathType Leaf)) {
          throw 'GCO_DF_ICON_REFERENCE_DID_NOT_COMPOSE'
        }
        $old=Get-Content -LiteralPath $dest -Raw
        $new=Get-Content -LiteralPath $file.FullName -Raw
        if (!$old.Contains('m_gcoDfInfiniteTagValid') -or
            !$new.Contains('m_gcoDfInfiniteTagValid') -or
            !$new.Contains('DarkFutureCanHideOnBuffBar') -or
            !$new.Contains('now + 0.40') -or
            !$new.Contains('this.GetPlayerControlledObject()') -or
            !$new.Contains('GameInstance.GetTimeSystem(__gcoDfPlayer.GetGame())') -or
            !$new.Contains('this.SendVisibilityUpdate(') -or
            !$new.Contains('currBuffLoc.SetStatusEffectRecord(null);') -or
            $new.Contains('GameInstance.GetTimeSystem(this.GetGameInstance())')) {
          throw 'GCO_DF_ICON_REFERENCE_DROPPED_TAG_CACHE_OR_CADENCE'
        }
        foreach ($m in @([regex]::Matches($old,
                   '(?:GCO|GRSP)-GENERIC:[A-Z0-9_]+') |
                   ForEach-Object Value | Sort-Object -Unique)) {
          if (!$new.Contains($m,[StringComparison]::Ordinal)) {
            throw 'GCO_DF_ICON_REFERENCE_DROPPED_GENERIC_SOURCE'
          }
        }
        Copy-Item -LiteralPath $file.FullName -Destination $dest -Force
      }
    }
    Write-Host ('PASS Phase 2 Dark Future 400ms icon reference: status='+
        $dfIconReport.status+' edits='+[int]$dfIconReport.appliedEdits+
        ' existingTagCachePreserved=true')

    # The fourth semantic operation must also be independently generated
    # from the effective composition, not just accepted because Stage()
    # reports a count. This proves FE's source-level generic helper, the
    # original wrappedMethod and all earlier source changes survive.
    $feReference = Join-Path $work 'gco-phase2-fe-input-reference'
    $feReferenceLog = Join-Path $work 'gco-phase2-fe-input-reference.log'
    & dotnet $dll phase2-fe-input --game $game --base-overlay $callbackFolder --out $feReference *> $feReferenceLog
    if ($LASTEXITCODE -ne 0) {
      $diagnostic = if (Test-Path -LiteralPath $feReferenceLog -PathType Leaf) {
        Get-Content -LiteralPath $feReferenceLog -Raw
      } else { '' }
      $m=[regex]::Match([string]$diagnostic,'FE_INPUT_[A-Z0-9_]+')
      $safe=if ($m.Success) { $m.Value } else { 'NO_SAFE_SOURCE_ERROR_CODE' }
      throw ('GCO_FE_INPUT_REFERENCE_GENERATOR_FAILED: '+$safe)
    }
    $feReport = Get-Content -LiteralPath (Join-Path $feReference 'phase2-result.json') -Raw | ConvertFrom-Json
    if ($feReport.ruleId -ne 'FLUSHING_ETIQUETTE_UNRELATED_INPUT_GATE_V1' -or
        $feReport.status -notin @('APPLIED','COMPLETE','NOT_APPLICABLE','REFUSED')) {
      throw 'GCO_FE_INPUT_REFERENCE_RESULT_INVALID'
    }
    if ($feReport.status -eq 'APPLIED') {
      $feScripts = Join-Path $feReference 'r6/scripts'
      $feFiles = @(Get-ChildItem -LiteralPath $feScripts -Filter '*.reds' -File -Recurse)
      if ($feFiles.Count -ne 1 -or [int]$feReport.appliedEdits -ne 1) {
        throw 'GCO_FE_INPUT_REFERENCE_EXPECTED_ONE_SOURCE'
      }
      foreach ($file in $feFiles) {
        $rel=[IO.Path]::GetRelativePath($feScripts,$file.FullName)
        if ([IO.Path]::IsPathRooted($rel) -or $rel.StartsWith('..')) {
          throw 'GCO_FE_INPUT_REFERENCE_PATH_ESCAPE'
        }
        $dest=Join-Path (Join-Path $callbackFolder 'r6/scripts') $rel
        $new=Get-Content -LiteralPath $file.FullName -Raw
        if (!$new.Contains('ListenerAction.GetName(action)') -or
            !$new.Contains('ListenerAction.GetType(action)') -or
            !$new.Contains('return wrappedMethod(action, consumer);')) {
          throw 'GCO_FE_INPUT_REFERENCE_DROPPED_ORIGINAL_ACTION_FLOW'
        }
        if (Test-Path -LiteralPath $dest -PathType Leaf) {
          $old=Get-Content -LiteralPath $dest -Raw
          foreach ($marker in @([regex]::Matches($old,
              '(?:GCO|GRSP)-GENERIC:[A-Z0-9_]+') |
              ForEach-Object Value | Sort-Object -Unique)) {
            if (!$new.Contains($marker,[StringComparison]::Ordinal)) {
              throw 'GCO_FE_INPUT_REFERENCE_DROPPED_GENERIC_SOURCE'
            }
          }
        }
        else {
          New-Item -ItemType Directory -Force -Path (Split-Path $dest -Parent) | Out-Null
        }
        Copy-Item -LiteralPath $file.FullName -Destination $dest -Force
      }
    }
    Write-Host ('PASS Phase 2 FE input reference: status=' + $feReport.status +
        ' edits=' + [int]$feReport.appliedEdits +
        ' genericSourceAndOnActionFallbackPreserved=true')

    # The fifth approved semantic reconstructs historical FE washroom
    # session ownership AND pin activation before the hot global Update
    # wrapper is removed. Compare the actual original-path generated bytes.
    $pinReference = Join-Path $work 'gco-phase2-fe-session-pins-reference'
    $pinReferenceLog = Join-Path $work 'gco-phase2-fe-session-pins-reference.log'
    & dotnet $dll phase2-fe-pins --game $game --base-overlay $callbackFolder --out $pinReference *> $pinReferenceLog
    if ($LASTEXITCODE -ne 0) {
      $diagnostic = if (Test-Path -LiteralPath $pinReferenceLog -PathType Leaf) {
        Get-Content -LiteralPath $pinReferenceLog -Raw
      } else { '' }
      $m = [regex]::Match([string]$diagnostic, 'FE_PIN_[A-Z0-9_]+')
      $safe = if ($m.Success) { $m.Value } else { 'NO_SAFE_SOURCE_ERROR_CODE' }
      throw ('GCO_FE_PIN_REFERENCE_GENERATOR_FAILED: ' + $safe)
    }
    $pinReport = Get-Content -LiteralPath (Join-Path $pinReference 'phase2-result.json') -Raw | ConvertFrom-Json
    if ($pinReport.ruleId -ne 'FE_SESSION_WASHROOM_PINS_V1' -or
        $pinReport.status -notin @('APPLIED','COMPLETE','NOT_APPLICABLE','REFUSED')) {
      throw 'GCO_FE_PIN_REFERENCE_RESULT_INVALID'
    }
    if ($pinReport.status -eq 'APPLIED') {
      $pinScripts = Join-Path $pinReference 'r6/scripts'
      $pinFiles = @(Get-ChildItem -LiteralPath $pinScripts -Filter '*.reds' -File -Recurse)
      if ($pinFiles.Count -ne 2 -or [int]$pinReport.appliedEdits -ne 1 -or
          @($pinReport.sourcePaths).Count -ne 2) {
        throw 'GCO_FE_PIN_REFERENCE_EXPECTED_TWO_OWNER_FILES'
      }
      foreach ($file in $pinFiles) {
        $rel = [IO.Path]::GetRelativePath($pinScripts, $file.FullName)
        if ([IO.Path]::IsPathRooted($rel) -or $rel.StartsWith('..')) {
          throw 'GCO_FE_PIN_REFERENCE_PATH_ESCAPE'
        }
        $dest = Join-Path (Join-Path $callbackFolder 'r6/scripts') $rel
        $now = Get-Content -LiteralPath $file.FullName -Raw
        if ($file.Name -eq 'FE_Places.reds') {
          if (!$now.Contains('class FEWashroomMappinService') -or
              !$now.Contains('mappins.SetMappinActive(id, true);') -or
              !$now.Contains('callbacks.RegisterCallback(n"Session/Ready"') -or
              !$now.Contains('callbacks.RegisterCallback(n"Session/BeforeEnd"') -or
              !$now.Contains('FEPlaces.UnregisterPins(this.GetGameInstance(), this.m_ids);') -or
              !$now.Contains('FEPlaces.PinCount();') -or
              !$now.Contains('return mappin.GetScriptData() as FEWashroomPinData;') -or
              $now.Contains('NotEquals(mappin.GetVariant(), gamedataMappinVariant.SitVariant)') -or
              !$now.Contains('@wrapMethod(MinimapPOIMappinController)') -or
              !$now.Contains('this.FEApplyWashroomIcon();') -or
              !$now.Contains('inkWidgetRef.SetTintColor(this.iconWidget, FEPlaces.PinTint());') -or
              $now.Contains('@wrapMethod(BaseMinimapMappinController)')) {
            throw 'GCO_FE_PIN_REFERENCE_MISSING_SESSION_OR_ICON_PROOF'
          }
        }
        if ($file.Name -eq 'FlushingEtiquette.reds') {
          if (!$now.Contains('FEWashroomMappinService.Get(this.GetGameInstance())') -or
              $now.Contains('FEPlaces.UnregisterPins(gi, this.m_pins);')) {
            throw 'GCO_FE_PIN_REFERENCE_PLAYER_LIFECYCLE_UNCHANGED'
          }
        }
        if (Test-Path -LiteralPath $dest -PathType Leaf) {
          $old = Get-Content -LiteralPath $dest -Raw
          foreach ($marker in @([regex]::Matches($old,
              '(?:GCO|GRSP)-GENERIC:[A-Z0-9_]+') |
              ForEach-Object Value | Sort-Object -Unique)) {
            if (!$now.Contains($marker,[StringComparison]::Ordinal)) {
              throw 'GCO_FE_PIN_REFERENCE_DROPPED_GENERIC_SOURCE'
            }
          }
        }
        else {
          New-Item -ItemType Directory -Force -Path (Split-Path $dest -Parent) | Out-Null
        }
        Copy-Item -LiteralPath $file.FullName -Destination $dest -Force
      }
    }
    Write-Host ('PASS Phase 2 FE complete washroom pin reference: status='+
        $pinReport.status+' ownerFiles='+@($pinReport.sourcePaths).Count+
        ' sessionOwnershipAndSpecificIconPreserved=true')

    # Source-proven independent reference for Quickslots' specific InputHub
    # route. Compare real bytes, not just Stage's edit count: the inherited
    # OnAction, generics and fallback must survive the composed one ZIP.
    $cqReference=Join-Path $work 'gco-phase2-quickslots-reference'
    $cqReferenceLog=Join-Path $work 'gco-phase2-quickslots-reference.log'
    & dotnet $dll phase2-quickslots-input --game $game --base-overlay $callbackFolder --out $cqReference *> $cqReferenceLog
    if ($LASTEXITCODE -ne 0) {
      $diagnostic=if(Test-Path -LiteralPath $cqReferenceLog -PathType Leaf) {
        Get-Content -LiteralPath $cqReferenceLog -Raw
      } else { '' }
      $m=[regex]::Match([string]$diagnostic,'CQ_INPUT_[A-Z0-9_]+')
      $safe=if($m.Success){$m.Value}else{'NO_SAFE_SOURCE_ERROR_CODE'}
      throw ('GCO_CQ_INPUT_REFERENCE_FAILED: '+$safe)
    }
    $cqReport=Get-Content -LiteralPath (Join-Path $cqReference 'phase2-result.json') -Raw | ConvertFrom-Json
    if ($cqReport.ruleId -ne 'CUSTOM_QUICKSLOTS_SPECIFIC_INPUT_BRIDGE_V1' -or
        $cqReport.status -notin @('APPLIED','COMPLETE','NOT_APPLICABLE','REFUSED')) {
      throw 'GCO_CQ_INPUT_REFERENCE_RESULT_INVALID'
    }
    if ($cqReport.status -eq 'APPLIED') {
      $cqScripts=Join-Path $cqReference 'r6/scripts'
      $cqFiles=@(Get-ChildItem -LiteralPath $cqScripts -File -Recurse -Filter '*.reds')
      if ($cqFiles.Count -ne 1 -or [int]$cqReport.appliedEdits -ne 1) {
        throw 'GCO_CQ_INPUT_REFERENCE_EXPECTED_ONE_FILE'
      }
      foreach($file in $cqFiles) {
        $rel=[IO.Path]::GetRelativePath($cqScripts,$file.FullName)
        if([IO.Path]::IsPathRooted($rel) -or $rel.StartsWith('..')) {
          throw 'GCO_CQ_INPUT_REFERENCE_PATH_ESCAPE'
        }
        $dest=Join-Path (Join-Path $callbackFolder 'r6/scripts') $rel
        $now=Get-Content -LiteralPath $file.FullName -Raw
        if(!$now.Contains('class CustomQuickslotsGlobalInputListener extends InputListener') -or
           !$now.Contains('class CustomQuickslotsDeviceListener extends InputDeviceListener') -or
           !$now.Contains('this.customQuickslotsInputHub.Subscribe(') -or
           !$now.Contains('this.customQuickslotsInputHub.SubscribeDevice(') -or
           !$now.Contains('this.customQuickslotsInputHub.UnsubscribeDevice(') -or
           !$now.Contains('this.customQuickslotsFallbackRegistered = true;') -or
           !$now.Contains('GRSP-GENERIC:CNAME_ARRAY_MEMBERSHIP_V1') -or
           !$now.Contains('gamepadQuickslotInput = ArrayContains(this.gamepadActions, actionName)') -or
           !$now.Contains('this.customQuickslotsInputListener.SyncGraceState();') -or
           !$now.Contains('this.UnregisterInputListener(this.customQuickslotsInputListener);') -or
           !$now.Contains('CustomQuickslotsGlobalInputListener.SetLastUsedKBM(') -or
           @([regex]::Matches($now, 'func\s+OnGRedInput\s*\(')).Count -ne 1 -or
           @([regex]::Matches($now, 'func\s+OnGRedInputDeviceChanged\s*\(')).Count -ne 1) {
          throw 'GCO_CQ_INPUT_REFERENCE_DISCARDED_GENERIC_OR_FALLBACK'
        }
        if(Test-Path -LiteralPath $dest -PathType Leaf) {
          $old=Get-Content -LiteralPath $dest -Raw
          foreach($marker in @([regex]::Matches($old,
              '(?:GCO|GRSP)-GENERIC:[A-Z0-9_]+') |
              ForEach-Object Value | Sort-Object -Unique)) {
            if(!$now.Contains($marker,[StringComparison]::Ordinal)) {
              throw 'GCO_CQ_INPUT_REFERENCE_DISCARDED_GENERIC'
            }
          }
        } else {
          New-Item -ItemType Directory -Force -Path (Split-Path $dest -Parent) | Out-Null
        }
        Copy-Item -LiteralPath $file.FullName -Destination $dest -Force
      }
    }
    Write-Host ('PASS Phase 2 Quickslots specific input reference: status='+
        $cqReport.status+' edits='+[int]$cqReport.appliedEdits+
        ' legacyEngineFallbackAndGenericPreserved=true')

    # Independent semantic reference composition for newly admitted historical
    # ports. These use the same already-generic source and current *effective*
    # overlay as the production Stage, without copying historical files.
    # The strict file-count and byte-for-byte source comparison below remain
    # mandatory and catch omitted generators, duplicated owners or lost edits.
    $historicalPorts = @(
      @{ Id='quickhack-direct-tags'; Order=18; Command='phase2-quickhack-tags';
         Rule='QUICKHACK_FIXES_DIRECT_TAG_PREDICATE_V1'; Owner='Quickhack_Fixes' },
      @{ Id='redwindows-widget-lookup'; Order=19; Command='phase2-redwindows-find';
         Rule='REDWINDOWS_RETAINED_WIDGET_LOOKUP_V1'; Owner='RedWindows' },
      @{ Id='trigger-mode-literal-tags'; Order=20; Command='phase2-trigger-mode-tags';
         Rule='TRIGGER_MODE_LITERAL_TAG_NAMES_V1'; Owner='TriggerModeControl' },
      @{ Id='disable-position-tracing'; Order=21; Command='phase2-disable-position-tracing';
         Rule='DISABLE_POSITION_REVEAL_TRACING_GUARDS_V1'; Owner='DisablePositionReveal' },
      @{ Id='hologirl-material-event'; Order=22; Command='phase2-hologirl-material';
         Rule='HQ_HOLOGIRL_MATERIAL_EVENT_FASTPATH_V1'; Owner='HQ_HoloGirl' },
      @{ Id='redwindows-shape-label-cursors'; Order=23; Command='phase2-redwindows-cursors';
         Rule='REDWINDOWS_SHAPE_LABEL_CURSORS_V1'; Owner='RedWindows' },
      @{ Id='redwindows-render-dormancy'; Order=24; Command='phase2-redwindows-render';
         Rule='REDWINDOWS_RENDER_AWARE_DORMANCY_V1'; Owner='RedWindows' },
      @{ Id='dark-future-aggressive-throttles'; Order=25; Command='phase2-dark-future-aggressive';
         Rule='DARK_FUTURE_HISTORICAL_AGGRESSIVE_THROTTLES_V1'; Owner='Dark Future' },
      @{ Id='redwindows-redundant-setters'; Order=26; Command='phase2-redwindows-setters';
         Rule='REDWINDOWS_HISTORICAL_REDUNDANT_SETTERS_V1'; Owner='RedWindows' },
      @{ Id='digitalvixen-ui-dormancy'; Order=27; Command='phase2-digitalvixen-ui';
         Rule='DIGITAL_VIXEN_HISTORICAL_UI_DORMANCY_V1'; Owner='DigitalVixenCore' },
      @{ Id='browser-extension-journal'; Order=28; Command='phase2-browser-journal';
         Rule='BROWSER_EXTENSION_JOURNAL_HANDLES_V1'; Owner='BrowserExtension' },
      @{ Id='browser-extension-input'; Order=29; Command='phase2-browser-input';
         Rule='BROWSER_EXTENSION_INPUT_HUB_WITH_FALLBACK_V1'; Owner='BrowserExtension' },
      @{ Id='evs-onaction-ps'; Order=30; Command='phase2-evs-ps';
         Rule='EVS_ONACTION_PS_HANDLE_REUSE_V1'; Owner='Enhanced Vehicle System' },
      @{ Id='flushing-etiquette-input-hub'; Order=31; Command='phase2-fe-input-hub';
         Rule='FLUSHING_ETIQUETTE_INPUT_HUB_FALLBACK_V1'; Owner='FlushingEtiquette' }
    )
    $historicalReferences = @()
    foreach ($port in $historicalPorts) {
      $portOutput = Join-Path $work ('gco-phase2-reference-'+$port.Order)
      $portLog = Join-Path $work ('gco-phase2-reference-'+$port.Order+'.log')
      & dotnet $dll $port.Command --game $game --base-overlay $callbackFolder --out $portOutput *> $portLog
      if ($LASTEXITCODE -ne 0) {
        throw ('GCO_HISTORICAL_REFERENCE_GENERATOR_FAILED: '+$port.Id)
      }
      $reportPath = Join-Path $portOutput 'phase2-result.json'
      if (!(Test-Path -LiteralPath $reportPath -PathType Leaf)) {
        throw ('GCO_HISTORICAL_REFERENCE_REPORT_MISSING: '+$port.Id)
      }
      $portReport = Get-Content -LiteralPath $reportPath -Raw | ConvertFrom-Json
      if ($portReport.ruleId -ne $port.Rule -or
          $portReport.status -notin @('APPLIED','COMPLETE','NOT_APPLICABLE','REFUSED')) {
        throw ('GCO_HISTORICAL_REFERENCE_STATUS_INVALID: '+$port.Id)
      }
      $portFiles = @()
      $portScripts = Join-Path $portOutput 'r6/scripts'
      if (Test-Path -LiteralPath $portScripts -PathType Container) {
        $portFiles = @(Get-ChildItem -LiteralPath $portScripts -File -Recurse -Filter '*.reds')
      }
      if ($portReport.status -eq 'APPLIED') {
        if ($portFiles.Count -eq 0 -or [int]$portReport.appliedEdits -le 0) {
          throw ('GCO_HISTORICAL_APPLIED_OUTPUT_MISSING: '+$port.Id)
        }
        if ($port.Id -eq 'flushing-etiquette-input-hub') {
          if ($portFiles.Count -ne 1 -or [int]$portReport.appliedEdits -lt 3) {
            throw 'GCO_FE_HUB_REFERENCE_FILE_COUNT_INVALID'
          }
          $file=Get-Content -LiteralPath $portFiles[0].FullName -Raw
          foreach($part in @('private func FETryHubInputRoutes()',
              'private func FECleanupInputRoutes()',
              'if !this.m_feHubInputActive && FEInput.Handle(this, action)',
              'return FEInput.HandleValues(player, name, type);',
              'GCO-GENERIC:CNAME_FIXED_INDEX_V1',
              'return wrappedMethod(action, consumer);')) {
            if (!$file.Contains($part,[StringComparison]::Ordinal)) {
              throw ('GCO_FE_HUB_UNSAFE_INPUT_OUTPUT:'+ $part)
            }
          }
        }
        if ($port.Id -eq 'evs-onaction-ps') {
          if ($portFiles.Count -ne 1 -or [int]$portReport.appliedEdits -lt 2) {
            throw 'GCO_EVS_PS_REFERENCE_SOURCE_NOT_APPLIED'
          }
          $file=Get-Content -LiteralPath $portFiles[0].FullName -Raw
          if (!$file.Contains('gcoOnActionPS: ref<VehicleComponentPS>') -and
              !$file.Contains('let ps: ref<VehicleComponentPS>')) {
            throw 'GCO_EVS_PS_LOCAL_HANDLE_NOT_PRESENT'
          }
          if (!$file.Contains('GetIsDestroyed()')) {
            throw 'GCO_EVS_PS_GUARD_LOST'
          }
        }
        if ($port.Id -eq 'browser-extension-input') {
          if ($portFiles.Count -lt 1 -or $portFiles.Count -gt 2 -or
              [int]$portReport.appliedEdits -ne $portFiles.Count) {
            throw 'GCO_BROWSER_INPUT_REFERENCE_FILE_COUNT_INVALID'
          }
          foreach($file in $portFiles) {
            $ownerText=Get-Content -LiteralPath $file.FullName -Raw
            if ($file.Name -eq 'HomePagePagination.reds' -and
                (!$ownerText.Contains('extends InputListener') -or
                 !$ownerText.Contains('func OnGRedInput(evt: ref<InputEvent>)') -or
                 !$ownerText.Contains('func OnAction(action: ListenerAction'))) {
              throw 'GCO_BROWSER_INPUT_LISTENER_FALLBACK_MISSING'
            }
            if ($file.Name -eq 'browserController.overrides.reds' -and
                (!$ownerText.Contains('m_customBrowserFallbackRegistered = true;') -or
                 !$ownerText.Contains('m_customBrowserFallbackPlayer.UnregisterInputListener(') -or
                 !$ownerText.Contains('m_customBrowserInputHub.Subscribe('))) {
              throw 'GCO_BROWSER_INPUT_UNSUBSCRIBE_OR_FALLBACK_MISSING'
            }
          }
        }
        if ($port.Id -eq 'browser-extension-journal') {
          if ($portFiles.Count -ne 1 -or
              [int]$portReport.appliedEdits -ne 1) {
            throw 'GCO_BROWSER_JOURNAL_REFERENCE_COUNT_INVALID'
          }
          $ownerFile=Join-Path $portScripts 'BrowserExtension/BrowserExtension.System.reds'
          if (!(Test-Path -LiteralPath $ownerFile -PathType Leaf)) {
            throw 'GCO_BROWSER_JOURNAL_OWNER_FILE_MISSING'
          }
          $ownerText=Get-Content -LiteralPath $ownerFile -Raw
          if (!$ownerText.Contains('private func GetCachedJournal()') -or
              !$ownerText.Contains('m_corpoStartQuest: wref<JournalQuest>') -or
              !$ownerText.Contains('m_ep1EpilogueQuest: wref<JournalQuest>') -or
              !$ownerText.Contains('journal.GetEntryState(this.m_corpoStartQuest)') -or
              !$ownerText.Contains('journal.GetEntryState(this.m_ep1EpilogueQuest)')) {
            throw 'GCO_BROWSER_JOURNAL_STATE_FRESHNESS_CONTRACT'
          }
        }
        if ($port.Id -eq 'digitalvixen-ui-dormancy') {
          if ($portFiles.Count -ne 3 -or
              [int]$portReport.appliedEdits -ne 7) {
            throw 'GCO_DVC_UI_REFERENCE_SEVEN_EDITS_THREE_FILES_REQUIRED'
          }
          $body=Join-Path $portScripts 'DigitalVixenCore/DVCore_BodyMapWindow.reds'
          $item=Join-Path $portScripts 'DigitalVixenCore/DVCore_ItemEditor.reds'
          $status=Join-Path $portScripts 'DigitalVixenCore/DVCore_StatusWindow.reds'
          foreach($required in @($body,$item,$status)) {
            if (!(Test-Path -LiteralPath $required -PathType Leaf)) {
              throw 'GCO_DVC_UI_REFERENCE_OWNER_MISSING'
            }
          }
          $bodyText=Get-Content -LiteralPath $body -Raw
          $itemText=Get-Content -LiteralPath $item -Raw
          $statusText=Get-Content -LiteralPath $status -Raw
          if (!$bodyText.Contains('if !overlay && !cfgPinned') -or
              !$bodyText.Contains('win.SetOverlayOnly(true);') -or
              !$bodyText.Contains('win.Checkbox(n"hiPin"') -or
              !$bodyText.Contains('DVBodyMapWindow.HideRuleActive(')) {
            throw 'GCO_DVC_UI_BODYMAP_PINNED_BEHAVIOR_LOST'
          }
          foreach($t in @($itemText,$statusText)) {
            if (!$t.Contains('let delay: Float = 2.0;') -or
                !$t.Contains('delay = 0.5;') -or
                !$t.Contains('delay = 0.25;') -or
                !$t.Contains('cb.initialized = true;') -or
                !$t.Contains('DelayCallback(cb, delay, false)') -or
                !$t.Contains('if !win.IsRendered() { return; }')) {
              throw 'GCO_DVC_UI_ADAPTIVE_LOOP_OR_RENDER_GUARD_MISSING'
            }
          }
          if (!$statusText.Contains('if this.initialized && IsDefined(mgr) {') -or
              !$statusText.Contains('win.SetOverlayOnly(!pinned)')) {
            throw 'GCO_DVC_UI_PINNED_STATUS_REFRESH_REGRESSION'
          }
        }
        if ($port.Id -eq 'redwindows-render-dormancy') {
          # This full-source integration check must prove that all three
          # dependent outputs were composed over #19 and current generic
          # source, not merely that the stage reported a success count.
          if ($portFiles.Count -ne 3) {
            throw 'GCO_RW_RENDER_REFERENCE_THREE_FILES_REQUIRED'
          }
          $w=Join-Path $portScripts 'RedWindows/RW_Window.reds'
          $b=Join-Path $portScripts 'RedWindows/RW_Builder.reds'
          $l=Join-Path $portScripts 'RedWindows/RW_Logs.reds'
          if (!(Test-Path -LiteralPath $w -PathType Leaf) -or
              !(Test-Path -LiteralPath $b -PathType Leaf) -or
              !(Test-Path -LiteralPath $l -PathType Leaf)) {
            throw 'GCO_RW_RENDER_REFERENCE_OWNER_MISSING'
          }
          $wt=Get-Content -LiteralPath $w -Raw
          $bt=Get-Content -LiteralPath $b -Raw
          $lt=Get-Content -LiteralPath $l -Raw
          if (!$wt.Contains('return !this.overlayOnly;') -or
              !$wt.Contains('this.m_findCursor = idx + 1;') -or
              !$bt.Contains('builder.IsRendered()') -or
              !$bt.Contains('gallery.IsRendered()') -or
              !$bt.Contains('GCO-GENERIC:STABLE_ARRAY_BOUND_V1') -or
              !$lt.Contains('win.IsRendered() && RedLogger.Available()')) {
            throw 'GCO_RW_RENDER_REFERENCE_LOST_LOOKUP_GENERIC_OR_LIFECYCLE'
          }
        }
        foreach ($file in $portFiles) {
          $rel = [IO.Path]::GetRelativePath($portScripts, $file.FullName)
          $relUnix = $rel.Replace([IO.Path]::DirectorySeparatorChar, '/')
          if ([IO.Path]::IsPathRooted($rel) -or $relUnix.StartsWith('..') -or
              !$relUnix.StartsWith(($port.Owner+'/'),[StringComparison]::Ordinal) -or
              !$relUnix.EndsWith('.reds',[StringComparison]::OrdinalIgnoreCase)) {
            throw ('GCO_HISTORICAL_OWNER_PATH_UNSAFE: '+$port.Id)
          }
          $dest = Join-Path (Join-Path $callbackFolder 'r6/scripts') $rel
          $new = Get-Content -LiteralPath $file.FullName -Raw
          if (Test-Path -LiteralPath $dest -PathType Leaf) {
            $old = Get-Content -LiteralPath $dest -Raw
            foreach ($marker in @([regex]::Matches($old,
                  '(?:GCO|GRSP)-GENERIC:[A-Z0-9_]+') |
                  ForEach-Object Value | Sort-Object -Unique)) {
              if (!$new.Contains($marker,[StringComparison]::Ordinal)) {
                throw ('GCO_HISTORICAL_REFERENCE_GENERIC_LOST: '+$port.Id)
              }
            }
          } else {
            New-Item -ItemType Directory -Force -Path (Split-Path $dest -Parent) | Out-Null
          }
          Copy-Item -LiteralPath $file.FullName -Destination $dest -Force
        }
      }
      elseif ($portFiles.Count -gt 0 -or [int]$portReport.appliedEdits -ne 0) {
        throw ('GCO_HISTORICAL_UNEXPECTED_OUTPUT_FOR_SKIP: '+$port.Id)
      }
      $historicalReferences += @{
        Id=$port.Id; Order=$port.Order;
        Status=$portReport.status; Edits=[int]$portReport.appliedEdits;
        Files=$portFiles.Count
      }
      Write-Host ('PASS Phase 2 '+$port.Id+' independent reference: status='+
          $portReport.status+' edits='+[int]$portReport.appliedEdits+
          ' files='+$portFiles.Count)
    }

    $orderedStage = Join-Path $work 'gco-ordered-redscript-preview'
    $orderedStageLog = Join-Path $work 'gco-ordered-redscript-preview.log'
    & dotnet $dll stage-redscript-plan --game $game --plan $orderedPlanFile --analysis $result --out $orderedStage *> $orderedStageLog
    if ($LASTEXITCODE -ne 0) {
        throw 'GCO_REDSCRIPT_ORDERED_STAGE_FAILED'
    }
    $stageReportPath = Join-Path $orderedStage 'stage-result.json'
    if (!(Test-Path -LiteralPath $stageReportPath -PathType Leaf)) {
        throw 'GCO_REDSCRIPT_ORDERED_STAGE_NO_RESULT'
    }
    $stageReport = Get-Content -LiteralPath $stageReportPath -Raw | ConvertFrom-Json
    $stageScripts = Join-Path $orderedStage 'effective-preview/r6/scripts'
    $manualScripts = Join-Path $callbackFolder 'r6/scripts'
    if ($stageReport.schemaVersion -ne 'gco.redscript.staged-plan.v1' -or
        $stageReport.sourceStackSha256 -ne $expectedSourceHash -or
        $stageReport.productionZipAuthorized -ne $false -or
        [int]$stageReport.deferredCollisions -ne 0 -or
        !(Test-Path -LiteralPath $stageScripts -PathType Container)) {
        throw 'GCO_REDSCRIPT_ORDERED_STAGE_CONTRACT_INVALID'
    }
    $stageFiles = @(Get-ChildItem -LiteralPath $stageScripts -Recurse -Filter *.reds -File)
    $manualFiles = @(Get-ChildItem -LiteralPath $manualScripts -Recurse -Filter *.reds -File)
    if ($stageFiles.Count -ne [int]$stageReport.generatedFiles -or
        $stageFiles.Count -ne $manualFiles.Count) {
        throw 'GCO_REDSCRIPT_ORDERED_GENERATOR_FILE_COUNT_DIFF'
    }
    if ($dfReport.status -eq 'APPLIED') {
      $darkFamily=@($stageReport.completed | Where-Object { $_.id -eq 'dark-future-infinite-tag' })
      if ($darkFamily.Count -ne 1 -or
          $darkFamily[0].order -ne 12 -or
          [int]$darkFamily[0].rewrittenExpressions -ne 1) {
        throw 'GCO_DARK_FUTURE_CANONICAL_GENERATION_NOT_CONNECTED'
      }
    }
    if ($dfIconReport.status -eq 'APPLIED') {
      $iconFamily=@($stageReport.completed |
        Where-Object { $_.id -eq 'dark-future-status-icons' })
      if ($iconFamily.Count -ne 1 -or
          $iconFamily[0].order -ne 13 -or
          [int]$iconFamily[0].rewrittenExpressions -ne 1 -or
          [int]$iconFamily[0].composedFiles -ne 1) {
        throw 'GCO_DF_ICON_CANONICAL_STAGING_NOT_CONNECTED'
      }
    }
    if ($feReport.status -eq 'APPLIED') {
      $feFamily=@($stageReport.completed |
        Where-Object { $_.id -eq 'flushing-etiquette-input' })
      if ($feFamily.Count -ne 1 -or
          [int]$feFamily[0].order -ne 14 -or
          [int]$feFamily[0].rewrittenExpressions -ne 1) {
        throw 'GCO_FE_INPUT_CANONICAL_STAGING_NOT_CONNECTED'
      }
    }
    if ($pinReport.status -eq 'APPLIED') {
      $pinFamily = @($stageReport.completed |
        Where-Object { $_.id -eq 'flushing-etiquette-session-pins' })
      if ($pinFamily.Count -ne 1 -or
          [int]$pinFamily[0].order -ne 15 -or
          [int]$pinFamily[0].rewrittenExpressions -ne 1) {
        throw 'GCO_FE_SESSION_PIN_CANONICAL_STAGING_NOT_CONNECTED'
      }
    }
    if ($cqReport.status -eq 'APPLIED') {
      $cqFamily=@($stageReport.completed |
        Where-Object { $_.id -eq 'custom-quickslots-input' })
      if($cqFamily.Count -ne 1 -or
          [int]$cqFamily[0].order -ne 16 -or
          [int]$cqFamily[0].rewrittenExpressions -ne 1) {
        throw 'GCO_CQ_INPUT_CANONICAL_GENERATION_NOT_CONNECTED'
      }
    }
    foreach ($reference in $historicalReferences) {
      $actual = @($stageReport.completed | Where-Object { $_.id -eq $reference.Id })
      if ($reference.Status -eq 'APPLIED') {
        if ($actual.Count -ne $reference.Files -or
            @($actual | Where-Object { $_.order -ne $reference.Order }).Count -gt 0 -or
            [int](($actual | Measure-Object -Property rewrittenExpressions -Sum).Sum) -ne
              $reference.Edits) {
          throw ('GCO_HISTORICAL_REFERENCE_CANONICAL_COUNT_DIFF: '+$reference.Id)
        }
      }
      elseif ($actual.Count -ne 0) {
        throw ('GCO_HISTORICAL_REFERENCE_UNEXPECTED_CANONICAL: '+$reference.Id)
      }
      if ($reference.Status -eq 'REFUSED' -and
          @($stageReport.skipped | Where-Object { $_.id -eq $reference.Id }).Count -eq 0) {
        throw ('GCO_HISTORICAL_REFERENCE_MISSING_REFUSAL: '+$reference.Id)
      }
    }
    # Every ordered generated REDscript source must match the independent
    # composition. Retired native FAR/subscriber source is never emitted.
    foreach ($f in $stageFiles) {
        $rel = [IO.Path]::GetRelativePath($stageScripts, $f.FullName)
        $manual = Join-Path $manualScripts $rel
        $text = Get-Content -LiteralPath $f.FullName -Raw
        if ($f.Name -match '^GCO_NativeLocation_[0-9]{9,10}\.reds$' -or
            $text.Contains('GCO-GENERIC:NATIVE_PLAYER_LOCATION_PREDICATE_V1',[StringComparison]::Ordinal) -or
            $text.Contains('GCO-GENERIC:NATIVE_PLAYER_LOCATION_SUBSCRIBER_V1',[StringComparison]::Ordinal) -or
            $text.Contains('GCO-GENERIC:LOCATION_DEPENDENCY_FAR_V1:',[StringComparison]::Ordinal)) {
            throw 'GCO_RETIRED_LOCATION_RULE_EMITTED_SOURCE'
        }
        if (!(Test-Path -LiteralPath $manual -PathType Leaf) -or
            (Get-FileHash -LiteralPath $manual -Algorithm SHA256).Hash -ne
            (Get-FileHash -LiteralPath $f.FullName -Algorithm SHA256).Hash) {
            throw 'GCO_REDSCRIPT_ORDERED_GENERATOR_SOURCE_DIFF'
        }
    }
    Write-Host ('PASS GCO REDscript Resolver-to-generator order: rules=' +
        $scheduled.Count + ' ready=' + $orderedPlan.readyRuleCount +
        ' eligible=' + $planEligible + ' generatedFiles=' +
        $stageFiles.Count + ' deferredExperimental=' + $deferred.Count +
        ' realSourceOutputMatchesExistingCI=true productionZip=false')

    # Publish the ACTUAL independently source-verified ordered REDscript
    # output to a temporary game-relative archive. Validate with the same
    # GCO shipping inspector used by the combined CET+REDscript product.
    # Never include stage-result.json, profiler binaries or test evidence.
    $gameOnlyStage = Join-Path $orderedStage 'effective-preview'
    $realGameZip = Join-Path $work 'gco-ordered-real-source-game-pass.zip'
    [IO.Compression.ZipFile]::CreateFromDirectory($gameOnlyStage, $realGameZip)
    & dotnet run --project tests/GTotalOptimizer.Core.Tests/GTotalOptimizer.Core.Tests.csproj -c Release -- --verify-combined-archive $realGameZip $stageFiles.Count
    if ($LASTEXITCODE -ne 0) {
        throw 'GCO_REDSCRIPT_REAL_SOURCE_GAME_ZIP_INVALID'
    }

    # Retired REDscript native FAR/subscriber experiments must not be present
    # in either generated overlay or the independent composed source. CET's
    # native player/location contract is tested only by its source owner.
    if ($stageFiles.Count -ne $manualFiles.Count) {
        throw 'GCO_RETIRED_NATIVE_LOCATION_EXTRA_STAGED_FILE'
    }
    Write-Host 'PASS default REDscript ZIP excludes native FAR groups, adapters and optional RTTI bridge'

    # GRSP owns the actual isolated SCC 0.5.31 dual-compile contract.
    # The GCO harness must not duplicate its compiler logic or publish output.
    $ownerScript = (Resolve-Path -LiteralPath $OwnerCompilerScript -ErrorAction Stop).Path
    $extra = @{}
    if (![string]::IsNullOrWhiteSpace($FrameworkRoot)) {
        $extra.FrameworkRoot = (Resolve-Path -LiteralPath $FrameworkRoot -ErrorAction Stop).Path
    }
    if ($callbackCount -gt 0 -or $actionCount -gt 0 -or $angleCount -gt 0 -or $lateCount -gt 0 -or $bbStageCount -gt 0 -or $bbComposedCount -gt 0 -or $pureStaged -gt 0 -or $pureComposed -gt 0 -or $snapshotMerged -gt 0 -or $sharedPlayerMerged -gt 0) {
        $extra.CallbackPreviewRoot = $legacyCallbackFolder
        $extra.FrameworkPreviewRoot = $callbackFolder
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
