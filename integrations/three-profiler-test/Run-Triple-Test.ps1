param([string]$GameRoot)
$ErrorActionPreference='Stop'
Set-StrictMode -Version Latest

$root=$PSScriptRoot
$session=Join-Path $root ('RESULTS/Triple-' + (Get-Date -Format 'yyyyMMdd-HHmmss'))
New-Item -ItemType Directory -Force $session | Out-Null
$log=Join-Path $session 'test.log'
$installedCet=$false
$installedGrsp=$false
$collectedCet=$false
$collectedGrsp=$false
$presentMon=$null
$gameExe=$null
function Log([string]$Message) {
  $line=(Get-Date -Format 'o') + ' ' + $Message
  Write-Host $line
  Add-Content -LiteralPath $log -Value $line
}
function Verify([string]$Path,[string]$Sha) {
  if (!(Test-Path $Path -PathType Leaf)) { throw "Missing verified payload $Path" }
  $actual=(Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash.ToLowerInvariant()
  if ($actual -ne $Sha.ToLowerInvariant()) { throw "Payload changed: $Path" }
}
function Backend([string]$Which,[string]$Action) {
  $exe=if ($Which -eq 'CET') { Join-Path $root 'components/cet/G-CET-Runtime-Profiler.exe' }
       else { Join-Path $root 'components/grsp/G-REDscript-Profiler.exe' }
  Log "$Which --$Action"
  $output=@(& $exe ('--'+$Action) '--game' $GameRoot '--json' 2>&1)
  $code=$LASTEXITCODE
  $payload=$output -join [Environment]::NewLine
  if ($code -ne 0) { throw "$Which --$Action failed (exit $code): $payload" }
  $response=$payload | ConvertFrom-Json
  if ($null -eq $response) { throw "$Which --$Action gave no status JSON" }
  if ($response.PSObject.Properties.Name -contains 'ok') {
    if ($response.ok -ne $true) { throw "$Which --$Action reported failure: $payload" }
  }
  return $response
}
function RestoreOwned {
  if (Get-Process -Name Cyberpunk2077 -ErrorAction SilentlyContinue) {
    Log 'RESTORE BLOCKED: Cyberpunk is still running. Close it, then use the standalone managers to restore.'
    return $false
  }
  $okay=$true
  if ($installedGrsp) {
    try { $result=Backend 'GRSP' 'restore'; Log 'GRSP restored through owner'; $script:installedGrsp=$false }
    catch { $okay=$false; Log ('GRSP RESTORE FAILED: ' + $_) }
  }
  if ($installedCet) {
    try { $result=Backend 'CET' 'restore'; Log 'CET restored through owner'; $script:installedCet=$false }
    catch { $okay=$false; Log ('CET RESTORE FAILED: ' + $_) }
  }
  return $okay
}
try {
  $rt=Join-Path $root 'runtime'
  if (!(Test-Path (Join-Path $rt 'dotnet.exe'))) { throw 'Private .NET10 runtime missing' }
  $env:DOTNET_ROOT_X64=$rt
  $env:DOTNET_ROOT=$rt
  $env:DOTNET_MULTILEVEL_LOOKUP='0'
  $data=Get-Content (Join-Path $root 'components/TEST-PROVENANCE.json') -Raw | ConvertFrom-Json
  Verify (Join-Path $root 'components/cet/payload/cyber_engine_tweaks.PROFILER.dll') $data.cet.nativeSha256
  Verify (Join-Path $root 'components/presentmon/PresentMon.exe') $data.presentMon.executableSha256
  $grNative=Join-Path $root 'components/grsp/payload/G-REDscript-Profiler.dll'
  if (!(Test-Path $grNative)) { throw 'Verified GRSP source payload missing' }

  if ([string]::IsNullOrWhiteSpace($GameRoot)) {
    Add-Type -AssemblyName System.Windows.Forms
    $dialog=New-Object System.Windows.Forms.FolderBrowserDialog
    $dialog.Description='Select Cyberpunk 2077 game root (one folder for all three profilers)'
    if ($dialog.ShowDialog() -ne [System.Windows.Forms.DialogResult]::OK) {
      throw 'No game folder selected; no game files changed'
    }
    $GameRoot=$dialog.SelectedPath
  }
  $GameRoot=[IO.Path]::GetFullPath($GameRoot)
  $gameExe=Join-Path $GameRoot 'bin/x64/Cyberpunk2077.exe'
  if (!(Test-Path $gameExe -PathType Leaf)) { throw 'Selected folder is not a Cyberpunk 2077 game root' }
  if (Get-Process -Name Cyberpunk2077 -ErrorAction SilentlyContinue) { throw 'Close Cyberpunk BEFORE installing profiler DLLs' }
  Log "Game root: $GameRoot"
  $cetStatus=Backend 'CET' 'status'
  $grStatus=Backend 'GRSP' 'status'
  $grState=[string]$grStatus.result.State
  if ($cetStatus.CetState -ne 'OFFICIAL' -or $cetStatus.Managed -or $cetStatus.CaptureReadyForCollection) {
    throw "CET is not an untouched supported installation; current state $($cetStatus.CetState), managed $($cetStatus.Managed). No files changed."
  }
  if ($grState -ne 'NOT_INSTALLED' -or $grStatus.result.LegacyDllPresent) {
    throw "GRSP live state is $grState. Must be NOT_INSTALLED. No files changed."
  }
  Log 'Both standalone profilers passed non-mutating preflight'
  $null=Backend 'CET' 'install'
  $installedCet=$true
  Log 'CET installed by its authoritative transaction'
  $null=Backend 'GRSP' 'install'
  $installedGrsp=$true
  Log 'GRSP installed by its authoritative transaction'

  # Validate actual installed state. Never overwrite existing user F11 binds.
  $cetStatus=Backend 'CET' 'status'
  $grStatus=Backend 'GRSP' 'status'
  if ($cetStatus.CetState -ne 'PROFILER_ACTIVE' -or !$cetStatus.Managed -or
      $grStatus.result.State -ne 'INSTALLED_CURRENT') { throw 'Post-install ownership/identity proof failed' }
  $bindings=Join-Path $GameRoot 'bin/x64/plugins/cyber_engine_tweaks/bindings.json'
  if (!(Test-Path $bindings)) { throw 'CET bindings missing: cannot establish shared F11' }
  $keys=Get-Content $bindings -Raw | ConvertFrom-Json
  $key=$keys.CETProfilerControls.CETProfiler_Toggle
  if ([string]$key -ne '34339947158700032') {
    throw "CET key is not F11 ($key). No binding was overwritten. Restore and set your binding to F11 manually."
  }
  Log 'CET F11 confirmed; GRSP native uses physical F11; PresentMon will register F11'
  Log 'IMPORTANT: Win32 RegisterHotKey may interfere with CET/GRSP F11; verify all 3 captures'

  # Give PresentMon its own unique session/CSV. No system runtime installation.
  $pmExe=Join-Path $root 'components/presentmon/PresentMon.exe'
  $pmBase=Join-Path $session 'PresentMon.csv'
  $q=[char]34
  $arguments='--process_name Cyberpunk2077.exe --hotkey F11 --session_name GTotalTriple --output_file ' + $q + $pmBase + $q + ' --restart_as_admin'
  Log "Launching PresentMon elevated with args: $arguments"
  $presentMon=Start-Process -FilePath $pmExe -ArgumentList $arguments -Verb RunAs -WorkingDirectory (Split-Path $pmExe) -PassThru
  Start-Sleep -Seconds 2
  if ($presentMon.HasExited) { throw "PresentMon exited early: $($presentMon.ExitCode)" }
  Log 'PresentMon started. Opening Cyberpunk; press F11 once to START, again to STOP.'
  Start-Process -FilePath $gameExe -WorkingDirectory (Split-Path $gameExe) | Out-Null
  Write-Host ''
  Write-Host 'In-game: F11 START, F11 STOP. Then CLOSE Cyberpunk completely.' -ForegroundColor Cyan
  Read-Host 'Press Enter ONLY AFTER the game is closed and the capture is complete' | Out-Null
  if (Get-Process -Name Cyberpunk2077 -ErrorAction SilentlyContinue) { throw 'Cyberpunk still running; cannot collect/restore yet' }

  # All 3 captures are required. Read-only status proof precedes collection.
  $cetStatus=Backend 'CET' 'status'
  $grStatus=Backend 'GRSP' 'status'
  if (!$cetStatus.CaptureReadyForCollection) { throw 'CET capture not completed; do not claim a three-way capture' }
  if ([int]$grStatus.result.CompletedCaptureCount -le 0) { throw 'GRSP capture not completed; do not claim a three-way capture' }
  $pms=@(Get-ChildItem $session -File -Filter '*.csv' | Where-Object { $_.Length -gt 500 })
  if ($pms.Count -eq 0) { throw 'PresentMon produced no complete frame CSV. F11 may have been intercepted.' }
  $found=@()
  foreach ($file in $pms) {
    $sample=@(Import-Csv -LiteralPath $file.FullName | Select-Object -First 50)
    if ($sample.Count -gt 0 -and ($sample[0].PSObject.Properties.Name -contains 'MsBetweenPresents') -and ($sample[0].PSObject.Properties.Name -contains 'Application')) {
      $found+=,$file
    }
  }
  if ($found.Count -eq 0) { throw 'PresentMon CSV does not match expected frame schema' }
  Log "PresentMon evidence: $($found.Count) CSV(s). Source retained in session."

  $cetArchive=Backend 'CET' 'collect'
  $collectedCet=$true
  $grArchive=Backend 'GRSP' 'collect'
  $collectedGrsp=$true
  Log "CET archive: $($cetArchive.destination)"
  Log "GRSP archive: $($grArchive.result.path)"
  if (!$cetArchive.destination -or !$grArchive.result.path) { throw 'One profiler did not identify its archived capture' }
  $metadata=@{ captureUtc=(Get-Date).ToUniversalTime().ToString('o'); gameRoot=$GameRoot; cetArchive=$cetArchive.destination; grspArchive=$grArchive.result.path; presentMonCsv=$found.FullName; validation='PENDING_CROSS_CLOCK_GAMEPLAY_VALIDATION'; note='No timings were added together' }
  $metadata | ConvertTo-Json -Depth 4 | Set-Content (Join-Path $session 'session.json') -Encoding UTF8
  Log 'PASS all three present in current session; cross-capture timing equivalence remains UNVERIFIED'
}
catch {
  Log ('FAIL: ' + $_)
}
finally {
  if ($null -ne $presentMon -and !$presentMon.HasExited) {
    try { $presentMon.Kill(); Log 'PresentMon controller terminated after CSV inspection' }
    catch { Log ('PresentMon termination requires manual review: ' + $_) }
  }
  $recovered=RestoreOwned
  if ($recovered) { Log 'RESTORE complete for both owned profiler transactions' }
  else { Log 'RECOVERY REQUIRED. Inspect game state; do not generate optimizations.' }
  Write-Host ''
  Write-Host "Full test log and PresentMon evidence: $session"
  Write-Host 'If one profiler failed, send the log and preserve the captured files.'
}
