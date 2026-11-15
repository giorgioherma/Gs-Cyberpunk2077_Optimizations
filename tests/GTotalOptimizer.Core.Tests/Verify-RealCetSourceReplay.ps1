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
    # Stage 2: actual sealed index/CET+GRSP F11 markers, no imported fake
    # CapFrameX JSON. Mirror the captured RESULTS tree in CI temporary space.
    $indexes=@(Get-ChildItem -LiteralPath $stage -Filter 'collection.json' -Recurse -File)
    if ($indexes.Count -ne 1) { throw 'Real fixture needs exactly one collection.json.' }
    $sealedSession=$indexes[0].DirectoryName
    $packageSandbox=Join-Path $stage 'GCO-FrameContract-Package'
    $results=Join-Path $packageSandbox 'RESULTS'
    New-Item -ItemType Directory -Force -Path $results | Out-Null
    $copiedSession=Join-Path $results (Split-Path $sealedSession -Leaf)
    Copy-Item -LiteralPath $sealedSession -Destination $copiedSession -Recurse -ErrorAction Stop
    & dotnet run --configuration Release --project 'tests/GTotalOptimizer.Core.Tests/GTotalOptimizer.Core.Tests.csproj' -- --stage-presentmon $copiedSession $cetCapture
    if ($LASTEXITCODE -ne 0) {
        throw 'Production GCO PresentMon source-clock conversion refused the real sealed F11 capture.'
    }
    $frames=Join-Path $cetCapture 'PresentMon/GCO-PresentMon-Frames.v1.json'
    if (!(Test-Path -LiteralPath $frames -PathType Leaf)) {
        throw 'GCO did not stage its versioned PresentMon chronology for CET.'
    }
    # The actual source-owned report engine must regenerate its canonical
    # resolver handoff from frame evidence in the derived CET workspace.
    $null=InvokeOwnerCli $owner @('--report','--capture',$cetCapture,'--json')
    if (!(Test-Path -LiteralPath $handoff -PathType Leaf)) {
        throw 'CET owner report did not produce a PresentMon resolver handoff.'
    }
    $reportHandoff=Get-Content -LiteralPath $handoff -Raw | ConvertFrom-Json -Depth 70
    if ($reportHandoff.frameEvidence.provider -ne 'PRESENTMON_2_6' -or
        $reportHandoff.frameEvidence.correlation -ne 'QPC_F11_CET_WINDOW_CORRELATION_ONLY' -or
        $reportHandoff.frameEvidence.exactCallbackAttribution -ne $false -or
        $null -eq $reportHandoff.hitchPressure) {
        throw 'CET report failed to consume real PresentMon with truthful callback uncertainty.'
    }

    # Independent numerical oracle from *raw derived PresentMon intervals*;
    # never accept a CET report simply because it reports provider=PRESENTMON.
    $chronology=Get-Content -LiteralPath $frames -Raw | ConvertFrom-Json -Depth 70
    $summary=Get-Content -LiteralPath (Join-Path $cetCapture 'CET_Summary.json') -Raw |
        ConvertFrom-Json -Depth 70
    $frameReport=$summary.frameTime
    if ($frameReport.provider -ne 'PRESENTMON_2_6' -or
        $frameReport.frames.captureEndExclusionPolicy -ne 'NONE_GCO_F11_TRIMMED' -or
        $frameReport.frames.excludedCaptureEndCount -ne 0 -or
        $frameReport.pcLatencyStatus -ne 'NOT_CAPTURED' -or
        $frameReport.frameTypeStatus -ne 'NOT_CAPTURED' -or
        $frameReport.frames.cpuMetric -ne 'MsCPUBusy' -or
        $frameReport.frames.gpuMetric -ne 'MsGPUBusy' -or
        $frameReport.sync.exactAlignment -ne $false) {
        throw 'CET frame report has misleading PresentMon provenance, tail exclusion or unavailable metrics.'
    }
    if (@($frameReport.worstFrames).Count -lt 1 -or
        $null -ne $frameReport.worstFrames[0].pcLatencyMs -or
        $null -ne $frameReport.worstFrames[0].frameType) {
        throw 'Missing PresentMon PC latency and frame type must remain null in worst-frame evidence.'
    }
    [double[]]$allIntervals=@($chronology.frames | ForEach-Object { [double]$_.intervalMs })
    [Array]::Sort($allIntervals)
    if ($allIntervals.Length -lt 2 -or
        $allIntervals.Length -ne [int]$frameReport.frames.count -or
        $allIntervals.Length -ne [int]$frameReport.frames.capturedCount -or
        $allIntervals[0] -le 0) {
        throw 'Actual PresentMon report did not retain the complete positive F11 frame interval series.'
    }
    function MeasuredPercentile([double[]]$numbers,[double]$fraction) {
        $offset=($numbers.Length-1)*$fraction
        $low=[int][Math]::Floor($offset)
        $high=[int][Math]::Ceiling($offset)
        return $numbers[$low]+($numbers[$high]-$numbers[$low])*($offset-$low)
    }
    function AssertFrameNumber([double]$expected,[double]$actual,[double]$precision,[string]$metric) {
        if (![double]::IsFinite($actual) -or
            [Math]::Abs($expected-$actual) -gt $precision) {
            throw ("Independent real PresentMon arithmetic mismatch: "+$metric+
                " expected="+$expected+" actual="+$actual)
        }
    }
    $median=MeasuredPercentile $allIntervals 0.50
    $p95=MeasuredPercentile $allIntervals 0.95
    $p99=MeasuredPercentile $allIntervals 0.99
    $mean=($allIntervals | Measure-Object -Sum).Sum / $allIntervals.Length
    AssertFrameNumber $mean ([double]$frameReport.frames.meanMs) 0.00001 'mean'
    AssertFrameNumber $median ([double]$frameReport.frames.medianMs) 0.00001 'P50'
    AssertFrameNumber $p95 ([double]$frameReport.frames.p95Ms) 0.00001 'P95'
    AssertFrameNumber $p99 ([double]$frameReport.frames.p99Ms) 0.00001 'P99'
    AssertFrameNumber $allIntervals[-1] ([double]$frameReport.frames.maxMs) 0.00001 'maximum'
    AssertFrameNumber (1000.0/$mean) ([double]$frameReport.frames.averageFps) 0.001 'average FPS'
    $tolerance=[Math]::Max(25.0,$median*1.5)
    AssertFrameNumber $tolerance ([double]$frameReport.hitchPressure.tolerance.thresholdFrameMs) 0.00001 'hitch threshold'
    $hitchCount=0
    $hitchToll=0.0
    foreach ($interval in $allIntervals) {
        if ($interval -ge $tolerance) {
            $hitchCount++
            $hitchToll += $interval-$tolerance
        }
    }
    if ([int]$frameReport.hitchPressure.frequency.hitchFrames -ne $hitchCount) {
        throw 'Independent real PresentMon hitch-frame count differs from CET report.'
    }
    AssertFrameNumber $hitchToll ([double]$frameReport.hitchPressure.frequency.hitchTollMs) 0.0001 'hitch toll'
    $episodes=0
    $previousEnd=[double]::NegativeInfinity
    foreach ($row in @($chronology.frames | Sort-Object { [double]$_.relativeStartMs })) {
        $duration=[double]$row.intervalMs
        if ($duration -lt $tolerance) { continue }
        $start=[double]$row.relativeStartMs
        $end=$start+$duration
        if ($start-$previousEnd -gt 250.0) { $episodes++ }
        $previousEnd=[Math]::Max($previousEnd,$end)
    }
    if ([int]$frameReport.hitchPressure.frequency.episodes -ne $episodes) {
        throw ('Independent real PresentMon 250 ms-merged hitch episodes disagree: CET='+
            $frameReport.hitchPressure.frequency.episodes+' independently='+$episodes)
    }
    Write-Host ("PASS independent Stage 2 real PresentMon numerical oracle: "+
        $allIntervals.Length+" frames, P95="+$p95+", P99="+$p99+
        ", hitch episodes="+$episodes+", tollMs="+$hitchToll)
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
    if ([string]$analysis.gGcCaptureCalibration.gameplay.toleranceSource -notlike 'PRESENTMON_QPC_*') {
        throw 'G-GC calibration did not consume the QPC-verified PresentMon frame allowance.'
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
    # Stage 3 additionally exercises the production Page 2 GCO analysis
    # service on a TEMP physical CET source copy and simulated restored
    # GCO session; never alter the real private original source checkout.
    & ./tests/GTotalOptimizer.Core.Tests/Verify-Stage3ReadOnlyAnalysis.ps1 -StagedSealedSession $sealedSession -PrivateSourceRoot $PrivateSourceRoot -PackageRoot $PackageRoot -WorkRoot $stage
    if ($LASTEXITCODE -ne 0) {
        throw 'Stage 3 restored-source analysis regression failed.'
    }
    if ((Get-FileHash -LiteralPath $FixtureZip -Algorithm SHA256).Hash -ne $zipHash) {
        throw 'Stage 3 changed the immutable real GCO capture ZIP.'
    }
    Write-Host ('PASS real sealed CET capture + private live-source replay: callback families=' +
        [int]$result.FamilyCount + ', ranked=' + [int]$result.RankedCallbackCount +
        ', original source files unchanged=' + $hashes.Count)
    Write-Host 'PASS Stage 2 real GCO PresentMon QPC -> CET report/hitch -> G-GC allowance; CapFrameX not required'
    Write-Host 'PASS GCO .NET10 owner Resolver produced analysis in disposable workspace; no private source packaged'
} finally {
    if (Test-Path -LiteralPath $stage) {
        Remove-Item -LiteralPath $stage -Recurse -Force -ErrorAction SilentlyContinue
    }
}
$global:LASTEXITCODE=0
