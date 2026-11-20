# GCO-local REDscript component headless SCC 0.5.31 compiler contract. Runner only; no game writes.
param(
  [Parameter(Mandatory=$true)][string]$GameRoot,
  [Parameter(Mandatory=$true)][string]$PreviewRoot,
  [Parameter(Mandatory=$true)][string]$CompilerArchive,
  [Parameter(Mandatory=$true)][string]$CleanBaselineArchive,
  [Parameter(Mandatory=$true)][string]$EffectiveCacheArchive,
  [Parameter(Mandatory=$true)][string]$PluginsArchive,
  [Parameter(Mandatory=$true)][string]$ResolverDll,
  [string]$CallbackPreviewRoot = '',
  [string]$FrameworkPreviewRoot = '',
  [string]$FrameworkRoot = '',
  [string]$PostGenericSourceRoot = ''
)
$ErrorActionPreference='Stop'
Set-StrictMode -Version Latest
$game=(Resolve-Path -LiteralPath $GameRoot).Path
$preview=(Resolve-Path -LiteralPath $PreviewRoot).Path
$compilerZip=(Resolve-Path -LiteralPath $CompilerArchive).Path
$baselineZip=(Resolve-Path -LiteralPath $CleanBaselineArchive).Path
$effectiveZip=(Resolve-Path -LiteralPath $EffectiveCacheArchive -ErrorAction Stop).Path
$pluginsZip=(Resolve-Path -LiteralPath $PluginsArchive -ErrorAction Stop).Path
$dll=(Resolve-Path -LiteralPath $ResolverDll).Path
$plan=Get-Content -LiteralPath (Join-Path $preview 'preview.json') -Raw | ConvertFrom-Json -Depth 80
if ($plan.schemaVersion -ne 'grsp.generic.preview.v1' -or
    $plan.rule -ne 'CNAME_ARRAY_MEMBERSHIP_V1' -or
    $plan.generationAuthorized -ne $false -or
    $plan.compilerValidated -ne $false -or
    $plan.deployable -ne $false -or
    [int]$plan.stagedFiles -lt 1) {
  throw 'R6 requires real structurally proven, nondeployable GRSP source preview.'
}
# One native invocation implementation for both the smoke and full-source gates.
# Logs stay on the private runner; never publish third-party source paths.
# The official scc.exe CLI hardcodes show_error_popup=true. Its error
# dialog hangs headless Windows Actions; use the source-owned .NET 10 CLI
# over scc_lib.dll exports to disable the popup and report real errors.
function Invoke-HeadlessScc {
  param([string]$Phase,[string]$IsolatedRoot,[string]$CompilerLib,[int]$TimeoutSeconds)
  $r6=Join-Path $IsolatedRoot 'r6'
  $cache=Join-Path $r6 'cache/final.redscripts'
  $compiled=$cache+'.modded'
  $report=Join-Path $work ($Phase+'-headless-result.json')
  $privateLog=Join-Path $work ($Phase+'-native-compiler-error.log')
  Set-Content -LiteralPath (Join-Path $IsolatedRoot '.grsp-r6-workspace') -Value 'GRSP_R6_ISOLATED_WORKSPACE' -NoNewline
  New-Item -ItemType Directory -Force -Path (Join-Path $r6 'logs') | Out-Null
  $args=@($dll,'scc-headless','--workspace',$IsolatedRoot,'--r6',$r6,'--cache',$cache,'--compiled',$compiled,'--scc-native',$CompilerLib,'--out',$report,'--log',$privateLog)
  $externalScripts=Join-Path $IsolatedRoot 'red4ext/plugins'
  if (Test-Path -LiteralPath $externalScripts -PathType Container) {
    $args+=@('--external-scripts',$externalScripts)
  }
  $expectedExternalPaths=0
  if (Test-Path -LiteralPath $externalScripts -PathType Container) {
    foreach($plugin in @(Get-ChildItem -LiteralPath $externalScripts -Directory)) {
      $scriptDir=Join-Path $plugin.FullName 'Scripts'
      $scriptPrefix=$scriptDir+[System.IO.Path]::DirectorySeparatorChar
      $allReds=@(Get-ChildItem -LiteralPath $plugin.FullName -File -Recurse -Filter '*.reds')
      $underScripts=@($allReds | Where-Object {
        $_.FullName.StartsWith($scriptPrefix,[System.StringComparison]::OrdinalIgnoreCase) })
      if ($underScripts.Count -gt 0) { $expectedExternalPaths++ }
      $expectedExternalPaths+=($allReds.Count-$underScripts.Count)
    }
  }
  $info=[System.Diagnostics.ProcessStartInfo]::new('dotnet')
  $info.WorkingDirectory=Split-Path $dll -Parent
  $info.UseShellExecute=$false
  $info.RedirectStandardOutput=$true
  $info.RedirectStandardError=$true
  foreach($arg in $args) { [void]$info.ArgumentList.Add([string]$arg) }
  $p=[System.Diagnostics.Process]::new()
  $p.StartInfo=$info
  $timer=[System.Diagnostics.Stopwatch]::StartNew()
  try {
    if (!$p.Start()) { throw ('R6_HEADLESS_SCC_LAUNCH_FAILED: phase='+$Phase) }
    $stdout=$p.StandardOutput.ReadToEndAsync()
    $stderr=$p.StandardError.ReadToEndAsync()
    $exited=$p.WaitForExit($TimeoutSeconds*1000)
    if (!$exited) { $p.Kill($true); $p.WaitForExit() }
    $out=$stdout.GetAwaiter().GetResult()
    $err=$stderr.GetAwaiter().GetResult()
    $cpu=[Math]::Round($p.TotalProcessorTime.TotalSeconds,1)
    $exitLabel=if($exited){[string]$p.ExitCode}else{'TIMEOUT'}
    Write-Host ('R6 SCC headless: phase='+$Phase+' seconds='+[Math]::Round($timer.Elapsed.TotalSeconds,1)+' cpuSeconds='+$cpu+' exit='+$exitLabel)
    if (!$exited) { throw ('R6_HEADLESS_SCC_TIMEOUT: phase='+$Phase+' limit='+$TimeoutSeconds+'s') }
    if (!(Test-Path -LiteralPath $report -PathType Leaf)) {
      $missingExternalAbi=[bool]($err -match 'scc_settings_add_script_path')
      $missingNativeSymbol=[bool]($err -match '(?i)entry point|export|symbol')
      Write-Host ('R6 headless launch diagnostics: externalScriptAbiMentioned='+$missingExternalAbi+' missingNativeSymbolHint='+$missingNativeSymbol)
      throw ('R6_HEADLESS_SCC_REPORT_MISSING: phase='+$Phase+' exit='+$p.ExitCode+' stderrChars='+$err.Length)
    }
    $result=Get-Content -LiteralPath $report -Raw | ConvertFrom-Json -Depth 40
    if ($result.schemaVersion -ne 'grsp.scc.headless.v1' -or
        $result.guiErrorPopupDisabled -ne $true -or
        $result.originalCacheUnchanged -ne $true -or
        [int]$result.externalScriptPaths -ne $expectedExternalPaths) {
      throw ('R6_HEADLESS_SCC_EVIDENCE_INVALID: phase='+$Phase)
    }
    if ($p.ExitCode -ne 0 -or $result.success -ne $true) {
      # Native flexi_logger files are closed now, after the DLL-hosting
      # dotnet process exited. Extract codes only; never print script paths
      # or upload raw private compiler diagnostics.
      $codes=@($result.errorCodeCounts.PSObject.Properties | ForEach-Object { $_.Name+'='+$_.Value })
      $nativeCodes=@()
      foreach($file in @(Get-ChildItem -LiteralPath (Join-Path $r6 'logs') -Filter 'redscript*.log' -File)) {
        $content=Get-Content -LiteralPath $file.FullName -Raw
        foreach($match in [regex]::Matches($content,'\[[A-Z][A-Z0-9_]+\]')) {
          if ($match.Value -notin @('[INFO]','[WARN]','[ERROR]','[DEBUG]','[TRACE]')) {
            $nativeCodes+=@($match.Value)
          }
        }
      }
      if ($nativeCodes.Count -gt 0) {
        $codes=@($nativeCodes | Group-Object | Sort-Object Name | ForEach-Object { $_.Name+'='+$_.Count })
      }
      $safeCodes=if($codes.Count -gt 0){$codes -join ','}else{'NO_STRUCTURED_ERROR_CODES'}
      # Never print the underlying private source or path. The native error
      # is retained only as an isolated-runner diagnostic.
      $safeUnresolved='NOT_IDENTIFIED'
      $locationParsed=$false
      $nativeLogFiles=@(Get-ChildItem -LiteralPath (Join-Path $r6 'logs') -Filter 'redscript*.log' -File)
      $diagnosticFiles=@($privateLog) + @($nativeLogFiles | ForEach-Object FullName)
      foreach($diagnosticFile in $diagnosticFiles) {
        if (!(Test-Path -LiteralPath $diagnosticFile -PathType Leaf)) { continue }
        $diagnostic=Get-Content -LiteralPath $diagnosticFile -Raw
        $position=[regex]::Match($diagnostic,'(?i)\bAt\s+(.+?\.reds):(\d+):(\d+)')
        if (!$position.Success) { continue }
        $locationParsed=$true
        $filePath=$position.Groups[1].Value
        $lineNo=[int]$position.Groups[2].Value
        $columnNo=[int]$position.Groups[3].Value
        if (!(Test-Path -LiteralPath $filePath -PathType Leaf) -or $lineNo -lt 1 -or $columnNo -lt 1) { continue }
        $sourceLine=@(Get-Content -LiteralPath $filePath)[$lineNo-1]
        if ($null -eq $sourceLine) { continue }
        $columnIndex=$columnNo-1
        foreach($match in [regex]::Matches([string]$sourceLine,'[A-Za-z_][A-Za-z0-9_]*')) {
          if ($match.Index -le $columnIndex -and $columnIndex -lt ($match.Index+$match.Length)) {
            if ($match.Value.Length -le 100) { $safeUnresolved=$match.Value }
            break
          }
        }
        if ($safeUnresolved -ne 'NOT_IDENTIFIED') { break }
      }
      $originExternal=0
      $originInstalled=0
      $symbolNames=[System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::Ordinal)
      foreach($logItem in $nativeLogFiles) {
        $logLines=@(Get-Content -LiteralPath $logItem.FullName)
        foreach($oneLine in $logLines) {
          if ($oneLine -notmatch 'UNRESOLVED_') { continue }
          if ($oneLine -match 'Codeware[\\/]Scripts') { $originExternal++ }
          elseif ($oneLine -match '[\\/]r6[\\/]scripts[\\/]') { $originInstalled++ }
        }
        $contents=Get-Content -LiteralPath $logItem.FullName -Raw
        foreach($match in [regex]::Matches($contents, 'unresolved [a-z ]+''([A-Za-z_][A-Za-z0-9_.]{0,90})''')) {
          if ($symbolNames.Count -lt 12) { [void]$symbolNames.Add($match.Groups[1].Value) }
        }
      }
      Write-Host ('R6 unresolved origin: codeware='+$originExternal+' installed='+$originInstalled+' symbols='+(@($symbolNames) -join ','))
      $nativeLogCount=$nativeLogFiles.Count
      $nativeLogHasLocation=[bool](@($nativeLogFiles | Where-Object {
        [regex]::IsMatch((Get-Content -LiteralPath $_.FullName -Raw),'(?i)\.reds:\d+:\d+')
      }).Count -gt 0)
      throw ('R6_HEADLESS_SCC_COMPILATION_FAILED: phase='+$Phase+' errorCodes='+$safeCodes+' unresolvedIdentifier='+$safeUnresolved+' nativeLogCount='+$nativeLogCount+' nativeLogHasLocation='+$nativeLogHasLocation+' locationParsed='+$locationParsed+' diagnosticBytes='+$result.diagnosticBytes+' rawErrorsRunnerPrivate=true')
    }
    if ($result.generatedOutputPresent -ne $true -or
        [long]$result.generatedOutputBytes -lt 1048576) {
      throw ('R6_HEADLESS_SCC_OUTPUT_MISSING: phase='+$Phase)
    }
    return $result
  }
  finally { $p.Dispose(); $timer.Stop() }
}
$work=Join-Path $env:RUNNER_TEMP ('grsp-r6-' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $work -Force | Out-Null
try {
  $compilerRoot=Join-Path $work 'compiler'
  $baselineRoot=Join-Path $work 'supplied-base'
  $effectiveRoot=Join-Path $work 'supplied-effective-cache'
  $pluginsRoot=Join-Path $work 'supplied-plugins'
  New-Item -ItemType Directory -Path @($compilerRoot,$baselineRoot,$effectiveRoot,$pluginsRoot) -Force | Out-Null
  Expand-Archive -LiteralPath $compilerZip -DestinationPath $compilerRoot -ErrorAction Stop
  Expand-Archive -LiteralPath $baselineZip -DestinationPath $baselineRoot -ErrorAction Stop
  Expand-Archive -LiteralPath $effectiveZip -DestinationPath $effectiveRoot -ErrorAction Stop
  Expand-Archive -LiteralPath $pluginsZip -DestinationPath $pluginsRoot -ErrorAction Stop
  $tools=@(Get-ChildItem -LiteralPath $compilerRoot -Recurse -File -Filter 'scc.exe')
  $libs=@(Get-ChildItem -LiteralPath $compilerRoot -Recurse -File -Filter 'scc_lib.dll')
  $bases=@(Get-ChildItem -LiteralPath $baselineRoot -Recurse -File | Where-Object Name -eq 'final.redscripts')
  if ($tools.Count -ne 1 -or $libs.Count -ne 1 -or $bases.Count -ne 1 -or
      $tools[0].DirectoryName -ne $libs[0].DirectoryName) {
    throw 'R6 requires one SCC executable + DLL together and one original final.redscripts candidate.'
  }
  $upstream=@{
    'scc.exe'='DA2C0DE580173010C907A05FDD2F3CD2430D6E62BF748339D65060118AE32EDD'
    'scc_lib.dll'='49EB60A3613C990CD13B6136E827A74F9F64AD68E2F911D8C93E4E3D3F53C100'
  }
  foreach($binary in @($tools[0],$libs[0])) {
    if ((Get-FileHash -Algorithm SHA256 -LiteralPath $binary.FullName).Hash -ne $upstream[$binary.Name]) {
      throw ('R6 supplied compiler differs from verified redscript 0.5.31: '+$binary.Name)
    }
  }
  $baseFile=$bases[0]
  if ($baseFile.Length -lt 1048576) {
    throw 'R6 baseline candidate too small to contain a valid game script bundle.'
  }
  $baseSha=(Get-FileHash -Algorithm SHA256 -LiteralPath $baseFile.FullName).Hash
  # Phase 2: a separate already-generic source baseline. The historical
  # capture-matched source above remains the generic regression authority.
  $postGenericSources=''
  $postGenericCount=0
  $postGenericBefore=@{}
  if (![string]::IsNullOrWhiteSpace($PostGenericSourceRoot)) {
    $postGenericGame=(Resolve-Path -LiteralPath $PostGenericSourceRoot -ErrorAction Stop).Path
    if ($postGenericGame.Equals($game,[StringComparison]::OrdinalIgnoreCase) -or
        $postGenericGame.StartsWith($game+[IO.Path]::DirectorySeparatorChar,[StringComparison]::OrdinalIgnoreCase) -or
        $game.StartsWith($postGenericGame+[IO.Path]::DirectorySeparatorChar,[StringComparison]::OrdinalIgnoreCase)) {
      throw 'R6_PHASE2_SOURCE_MUST_BE_DISTINCT_FROM_HISTORICAL'
    }
    $postGenericSources=Join-Path $postGenericGame 'r6/scripts'
    if (!(Test-Path -LiteralPath $postGenericSources -PathType Container)) {
      throw 'R6_PHASE2_SOURCE_DIRECTORY_MISSING'
    }
    $phase2Files=@(Get-ChildItem -LiteralPath $postGenericSources -File -Recurse -Filter '*.reds')
    $postGenericCount=$phase2Files.Count
    if ($postGenericCount -lt 1000) { throw 'R6_PHASE2_SOURCE_INVENTORY_INCOMPLETE' }
    $hasGenericMarker=$false
    foreach ($file in $phase2Files) {
      if ($file.Attributes -band [IO.FileAttributes]::ReparsePoint) { throw 'R6_PHASE2_SYMLINK_REFUSED' }
      $relative=[IO.Path]::GetRelativePath($postGenericSources,$file.FullName)
      if ([IO.Path]::IsPathRooted($relative) -or $relative.StartsWith('..') -or
          $postGenericBefore.ContainsKey($relative)) { throw 'R6_PHASE2_SOURCE_PATH_INVALID' }
      $postGenericBefore[$relative]=(Get-FileHash -LiteralPath $file.FullName -Algorithm SHA256).Hash
      if (!$hasGenericMarker -and (Select-String -LiteralPath $file.FullName -SimpleMatch -Pattern 'GCO-GENERIC:' -Quiet -ErrorAction Stop)) {
        $hasGenericMarker=$true
      }
    }
    if (!$hasGenericMarker) { throw 'R6_PHASE2_POST_GENERIC_MARKERS_MISSING' }
    if (!(Test-Path -LiteralPath (Join-Path $postGenericSources 'G-RedRuntime') -PathType Container)) {
      throw 'R6_PHASE2_INSTALLED_FRAMEWORK_SOURCE_MISSING'
    }
    Write-Host ('R6 Phase 2 post-generic source admitted: files='+$postGenericCount+
      ' genericMarkersPresent=true originalSourceUntouched=true')
  }
  $liveSources=Join-Path $game 'r6/scripts'
  $liveCount=@(Get-ChildItem -LiteralPath $liveSources -Filter '*.reds' -Recurse -File).Count
  if ($liveCount -lt 100 -or $liveCount -ne [int]$plan.sourceCount) {
    throw 'R6 source inventory differs from the admitted original capture.'
  }
  $overlayRoot=Join-Path $preview 'overlay/r6/scripts'
  $overlay=@(Get-ChildItem -LiteralPath $overlayRoot -Recurse -File)
  if ($overlay.Count -ne [int]$plan.stagedFiles) {
    throw 'R6 staged overlay has unverified file count.'
  }
  foreach($file in $overlay) {
    if ($file.Extension -ne '.reds' -or
        ($file.Attributes -band [System.IO.FileAttributes]::ReparsePoint)) {
      throw 'R6 staged overlay contains an unsafe non-REDscript source.'
    }
  }
  # Fast executable and vanilla-cache probe before thousands of mod sources.
  # Explicit -outputCacheFile is required: SCC otherwise writes back into the source cache.
  $smokeScripts=Join-Path $work 'smoke/r6/scripts'
  $smokeCache=Join-Path $work 'smoke/r6/cache/final.redscripts'
  New-Item -ItemType Directory -Force -Path @($smokeScripts,(Split-Path $smokeCache -Parent)) | Out-Null
  Copy-Item -LiteralPath $baseFile.FullName -Destination $smokeCache -Force
  $smokeStatus=Invoke-HeadlessScc -Phase 'empty-source-smoke' -IsolatedRoot (Join-Path $work 'smoke') -CompilerLib $libs[0].FullName -TimeoutSeconds 35
  $smokeModded=$smokeCache+'.modded'
  $smokeOriginal=(Get-FileHash -LiteralPath $smokeCache -Algorithm SHA256).Hash
  if ($smokeOriginal -ne $baseSha -or !(Test-Path -LiteralPath $smokeModded -PathType Leaf) -or (Get-Item -LiteralPath $smokeModded).Length -lt 1048576) {
    throw 'R6 SCC smoke must preserve vanilla cache and emit a .modded compiled bundle.'
  }
  Write-Host ('PASS GRSP SCC 0.5.31 empty-source smoke: baselineUnchanged=true moddedBytes='+(Get-Item -LiteralPath $smokeModded).Length)
  # The user's REDmod configuration compiles with a separate effective cache.
  # Treat the private upload as a *candidate*, not an authenticated vanilla
  # cache. A previously compiled .modded bundle is never silently reused as
  # source: SCC's official REDSCRIPT_SIGNATURE guard decides admissibility.
  $effectiveEntries=@(Get-ChildItem -LiteralPath $effectiveRoot -Recurse -File)
  $effectiveFiles=@($effectiveEntries | Where-Object Name -CEQ 'final.redscripts')
  if ($effectiveFiles.Count -ne 1 -or $effectiveEntries.Count -ne 1) {
    throw ('R6_EFFECTIVE_CACHE_ARCHIVE_INVALID: expected exactly one final.redscripts file, found='+$effectiveEntries.Count+' correctNamed='+$effectiveFiles.Count)
  }
  $effectiveFile=$effectiveFiles[0]
  if ($effectiveFile.Attributes -band [System.IO.FileAttributes]::ReparsePoint) {
    throw 'R6_EFFECTIVE_CACHE_REPARSE_POINT_REFUSED'
  }
  if ($effectiveFile.Length -lt 1048576) {
    throw 'R6_EFFECTIVE_CACHE_TOO_SMALL: cannot use as game cache input'
  }
  $effectiveSha=(Get-FileHash -Algorithm SHA256 -LiteralPath $effectiveFile.FullName).Hash
  Write-Host ('R6 effective-cache candidate admitted for independent compilation experiment: bytes='+$effectiveFile.Length+' differsFromVanilla='+($effectiveSha -ne $baseSha)+' originalVanillaSmokePassed=true')
  # RED4ext plugins may expose Scripts/*.reds OR packed.reds/module.reds
  # immediately under their own plugin folder. Discover both from the real
  # private archive, not a hard-coded provider list.
  $cwAnchor=@(Get-ChildItem -LiteralPath $pluginsRoot -Directory -Recurse |
    Where-Object { $_.Name -eq 'Codeware' -and
      (Test-Path -LiteralPath (Join-Path $_.FullName 'Scripts') -PathType Container) })
  if ($cwAnchor.Count -ne 1) { throw 'R6_PLUGIN_ARCHIVE_ROOT_UNPROVEN' }
  $pluginInventory=$cwAnchor[0].Parent.FullName
  $providers=@{}
  foreach($dir in @(Get-ChildItem -LiteralPath $pluginInventory -Directory)) {
    $owner=$dir.Name
    if ($owner -notmatch '^[A-Za-z0-9_. -]{1,100}$') { throw 'R6_INVALID_SCRIPT_PROVIDER_NAME' }
    $files=@(Get-ChildItem -LiteralPath $dir.FullName -File -Recurse -Filter '*.reds')
    if ($files.Count -eq 0) { continue }
    if (@($files | Where-Object {
        $_.Attributes -band [System.IO.FileAttributes]::ReparsePoint }).Count -gt 0) {
      throw 'R6_UNSAFE_PLUGIN_SCRIPT_ENTRY'
    }
    $providers[$owner]=@($files | ForEach-Object {
      [pscustomobject]@{
        Relative=[System.IO.Path]::GetRelativePath($dir.FullName,$_.FullName)
        Source=$_.FullName
        SHA=(Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash
      }
    })
  }
  if (!$providers.ContainsKey('Codeware')) { throw 'R6_CODEWARE_SCRIPTS_MISSING_FROM_PLUGIN_ARCHIVE' }
  $totalProviderFiles=@($providers.Values | ForEach-Object { $_ }).Count
  Write-Host ('R6 native plugin script providers admitted: names='+(@($providers.Keys | Sort-Object) -join ','))
  Write-Host ('R6 native plugin script providers admitted: providers='+$providers.Count+
    ' files='+$totalProviderFiles+' identicalInBothStacks=true viaOfficialSccAdditionalScriptPath=true')
  # Reuse this exact isolated SCC implementation for the new generic callback
  # preview too; the original CName-membership preview contract is unchanged.
  $callbackOverlay=@()
  $callbackOverlayRoot=''
  if (![string]::IsNullOrWhiteSpace($CallbackPreviewRoot)) {
    $callbackBase=(Resolve-Path -LiteralPath $CallbackPreviewRoot -ErrorAction Stop).Path
    $callbackOverlayRoot=Join-Path $callbackBase 'r6/scripts'
    if (!(Test-Path -LiteralPath $callbackOverlayRoot -PathType Container)) {
      throw 'R6_CALLBACK_PREVIEW_MISSING'
    }
    $callbackOverlay=@(Get-ChildItem -LiteralPath $callbackOverlayRoot -File -Recurse)
    if ($callbackOverlay.Count -lt 1) { throw 'R6_CALLBACK_PREVIEW_EMPTY' }
    foreach($file in $callbackOverlay) {
      if ($file.Extension -ne '.reds' -or
          ($file.Attributes -band [System.IO.FileAttributes]::ReparsePoint)) {
        throw 'R6_CALLBACK_PREVIEW_UNSAFE_SOURCE'
      }
    }
  }
  # A distinct *framework-dependent* overlay contains the source-generated
  # shared-player consumers and the exact single GCO native bridge. Do not
  # reclassify these as legacy callback-only rewrites or weaken their gate.
  $frameworkOverlay=@()
  $frameworkOverlayRoot=''
  if (![string]::IsNullOrWhiteSpace($FrameworkPreviewRoot)) {
    if ([string]::IsNullOrWhiteSpace($FrameworkRoot)) {
      throw 'R6_FRAMEWORK_PREVIEW_REQUIRES_OWNER_FRAMEWORK'
    }
    $frameworkBase=(Resolve-Path -LiteralPath $FrameworkPreviewRoot -ErrorAction Stop).Path
    if ($frameworkBase.Equals($game,[StringComparison]::OrdinalIgnoreCase) -or
        $frameworkBase.StartsWith($game+[IO.Path]::DirectorySeparatorChar,
            [StringComparison]::OrdinalIgnoreCase)) {
      throw 'R6_FRAMEWORK_PREVIEW_MUST_BE_EXTERNAL'
    }
    $frameworkOverlayRoot=Join-Path $frameworkBase 'r6/scripts'
    if (!(Test-Path -LiteralPath $frameworkOverlayRoot -PathType Container)) {
      throw 'R6_FRAMEWORK_PREVIEW_SCRIPTS_MISSING'
    }
    $frameworkOverlay=@(Get-ChildItem -LiteralPath $frameworkOverlayRoot -File -Recurse)
    if ($frameworkOverlay.Count -lt 1) { throw 'R6_FRAMEWORK_PREVIEW_EMPTY' }
    foreach($file in $frameworkOverlay) {
      if ($file.Extension -ne '.reds' -or
          ($file.Attributes -band [IO.FileAttributes]::ReparsePoint)) {
        throw 'R6_FRAMEWORK_PREVIEW_UNSAFE_SOURCE'
      }
    }
  }
  # Owner-owned G-REDruntime files are compiled in a separate isolated
  # workspace, never inserted into original private source or previews.
  $frameworkFiles=@()
  $frameworkScripts=''
  if (![string]::IsNullOrWhiteSpace($FrameworkRoot)) {
    $frameworkGame=(Resolve-Path -LiteralPath $FrameworkRoot -ErrorAction Stop).Path
    if ($frameworkGame.Equals($game,[StringComparison]::OrdinalIgnoreCase) -or
        $frameworkGame.StartsWith($game+[IO.Path]::DirectorySeparatorChar,[StringComparison]::OrdinalIgnoreCase)) {
      throw 'R6_FRAMEWORK_SOURCE_MUST_BE_EXTERNAL_TO_GAME'
    }
    $frameworkScripts=Join-Path $frameworkGame 'r6/scripts/G-RedRuntime'
    if (!(Test-Path -LiteralPath $frameworkScripts -PathType Container)) {
      throw 'R6_OWNER_FRAMEWORK_SOURCE_MISSING'
    }
    $frameworkFiles=@(Get-ChildItem -LiteralPath $frameworkScripts -Recurse -File -Filter '*.reds')
    if ($frameworkFiles.Count -lt 1) { throw 'R6_OWNER_FRAMEWORK_REDS_MISSING' }
    foreach ($file in $frameworkFiles) {
      $relative=[IO.Path]::GetRelativePath($frameworkScripts,$file.FullName)
      if ([IO.Path]::IsPathRooted($relative) -or $relative.StartsWith('..') -or
          $relative.Contains(':') -or
          ($file.Attributes -band [IO.FileAttributes]::ReparsePoint)) {
        throw 'R6_OWNER_FRAMEWORK_SOURCE_UNSAFE'
      }
    }
    Write-Host ('R6 authoritative framework compiler input: files='+$frameworkFiles.Count)
  }
  $outputs=@()
  $scenarios=@('untouched','optimized-preview')
  if ($callbackOverlay.Count -gt 0) { $scenarios+=@('optimized-callback-preview') }
  if ($frameworkFiles.Count -gt 0) { $scenarios+=@('framework-untouched') }
  if ($frameworkFiles.Count -gt 0 -and $callbackOverlay.Count -gt 0) {
    $scenarios+=@('framework-all-generics-preview')
  }
  # Validate the exact post-generic source independently of the sealed
  # pre-generic capture/preview. Future semantics will extend this lane.
  if ($postGenericCount -gt 0) { $scenarios+=@('phase2-postgeneric-stock') }
  foreach($scenario in $scenarios) {
    $isPhase2=($scenario -eq 'phase2-postgeneric-stock')
    $selectedScripts=if ($isPhase2) { $postGenericSources } else { $liveSources }
    $selectedCount=if ($isPhase2) { $postGenericCount } else { $liveCount }
    $root=Join-Path $work $scenario
    $scripts=Join-Path $root 'r6/scripts'
    $cache=Join-Path $root 'r6/cache/final.redscripts'
    New-Item -ItemType Directory -Path $scripts -Force | Out-Null
    New-Item -ItemType Directory -Path (Split-Path $cache -Parent) -Force | Out-Null
    foreach($item in @(Get-ChildItem -LiteralPath $selectedScripts -Force)) {
      Copy-Item -LiteralPath $item.FullName -Destination $scripts -Recurse -Force
    }
    $pluginStage=Join-Path $root 'red4ext/plugins'
    foreach($owner in @($providers.Keys | Sort-Object)) {
      $dest=Join-Path $pluginStage $owner
      if (Test-Path -LiteralPath $dest) { throw 'R6_PLUGIN_STAGE_PATH_COLLISION' }
      New-Item -ItemType Directory -Force -Path $dest | Out-Null
      foreach($file in $providers[$owner]) {
        $target=Join-Path $dest $file.Relative
        New-Item -ItemType Directory -Force -Path (Split-Path $target -Parent) | Out-Null
        Copy-Item -LiteralPath $file.Source -Destination $target -Force
        if ((Get-FileHash -LiteralPath $target -Algorithm SHA256).Hash -ne $file.SHA) {
          throw 'R6_PLUGIN_STAGE_INTEGRITY_MISMATCH'
        }
      }
    }
    Copy-Item -LiteralPath $effectiveFile.FullName -Destination $cache -Force
    if ((Get-FileHash -LiteralPath $cache -Algorithm SHA256).Hash -ne $effectiveSha) {
      throw 'R6 source compilation input cache differs from user supplied effective cache.'
    }
    if ($scenario -eq 'optimized-preview' -or $scenario -eq 'framework-all-generics-preview') {
      foreach($file in $overlay) {
        $relative=[System.IO.Path]::GetRelativePath($overlayRoot,$file.FullName)
        if ([System.IO.Path]::IsPathRooted($relative) -or
            $relative.StartsWith('..') -or $relative.Contains(':')) {
          throw 'R6 staged source escapes overlay ownership.'
        }
        $target=Join-Path $scripts $relative
        if (!(Test-Path -LiteralPath $target -PathType Leaf)) {
          throw 'R6 staged source cannot replace a missing original file.'
        }
        Copy-Item -LiteralPath $file.FullName -Destination $target -Force
      }
    }
    # Only already-installed author source files may be rewritten by the
    # nine default generic rules. No generated location subscriber or optional
    # native bridge is accepted as a new source file.
    if ($scenario -eq 'optimized-callback-preview' -or $scenario -eq 'framework-all-generics-preview') {
      $selected=if ($scenario -eq 'framework-all-generics-preview' -and
                     $frameworkOverlay.Count -gt 0) { $frameworkOverlay } else { $callbackOverlay }
      $selectedRoot=if ($scenario -eq 'framework-all-generics-preview' -and
                         $frameworkOverlay.Count -gt 0) { $frameworkOverlayRoot } else { $callbackOverlayRoot }
      foreach($file in $selected) {
        $relative=[System.IO.Path]::GetRelativePath($selectedRoot,$file.FullName)
        if ([System.IO.Path]::IsPathRooted($relative) -or
            $relative.StartsWith('..') -or $relative.Contains(':')) {
          throw 'R6_CALLBACK_PREVIEW_PATH_ESCAPE'
        }
        $target=Join-Path $scripts $relative
        if (!(Test-Path -LiteralPath $target -PathType Leaf)) {
          throw 'R6_CALLBACK_PREVIEW_UNOWNED_SOURCE'
        }
        $text=Get-Content -LiteralPath $file.FullName -Raw
        if ($file.Name -match '^GCO_NativeLocation_[0-9]{9,10}\.reds$' -or
            $text.Contains('GCO-GENERIC:NATIVE_PLAYER_LOCATION_PREDICATE_V1',[StringComparison]::Ordinal) -or
            $text.Contains('GCO-GENERIC:NATIVE_PLAYER_LOCATION_SUBSCRIBER_V1',[StringComparison]::Ordinal) -or
            $text.Contains('GCO-GENERIC:LOCATION_DEPENDENCY_FAR_V1:',[StringComparison]::Ordinal)) {
          throw 'R6_RETIRED_NATIVE_LOCATION_OUTPUT_REFUSED'
        }
        Copy-Item -LiteralPath $file.FullName -Destination $target -Force
      }
    }
    $addedFrameworkFiles=0
    if ($scenario -eq 'framework-untouched' -or $scenario -eq 'framework-all-generics-preview') {
      foreach($file in $frameworkFiles) {
        $relative=[IO.Path]::GetRelativePath($frameworkScripts,$file.FullName)
        $target=Join-Path (Join-Path $scripts 'G-RedRuntime') $relative
        if (Test-Path -LiteralPath $target -PathType Leaf) {
          if ((Get-FileHash -LiteralPath $target -Algorithm SHA256).Hash -ne
              (Get-FileHash -LiteralPath $file.FullName -Algorithm SHA256).Hash) {
            throw 'R6_FRAMEWORK_SOURCE_COLLIDES_WITH_INSTALLED_GAME'
          }
          continue
        }
        New-Item -ItemType Directory -Force -Path (Split-Path $target -Parent) | Out-Null
        Copy-Item -LiteralPath $file.FullName -Destination $target -ErrorAction Stop
        if ((Get-FileHash -LiteralPath $target -Algorithm SHA256).Hash -ne
            (Get-FileHash -LiteralPath $file.FullName -Algorithm SHA256).Hash) {
          throw 'R6_FRAMEWORK_SOURCE_COPY_CORRUPTED'
        }
        $addedFrameworkFiles++
      }
    }
    $actualCount=@(Get-ChildItem -LiteralPath $scripts -Recurse -File -Filter '*.reds').Count
    if ($actualCount -ne ($selectedCount+$addedFrameworkFiles)) {
      throw 'R6 isolated base script source count changed.'
    }
    if (@(Get-ChildItem -LiteralPath $pluginStage -Recurse -File -Filter '*.reds').Count -ne $totalProviderFiles) {
      throw 'R6 external plugin source inventory is incomplete.'
    }
    # Same direct native API proven by empty-source smoke, now complete
    # installed source tree. Explicit output path protects the vanilla cache.
    $privateLog=Join-Path $work ($scenario + '-scc.log')
    $fullStatus=Invoke-HeadlessScc -Phase $scenario -IsolatedRoot $root -CompilerLib $libs[0].FullName -TimeoutSeconds 240
    $compiled=$cache+'.modded'
    if (!(Test-Path -LiteralPath $compiled -PathType Leaf)) {
      throw ('R6_SCC_OUTPUT_MISSING: phase='+$scenario+' expected .modded output')
    }
    $result=Get-Item -LiteralPath $compiled
    $sha=(Get-FileHash -LiteralPath $compiled -Algorithm SHA256).Hash
    if ($result.Length -lt 1048576 -or $sha -eq $effectiveSha -or (Get-FileHash -LiteralPath $cache -Algorithm SHA256).Hash -ne $effectiveSha) {
      throw ('R6_SCC_OUTPUT_UNPROVEN: phase='+$scenario+' invalid compiled cache or mutated vanilla input')
    }
    $outputs+=@([pscustomobject]@{ Scenario=$scenario; Bytes=$result.Length; Sha256=$sha })
  }
  $expectedScenarios=if($callbackOverlay.Count -gt 0){3}else{2}
  if ($frameworkFiles.Count -gt 0) { $expectedScenarios++ }
  if ($frameworkFiles.Count -gt 0 -and $callbackOverlay.Count -gt 0) { $expectedScenarios++ }
  if ($postGenericCount -gt 0) { $expectedScenarios++ }
  if ($outputs.Count -ne $expectedScenarios -or
      $outputs[0].Sha256 -eq $outputs[1].Sha256) {
    throw 'R6 compiled baseline and optimized-preview caches are indistinguishable.'
  }
  if ($callbackOverlay.Count -gt 0 -and
      $outputs[0].Sha256 -eq $outputs[2].Sha256) {
    throw 'R6 compiled baseline and callback-preview caches are indistinguishable.'
  }
  if ($frameworkFiles.Count -gt 0) {
    $frameworkOnly=@($outputs | Where-Object { $_.Scenario -eq 'framework-untouched' })
    if ($frameworkOnly.Count -ne 1 -or
        $outputs[0].Sha256 -eq $frameworkOnly[0].Sha256) {
      throw 'R6_FRAMEWORK_COMPILE_OUTPUT_INDISTINGUISHABLE_FROM_BASE'
    }
  }
  if ($frameworkFiles.Count -gt 0 -and $callbackOverlay.Count -gt 0) {
    # The fifth scenario combines ALL currently staged generic passes with
    # authoritative framework source. Refuse empty/no-op integration output.
    if ($outputs[$outputs.Count-1].Scenario -ne 'framework-all-generics-preview') {
      throw 'R6_FRAMEWORK_COMBINED_SCENARIO_MISSING'
    }
    $combinedHash=$outputs[$outputs.Count-1].Sha256
    foreach($earlier in $outputs[0..($outputs.Count-2)]) {
      if ($combinedHash -eq $earlier.Sha256) {
        throw 'R6_FRAMEWORK_COMBINED_GENERIC_OUTPUT_INDISTINGUISHABLE'
      }
    }
  }
  if ($postGenericCount -gt 0) {
    $phase2Compiled=@($outputs | Where-Object { $_.Scenario -eq 'phase2-postgeneric-stock' })
    if ($phase2Compiled.Count -ne 1) { throw 'R6_PHASE2_COMPILED_OUTPUT_MISSING' }
    $afterFiles=@(Get-ChildItem -LiteralPath $postGenericSources -File -Recurse -Filter '*.reds')
    if ($afterFiles.Count -ne $postGenericCount) { throw 'R6_PHASE2_SOURCE_COUNT_MUTATED' }
    foreach ($file in $afterFiles) {
      $relative=[IO.Path]::GetRelativePath($postGenericSources,$file.FullName)
      if (!$postGenericBefore.ContainsKey($relative) -or
          (Get-FileHash -LiteralPath $file.FullName -Algorithm SHA256).Hash -ne $postGenericBefore[$relative]) {
        throw 'R6_PHASE2_SOURCE_BYTES_MUTATED'
      }
    }
    Write-Host ('PASS REDscript Phase 2 separate post-generic baseline SCC: files='+
      $postGenericCount+' compiledBytes='+$phase2Compiled[0].Bytes+
      ' preservedGenericSource=true userSourceUntouched=true semanticRulesPending=true')
  }
  $audit=Join-Path $work 'verify-source.json'
  $auditLog=Join-Path $work 'verify-source.log'
  $expectedSha=[string]$plan.sourceStackSha256
  & dotnet $dll source-audit --game $game --out $audit --expected-source-sha256 $expectedSha *> $auditLog
  if ($LASTEXITCODE -ne 0 -or !(Test-Path -LiteralPath $audit -PathType Leaf)) {
    throw 'R6 original source changed during isolated compiler validation.'
  }
  Write-Host ('PASS GRSP full actual SCC 0.5.31 compiles: sourceFiles=' +
      $liveCount + ' optimizedFiles=' + $overlay.Count +
      ' untouchedCacheBytes=' + $outputs[0].Bytes +
      ' optimizedCacheBytes=' + $outputs[1].Bytes +
      ' callbackPreviewFiles=' + $callbackOverlay.Count +
       ' frameworkPreviewFiles=' + $frameworkOverlay.Count +
      ' ownerFrameworkSourceFiles=' + $frameworkFiles.Count +
      ' frameworkCompilationOnly=true' +
      ' compiledOutputsDistinct=true originalSourcesUntouched=true ' +
      'inputCache=USER_EFFECTIVE_CANDIDATE_NOT_VANILLA ' +
      'gameplayValidated=false deployable=false')
}
finally {
  if (Test-Path -LiteralPath $work) {
    Remove-Item -LiteralPath $work -Recurse -Force -ErrorAction Stop
  }
}
$global:LASTEXITCODE=0
