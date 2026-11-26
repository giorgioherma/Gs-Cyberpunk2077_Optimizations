param(
    [Parameter(Mandatory=$true)][string]$PackageRoot,
    [Parameter(Mandatory=$true)][string]$OwnerArtifactRoot
)
$ErrorActionPreference='Stop'
$PSNativeCommandUseErrorActionPreference=$false
$PackageRoot=(Resolve-Path -LiteralPath $PackageRoot).Path
$OwnerArtifactRoot=(Resolve-Path -LiteralPath $OwnerArtifactRoot).Path
$mode=if ($env:GCO_CET_MODE) { $env:GCO_CET_MODE } else { 'production' }
$pins=Import-PowerShellDataFile (Join-Path $PSScriptRoot '../../integrations/three-profiler-test/Cet-PackagePins.psd1')
$pin=$pins[$mode]
if (!$pin) { throw 'Unknown CET package mode' }
$manifest=Get-Content -LiteralPath (Join-Path $PackageRoot 'components/manifest.json') -Raw | ConvertFrom-Json
$tools=@($manifest.analysisTools)
if ($tools.Count -ne 1 -or $tools[0].id -ne 'cet-resolver' -or
    $tools[0].sourceCommit -ne $pin.SourceCommit -or
    $tools[0].ownerReferenceSourceCommit -ne $pin.SourceCommit -or
    $tools[0].ownerCiRun -ne $pin.RunId -or
    $tools[0].ownerArtifactId -ne $pin.ResolverArtifactId -or
    $tools[0].framework -ne 'net10.0-windows') {
    throw 'CET Resolver source/provenance manifest is not exact.'
}
$tool=$tools[0]
if ($tool.nativeSha256 -ne $pin.NativeSha256) { throw 'Resolver native hash differs from selected pin' }
if ($mode -eq 'experimental' -and ($tool.experimentalNative -ne $true -or
    $tool.nativeSourceCommit -ne $pin.NativeSourceCommit -or
    $tool.nativeOwnerCiRun -ne $pin.NativeRunId -or
    $tool.nativeOwnerArtifactId -ne $pin.NativeArtifactId -or
    $tool.ownerNativeSha256 -ne $pin.OwnerNativeSha256)) {
    throw 'Experimental native owner provenance missing or incorrect'
}
$rootPrefix=$PackageRoot.TrimEnd('\') + '\'
$files=@($tool.files)
if ($files.Count -lt 12) { throw 'Complete CET Resolver file/hash inventory missing.' }
foreach ($entry in $files) {
    if (!$entry.path -or $entry.path -notmatch '^components/cet-resolver/app/' -or
        $entry.path.Contains('..') -or $entry.sha256 -notmatch '^[0-9a-fA-F]{64}$') {
        throw "Unsafe Resolver asset identity: $($entry.path)"
    }
    $file=[IO.Path]::GetFullPath((Join-Path $PackageRoot $entry.path))
    if (!$file.StartsWith($rootPrefix,[StringComparison]::OrdinalIgnoreCase) -or
        !(Test-Path -LiteralPath $file -PathType Leaf) -or
        (Get-FileHash -LiteralPath $file -Algorithm SHA256).Hash -ne $entry.sha256) {
        throw "Unverified/missing Resolver asset: $($entry.path)"
    }
}
$exe=Join-Path $PackageRoot $tool.executable
if (!(Test-Path -LiteralPath $exe -PathType Leaf) -or
    (Get-FileHash -LiteralPath $exe -Algorithm SHA256).Hash -ne $tool.sha256) {
    throw 'Published Resolver executable does not match its manifest.'
}
$bin=Split-Path $exe -Parent
$runtimeConfig=Get-Content ([IO.Path]::ChangeExtension($exe,'.runtimeconfig.json')) -Raw
if ($runtimeConfig -notmatch 'net10\.0' -or
    @(Get-ChildItem $PackageRoot -Filter coreclr.dll -File -Recurse).Count -ne 1) {
    throw 'CET Resolver did not use the one shared framework-dependent .NET10 runtime.'
}
$native=Join-Path $bin 'runtime/native/GCETStaticLocations.dll'
if ((Get-FileHash $native -Algorithm SHA256).Hash -ne $tool.nativeSha256) {
    throw 'Pinned CET native service differs from owner CI.'
}
$refs=@(Get-ChildItem -LiteralPath $OwnerArtifactRoot -Recurse -File -Filter G-CET-Resolver.exe |
    Where-Object { Test-Path (Join-Path $_.DirectoryName 'knowledge/semantic-library.json') })
if ($refs.Count -eq 0) {
    $expanded=Join-Path $env:RUNNER_TEMP 'GTriple/Resolver'
    if (Test-Path $expanded) {
        $refs=@(Get-ChildItem $expanded -Recurse -File -Filter G-CET-Resolver.exe |
            Where-Object { Test-Path (Join-Path $_.DirectoryName 'knowledge/semantic-library.json') })
    }
}
if ($refs.Count -ne 1) { throw 'Pinned CET Resolver reference missing or ambiguous.' }
$ownerExe=$refs[0].FullName
$ownerRoot=$refs[0].DirectoryName
if ((Get-FileHash (Join-Path $ownerRoot 'runtime/native/GCETStaticLocations.dll') -Algorithm SHA256).Hash -ne $pin.OwnerNativeSha256) {
    throw 'Original reference native owner hash mismatch'
}
if ($mode -eq 'experimental') {
    $originalRoot=$ownerRoot
    $ownerRoot=Join-Path $env:RUNNER_TEMP 'GTriple/ResolverExperimental'
    $ownerExe=Join-Path $ownerRoot 'G-CET-Resolver.exe'
    foreach ($asset in @('G-CET-Resolver.exe','G-CET-Resolver.dll','G.CETProfiler.Core.dll')) {
        if ((Get-FileHash (Join-Path $ownerRoot $asset)).Hash -ne
            (Get-FileHash (Join-Path $originalRoot $asset)).Hash) {
            throw "Experimental reference changed owner managed executable: $asset"
        }
    }
    if ((Get-FileHash (Join-Path $ownerRoot 'runtime/native/GCETStaticLocations.dll')).Hash -ne $pin.NativeSha256) {
        throw 'Experimental reference native hash mismatch'
    }
}
foreach ($asset in @(
    'knowledge/semantic-library.json',
    'runtime/0-Engine/fixed-init.lua.gz.b64',
    'runtime/0-Engine/modules/ActionRouter.lua.b64',
    'runtime/0-Engine/modules/GCETNativeLocationHost.lua',
    'runtime/native/GCETStaticLocations.dll'
)) {
    if ((Get-FileHash (Join-Path $bin $asset) -Algorithm SHA256).Hash -ne
        (Get-FileHash (Join-Path $ownerRoot $asset) -Algorithm SHA256).Hash) {
        throw "Rebuilt CET Resolver changed owner asset: $asset"
    }
}
$help=& $exe --help 2>&1
if ($LASTEXITCODE -ne 0 -or ($help -join ' ') -notmatch '\-\-generate-pass') {
    throw 'CET Resolver source-owned CLI entrypoints missing.'
}
$scratch=Join-Path $env:RUNNER_TEMP ("GCO-Resolver-Stage1-" + [guid]::NewGuid().ToString('N'))
try {
    $game=Join-Path $scratch 'Cyberpunk 2077'
    $mods=Join-Path $game 'bin/x64/plugins/cyber_engine_tweaks/mods'
    $zero=Join-Path $mods '0-Engine'
    $owner=Join-Path $mods 'FixtureMeasured'
    New-Item -ItemType Directory -Force $zero,$owner | Out-Null
    # The exact source-owned fixed runtime, not synthetic replacement code.
    $b64=(Get-Content -LiteralPath (Join-Path $bin 'runtime/0-Engine/fixed-init.lua.gz.b64') -Raw).Trim()
    $input=[IO.MemoryStream]::new([Convert]::FromBase64String($b64))
    $gzip=[IO.Compression.GZipStream]::new($input,[IO.Compression.CompressionMode]::Decompress)
    $output=[IO.MemoryStream]::new()
    try {
        $gzip.CopyTo($output)
        [IO.File]::WriteAllBytes((Join-Path $zero 'init.lua'),$output.ToArray())
    } finally { $output.Dispose(); $gzip.Dispose(); $input.Dispose() }
    @'
registerForEvent("onUpdate", function(dt)
    if dt > 0 then
        local player = Game.GetPlayer()
        if player then DoMeasuredWork(player) end
    end
end)
'@ | Set-Content -LiteralPath (Join-Path $owner 'init.lua') -Encoding utf8
    $handoff=@{
        schemaVersion='2.0'
        interop=@{ contractVersion='1.1'; domain='cet' }
        capture=@{ durationSeconds=60.0; exclusiveMsPerSecond=8.0; measuredCalls=3600; callsPerSecond=60.0 }
        callbacks=@(
            @{ registrationId=101; owner='FixtureMeasured'; infrastructure=$false;
               kind='event'; target='onUpdate'; source=@{ file='init.lua'; lineStart=1; lineEnd=6 };
               callsPerSecond=60.0; exclusiveMsPerSecond=8.0; globalWorkSharePct=100.0;
               familyWorkSharePct=100.0; avgExclusiveUs=133.33; maxExclusiveMs=0.5;
               spikeCount=0; maxSpikeExclusiveMs=0.0 }
        )
        scheduler=@{ available=$false; jobs=@() }
        garbageCollection=@{ slowOperations=@() }
        quality=@{ frameNormalizationAvailable=$false }
    } | ConvertTo-Json -Depth 20
    $captures=@{}
    foreach ($label in @('owner','gco')) {
        $folder=Join-Path $scratch ("capture-" + $label)
        New-Item -ItemType Directory -Force $folder | Out-Null
        [IO.File]::WriteAllText((Join-Path $folder 'CET_Resolver_Input.json'),
            $handoff,[Text.UTF8Encoding]::new($false))
        $captures[$label]=$folder
    }
    $originHash=(Get-FileHash (Join-Path $captures.gco 'CET_Resolver_Input.json') -Algorithm SHA256).Hash
    $sourceHash=(Get-FileHash (Join-Path $owner 'init.lua') -Algorithm SHA256).Hash
    function Execute-Resolver([string]$Program,[string]$Capture) {
        $outputLines=@(& $Program --capture $Capture --game $game --json 2>&1)
        $exitCode=$LASTEXITCODE
        if ($exitCode -ne 0) { throw "Owner Resolver ($Program) failed: $($outputLines -join ' ')" }
        $result=($outputLines -join [Environment]::NewLine) | ConvertFrom-Json
        if ($result.ok -ne $true -or !$result.ResolverPath -or
            !(Test-Path -LiteralPath $result.ResolverPath -PathType Leaf)) {
            throw 'Resolver did not produce its original JSON report.'
        }
        $doc=Get-Content -LiteralPath $result.ResolverPath -Raw | ConvertFrom-Json
        if (!$doc.captureRuntimeInterpretation -or !$doc.gGcCaptureCalibration) {
            throw 'Source-backed report omitted original capture/G-GC provenance.'
        }
        return $result
    }
    $baseline=Execute-Resolver $ownerExe $captures.owner
    $rebuilt=Execute-Resolver $exe $captures.gco
    foreach ($key in @('FamilyCount','RankedCallbackCount','GenericResolvedCount',
        'NonFrameOnlyAutoCount','FrameOnlyAutoCount','SemanticReadyRuleCount',
        'AlreadySatisfiedCount','MaterialRemainingCount','BelowThresholdCount',
        'UnresolvedCount')) {
        if ($rebuilt.$key -ne $baseline.$key) {
            throw "Rebuilt .NET10 Resolver differs from owner baseline ($key)."
        }
    }
    if ($rebuilt.FamilyCount -lt 1 -or $rebuilt.RankedCallbackCount -lt 1) {
        throw 'Measured, current-source callback did not survive Resolver baseline.'
    }
    if ((Get-FileHash (Join-Path $captures.gco 'CET_Resolver_Input.json') -Algorithm SHA256).Hash -ne $originHash -or
        (Get-FileHash (Join-Path $owner 'init.lua') -Algorithm SHA256).Hash -ne $sourceHash) {
        throw 'Resolver changed source evidence or current installed-mod fixture.'
    }
    if ($mode -eq 'experimental') {
        # Exercise capability-aware generation through the ACTUAL shipped
        # .NET10 Resolver, not merely the owner artifact or copied DLL.
        # Keep the measured fixture unchanged; this unmeasured mod also
        # proves source-first generic adoption.
        $poseDir=Join-Path $mods 'FixtureExactPose'
        New-Item -ItemType Directory -Force $poseDir | Out-Null
        $posePath=Join-Path $poseDir 'init.lua'
        [IO.File]::WriteAllText($posePath,@'
local function readPose()
    local player = Game.GetPlayer()
    local pos = player:GetWorldPosition()
    local angles = player:GetWorldOrientation():ToEulerAngles()
    return pos, angles
end
return readPose
'@,[Text.UTF8Encoding]::new($false))
        $poseHash=(Get-FileHash -LiteralPath $posePath).Hash
        $zip=Join-Path $scratch 'experimental-pass.zip'
        $result=(& $exe --capture $captures.gco --mods $mods --generate-pass --pass-output $zip --json | ConvertFrom-Json)
        if ($LASTEXITCODE -ne 0 -or !$result.ok -or !$result.pass) { throw 'Packaged experimental CET generation failed' }
        $generated=Get-Content -LiteralPath $result.pass.ManifestPath -Raw | ConvertFrom-Json
        $pairs=@($generated.transforms | Where-Object { $_.type -eq 'NATIVE_EXACT_POSITION_EULER' -and $_.file -eq 'FixtureExactPose/init.lua' })
        if ($pairs.Count -ne 1 -or [int]$pairs[0].exactPairs -ne 1 -or !$pairs[0].sourceProof) {
            throw 'Packaged experimental CET Resolver did not adopt exact pose source'
        }
        $overlay=Join-Path $scratch 'overlay'
        Expand-Archive -LiteralPath $zip -DestinationPath $overlay
        $emitted=Join-Path $overlay 'red4ext/plugins/G-CET-StaticLocations/GCETStaticLocations.dll'
        if ((Get-FileHash -LiteralPath $emitted).Hash -ne $pin.NativeSha256) { throw 'Generated overlay used wrong native service' }
        $generatedPose=Get-Content -Raw -LiteralPath (Join-Path $overlay 'bin/x64/plugins/cyber_engine_tweaks/mods/FixtureExactPose/init.lua')
        if (!$generatedPose.Contains('Game.GCETReadPlayerPositionEulerV1') -or
            !$generatedPose.Contains('__gcetReadExactPositionEuler(player, false)') -or
            (Get-FileHash -LiteralPath $posePath).Hash -ne $poseHash) {
            throw 'Packaged experimental pose output or source preservation failed'
        }
        Write-Host 'PASS actual packaged .NET10 experimental Resolver emits verified native DLL and ordered pose on untouched new owner'
    }
    $bad=@(& $exe --capture (Join-Path $scratch 'missing-capture') --game $game --json 2>&1)
    if ($LASTEXITCODE -eq 0 -or ($bad -join ' ') -notmatch '"ok":false') {
        throw 'Resolver must reject missing capture with nonzero exit and JSON error.'
    }
    $bad=@(& $exe --capture $captures.gco --game (Join-Path $scratch 'absent-game') --json 2>&1)
    if ($LASTEXITCODE -eq 0 -or ($bad -join ' ') -notmatch '"ok":false') {
        throw 'Resolver must reject unavailable live source.'
    }
    # The two negative-case invocations must exit nonzero. Their validated
    # result cannot become this *test script's* exit code in the calling CI
    # step: report success only after all assertions above have passed.
    $global:LASTEXITCODE = 0
    Write-Host 'PASS CET Resolver .NET10 original CLI vs .NET8 owner baseline, source-backed measured callback, full hashed assets, single runtime and safe invalid-input refusal'
} finally {
    if (Test-Path -LiteralPath $scratch) { Remove-Item -LiteralPath $scratch -Recurse -Force }
}
