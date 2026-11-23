param(
    [Parameter(Mandatory = $true)][string]$ExtractedMo2Root,
    [Parameter(Mandatory = $true)][string]$SourcePlugin
)
$ErrorActionPreference = 'Stop'

# TEST-ONLY: official MO2 Windows distribution, inert executable, disposable
# portable instance. No user game, real MO2 profile, or mods are modified.
if (-not $env:RUNNER_TEMP -or -not $env:GITHUB_WORKSPACE) {
    throw 'MO2_LIVE_APP_SMOKE_REQUIRES_EPHEMERAL_WINDOWS_CI'
}
$exes = @(Get-ChildItem -LiteralPath $ExtractedMo2Root -Filter 'ModOrganizer.exe' -File -Recurse)
if ($exes.Count -ne 1) { throw 'MO2_REAL_APP_EXECUTABLE_AMBIGUOUS' }
$appDir = $exes[0].DirectoryName
$exe = $exes[0].FullName
$ini = Join-Path $appDir 'ModOrganizer.ini'
$portable = Join-Path $appDir 'portable.txt'
$plugin = Join-Path $appDir 'plugins/gco_mo2_bridge.py'

if (!(Test-Path -LiteralPath $SourcePlugin) -or
    (Test-Path -LiteralPath $plugin) -or
    (Test-Path -LiteralPath $ini) -or
    (Test-Path -LiteralPath $portable)) {
    throw 'MO2_REAL_APP_FIXTURE_NOT_PRISTINE_OR_PLUGIN_MISSING'
}
$game = Join-Path $env:RUNNER_TEMP ('gco-mo2-inert-game-' + [guid]::NewGuid().ToString('N'))
$gameExeDir = Join-Path $game 'bin/x64'
New-Item -ItemType Directory -Path $gameExeDir -Force | Out-Null
[IO.File]::WriteAllBytes((Join-Path $gameExeDir 'Cyberpunk2077.exe'), [byte[]]@())
New-Item -ItemType Directory -Path (Join-Path $game 'archive/pc/content') -Force | Out-Null
foreach ($dir in @('profiles/Other','profiles/Heavy','mods','overwrite')) {
    New-Item -ItemType Directory -Path (Join-Path $appDir $dir) -Force | Out-Null
}
Set-Content -LiteralPath (Join-Path $appDir 'profiles/Other/modlist.txt') -Value '' -NoNewline
Set-Content -LiteralPath (Join-Path $appDir 'profiles/Heavy/modlist.txt') -Value '' -NoNewline

# GameSettings::directory expects QSettings QByteArray under General.
$gamePath = $game.Replace('\', '/')
$iniLines = @(
    '[General]',
    'gameName=Cyberpunk 2077',
    "gamePath=@ByteArray($gamePath)",
    'selected_profile=Other',
    '[Settings]',
    'base_directory=.',
    'mod_directory=mods',
    'profiles_directory=profiles',
    'overwrite_directory=overwrite',
    'selected_profile=Other'
)
Set-Content -LiteralPath $ini -Value $iniLines -Encoding UTF8
Set-Content -LiteralPath $portable -Value '' -NoNewline
Copy-Item -LiteralPath $SourcePlugin -Destination $plugin -ErrorAction Stop

$process = $null
try {
    Write-Host 'Launching official MO2 in disposable portable Windows CI fixture'
    $process = Start-Process -FilePath $exe -ArgumentList @('--multiple','--instance=','--logs') -WorkingDirectory $appDir -PassThru -ErrorAction Stop
    Write-Host ("MO2 process launched PID=" + $process.Id)
    $statusFolder = Join-Path ([Environment]::GetFolderPath('LocalApplicationData')) 'GCO/MO2Bridge'
    $status = $null
    for ($i = 0; $i -lt 60; $i++) {
        Start-Sleep -Milliseconds 500
        if ($process.HasExited) {
            throw "MO2_REAL_APP_TERMINATED_DURING_PLUGIN_BOOTSTRAP exit=$($process.ExitCode)"
        }
        if (Test-Path -LiteralPath $statusFolder) {
            $candidates = @(Get-ChildItem -LiteralPath $statusFolder -Filter 'instance-*.json' -File | Where-Object { $_.LastWriteTimeUtc -gt (Get-Date).ToUniversalTime().AddMinutes(-2) })
            foreach ($candidate in $candidates) {
                $observed = Get-Content -LiteralPath $candidate.FullName -Raw | ConvertFrom-Json
                if ($observed.processId -eq $process.Id -and $observed.mo2Executable -and $observed.instanceRoot -and $observed.bridgeEpoch) {
                    $status = $observed
                    break
                }
            }
        }
        if ($status) { break }
    }
    if (!$status) { throw 'MO2_REAL_APP_PYTHON_PLUGIN_DID_NOT_PUBLISH_LIVE_STATUS' }
    if ($status.currentProfile -ne 'Other' -or $status.installationEnabled -ne $false) {
        throw 'MO2_REAL_APP_WRONG_PROFILE_OR_UNSAFE_INSTALL_GATE'
    }

    dotnet run --project tests/GTotalOptimizer.Core.Tests/GTotalOptimizer.Core.Tests.csproj -c Release -- --mo2-real-companion-probe "$appDir" "$appDir" "$game" Other
    if ($LASTEXITCODE -ne 0) { throw 'MO2_REAL_APP_SIGNED_COMPANION_PROBE_FAILED' }
    Write-Host 'PASS official MO2 application + embedded Python companion status and authenticated GCO probe'
}
catch {
    Write-Host ("MO2 app smoke blocked: " + $_.Exception.Message)
    $logDir = Join-Path $appDir 'logs'
    if (Test-Path -LiteralPath $logDir) {
        Get-ChildItem -LiteralPath $logDir -Filter '*.log' -File |
            Sort-Object LastWriteTime -Descending | Select-Object -First 2 |
            ForEach-Object {
                Write-Host ("Diagnostic from " + $_.Name)
                Get-Content -LiteralPath $_.FullName -Tail 30 | ForEach-Object { Write-Host $_ }
            }
    }
    throw
}
finally {
    if ($process -and -not $process.HasExited) {
        Stop-Process -Id $process.Id -Force -ErrorAction SilentlyContinue
    }
    # Entire extracted MO2 tree and fake game live in RUNNER_TEMP.
}
