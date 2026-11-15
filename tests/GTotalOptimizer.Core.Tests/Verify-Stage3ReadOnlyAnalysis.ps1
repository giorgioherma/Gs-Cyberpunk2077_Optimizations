param(
    [Parameter(Mandatory=$true)][string]$StagedSealedSession,
    [Parameter(Mandatory=$true)][string]$PrivateSourceRoot,
    [Parameter(Mandatory=$true)][string]$PackageRoot,
    [Parameter(Mandatory=$true)][string]$WorkRoot
)
$ErrorActionPreference='Stop'
Set-StrictMode -Version Latest
$PSNativeCommandUseErrorActionPreference=$false

$StagedSealedSession=(Resolve-Path -LiteralPath $StagedSealedSession).Path
$PrivateSourceRoot=(Resolve-Path -LiteralPath $PrivateSourceRoot).Path
$PackageRoot=(Resolve-Path -LiteralPath $PackageRoot).Path
$WorkRoot=(Resolve-Path -LiteralPath $WorkRoot).Path
$originalMods=Join-Path $PrivateSourceRoot 'bin/x64/plugins/cyber_engine_tweaks/mods'
$targetGame=Join-Path $WorkRoot 'Stage3-Test-Installed-Game'
$targetMods=Join-Path $targetGame 'bin/x64/plugins/cyber_engine_tweaks/mods'
$targetSession=Join-Path (Join-Path $PackageRoot 'RESULTS') (Split-Path $StagedSealedSession -Leaf)
$targetIndex=Join-Path $targetSession 'collection.json'
$testProject='tests/GTotalOptimizer.Core.Tests/GTotalOptimizer.Core.Tests.csproj'
$passZip=$null # Test-only private-source output must NEVER enter the public artifact.
$originalCaptureIndexHash=(Get-FileHash -LiteralPath (Join-Path $StagedSealedSession 'collection.json') -Algorithm SHA256).Hash
if (Test-Path -LiteralPath $targetSession) {
    throw 'CI session must never overwrite an existing player RESULTS session.'
}
try {
    # Copy the REAL private source snapshot to a disposable fake game.
    # Never add an EXE or change files in original_cet_source.
    New-Item -ItemType Directory -Force -Path (Split-Path $targetMods -Parent) | Out-Null
    & robocopy $originalMods $targetMods /E /R:1 /W:1 /NFL /NDL /NJH /NJS /NP | Out-Null
    if ($LASTEXITCODE -gt 7) {
        throw 'Could not build physical CET source fixture.'
    }
    $copied=@(Get-ChildItem -LiteralPath $targetMods -Recurse -File -ErrorAction Stop)
    if ($copied.Count -lt 2000 -or
        !(Test-Path -LiteralPath (Join-Path $targetMods '0-Engine/init.lua'))) {
        throw 'Full original CET source fixture was not copied.'
    }
    $bin=Join-Path $targetGame 'bin/x64'
    [IO.File]::WriteAllText((Join-Path $bin 'Cyberpunk2077.exe'),
        'CI-only game sentinel; not executable and never launched')

    # Simulate COLLECT's completed capture index in this private test copy.
    # Do not use a restored flag to grant or deny Resolver analysis.
    Copy-Item -LiteralPath $StagedSealedSession -Destination $targetSession -Recurse
    $index=Get-Content -LiteralPath $targetIndex -Raw | ConvertFrom-Json -Depth 70
    $id=[string]$index.SessionId
    if ($id -notmatch '^[0-9a-fA-F]{32}$' -or
        $index.CollectionAttempted -ne $true -or
        $index.PresentMonStopVerified -ne $true -or
        $null -eq $index.Cet -or $null -eq $index.Redscript) {
        throw 'Actual fixture cannot model a verified restored collection.'
    }
    $index.GameRoot=$targetGame
    $index.Restored=$false
    if ($null -ne $index.FrameTimeEvidence) {
        $relative=([string]$index.FrameTimeEvidence.Path).Replace('/','\')
        $index.FrameTimeCsv=Join-Path $targetSession $relative
    }
    $index | ConvertTo-Json -Depth 70 | Set-Content -LiteralPath $targetIndex -Encoding utf8

    function InvokeAnalysis([bool]$shouldPass,[string]$scenario) {
        $messages=@(& dotnet run --configuration Release --project $testProject -- --stage3-analyze $PackageRoot $targetGame $id 2>&1)
        $code=$LASTEXITCODE
        if ($shouldPass -and $code -ne 0) {
            # CI diagnostics must be actionable, but never expose paths from
            # the private original CET source repository in public Actions.
            $reasons=@($messages | ForEach-Object { [string]$_ } |
                Where-Object { $_ -match 'Exception:|Unhandled exception|^FAIL |\berror CS\d+\b' } |
                Select-Object -First 2)
            $diagnostic=if ($reasons.Count -gt 0) {
                ($reasons -join ' | ')
            } else { 'NO_EXCEPTION_LINE; EXIT_'+$code }
            foreach ($secretPath in @($PrivateSourceRoot,$originalMods,$targetGame,
                $targetMods,$StagedSealedSession,$targetSession,$PackageRoot,$WorkRoot)) {
                $diagnostic=$diagnostic.Replace($secretPath,'[PATH]')
            }
            $diagnostic=[regex]::Replace($diagnostic,'[A-Za-z]:\\[^:\r\n]*','[PATH]')
            if ($diagnostic.Length -gt 600) { $diagnostic=$diagnostic.Substring(0,600) }
            throw ('Stage 3 analysis '+$scenario+': '+$diagnostic)
        }
        if (!$shouldPass -and $code -eq 0) {
            throw 'Unsafe Stage 3 analysis falsely passed: '+$scenario
        }
        if ($shouldPass) {
            $success=@($messages | Where-Object {
                [string]$_ -like 'PASS Stage 3 read-only collected-session CET analysis:*'
            })
            if ($success.Count -ne 1) {
                throw 'Stage 3 CLI gave no successful production source report.'
            }
            return [string]$success[0]
        }
        return ''
    }

    $first=InvokeAnalysis $true 'initial genuine CET source/capture report'
    $again=InvokeAnalysis $true 'repeat report from unchanged live source'
    if ($first -ne $again) { throw 'Repeated Stage 3 CET analysis has unstable output.' }
    Write-Host 'PASS Stage 3 real current-source CET read-only report replay is stable across repeat runs'

    # Full profiler reporting is a COLLECT-derived product, not ANALYZE,
    # Restore or an alternate Resolver. Run original owner report engines
    # with genuine sealed PresentMon, and inspect the resulting combined graph.
    $reportRun=@(& dotnet run --configuration Release --project $testProject -- --stage5-profiler-report $PackageRoot $targetGame $id 2>&1)
    if ($LASTEXITCODE -ne 0) {
        $failures=@($reportRun | ForEach-Object { [string]$_ } |
            Where-Object { $_ -match 'Exception:|Unhandled exception|^FAIL ' } |
            Select-Object -First 2)
        $diagnostic=($failures -join ' | ')
        foreach ($secret in @($originalMods,$PrivateSourceRoot,$targetMods,$targetGame,$PackageRoot)) {
            $diagnostic=$diagnostic.Replace($secret,'[PATH]')
        }
        $diagnostic=[regex]::Replace($diagnostic,'[A-Za-z]:\\[^:\r\n]*','[PATH]')
        if ($diagnostic.Length -gt 450) { $diagnostic=$diagnostic.Substring(0,450) }
        throw ('Real GCO combined profiler report failed: '+$diagnostic)
    }
    if (@($reportRun | Where-Object {
        [string]$_ -like 'PASS GCO real CET+GRSP+PresentMon profiler report:*'
    }).Count -ne 1) {
        throw 'GCO report test did not prove all three real profiler series.'
    }
    Write-Host ($reportRun | Where-Object {
        [string]$_ -like 'PASS GCO real CET+GRSP+PresentMon profiler report:*'
    } | Select-Object -First 1)


    # ANALYZE and GENERATE are not profiler lifecycle/RESTORE managers. A
    # captured index does not need Restored=true, and a recovery marker does
    # not change the actual source-owned Resolver's inputs.
    $marker=Join-Path $targetGame 'bin/x64/plugins/.cet_runtime_profiler'
    New-Item -ItemType Directory -Force -Path $marker | Out-Null
    $null=InvokeAnalysis $true 'ANALYZE independent of profiler owner marker'
    Write-Host 'PASS Stage 3 collected data analysis independent of profiler restore flag and marker'

    # The production Page 2 operation must call the ALREADY DEVELOPED
    # source-owned CET --generate-pass. No synthetic ZIP or rewritten Lua.
    $passZip=Join-Path (Join-Path $PackageRoot 'RESULTS') ('G-CET_PASS_'+$id+'.zip')
    if (Test-Path -LiteralPath $passZip) { throw 'Unexpected pre-existing CET pass in CI workspace.' }
    $generated=@(& dotnet run --configuration Release --project $testProject -- --stage4-generate $PackageRoot $targetGame $id 2>&1)
    if ($LASTEXITCODE -ne 0) {
        $errorLines=@($generated | ForEach-Object { [string]$_ } |
            Where-Object { $_ -match 'Exception:|Unhandled exception|^FAIL ' } |
            Select-Object -First 2)
        # Never include private original source absolute paths in the log.
        $errorText=($errorLines -join ' | ')
        foreach ($secret in @($originalMods,$PrivateSourceRoot,$targetMods,$targetGame,$PackageRoot)) {
            $errorText=$errorText.Replace($secret,'[PATH]')
        }
        $errorText=[regex]::Replace($errorText,'[A-Za-z]:\\[^:\r\n]*','[PATH]')
        if ($errorText.Length -gt 400) { $errorText=$errorText.Substring(0,400) }
        throw ('Actual source-owned CET pass generation failed: '+$errorText)
    }
    if (!(Test-Path -LiteralPath $passZip -PathType Leaf)) {
        throw 'Owner CET generation did not publish a ZIP into GCO RESULTS.'
    }
    $generatedProof=@($generated | Where-Object {
        [string]$_ -like 'PASS Stage 4 CET source-owned generated game-relative ZIP:*'
    })
    if ($generatedProof.Count -ne 1) {
        throw 'CET generator did not report applied transforms and file count.'
    }
    Add-Type -AssemblyName System.IO.Compression
    $zip=[System.IO.Compression.ZipFile]::OpenRead($passZip)
    try {
        $entries=@($zip.Entries)
        $unique=@{}
        $lua=0
        foreach($entry in $entries) {
            $n=$entry.FullName
            $modFile=$n.StartsWith('bin/x64/plugins/cyber_engine_tweaks/mods/',[StringComparison]::OrdinalIgnoreCase)
            $nativeFile=$n.StartsWith('red4ext/plugins/G-CET-StaticLocations/',[StringComparison]::OrdinalIgnoreCase)
            if (!$modFile -and !$nativeFile -or $n -match '\\.\\.|\\' -or
                $entry.Length -le 0 -or $unique.ContainsKey($n.ToLowerInvariant())) {
                throw 'Owner-generated CET pass contains invalid, duplicate or non-game-relative entry.'
            }
            $unique[$n.ToLowerInvariant()]=$true
            if ($n.EndsWith('.lua',[StringComparison]::OrdinalIgnoreCase)) { $lua++ }
        }
        if ($entries.Count -lt 2 -or $lua -lt 1) {
            throw 'Actual CET owner generation emitted no usable Lua source in ZIP.'
        }
        Write-Host ('PASS Stage 4 actual owner CET pass ZIP entries='+$entries.Count+', Lua files='+$lua+', game-relative only; no profiler/report/RED assets')
    }
    finally { $zip.Dispose() }
    Remove-Item -LiteralPath $marker -Recurse -Force

    # Actual owner source-state regression. A *single* already optimized
    # mod file from the first ZIP is a real partial installation; the owner
    # must still produce the remaining CET pass from the CURRENT source.
    $firstPass=Join-Path $WorkRoot 'GCO-Stage4-First-CET-Pass.zip'
    Copy-Item -LiteralPath $passZip -Destination $firstPass -ErrorAction Stop
    $archive=[IO.Compression.ZipFile]::OpenRead($firstPass)
    try {
        $prefix='bin/x64/plugins/cyber_engine_tweaks/mods/'
        $sourceFile=@($archive.Entries | Where-Object {
            $_.FullName.StartsWith($prefix,[StringComparison]::OrdinalIgnoreCase) -and
            $_.FullName.EndsWith('.lua',[StringComparison]::OrdinalIgnoreCase) -and
            !$_.FullName.StartsWith(($prefix+'0-Engine/'),[StringComparison]::OrdinalIgnoreCase) -and
            (Test-Path -LiteralPath (Join-Path $targetGame ($_.FullName.Replace('/','\'))) -PathType Leaf)
        } | Select-Object -First 1)
        if ($sourceFile.Count -ne 1) {
            throw 'First CET pass has no existing mod Lua to exercise partially optimized source.'
        }
        $partialTarget=Join-Path $targetGame ($sourceFile[0].FullName.Replace('/','\'))
        $read=$sourceFile[0].Open()
        try {
            $write=[IO.File]::Open($partialTarget,[IO.FileMode]::Create,
                [IO.FileAccess]::Write,[IO.FileShare]::None)
            try { $read.CopyTo($write) } finally { $write.Dispose() }
        }
        finally { $read.Dispose() }
    } finally { $archive.Dispose() }

    # This ZIP is CI-only; never overwrite a user's output. Repeated GCO
    # generation on a partially optimized stack must still use the exact
    # original owner CLI and return genuine, remaining transforms.
    Remove-Item -LiteralPath $passZip -Force -ErrorAction Stop
    $partial=@(& dotnet run --configuration Release --project $testProject -- --stage4-generate $PackageRoot $targetGame $id 2>&1)
    if ($LASTEXITCODE -ne 0 -or
        @($partial | Where-Object { [string]$_ -like 'PASS Stage 4 CET source-owned generated game-relative ZIP:*' }).Count -ne 1) {
        throw 'PARTIAL_SOURCE_PASS_FAILED: existing CET generator could not complete partial original sources.'
    }
    Write-Host 'PASS Stage 4 real partial CET installation: original owner produced remaining source-proven transforms'

    # Fully apply the first complete ZIP to the DISPOSABLE game fixture only.
    # This is equivalent to a Vortex overlay, never a player installation.
    # The source-owned SECOND pass must contain ZERO new transformations.
    [IO.Compression.ZipFile]::ExtractToDirectory($firstPass,$targetGame,$true)
    Remove-Item -LiteralPath $passZip -Force -ErrorAction Stop
    $second=@(& dotnet run --configuration Release --project $testProject -- --stage4-idempotent $PackageRoot $targetGame $id 2>&1)
    if ($LASTEXITCODE -ne 0 -or
        @($second | Where-Object { [string]$_ -like 'PASS Stage 4 completed-source CET second pass:*' }).Count -ne 1) {
        throw 'SECOND_PASS_NOT_IDEMPOTENT: existing CET owner regenerated transforms or refused completed sources.'
    }
    Write-Host 'PASS Stage 4 fully optimized CET state: second source-owned pass produces ZERO new transforms'

    # Generate must not edit live mods or sealed capture. Keep COLLECT's
    # established corruption contract; no new ANALYZE restoration gate.
    $sourceEvidence=Join-Path $targetSession 'G-CET/CET_Summary.json'
    [IO.File]::AppendAllText($sourceEvidence,'tampered')
    $null=InvokeAnalysis $false 'indexed capture corrupted after Collect'
    if ((Get-FileHash -LiteralPath (Join-Path $StagedSealedSession 'collection.json') -Algorithm SHA256).Hash -ne $originalCaptureIndexHash) {
        throw 'Real original capture index changed during generation regression.'
    }
    Write-Host 'PASS collected capture originals unchanged; tampered copy refused'
} finally {
    if (![string]::IsNullOrWhiteSpace($passZip) -and
        (Test-Path -LiteralPath $passZip -PathType Leaf)) {
        # Only the disposable CI-generated private-source overlay is removed.
        # The real output feature remains intact for end users.
        Remove-Item -LiteralPath $passZip -Force -ErrorAction Stop
    }
    if (Test-Path -LiteralPath $targetSession) {
        Remove-Item -LiteralPath $targetSession -Recurse -Force -ErrorAction Stop
    }
    if (Test-Path -LiteralPath $targetGame) {
        Remove-Item -LiteralPath $targetGame -Recurse -Force -ErrorAction Stop
    }
}

# Expected negative native exits were asserted above; they must not poison
# the outer PowerShell workflow guard after a fully successful test.
$global:LASTEXITCODE=0
