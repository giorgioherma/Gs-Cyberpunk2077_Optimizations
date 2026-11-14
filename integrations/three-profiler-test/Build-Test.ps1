param([switch]$Integrated)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$version=(dotnet msbuild src/GTotalOptimizer.App/GTotalOptimizer.App.csproj -getProperty:Version).Trim()
if ($LASTEXITCODE -ne 0 -or $version -notmatch '^\d+\.\d+\.\d+$') { throw "Invalid unified app version from MSBuild: $version" }
$useWpf=(dotnet msbuild src/GTotalOptimizer.App/GTotalOptimizer.App.csproj -getProperty:UseWPF).Trim()
if ($LASTEXITCODE -ne 0 -or $useWpf -ne 'true') { throw 'Unified GUI must build its native WPF frontend' }
# Publish into a short stable staging path. The long branded end-user folder
# must be created only after .NET SDK has finished copying managed payloads.
$root = if ($Integrated) { Join-Path (Get-Location) 'publish/build' } else { Join-Path (Get-Location) 'triple-test' }
New-Item -ItemType Directory -Path $root -Force | Out-Null
$temp = Join-Path $env:RUNNER_TEMP 'GTriple'
New-Item -ItemType Directory -Path $temp -Force | Out-Null

function VerifyFile([string]$Path, [string]$Expected) {
  if (-not (Test-Path $Path -PathType Leaf)) { throw "File missing: $Path" }
  $hash=(Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash.ToLowerInvariant()
  if ($hash -ne $Expected.ToLowerInvariant()) { throw "SHA256 mismatch: $Path $hash" }
}
function GetExact([string]$Url,[string]$Path,[string]$Sha) {
  Invoke-WebRequest -Uri $Url -OutFile $Path
  VerifyFile $Path $Sha
}

$artifact=Join-Path (Get-Location) 'downloaded-cet'
foreach ($z in @(Get-ChildItem $artifact -Filter '*.zip' -File -Recurse)) {
  Expand-Archive -LiteralPath $z.FullName -DestinationPath (Join-Path $temp 'CET') -Force
}
$candidates=@(Get-ChildItem $artifact,(Join-Path $temp 'CET') -Filter 'G-CET-Runtime-Profiler.exe' -Recurse -File -ErrorAction SilentlyContinue | Where-Object { Test-Path (Join-Path $_.DirectoryName 'MANIFEST.json') })
if ($candidates.Count -ne 1) { throw "Expected exactly one CET package: $($candidates.Count)" }
$cetSource=$candidates[0].DirectoryName
$cm=Get-Content (Join-Path $cetSource 'MANIFEST.json') -Raw | ConvertFrom-Json
if ($cm.packageVersion -ne '1.3.4') { throw 'CET artifact version drift' }
VerifyFile (Join-Path $cetSource 'payload/cyber_engine_tweaks.PROFILER.dll') $cm.targetCET.profilerSha256
VerifyFile (Join-Path $cetSource 'payload/0-Engine/modules/G-CET/Scheduler.lua') $cm.zeroEngine.profilerSchedulerSha256
if (-not (Test-Path (Join-Path $cetSource 'payload/CETProfilerControls/init.lua'))) { throw 'CET profiler controls missing' }
Write-Host 'PASS current CET 1.3.4 native profiler, controls and canonical Scheduler'

$grzip=Join-Path $temp 'GRSP-1.0.0.zip'
GetExact 'https://github.com/giorgioherma/GRSP-G-REDscript-Profiler/releases/download/v1.0.0/G-REDscript-Profiler-v1.0.0.zip' $grzip '15b62ff6df95fa54f6a4fb6eb992e7aa07afe69c752922db34fdbe11b615e606'
$grUnzip=Join-Path $temp 'GRSP'
Expand-Archive $grzip $grUnzip -Force
$grCandidates=@(Get-ChildItem $grUnzip -Filter 'G-REDscript-Profiler.exe' -Recurse -File | Where-Object { Test-Path (Join-Path $_.DirectoryName 'MANIFEST.json') })
if ($grCandidates.Count -ne 1) { throw "Expected one official GRSP package: $($grCandidates.Count)" }
$grSource=$grCandidates[0].DirectoryName
$gm=Get-Content (Join-Path $grSource 'MANIFEST.json') -Raw | ConvertFrom-Json
if ($gm.version -ne '1.0.0') { throw 'GRSP release version drift' }
if (-not (Test-Path (Join-Path $grSource 'payload/G-REDscript-Profiler.dll'))) { throw 'GRSP native payload missing' }
Write-Host 'PASS official GRSP v1.0.0 release ZIP integrity and payload'

$components=Join-Path $root 'components'
$cet=Join-Path $components 'cet'
$gr=Join-Path $components 'grsp'
New-Item -ItemType Directory -Force $cet,$gr | Out-Null
Copy-Item (Join-Path $cetSource '*') $cet -Recurse -Force
Copy-Item (Join-Path $grSource '*') $gr -Recurse -Force
Remove-Item (Join-Path $cet 'app') -Recurse -Force
Remove-Item (Join-Path $gr 'app') -Recurse -Force

# Source-level retarget in CI's disposable checkout only. Rebuild both managers
# from the owner's pinned source rather than editing built EXEs/assemblies.
foreach ($project in @(
  'vendor-cet/src/G.CETProfiler.App/G.CETProfiler.App.csproj',
  'vendor-cet/src/G.CETProfiler.Core/G.CETProfiler.Core.csproj',
  'vendor-grsp/manager/GRedscriptProfiler.Manager.csproj'
)) {
  $text=Get-Content $project -Raw
  if ($text -notmatch '<TargetFramework>net8\.0-windows</TargetFramework>') {
    throw "Cannot safely retarget unexpected owner source: $project"
  }
  $text=$text.Replace('<TargetFramework>net8.0-windows</TargetFramework>','<TargetFramework>net10.0-windows</TargetFramework>')
  Set-Content $project $text -Encoding UTF8
}
# GCO-only adaptations run against pinned, disposable vendor source. The original
# downloaded packages and both standalone repositories remain unchanged.
if ($Integrated) {
  & ./integrations/three-profiler-test/Apply-GcoCaptureLifecycle.ps1
  if ($LASTEXITCODE -ne 0) { throw 'GCO owner source adaptation failed' }
  # Compile the GCO-specific native GRSP protocol from the exact source pin.
  # The official release Cargo.lock accompanies the verified release package.
  $lock=Join-Path $grSource 'docs/Cargo.lock'
  if (!(Test-Path $lock -PathType Leaf)) { throw 'Pinned GRSP Cargo.lock absent from verified release' }
  Copy-Item $lock 'vendor-grsp/Cargo.lock' -Force
  $cacheRoot=Join-Path (Get-Location) '.gco-grsp-native'
  $cachedNative=Join-Path $cacheRoot 'G-REDscript-Profiler.dll'
  $cacheProof=Join-Path $cacheRoot 'proof.json'
  $destination=Join-Path $gr 'payload/G-REDscript-Profiler.dll'
  $reuse=Test-Path -LiteralPath $cacheProof -PathType Leaf
  if ($reuse) {
    $proof=Get-Content -LiteralPath $cacheProof -Raw | ConvertFrom-Json
    if (!$env:GCO_GRSP_NATIVE_CACHE_KEY -or $proof.schema -ne 1 -or
        $proof.key -ne $env:GCO_GRSP_NATIVE_CACHE_KEY) {
      throw 'Cached GRSP native identity differs from the pinned Rust and adapter inputs.'
    }
    VerifyFile $cachedNative $proof.sha256
    Copy-Item -LiteralPath $cachedNative -Destination $destination -Force
    Write-Host 'PASS exact-input cached GCO GRSP native DLL; Rust recompilation skipped'
  }
  else {
    Push-Location 'vendor-grsp'
    try {
      & cargo build --release --locked
      if ($LASTEXITCODE -ne 0) { throw 'GCO-specific GRSP native build failed' }
    } finally { Pop-Location }
    $native=Join-Path 'vendor-grsp/target/release' 'g_redscript_profiler.dll'
    if (!(Test-Path $native -PathType Leaf)) { throw 'GCO GRSP native DLL missing after source build' }
    Copy-Item -LiteralPath $native -Destination $destination -Force
    if ($env:GCO_GRSP_NATIVE_CACHE_KEY) {
      New-Item -ItemType Directory -Force $cacheRoot | Out-Null
      Copy-Item -LiteralPath $native -Destination $cachedNative -Force
      $digest=(Get-FileHash -LiteralPath $cachedNative -Algorithm SHA256).Hash.ToLowerInvariant()
      @{ schema=1; key=$env:GCO_GRSP_NATIVE_CACHE_KEY; sha256=$digest } |
        ConvertTo-Json -Depth 3 | Set-Content -LiteralPath $cacheProof -Encoding UTF8
      VerifyFile $cachedNative $digest
      Write-Host 'PASS compiled GCO GRSP native DLL staged for exact-input reuse'
    }
  }
  $grManifest=Join-Path $gr 'MANIFEST.json'
  $grMetadata=Get-Content $grManifest -Raw | ConvertFrom-Json
  $grMetadata.shutdown_behavior='GCO: incomplete captures discarded; explicit F11 stop required'
  $grMetadata.total_profiler_contract.total_specific_profiler_variant=$true
  $grMetadata.total_profiler_contract.consume_exact_release_unchanged=$false
  $grMetadata | ConvertTo-Json -Depth 30 | Set-Content -LiteralPath $grManifest -Encoding UTF8
  Write-Host 'PASS GCO-only source-built GRSP native F11 completion protocol'
}
function PublishManager([string]$Csproj, [string]$Dest) {
  & dotnet publish $Csproj -c Release -r win-x64 --self-contained false -p:SelfContained=false -p:PublishSingleFile=false -p:PublishTrimmed=false -o $Dest
  if ($LASTEXITCODE -ne 0) { throw "Manager net10 retarget failed: $Csproj" }
  if (Test-Path (Join-Path $Dest 'coreclr.dll')) { throw 'Duplicate managed runtime' }
}
PublishManager 'vendor-cet/src/G.CETProfiler.App/G.CETProfiler.App.csproj' (Join-Path $cet 'app')
PublishManager 'vendor-grsp/manager/GRedscriptProfiler.Manager.csproj' (Join-Path $gr 'app')
# Source of truth for both Windows executables and WPF windows.
# Keep the owner-provided multi-resolution ICO intact (no resampling).
$iconSource=(Resolve-Path 'assets/icons/GOT_LAG_final/GOT_LAG_Hybrid_16-24_brush_32plus_V.ico' -ErrorAction Stop).Path
$iconBytes=[IO.File]::ReadAllBytes($iconSource)
$iconSizes=@(16,24,32,36,40,48,64,96,128,256)
if ($iconBytes.Length -lt 100 -or [BitConverter]::ToUInt16($iconBytes,0) -ne 0 -or
    [BitConverter]::ToUInt16($iconBytes,2) -ne 1 -or
    [BitConverter]::ToUInt16($iconBytes,4) -ne $iconSizes.Count) {
  throw 'Canonical GOT LAG ICO header or multi-size directory invalid'
}
for ($i=0; $i -lt $iconSizes.Count; $i++) {
  $entrySize=if ($iconSizes[$i] -eq 256) { 0 } else { $iconSizes[$i] }
  if ($iconBytes[6+16*$i] -ne $entrySize -or $iconBytes[7+16*$i] -ne $entrySize) {
    throw "GOT LAG ICO frame $i does not match approved size $($iconSizes[$i])"
  }
}
Write-Host 'PASS owner hybrid ICO 16/24/32/36/40/48/64/96/128/256 frames'
PublishManager 'src/GTotalOptimizer.App/GTotalOptimizer.App.csproj' (Join-Path $root 'app')

# Copy the original user-owned PNGs from their current source folders.
# Never flatten paths: the WPF runtime resolves images/ and elements/.
$uiSource=Join-Path (Get-Location) 'assets/ui'
$uiNames=@(
  'images/V_background_1_color.png',
  'images/V_background_1_black.png',
  'images/The overflow.png',
  'images/TrueHeader_GCO_page1.png',
  'elements/GCO_Border_Regular_1280x760_FINAL.png',
  'elements/Logo_GCO_Official.png'
)
$uiTarget=Join-Path $root 'assets/ui'
foreach ($name in $uiNames) {
  $source=Join-Path $uiSource $name
  $dest=Join-Path $uiTarget $name
  if (-not (Test-Path -LiteralPath $source -PathType Leaf)) {
    throw "Required user-owned WPF asset missing: $name"
  }
  New-Item -ItemType Directory -Force (Split-Path -Parent $dest) | Out-Null
  Copy-Item -LiteralPath $source -Destination $dest -Force
  if ((Get-FileHash -LiteralPath $source -Algorithm SHA256).Hash -ne
      (Get-FileHash -LiteralPath $dest -Algorithm SHA256).Hash) {
    throw "Packaged artwork hash mismatch: $name"
  }
}
Write-Host 'PASS V pair, transparent puddle, skyline, Regular border and official GCO logo copied byte-identically'

# Fonts are embedded as WPF assembly resources; ship OFL license notices.
foreach ($family in @('Rajdhani','Oxanium')) {
  $license=Join-Path $uiSource "fonts/$family/OFL.txt"
  if (-not (Test-Path -LiteralPath $license -PathType Leaf)) { throw "Missing OFL license for $family" }
  $licenseTarget=Join-Path $uiTarget "licenses"
  New-Item -ItemType Directory -Force $licenseTarget | Out-Null
  Copy-Item -LiteralPath $license -Destination (Join-Path $licenseTarget "$family-OFL.txt")
}
Write-Host 'PASS WPF Rajdhani + Oxanium font licenses bundled'
# Never package 02_cityline_header.png or GCO_approved_blue_skyline_crop.png.

# The official .NET 10 Windows SDK was installed by actions/setup-dotnet.
# Copy matching native runtime files from that SDK instead of downloading the
# intermittently failing dotnet-install.ps1 script. No SDK shipped in package.
$runtime=Join-Path $root 'runtime'
$dotnetExe=(Get-Command dotnet -CommandType Application -ErrorAction Stop).Source
$homeDotnet=Split-Path -Parent $dotnetExe
$coreSource=Join-Path $homeDotnet 'shared/Microsoft.NETCore.App'
$desktopSource=Join-Path $homeDotnet 'shared/Microsoft.WindowsDesktop.App'
$fxrSource=Join-Path $homeDotnet 'host/fxr'
foreach ($required in @($coreSource,$desktopSource,$fxrSource)) {
  if (!(Test-Path -LiteralPath $required -PathType Container)) {
    throw "Missing official .NET Desktop runtime source: $required"
  }
}
$coreVersions=@(Get-ChildItem -LiteralPath $coreSource -Directory |
  Where-Object { $_.Name -match '^10[.][0-9]+[.][0-9]+$' } | ForEach-Object Name)
$desktopVersions=@(Get-ChildItem -LiteralPath $desktopSource -Directory |
  Where-Object { $_.Name -match '^10[.][0-9]+[.][0-9]+$' } | ForEach-Object Name)
$matchingVersions=@($coreVersions | Where-Object { $_ -in $desktopVersions } |
  Sort-Object -Property { [version]$_ } -Descending)
if ($matchingVersions.Count -lt 1) {
  throw "No matching .NET 10 Core/Desktop runtime: core=$($coreVersions -join ',') desktop=$($desktopVersions -join ',')"
}
$runtimeVersion=$matchingVersions[0]
$fxrVersions=@(Get-ChildItem -LiteralPath $fxrSource -Directory |
  Where-Object { $_.Name -match '^10[.][0-9]+[.][0-9]+$' } |
  Sort-Object -Property { [version]$_.Name } -Descending)
if ($fxrVersions.Count -eq 0) { throw "No official .NET 10 host/fxr: $fxrSource" }
New-Item -ItemType Directory -Force $runtime,
  (Join-Path $runtime 'host/fxr'),
  (Join-Path $runtime 'shared/Microsoft.NETCore.App'),
  (Join-Path $runtime 'shared/Microsoft.WindowsDesktop.App') | Out-Null
Copy-Item -LiteralPath $dotnetExe -Destination (Join-Path $runtime 'dotnet.exe') -Force
Copy-Item -LiteralPath $fxrVersions[0].FullName -Destination (Join-Path $runtime 'host/fxr') -Recurse -Force
Copy-Item -LiteralPath (Join-Path $coreSource $runtimeVersion) -Destination (Join-Path $runtime 'shared/Microsoft.NETCore.App') -Recurse -Force
Copy-Item -LiteralPath (Join-Path $desktopSource $runtimeVersion) -Destination (Join-Path $runtime 'shared/Microsoft.WindowsDesktop.App') -Recurse -Force
foreach ($notice in @('LICENSE.txt','ThirdPartyNotices.txt')) {
  $source=Join-Path $homeDotnet $notice
  if (Test-Path -LiteralPath $source -PathType Leaf) {
    Copy-Item -LiteralPath $source -Destination (Join-Path $runtime $notice) -Force
  }
}
$runtimes=@(& (Join-Path $runtime 'dotnet.exe') --list-runtimes)
if ($LASTEXITCODE -ne 0 -or
    -not ($runtimes | Where-Object { $_ -like "Microsoft.NETCore.App $runtimeVersion *" }) -or
    -not ($runtimes | Where-Object { $_ -like "Microsoft.WindowsDesktop.App $runtimeVersion *" })) {
  throw "Private .NET 10 Desktop layout incomplete: $($runtimes -join '; ')"
}
Write-Host "PASS one official private .NET 10 Core/Desktop runtime $runtimeVersion"

# Build the existing source-owned native root launcher.
$vswhere=Join-Path ([Environment]::GetFolderPath('ProgramFilesX86')) 'Microsoft Visual Studio/Installer/vswhere.exe'
$vs=(& $vswhere -latest -products * -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -property installationPath).Trim()
if (-not $vs) { throw 'Required x64 compiler missing on runner' }
$vcvars=Join-Path $vs 'VC/Auxiliary/Build/vcvars64.bat'
$rcPath=Join-Path $temp 'launcher.rc'
$rcTxt=(Get-Content 'src/GTotalOptimizer.Launcher/launcher.rc.in' -Raw).Replace('@VERSION_COMMAS@',$version.Replace('.',',')).Replace('@VERSION_DOTS@',$version).Replace('@ICON_PATH@',$iconSource.Replace('\','\\'))
Set-Content $rcPath $rcTxt -Encoding ascii
$nativeSource=(Resolve-Path 'src/GTotalOptimizer.Launcher/launcher.c').Path
$nativeRes=Join-Path $temp 'launcher.res'
$nativeObj=Join-Path $temp 'launcher.obj'
$nativeExe=Join-Path $root 'Gs-Cyberpunk2077_Optimizer.exe'
$cmdPath=Join-Path $temp 'build.cmd'
$buildLines=@(
  '@echo off'
  ('call "{0}" >nul' -f $vcvars)
  'if errorlevel 1 exit /b %errorlevel%'
  ('rc.exe /nologo /fo"{0}" "{1}"' -f $nativeRes,$rcPath)
  'if errorlevel 1 exit /b %errorlevel%'
  ('cl.exe /nologo /O2 /MT /DUNICODE /D_UNICODE /Fo:"{0}" "{1}" "{2}" /link /SUBSYSTEM:WINDOWS user32.lib shell32.lib /OUT:"{3}"' -f $nativeObj,$nativeSource,$nativeRes,$nativeExe)
  'exit /b %errorlevel%'
)
Set-Content $cmdPath $buildLines -Encoding ascii
& $cmdPath
if ($LASTEXITCODE -ne 0 -or !(Test-Path $nativeExe)) { throw 'Native launcher build failed' }

# Assert version identity in the built native launcher and managed application.
foreach ($exe in @($nativeExe,(Join-Path $root 'app/G-Total-Optimizer.App.exe'))) {
  if ((Get-Item $exe).VersionInfo.FileVersion -ne "$version.0") { throw "Unified executable version mismatch: $exe" }
}

# Verify Windows Explorer would extract the exact same owner ICO from both
# the public native launcher and the internal managed WPF application.
Add-Type -AssemblyName System.Drawing
$expectedIcon=[System.Drawing.Icon]::new($iconSource,[System.Drawing.Size]::new(32,32))
try {
  $expectedBitmap=$expectedIcon.ToBitmap()
  try {
    foreach ($exe in @($nativeExe,(Join-Path $root 'app/G-Total-Optimizer.App.exe'))) {
      $actualIcon=[System.Drawing.Icon]::ExtractAssociatedIcon($exe)
      if ($null -eq $actualIcon) { throw "Windows icon missing from $exe" }
      try {
        $bitmap=$actualIcon.ToBitmap()
        try {
          if ($bitmap.Width -ne $expectedBitmap.Width -or $bitmap.Height -ne $expectedBitmap.Height) {
            throw "Windows icon dimensions differ from owner ICO for $exe"
          }
          for ($y=0; $y -lt $bitmap.Height; $y++) {
            for ($x=0; $x -lt $bitmap.Width; $x++) {
              if ($bitmap.GetPixel($x,$y).ToArgb() -ne $expectedBitmap.GetPixel($x,$y).ToArgb()) {
                throw "Windows icon pixel mismatch at ($x,$y) in $exe"
              }
            }
          }
        } finally { $bitmap.Dispose() }
      } finally { $actualIcon.Dispose() }
    }
  } finally { $expectedBitmap.Dispose() }
} finally { $expectedIcon.Dispose() }
Write-Host 'PASS both packaged EXEs expose the exact owner GOT LAG 32px icon'
# Native PresentMon CLI, exact release digest. No 160 MB MSI.
$pm=Join-Path $components 'presentmon'
New-Item -ItemType Directory -Force $pm | Out-Null
$pmExe=Join-Path $pm 'PresentMon.exe'
GetExact 'https://github.com/GameTechDev/PresentMon/releases/download/v2.6.0/PresentMon-2.6.0-x64.exe' $pmExe 'b2a706bc6ad475749e3b7e3409263aa1e6906d45bdcf993f6dbc0f660188f1af'
Invoke-WebRequest 'https://raw.githubusercontent.com/GameTechDev/PresentMon/v2.6.0/LICENSE.txt' -OutFile (Join-Path $pm 'LICENSE-PresentMon.txt')
Write-Host 'PASS official PresentMon CLI 2.6.0'

# One coreclr in entire bundle. CET native payload remains source-owned and pinned.
# Integrated GRSP native payload is built from its pinned source with GCO-only F11 semantics.
$core=@(Get-ChildItem $root -Filter 'coreclr.dll' -File -Recurse)
if ($core.Count -ne 1 -or !$core[0].FullName.StartsWith($runtime, [StringComparison]::OrdinalIgnoreCase)) {
  throw "Expected one shared .NET runtime; found $($core.Count)"
}
foreach ($exe in @((Join-Path $root 'app/G-Total-Optimizer.App.exe'),(Join-Path $cet 'app/G-CET-Runtime-Profiler.App.exe'),(Join-Path $gr 'app/G-REDscript-Profiler.App.exe'))) {
  if (!(Test-Path $exe)) { throw "Manager executable absent $exe" }
  $json=Get-Content ([IO.Path]::ChangeExtension($exe,'.runtimeconfig.json')) -Raw
  if ($json -notmatch '10\.0') { throw "Framework-dependent manager did not retarget to net10: $exe" }
}
New-Item -ItemType Directory -Force (Join-Path $root 'RESULTS') | Out-Null
if (!$Integrated) { Copy-Item 'integrations/three-profiler-test/Run-Triple-Test.ps1' (Join-Path $root 'Run-Triple-Test.ps1') }
if (!$Integrated) { Set-Content (Join-Path $root 'RUN-ALL-THREE-TEST.cmd') @('@echo off','powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0Run-Triple-Test.ps1"','pause') -Encoding ascii }
Set-Content (Join-Path $root 'VERSION.txt') $version -NoNewline
$provenance=@{
  testOnly=$true
  cet=@{ sourceSha='7f3489d8fd7e02d595f4a053616044cf2f6b2dab'; runId=41820765951; artifactId=13317131256; nativeSha256=$cm.targetCET.profilerSha256 }
  grsp=@{ releaseTag='v1.0.0'; releaseZipSha256='15b62ff6df95fa54f6a4fb6eb992e7aa07afe69c752922db34fdbe11b615e606'; sourceSha='c6871e21d3d3ece8e36d3ea47d45e18d0d7acead'; gcoNativeVariant=$Integrated; nativeSha256=(Get-FileHash (Join-Path $gr 'payload/G-REDscript-Profiler.dll') -Algorithm SHA256).Hash.ToLowerInvariant() }
  presentMon=@{ releaseTag='v2.6.0'; executableSha256='b2a706bc6ad475749e3b7e3409263aa1e6906d45bdcf993f6dbc0f660188f1af' }
}
$provenance | ConvertTo-Json -Depth 6 | Set-Content (Join-Path $components $(if ($Integrated) {'COMPONENT-PROVENANCE.json'} else {'TEST-PROVENANCE.json'})) -Encoding UTF8
if (!$Integrated) {
Set-Content (Join-Path $root 'README-TRIPLE-TEST.txt') @'
CET + GRSP + PresentMon triple test package — NOT a production optimizer build.
Extract entirely. Run RUN-ALL-THREE-TEST.cmd and select Cyberpunk 2077 root.
It uses both profiler owners' original install/collect/restore headless CLIs.
PresentMon starts with F11 capture targeting Cyberpunk2077.exe and asks for UAC.
Start/stop F11 twice in the game. CLOSE game before collecting/restore.
Retain RESULTS/Triple-* logs and raw data to report success or errors.
The ordinary G-Total GUI install/generate buttons remain gated.
No third-party mod source or optimization output is distributed.
'@ -Encoding UTF8
}
if ($Integrated) {
  $entries=@(
    @{ id='cet'; executable='components/cet/G-CET-Runtime-Profiler.exe'; sourceCommit='7f3489d8fd7e02d595f4a053616044cf2f6b2dab'; ciRun=41820765951; artifactId=13317131256 },
    @{ id='grsp'; executable='components/grsp/G-REDscript-Profiler.exe'; sourceCommit='c6871e21d3d3ece8e36d3ea47d45e18d0d7acead'; releaseSha256='15b62ff6df95fa54f6a4fb6eb992e7aa07afe69c752922db34fdbe11b615e606' }
  )
  foreach ($entry in $entries) {
    $entry.sha256=(Get-FileHash (Join-Path $root $entry.executable) -Algorithm SHA256).Hash.ToLowerInvariant()
  }
  $payloadPaths=@(
    'components/cet/payload/cyber_engine_tweaks.PROFILER.dll',
    'components/cet/payload/0-Engine/modules/G-CET/Scheduler.lua',
    'components/cet/payload/CETProfilerControls/init.lua',
    'components/grsp/payload/G-REDscript-Profiler.dll',
    'components/presentmon/PresentMon.exe'
  )
  $payloads=@($payloadPaths | ForEach-Object {
    @{ path=$_; sha256=(Get-FileHash (Join-Path $root $_) -Algorithm SHA256).Hash.ToLowerInvariant() }
  })
  VerifyFile (Join-Path $root $payloadPaths[0]) $cm.targetCET.profilerSha256
  VerifyFile (Join-Path $root $payloadPaths[1]) $cm.zeroEngine.profilerSchedulerSha256
  VerifyFile (Join-Path $root $payloadPaths[4]) 'b2a706bc6ad475749e3b7e3409263aa1e6906d45bdcf993f6dbc0f660188f1af'
  @{ schemaVersion=2; components=$entries; payloads=$payloads } |
    ConvertTo-Json -Depth 8 | Set-Content (Join-Path $components 'manifest.json') -Encoding UTF8
  # The GitHub artifact contains ONE portable app directory plus a short
  # adjacent user guide. Never copy the guide into the game optimization ZIP.
  $releaseRoot=Join-Path (Get-Location) 'publish/release'
  New-Item -ItemType Directory -Force $releaseRoot | Out-Null
  $finalApp=Join-Path $releaseRoot "G's Cyberpunk 2077 Optimization"
  if (Test-Path $finalApp) { throw 'Existing final app folder would be overwritten' }
  Move-Item -LiteralPath $root -Destination $finalApp
  $root=$finalApp
  # Generate the visible version directly from the canonical MSBuild value.
  # Keep this alongside the app, not inside the game's optimization output.
  @"
GOT LAG? - G's Cyberpunk 2077 Optimizer v$version
=======================================

QUICK START
1. Extract the complete ZIP to a writable folder. Keep the app folder intact.
2. Open "G's Cyberpunk 2077 Optimization" and run:
   Gs-Cyberpunk2077_Optimizer.exe
3. Select the Cyberpunk 2077 game installation folder (containing bin/x64).
4. Click INSTALL PROFILERS. CET and REDscript install through their own managers.
5. Click START CYBERPUNK / CAPTURE. PresentMon records automatically.
6. In the game world, press F11 once to start CET + REDscript and again to stop.
   Sample movement, driving and combat. A few minutes is sufficient.
7. Exit Cyberpunk completely. Click COLLECT RESULTS.
8. Click CONTINUE. Confirm any missing-capture skip boxes if necessary.
   GCO verifies the current session and restores the original profiler files.

IMPORTANT
- Keep the entire folder together; do not run the launcher from inside the ZIP.
- If the game or a profiler is still running, close it before Collect/Continue.
- PresentMon is optional. It records automatically and does not use F11.
- If a capture is missing, the app identifies the missing domain before Continue.
- Do not replace or delete your original mods. Use the app's RESTORE ORIGINALS
  recovery action if interrupted during profiling.
- The optimization generator/combined ZIP feature is not yet enabled.
- Your captured data is under the app folder's RESULTS directory.

VERSION $version (portable build). No system .NET installation required.
"@ | Set-Content -LiteralPath (Join-Path $releaseRoot 'START-HERE.txt') -Encoding UTF8
  # Per-build provenance: same version can have multiple CI attempts with
  # different ZIP SHA-256 hashes. Do not infer a unique hash from a version.
  [ordered]@{
    product='Gs-Cyberpunk2077_Optimizer'
    version=$version
    sourceCommit=[string]$env:GITHUB_SHA
    githubRunId=[string]$env:GITHUB_RUN_ID
    githubRunAttempt=[string]$env:GITHUB_RUN_ATTEMPT
  } | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $root 'BUILD-INFO.json') -Encoding UTF8
  Write-Host 'PASS portable release layout: Gs launcher folder + sibling START-HERE.txt'
  Write-Host 'PASS integrated GCO: .NET10 CET+GRSP managers, source-owned native DLLs, PresentMon and verified payload manifest'
} else {
  Write-Host 'PASS THREE: CET + GRSP + PresentMon, one private .NET10 runtime, test harness package'
}
