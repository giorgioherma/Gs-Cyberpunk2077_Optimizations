param(
    [Parameter(Mandatory=$true)][string]$FixtureZip,
    [Parameter(Mandatory=$true)][string]$PrivateSourceRoot,
    [Parameter(Mandatory=$true)][string]$PackageRoot
)
$ErrorActionPreference='Stop'
Set-StrictMode -Version Latest

# Stage 1 only: exercise the real GCO-owned .NET10 CET Resolver with one
# collected capture and the user's read-only, PRIVATE live-source snapshot.
# The capture ZIP is sealed. The source tree is never copied to a package.
$FixtureZip=(Resolve-Path -LiteralPath $FixtureZip -ErrorAction Stop).Path
$PrivateSourceRoot=(Resolve-Path -LiteralPath $PrivateSourceRoot -ErrorAction Stop).Path
$PackageRoot=(Resolve-Path -LiteralPath $PackageRoot -ErrorAction Stop).Path
$mods=Join-Path $PrivateSourceRoot 'bin/x64/plugins/cyber_engine_tweaks/mods'
$zeroEngine=Join-Path $mods '0-Engine/init.lua'
$owner=Join-Path $PackageRoot 'components/cet/G-CET-Runtime-Profiler.exe'
$resolver=Join-Path $PackageRoot 'components/cet-resolver/app/G-CET-Resolver.exe'
foreach ($file in @($FixtureZip,$zeroEngine,$owner,$resolver)) {
    if (!(Test-Path -LiteralPath $file -PathType Leaf)) {
        throw 'Real CET replay input missing: expected collected ZIP, live 0-Engine, profiler and packaged Resolver.'
    }
}
$env:DOTNET_ROOT_X64=Join-Path $PackageRoot 'runtime'
$env:DOTNET_ROOT=$env:DOTNET_ROOT_X64
$env:DOTNET_MULTILEVEL_LOOKUP='0'

# Full byte-level read-only assertion. Never print private filenames or source.
$sourceFiles=@(Get-ChildItem -LiteralPath $mods -Recurse -File -ErrorAction Stop |
    Sort-Object FullName)
if ($sourceFiles.Count -lt 1) { throw 'Private CET source snapshot has no files.' }
$hashes=@{}
foreach ($file in $sourceFiles) {
    $relative=[IO.Path]::GetRelativePath($mods,$file.FullName)
    $hashes[$relative]=(Get-FileHash -LiteralPath $file.FullName -Algorithm SHA256).Hash
}
$zipHash=(Get-FileHash -LiteralPath $FixtureZip -Algorithm SHA256).Hash
$stage=Join-Path $env:RUNNER_TEMP ('GCO-RealCET-Replay-' + [guid]::NewGuid().ToString('N'))
function InvokeOwnerCli([string]$Program,[string[]]$Arguments) {
    $info=[Diagnostics.ProcessStartInfo]::new()
    $info.FileName=$Program
    $info.WorkingDirectory=Split-Path $Program -Parent
    $info.UseShellExecute=$false
    $info.CreateNoWindow=$true
    $info.RedirectStandardOutput=$true
    $info.RedirectStandardError=$true
    foreach($arg in $Arguments) { [void]$info.ArgumentList.Add($arg) }
    $process=[Diagnostics.Process]::new()
    $process.StartInfo=$info
    try {
        if (!$process.Start()) { throw 'Owner command failed to start.' }
        $stdoutTask=$process.StandardOutput.ReadToEndAsync()
        $stderrTask=$process.StandardError.ReadToEndAsync()
        if (!$process.WaitForExit(240000)) {
            $process.Kill()
            throw 'Owner command exceeded 240-second CI replay limit.'
        }
        $stdout=$stdoutTask.GetAwaiter().GetResult()
        $stderr=$stderrTask.GetAwaiter().GetResult()
        if ($process.ExitCode -ne 0) {
            # Do not expose private source paths or contents in public logs.
            throw ('Owner command refused real-capture replay: exit ' + $process.ExitCode +
                '; inspect the source-owned failure in a private test environment.')
        }
        try { $result=$stdout | ConvertFrom-Json -Depth 70 -ErrorAction Stop }
        catch { throw 'Owner command returned invalid JSON.' }
        if ($result.ok -ne $true) { throw 'Owner command returned an unsuccessful result.' }
        return $result
    } finally { $process.Dispose() }
}

try {
    New-Item -ItemType Directory -Path $stage -Force | Out-Null
    # The existing Verify-RealCaptureFixture.ps1 has already checked ZIP entry
    # paths, sealed index hashes, GRSP data and official PresentMon QPC rows.
    Expand-Archive -LiteralPath $FixtureZip -DestinationPath $stage -Force
    $cetSummaries=@(Get-ChildItem -LiteralPath $stage -Filter 'CET_Summary.json' -Recurse -File)
    if ($cetSummaries.Count -ne 1) {
        throw 'Real GCO fixture must contain exactly one CET capture summary.'
    }
    $cetCapture=$cetSummaries[0].DirectoryName
    $handoff=Join-Path $cetCapture 'CET_Resolver_Input.json'
    if (!(Test-Path -LiteralPath $handoff -PathType Leaf)) {
        # Generate the missing machine handoff from the owner raw capture in
        # TEMP only. Never forge CET_Resolver_Input.json or edit collection.json.
        $null=InvokeOwnerCli $owner @('--report','--capture',$cetCapture,'--json')
        if (!(Test-Path -LiteralPath $handoff -PathType Leaf)) {
            throw 'CET owner report did not produce Resolver handoff from the real capture.'
        }
    }
    $result=InvokeOwnerCli $resolver @('--capture',$cetCapture,'--mods',$mods,'--json')
    if (!$result.ResolverPath -or !(Test-Path -LiteralPath $result.ResolverPath -PathType Leaf)) {
        throw 'Real CET Resolver did not produce an analysis document.'
    }
    $analysis=Get-Content -LiteralPath $result.ResolverPath -Raw |
        ConvertFrom-Json -Depth 70
    if ($null -eq $analysis.captureRuntimeInterpretation -or
        $null -eq $analysis.gGcCaptureCalibration) {
        throw 'Real CET analysis lacks source-owned capture/G-GC provenance.'
    }
    if ([int]$result.FamilyCount -lt 1 -or [int]$result.RankedCallbackCount -lt 1) {
        throw 'No real measured CET callback family matched the live-source snapshot.'
    }
    if ((Get-FileHash -LiteralPath $FixtureZip -Algorithm SHA256).Hash -ne $zipHash) {
        throw 'Sealed GCO real-capture ZIP changed during analysis.'
    }
    $after=@(Get-ChildItem -LiteralPath $mods -Recurse -File -ErrorAction Stop)
    if ($after.Count -ne $hashes.Count) {
        throw 'Live-source fixture file inventory changed during Resolver analysis.'
    }
    foreach ($file in $after) {
        $relative=[IO.Path]::GetRelativePath($mods,$file.FullName)
        if (!$hashes.ContainsKey($relative) -or
            $hashes[$relative] -ne (Get-FileHash -LiteralPath $file.FullName -Algorithm SHA256).Hash) {
            throw 'Private live CET source was modified during Resolver analysis.'
        }
    }
    if (Test-Path -LiteralPath (Join-Path $PackageRoot 'GCO-Capture-15112026_021913')) {
        throw 'Private CET source fixture leaked into GCO distributable package.'
    }
    Write-Host ('PASS real sealed CET capture + private live-source replay: callback families=' +
        [int]$result.FamilyCount + ', ranked=' + [int]$result.RankedCallbackCount +
        ', original source files unchanged=' + $hashes.Count)
    Write-Host 'PASS GCO .NET10 owner Resolver produced analysis in disposable workspace; no private source packaged'
} finally {
    if (Test-Path -LiteralPath $stage) {
        Remove-Item -LiteralPath $stage -Recurse -Force -ErrorAction SilentlyContinue
    }
}
$global:LASTEXITCODE=0
