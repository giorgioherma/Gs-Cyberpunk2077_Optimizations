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
# A real native x64 child is required: managed .NET apphosts previously faulted
# when hooked by USVFS. Build only in the disposable Windows CI runner.
$vswhere = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio/Installer/vswhere.exe'
if (!(Test-Path -LiteralPath $vswhere)) { throw 'MO2_NATIVE_PROBE_MSVC_VSWHERE_MISSING' }
$vsRoot = (& $vswhere -latest -products '*' -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -property installationPath)
if ($LASTEXITCODE -ne 0 -or !$vsRoot) { throw 'MO2_NATIVE_PROBE_MSVC_TOOLCHAIN_MISSING' }
$vcvars = Join-Path ($vsRoot.Trim()) 'VC/Auxiliary/Build/vcvars64.bat'
if (!(Test-Path -LiteralPath $vcvars)) { throw 'MO2_NATIVE_PROBE_X64_ENV_MISSING' }
$nativeSource = Join-Path $env:GITHUB_WORKSPACE 'integrations/mo2/NativeGameProbe.cpp'
$nativeExe = Join-Path $gameExeDir 'Cyberpunk2077.exe'
if (!(Test-Path -LiteralPath $nativeSource)) { throw 'MO2_NATIVE_PROBE_SOURCE_MISSING' }
$compile = 'call "' + $vcvars + '" >nul && cl.exe /nologo /std:c++17 /EHsc /W4 /O2 /Fe:"' + $nativeExe + '" "' + $nativeSource + '"'
& $env:ComSpec /D /C $compile
if ($LASTEXITCODE -ne 0 -or !(Test-Path -LiteralPath $nativeExe)) {
    throw 'MO2_NATIVE_PROBE_CXX_COMPILATION_FAILED'
}
New-Item -ItemType Directory -Path (Join-Path $game 'archive/pc/content') -Force | Out-Null
foreach ($dir in @('profiles/Other','profiles/Heavy','mods','overwrite','downloads',
                    'mods/ProfileHeavy/r6/scripts','mods/ProfileOther/r6/scripts')) {
    New-Item -ItemType Directory -Path (Join-Path $appDir $dir) -Force | Out-Null
}
Set-Content -LiteralPath (Join-Path $appDir 'profiles/Other/modlist.txt') -Value '+ProfileOther' -NoNewline
Set-Content -LiteralPath (Join-Path $appDir 'profiles/Heavy/modlist.txt') -Value '+ProfileHeavy' -NoNewline
$otherRed = Join-Path $appDir 'mods/ProfileOther/r6/scripts/GcoMo2ProfileSource.reds'
$heavyRed = Join-Path $appDir 'mods/ProfileHeavy/r6/scripts/GcoMo2ProfileSource.reds'
Set-Content -LiteralPath $otherRed -Value 'OTHER_PROFILE_RED' -NoNewline
Set-Content -LiteralPath $heavyRed -Value 'HEAVY_PROFILE_RED' -NoNewline

$relativeAsi = 'bin/x64/plugins/cyber_engine_tweaks.asi'
$physicalAsi = Join-Path $game $relativeAsi
$overwriteAsi = Join-Path $appDir ('overwrite/' + $relativeAsi)
New-Item -ItemType Directory -Path (Split-Path $physicalAsi -Parent), (Split-Path $overwriteAsi -Parent) -Force | Out-Null
Set-Content -LiteralPath $physicalAsi -Value 'PHYSICAL_CET_ASI' -NoNewline
Set-Content -LiteralPath $overwriteAsi -Value 'USER_OVERWRITE_ASI' -NoNewline

# Prepare exactly one session-owned virtual layer, without enabling or editing
# any MO2 profile; the real Python mapper accepts only these profiler paths.
$sessionId = '0123456789abcdef0123456789abcdef'
$stage = Join-Path ([Environment]::GetFolderPath('LocalApplicationData')) ('GCO/MO2Bridge/staging/' + $sessionId)
if (Test-Path -LiteralPath $stage) { throw 'MO2_NATIVE_PROBE_STAGE_COLLISION' }
$stagedAsi = Join-Path $stage $relativeAsi
$stagedGrsp = Join-Path $stage 'red4ext/plugins/G-REDscript-Profiler.dll'
New-Item -ItemType Directory -Path (Split-Path $stagedAsi -Parent), (Split-Path $stagedGrsp -Parent) -Force | Out-Null
Set-Content -LiteralPath $stagedAsi -Value 'GCO_PROFILER_OVERRIDE' -NoNewline
Set-Content -LiteralPath $stagedGrsp -Value 'GCO_GRSP_OVERRIDE' -NoNewline
$nativeObservation = Join-Path $env:RUNNER_TEMP ('gco-mo2-selected-profile-' + $sessionId + '.txt')
$env:GCO_MO2_NATIVE_PROBE_OUTPUT = $nativeObservation

# Distinguish failure to execute a freshly compiled runner binary from a
# failure inside MO2/USVFS. A direct run must read the *physical* ASI and
# cannot see either profile's virtual REDscript or the temporary GCO layer.
& $nativeExe
if ($LASTEXITCODE -ne 0 -or !(Test-Path -LiteralPath $nativeObservation)) {
    throw 'MO2_TEST_NATIVE_CHILD_NOT_EXECUTABLE_OUTSIDE_MO2'
}
$plain = Get-Content -LiteralPath $nativeObservation -Raw
if (!($plain -split '[\r\n]+' -ccontains 'CET_ASI=PHYSICAL_CET_ASI') -or
    !($plain -split '[\r\n]+' -ccontains 'RED=<missing>') -or
    !($plain -split '[\r\n]+' -ccontains 'GRSP=<missing>')) {
    throw 'MO2_TEST_NATIVE_CHILD_DIRECT_BASELINE_MISMATCH'
}
Write-Host 'PASS native x64 test exe directly executes and sees only physical source'
Remove-Item -LiteralPath $nativeObservation -ErrorAction Stop


# MO2 v2.5.2 spawn.cpp uses CREATE_BREAKAWAY_FROM_JOB (0x01000000).
# Ordinary Start-Process success does not prove the runner allows that flag.
# Reproduce the actual Win32 call without USVFS, using the same inert executable,
# current directory and inherited environment. This is diagnosis, not a fallback.
Add-Type -TypeDefinition @'
using System;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;
public static class GcoMo2SpawnDiagnostic {
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct StartupInfo {
        public int cb;
        public string reserved, desktop, title;
        public int x, y, xSize, ySize, xChars, yChars, fill, flags;
        public short show, reserved2;
        public IntPtr reservedPtr, stdIn, stdOut, stdErr;
    }
    [StructLayout(LayoutKind.Sequential)]
    private struct ProcessInfo {
        public IntPtr process, thread;
        public uint processId, threadId;
    }
    [DllImport("kernel32.dll", SetLastError=true, CharSet=CharSet.Unicode)]
    private static extern bool CreateProcessW(string application, StringBuilder command,
        IntPtr processAttributes, IntPtr threadAttributes, bool inheritHandles, uint flags,
        IntPtr environment, string cwd, ref StartupInfo startup, out ProcessInfo process);
    [DllImport("kernel32.dll", SetLastError=true)]
    private static extern bool IsProcessInJob(IntPtr process, IntPtr job, out bool result);
    [DllImport("kernel32.dll")] private static extern IntPtr GetCurrentProcess();
    [DllImport("kernel32.dll")] private static extern bool CloseHandle(IntPtr handle);
    [DllImport("kernel32.dll")] private static extern uint WaitForSingleObject(IntPtr handle, uint ms);
    [DllImport("kernel32.dll")] private static extern bool TerminateProcess(IntPtr handle, uint code);
    [DllImport("kernel32.dll")] private static extern bool GetExitCodeProcess(IntPtr handle, out uint code);
    public static bool InJob(IntPtr process) {
        bool result;
        if (!IsProcessInJob(process, IntPtr.Zero, out result))
            throw new Win32Exception(Marshal.GetLastWin32Error());
        return result;
    }
    public static bool CurrentInJob() { return InJob(GetCurrentProcess()); }
    public static int Probe(string exe, string cwd, uint flags) {
        StartupInfo si = new StartupInfo();
        si.cb = Marshal.SizeOf(typeof(StartupInfo));
        ProcessInfo pi;
        if (!CreateProcessW(null, new StringBuilder("\"" + exe + "\""),
            IntPtr.Zero, IntPtr.Zero, false, flags, IntPtr.Zero, cwd, ref si, out pi))
            return Marshal.GetLastWin32Error();
        try {
            if (WaitForSingleObject(pi.process, 10000) != 0) {
                TerminateProcess(pi.process, 1);
                throw new InvalidOperationException("Native spawn diagnostic child timed out");
            }
            uint exit;
            if (!GetExitCodeProcess(pi.process, out exit) || exit != 0)
                throw new InvalidOperationException("Native spawn diagnostic child failed");
            return 0;
        } finally { CloseHandle(pi.thread); CloseHandle(pi.process); }
    }
}
'@
$runnerInJob = [GcoMo2SpawnDiagnostic]::CurrentInJob()
$normalSpawn = [GcoMo2SpawnDiagnostic]::Probe($nativeExe, $gameExeDir, 0)
if ($normalSpawn -ne 0 -or !(Test-Path -LiteralPath $nativeObservation)) {
    throw "MO2_NATIVE_WIN32_CONTROL_FAILED error=$normalSpawn"
}
Remove-Item -LiteralPath $nativeObservation -ErrorAction Stop
$breakawaySpawn = [GcoMo2SpawnDiagnostic]::Probe($nativeExe, $gameExeDir, 0x01000000)
Write-Host "MO2 spawn diagnostic: runnerInJob=$runnerInJob normalCreateProcess=$normalSpawn breakawayCreateProcess=$breakawaySpawn"
if ($breakawaySpawn -eq 0) {
    if (!(Test-Path -LiteralPath $nativeObservation)) { throw 'MO2_NATIVE_BREAKAWAY_CHILD_OBSERVATION_MISSING' }
    Remove-Item -LiteralPath $nativeObservation -ErrorAction Stop
}
# Continue the real MO2 bootstrap and strict selected-profile test even when
# the direct probe reports a restriction. Never mark that launch as passed.

# GameSettings::directory expects QSettings QByteArray under General.
$gamePath = $game.Replace('\', '/')
$iniLines = @(
    '[General]',
    'gameName=Cyberpunk 2077',
    "gamePath=@ByteArray($gamePath)",
    'selected_profile=Other',
    '[Settings]',
    ('base_directory=' + $appDir.Replace('\', '/')),
    'mod_directory=%BASE_DIR%/mods',
    'profiles_directory=%BASE_DIR%/profiles',
    'overwrite_directory=%BASE_DIR%/overwrite',
    'download_directory=%BASE_DIR%/downloads',
    'selected_profile=Other'
)
Set-Content -LiteralPath $ini -Value $iniLines -Encoding UTF8
Set-Content -LiteralPath $portable -Value '' -NoNewline
Copy-Item -LiteralPath $SourcePlugin -Destination $plugin -ErrorAction Stop

$process = $null
$standardOut = Join-Path $env:RUNNER_TEMP 'gco-mo2-actual-process.stdout.log'
$standardErr = Join-Path $env:RUNNER_TEMP 'gco-mo2-actual-process.stderr.log'
try {
    Write-Host 'Launching official MO2 in disposable portable Windows CI fixture'
    # The root INI + portable.txt select this instance, so avoid an optional
    # --instance= override whose empty-string parsing may reject startup.
    $process = Start-Process -FilePath $exe -ArgumentList @('--multiple','--logs') -WorkingDirectory $appDir -PassThru -RedirectStandardOutput $standardOut -RedirectStandardError $standardErr -ErrorAction Stop
    Write-Host ("MO2 process launched PID=" + $process.Id)
    $mo2InJob = [GcoMo2SpawnDiagnostic]::InJob($process.Handle)
    Write-Host "MO2 spawn diagnostic: realMo2InJob=$mo2InJob runnerInJob=$runnerInJob breakawayCreateProcess=$breakawaySpawn"
    $statusFolder = Join-Path ([Environment]::GetFolderPath('LocalApplicationData')) 'GCO/MO2Bridge'
    $status = $null
    $statusPath = $null
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
                    $statusPath = $candidate.FullName
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

    Write-Host 'Testing real MO2 startApplication(profile=Heavy) while UI profile is Other'
    $signedLaunch = & python integrations/mo2/SignRealMo2Launch.py "$statusPath" Heavy "$game" "$stage" "$sessionId"
    if ($LASTEXITCODE -ne 0) {
        if ($runnerInJob -and $mo2InJob -and $breakawaySpawn -eq 5) {
            throw 'MO2_REAL_NONCURRENT_PROFILE_LAUNCH_BLOCKED_WITH_DIRECT_WIN32_BREAKAWAY_ACCESS_DENIED'
        }
        throw 'MO2_REAL_NONCURRENT_PROFILE_SIGNED_LAUNCH_FAILED'
    }
    Write-Host ("Real MO2 HMAC launch response: " + ($signedLaunch -join ' '))
    $observed = $null
    for ($i = 0; $i -lt 40; $i++) {
        Start-Sleep -Milliseconds 250
        if (Test-Path -LiteralPath $nativeObservation) {
            $observed = Get-Content -LiteralPath $nativeObservation -Raw
            break
        }
    }
    if (!$observed) { throw 'MO2_REAL_NONCURRENT_PROFILE_NATIVE_CHILD_DID_NOT_RUN' }
    Write-Host ("Native selected-profile child observations: " + $observed)
    $expected = @(
        'CET_ASI=GCO_PROFILER_OVERRIDE',
        'RED=HEAVY_PROFILE_RED',
        'GRSP=GCO_GRSP_OVERRIDE'
    )
    foreach ($value in $expected) {
        if (!($observed -split '[\r\n]+' -ccontains $value)) {
            throw ('MO2_REAL_SELECTED_PROFILE_VFS_WINNER_MISMATCH: ' + $value)
        }
    }
    $current = Get-Content -LiteralPath $statusPath -Raw | ConvertFrom-Json
    if ($current.currentProfile -ne 'Other' -or
        (Get-Content -LiteralPath $physicalAsi -Raw) -ne 'PHYSICAL_CET_ASI' -or
        (Get-Content -LiteralPath $overwriteAsi -Raw) -ne 'USER_OVERWRITE_ASI' -or
        (Get-Content -LiteralPath $otherRed -Raw) -ne 'OTHER_PROFILE_RED' -or
        (Get-Content -LiteralPath $heavyRed -Raw) -ne 'HEAVY_PROFILE_RED') {
        throw 'MO2_REAL_NONCURRENT_PROFILE_LAUNCH_MUTATED_USER_SOURCE_OR_VISIBLE_PROFILE'
    }
    Write-Host 'PASS official MO2 launched Heavy while GUI displayed Other: virtual CET and GRSP overrides won, Heavy REDscript winner read, originals and Overwrite unchanged'
}
catch {
    Write-Host ("MO2 app smoke blocked: " + $_.Exception.Message)
    foreach ($captured in @($standardOut, $standardErr)) {
        if (Test-Path -LiteralPath $captured) {
            Write-Host ("Official MO2 process diagnostic from " + (Split-Path $captured -Leaf))
            Get-Content -LiteralPath $captured -Tail 90 | ForEach-Object { Write-Host $_ }
        }
    }
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
