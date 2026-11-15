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

    # Simulate a GCO VERIFIED RESTORED index only in this copied fixture.
    # The real sealed collection index and all file hashes remain original.
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
    $index.Restored=$true
    if ($null -ne $index.FrameTimeEvidence) {
        $relative=([string]$index.FrameTimeEvidence.Path).Replace('/','\')
        $index.FrameTimeCsv=Join-Path $targetSession $relative
    }
    $index | ConvertTo-Json -Depth 70 | Set-Content -LiteralPath $targetIndex -Encoding utf8

    function InvokeAnalysis([bool]$shouldPass,[string]$scenario) {
        $messages=@(& dotnet run --configuration Release --project $testProject -- --stage3-analyze $PackageRoot $targetGame $id 2>&1)
        $code=$LASTEXITCODE
        if ($shouldPass -and $code -ne 0) {
            throw ('Stage 3 production analysis failed: '+$scenario+'; see private logs.')
        }
        if (!$shouldPass -and $code -eq 0) {
            throw 'Unsafe Stage 3 analysis falsely passed: '+$scenario
        }
        if ($shouldPass) {
            $success=@($messages | Where-Object {
                [string]$_ -like 'PASS Stage 3 read-only restored-session CET analysis:*'
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

    $index.Restored=$false
    $index | ConvertTo-Json -Depth 70 | Set-Content -LiteralPath $targetIndex -Encoding utf8
    $null=InvokeAnalysis $false 'un-restored session refusal'
    $index.Restored=$true
    $index | ConvertTo-Json -Depth 70 | Set-Content -LiteralPath $targetIndex -Encoding utf8

    $marker=Join-Path $targetGame 'bin/x64/plugins/.cet_runtime_profiler'
    New-Item -ItemType Directory -Force -Path $marker | Out-Null
    $null=InvokeAnalysis $false 'managed original restoration marker refusal'
    Remove-Item -LiteralPath $marker -Recurse -Force

    $sourceEvidence=Join-Path $targetSession 'G-CET/CET_Summary.json'
    [IO.File]::AppendAllText($sourceEvidence,'tampered')
    $null=InvokeAnalysis $false 'indexed CET capture SHA256 drift refusal'
    if ((Get-FileHash -LiteralPath (Join-Path $StagedSealedSession 'collection.json') -Algorithm SHA256).Hash -ne $originalCaptureIndexHash) {
        throw 'Real original capture index changed during Stage 3 regression.'
    }
    Write-Host 'PASS Stage 3 refusal of incomplete restore, active managed source and corrupted capture hashes'
} finally {
    if (Test-Path -LiteralPath $targetSession) {
        Remove-Item -LiteralPath $targetSession -Recurse -Force -ErrorAction Stop
    }
    if (Test-Path -LiteralPath $targetGame) {
        Remove-Item -LiteralPath $targetGame -Recurse -Force -ErrorAction Stop
    }
}
