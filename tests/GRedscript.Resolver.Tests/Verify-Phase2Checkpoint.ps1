# Phase 2 checkpoint, logically after the generic pass: run one official SCC
# compile on the already-generic source, never the historical capture replay.
param(
 [Parameter(Mandatory=$true)][string]$PostGenericRoot,
 [Parameter(Mandatory=$true)][string]$CompilerRoot,
 [Parameter(Mandatory=$true)][string]$ResolverDll,
 [string]$SemanticOverlayRoot = ''
)
$ErrorActionPreference='Stop'
Set-StrictMode -Version Latest
$game=(Resolve-Path -LiteralPath $PostGenericRoot).Path
$inputRoot=Join-Path $game 'r6/scripts'
$compiler=(Resolve-Path -LiteralPath $CompilerRoot).Path
$dll=(Resolve-Path -LiteralPath $ResolverDll).Path
$files=@(Get-ChildItem -LiteralPath $inputRoot -File -Recurse -Filter '*.reds')
if ($files.Count -lt 2000 -or !(Test-Path -LiteralPath (Join-Path $inputRoot 'G-RedRuntime') -PathType Container)) {
  throw 'PHASE2_POST_GENERIC_INPUT_OR_FRAMEWORK_INCOMPLETE'
}
$hashes=@{}
$generic=$false
foreach ($file in $files) {
  if ($file.Attributes -band [IO.FileAttributes]::ReparsePoint) { throw 'PHASE2_LINKED_SOURCE_REFUSED' }
  $rel=[IO.Path]::GetRelativePath($inputRoot,$file.FullName)
  if ($rel.StartsWith('..') -or $hashes.ContainsKey($rel)) { throw 'PHASE2_DUPLICATE_OR_UNSAFE_PATH' }
  $hashes[$rel]=(Get-FileHash -LiteralPath $file.FullName -Algorithm SHA256).Hash
  if (!$generic -and (Select-String -LiteralPath $file.FullName -Pattern 'GCO-GENERIC:' -SimpleMatch -Quiet)) {
    $generic=$true
  }
}
if (!$generic) { throw 'PHASE2_SOURCE_NOT_POST_GENERIC' }
$working=Join-Path $env:RUNNER_TEMP ('gco-redscript-phase2-'+[guid]::NewGuid().ToString('N'))
$workspace=Join-Path $working 'workspace'
$r6=Join-Path $workspace 'r6'
$targetScripts=Join-Path $r6 'scripts'
$cache=Join-Path $r6 'cache/final.redscripts'
$report=Join-Path $working 'result.json'
$privateLog=Join-Path $working 'scc.log'
New-Item -ItemType Directory -Force -Path $working | Out-Null
try {
  foreach ($pair in @(
    @('ssc.zip','native'),
    @('modded/final.zip','effective'),
    @('plugins.zip','plugins'))) {
    $zip=Join-Path $compiler $pair[0]
    $folder=Join-Path $working $pair[1]
    if (!(Test-Path -LiteralPath $zip -PathType Leaf)) { throw 'PHASE2_REQUIRED_COMPILER_FIXTURE_MISSING' }
    Expand-Archive -LiteralPath $zip -DestinationPath $folder
  }
  $libs=@(Get-ChildItem -LiteralPath (Join-Path $working 'native') -File -Recurse -Filter 'scc_lib.dll')
  $effective=@(Get-ChildItem -LiteralPath (Join-Path $working 'effective') -File -Recurse |
    Where-Object Name -CEQ 'final.redscripts')
  if ($libs.Count -ne 1 -or $effective.Count -ne 1) { throw 'PHASE2_COMPILER_OR_CACHE_AMBIGUOUS' }
  if ((Get-FileHash -LiteralPath $libs[0].FullName -Algorithm SHA256).Hash -ne
      '49EB60A3613C990CD13B6136E827A74F9F64AD68E2F911D8C93E4E3D3F53C100') {
    throw 'PHASE2_UNEXPECTED_SCC_VERSION'
  }
  New-Item -ItemType Directory -Force -Path @($targetScripts,(Split-Path $cache -Parent)) | Out-Null
  foreach($item in @(Get-ChildItem -LiteralPath $inputRoot -Force)) {
    Copy-Item -LiteralPath $item.FullName -Destination $targetScripts -Recurse -Force
  }
  if ($SemanticOverlayRoot) {
    $overlay=(Resolve-Path -LiteralPath $SemanticOverlayRoot).Path
    $overlayScripts=Join-Path $overlay 'r6/scripts'
    if (!(Test-Path -LiteralPath $overlayScripts -PathType Container) -or
        $overlay.Equals($game,[StringComparison]::OrdinalIgnoreCase)) {
      throw 'PHASE2_SEMANTIC_OVERLAY_UNSAFE'
    }
    $overlayFiles=@(Get-ChildItem -LiteralPath $overlayScripts -File -Recurse)
    foreach ($file in $overlayFiles) {
      if ($file.Extension -ne '.reds' -or
          ($file.Attributes -band [IO.FileAttributes]::ReparsePoint)) {
        throw 'PHASE2_SEMANTIC_OUTPUT_NOT_REDSCRIPT'
      }
      $rel=[IO.Path]::GetRelativePath($overlayScripts,$file.FullName)
      if ($rel.StartsWith('..')) { throw 'PHASE2_SEMANTIC_OUTPUT_ESCAPES_OWNER' }
      $target=Join-Path $targetScripts $rel
      New-Item -ItemType Directory -Force -Path (Split-Path $target -Parent) | Out-Null
      Copy-Item -LiteralPath $file.FullName -Destination $target -Force
    }
  } else { $overlayFiles=@() }
  Copy-Item -LiteralPath $effective[0].FullName -Destination $cache
  $cacheSha=(Get-FileHash -LiteralPath $cache -Algorithm SHA256).Hash
  $cw=@(Get-ChildItem -LiteralPath (Join-Path $working 'plugins') -Directory -Recurse |
    Where-Object { $_.Name -eq 'Codeware' -and
      (Test-Path -LiteralPath (Join-Path $_.FullName 'Scripts') -PathType Container) })
  if ($cw.Count -ne 1) { throw 'PHASE2_CODEWARE_NATIVE_PLUGIN_MISSING' }
  $plugins=Join-Path $workspace 'red4ext/plugins'
  $providerCount=0
  foreach($owner in @(Get-ChildItem -LiteralPath $cw[0].Parent.FullName -Directory)) {
    $scripts=@(Get-ChildItem -LiteralPath $owner.FullName -Recurse -File -Filter '*.reds')
    if ($scripts.Count -eq 0) { continue }
    $providerCount++
    foreach($file in $scripts) {
      if ($file.Attributes -band [IO.FileAttributes]::ReparsePoint) { throw 'PHASE2_PLUGIN_LINK_REFUSED' }
      $rel=[IO.Path]::GetRelativePath($owner.FullName,$file.FullName)
      if ($rel.StartsWith('..')) { throw 'PHASE2_PLUGIN_PATH_UNSAFE' }
      $target=Join-Path (Join-Path $plugins $owner.Name) $rel
      New-Item -ItemType Directory -Force -Path (Split-Path $target -Parent) | Out-Null
      Copy-Item -LiteralPath $file.FullName -Destination $target
    }
  }
  if ($providerCount -lt 5) { throw 'PHASE2_EXTERNAL_ABI_INCOMPLETE' }
  Set-Content -LiteralPath (Join-Path $workspace '.grsp-r6-workspace') -Value 'GRSP_R6_ISOLATED_WORKSPACE' -NoNewline
  New-Item -ItemType Directory -Force -Path (Join-Path $r6 'logs') | Out-Null
  $cmd=@($dll,'scc-headless','--workspace',$workspace,'--r6',$r6,
    '--cache',$cache,'--compiled',($cache+'.modded'),
    '--scc-native',$libs[0].FullName,'--out',$report,'--log',$privateLog,
    '--external-scripts',$plugins)
  & dotnet @cmd *> (Join-Path $working 'process-output.txt')
  $exit=$LASTEXITCODE
  if (!(Test-Path -LiteralPath $report -PathType Leaf)) { throw 'PHASE2_OFFICIAL_SCC_REPORT_MISSING' }
  $result=Get-Content -LiteralPath $report -Raw | ConvertFrom-Json
  if ($exit -ne 0 -or $result.schemaVersion -ne 'grsp.scc.headless.v1' -or
      $result.success -ne $true -or $result.guiErrorPopupDisabled -ne $true -or
      $result.originalCacheUnchanged -ne $true -or
      $result.generatedOutputPresent -ne $true -or
      [long]$result.generatedOutputBytes -lt 1048576 -or
      [int]$result.externalScriptPaths -lt $providerCount -or
      (Get-FileHash -LiteralPath $cache -Algorithm SHA256).Hash -ne $cacheSha) {
    $codes=if($result.errorCodeCounts) {
      @($result.errorCodeCounts.PSObject.Properties | ForEach-Object { $_.Name }) -join ','
    } else { 'NO_STRUCTURED_CODES' }
    # Keep private compiler inputs/logs off published artifacts while
    # giving actionable, path-redacted error categories in CI failures.
    foreach ($logPath in @($privateLog, (Join-Path $working 'process-output.txt'))) {
      if (!(Test-Path -LiteralPath $logPath -PathType Leaf)) { continue }
      $lines=@(Get-Content -LiteralPath $logPath -ErrorAction SilentlyContinue |
        Where-Object { $_ -match '(?i)\b(error|unknown|undefined|type mismatch|not found|failed to compile|unexpected token)\b' } |
        Select-Object -Last 8)
      foreach ($line in $lines) {
        $safe=[string]$line
        $safe=$safe -replace '(?i)[A-Za-z]:\\\\[^:;\r\n]+','[PRIVATE_PATH]'
        $safe=$safe -replace '(?i)r6[/\\\\]scripts[/\\\\][^:;\r\n]+','[PRIVATE_SOURCE]'
        if ($safe.Length -gt 270) { $safe=$safe.Substring(0,270) }
        Write-Host ('PHASE2_SCC_DIAGNOSTIC: '+$safe)
      }
    }
    throw ('PHASE2_OFFICIAL_SCC_FAILED: exit='+$exit+' errorCodes='+$codes)
  }
  $after=@(Get-ChildItem -LiteralPath $inputRoot -File -Recurse -Filter '*.reds')
  if ($after.Count -ne $files.Count) { throw 'PHASE2_SOURCE_COUNT_MUTATED' }
  foreach($file in $after) {
    $rel=[IO.Path]::GetRelativePath($inputRoot,$file.FullName)
    if (!$hashes.ContainsKey($rel) -or
        $hashes[$rel] -ne (Get-FileHash -LiteralPath $file.FullName -Algorithm SHA256).Hash) {
      throw 'PHASE2_SOURCE_BYTES_MUTATED'
    }
  }
  Write-Host ('PASS REDscript Phase 2 CHECKPOINT: postGenericFiles='+$files.Count+
    ' semanticFiles='+$overlayFiles.Count+' SCC=0.5.31 compiledBytes='+
    [long]$result.generatedOutputBytes+' sourceUntouched=true genericFirst=true')
}
finally {
  if (Test-Path -LiteralPath $working) {
    Remove-Item -LiteralPath $working -Recurse -Force -ErrorAction SilentlyContinue
  }
}
