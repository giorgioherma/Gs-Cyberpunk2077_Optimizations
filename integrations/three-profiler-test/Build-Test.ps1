param([switch]$Integrated)
$ErrorActionPreference = 'Stop'
# Canonical GCO integration only; the legacy standalone triple-test and
# its independent F11 capture handler have been permanently retired.
if (!$Integrated) { throw 'Standalone triple-test retired; use the integrated GCO build.' }
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

# The owner CI shipped Profiler and Resolver from the SAME pinned source.
# We rebuild Resolver managed code below; these original artifact bytes serve
# only as a native-service provenance anchor and baseline behavior oracle.
$resolverArchive=Join-Path (Get-Location) 'downloaded-cet-resolver'
foreach ($z in @(Get-ChildItem $resolverArchive -Filter '*.zip' -File -Recurse)) {
  Expand-Archive -LiteralPath $z.FullName -DestinationPath (Join-Path $temp 'Resolver') -Force
}
$resolverCandidates=@(Get-ChildItem $resolverArchive,(Join-Path $temp 'Resolver') -Filter 'G-CET-Resolver.exe' -Recurse -File -ErrorAction SilentlyContinue |
  Where-Object { (Test-Path (Join-Path $_.DirectoryName 'knowledge/semantic-library.json')) -and
                 (Test-Path (Join-Path $_.DirectoryName 'runtime/native/GCETStaticLocations.dll')) })
if ($resolverCandidates.Count -ne 1) {
  throw "Expected exactly one pinned CET Resolver reference package: $($resolverCandidates.Count)"
}
$resolverReference=$resolverCandidates[0].DirectoryName
$nativeResolverDll=Join-Path $resolverReference 'runtime/native/GCETStaticLocations.dll'
# The owner-built static-location runtime is independently pinned and must
# never come from the user's live game or from an unverified historical ZIP.
$nativeResolverHash='737bcdd9d6cbfedd9b238d16ae49c9fa0b52497e4e1e14cb606e826dd56c1496'
VerifyFile $nativeResolverDll $nativeResolverHash
Write-Host 'PASS original CET Resolver artifact and independent native service provenance'

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
  'vendor-cet/src/G.CETResolver.App/G.CETResolver.App.csproj',
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
  # Never rebuild native GRSP in GCO. The independent GRSP owner CI
  # emits the exact F11/Developer-CSV variant, provenance and SHA-256.
  if ([string]::IsNullOrWhiteSpace($env:GCO_GRSP_NATIVE_ARTIFACT_ROOT)) {
    throw 'Missing GRSP owner-native artifact; old GRSP DLL is not an acceptable fallback'
  }
  $artifact=Join-Path (Get-Location) $env:GCO_GRSP_NATIVE_ARTIFACT_ROOT
  $proofPath=Join-Path $artifact 'provenance.json'
  if (!(Test-Path -LiteralPath $proofPath -PathType Leaf)) {
    throw 'Missing independent GRSP native producer provenance'
  }
  $proof=Get-Content -LiteralPath $proofPath -Raw | ConvertFrom-Json
  $source=(git -C vendor-grsp rev-parse HEAD).Trim()
  if ($source.Length -ne 40 -or $proof.schema -ne 1 -or
      $proof.producer -ne 'GRSP_NATIVE_CALLBACK_INSTRUMENTATION' -or
      $proof.sourceCommit -ne $source -or
      $proof.nativeVariant -ne 'gco_explicit_f11' -or
      $proof.strictF11StopOnly -ne $true -or
      $proof.dllSha256 -notmatch '^[0-9a-fA-F]{64}$') {
    throw 'GRSP owner-native source, explicit-F11 or hash proof failed'
  }
  $native=Join-Path $artifact 'G-REDscript-Profiler.dll'
  VerifyFile $native $proof.dllSha256
  $dest=Join-Path $gr 'payload/G-REDscript-Profiler.dll'
  Copy-Item -LiteralPath $native -Destination $dest -Force
  VerifyFile $dest $proof.dllSha256
  $text=[Text.Encoding]::ASCII.GetString([IO.File]::ReadAllBytes((Resolve-Path $dest)))
  foreach($marker in @('GCO-F11-Complete.flag','RSP_CallbackPressure.csv','RSP_CallbackTargets.csv','RSP_ProxyAttribution.csv','RSP_ProxyChildren.csv')) {
    if (!$text.Contains($marker)) { throw "GRSP DLL missing instrumentation contract: $marker" }
  }
  Write-Host 'PASS source-owned GRSP native instrumentation in the GCO package'
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

# Publish the actual CET-owned Resolver from this same pinned source, with
# the same private .NET10 runtime as the managers. Its WinForms UI is an
# internal owner capability; GCO will use only the original CLI in Stage 3.
# Source templates are copied by the owner's .csproj, never reconstructed.
$cetResolver=Join-Path $components 'cet-resolver'
$cetResolverApp=Join-Path $cetResolver 'app'
New-Item -ItemType Directory -Force $cetResolverApp | Out-Null
PublishManager 'vendor-cet/src/G.CETResolver.App/G.CETResolver.App.csproj' $cetResolverApp
$resolverAssetPaths=@(
  'G-CET-Resolver.exe',
  'G-CET-Resolver.dll',
  'G.CETProfiler.Core.dll',
  'knowledge/semantic-library.json',
  'runtime/0-Engine/fixed-init.lua.gz.b64',
  'runtime/0-Engine/modules/ActionRouter.lua.b64',
  'runtime/0-Engine/modules/Health.lua.b64',
  'runtime/0-Engine/modules/Scheduler.lua.b64',
  'runtime/0-Engine/modules/GCETNativeLocationHost.lua'
)
foreach ($asset in $resolverAssetPaths) {
  $published=Join-Path $cetResolverApp $asset
  if (!(Test-Path -LiteralPath $published -PathType Leaf)) {
    throw "CET source-owned Resolver asset missing from framework-dependent publish: $asset"
  }
  $ownerReference=Join-Path $resolverReference $asset
  if ($asset -notmatch '^(G-CET-Resolver\.|G\.CETProfiler\.Core\.)' -and
      (Test-Path -LiteralPath $ownerReference -PathType Leaf)) {
    $expected=(Get-FileHash -LiteralPath $ownerReference -Algorithm SHA256).Hash.ToLowerInvariant()
    VerifyFile $published $expected
  }
}
$nativeResolverTarget=Join-Path $cetResolverApp 'runtime/native'
New-Item -ItemType Directory -Force $nativeResolverTarget | Out-Null
Copy-Item -LiteralPath $nativeResolverDll -Destination (Join-Path $nativeResolverTarget 'GCETStaticLocations.dll')
VerifyFile (Join-Path $nativeResolverTarget 'GCETStaticLocations.dll') $nativeResolverHash
Write-Host 'PASS pinned CET Resolver recompiled for one shared .NET10 runtime with unmodified owner assets'

# GCO-local REDscript Resolver/Generator is a private, separately owned
# .NET 10 application component, never another GRSP-managed executable or
# an extra .NET runtime. Generation stays evidence-only/nondeployable until
# all source-state, SCC and gameplay gates are satisfied.
$redscriptResolverApp=Join-Path $components 'redscript-resolver/app'
New-Item -ItemType Directory -Force $redscriptResolverApp | Out-Null
PublishManager 'src/GRedscript.Resolver/GRSP.Resolver.csproj' $redscriptResolverApp
$redscriptResolverDll=Join-Path $redscriptResolverApp 'GRedscript.Resolver.dll'
$redscriptResolverConfig=Join-Path $redscriptResolverApp 'GRedscript.Resolver.runtimeconfig.json'
foreach($file in @($redscriptResolverDll,$redscriptResolverConfig)) {
  if (!(Test-Path -LiteralPath $file -PathType Leaf)) {
    throw "Bundled GCO REDscript Resolver missing: $file"
  }
}
$redscriptRuntime=Get-Content -LiteralPath $redscriptResolverConfig -Raw
if ($redscriptRuntime -notmatch 'net10\.0' -or $redscriptRuntime -notmatch 'Microsoft.NETCore.App') {
  throw 'GCO REDscript Resolver does not target the shared .NET 10 runtime'
}
if (@(Get-ChildItem -LiteralPath $redscriptResolverApp -Recurse -File |
      Where-Object { $_.Name -eq 'coreclr.dll' }).Count -ne 0) {
  throw 'Second .NET runtime found in GCO REDscript Resolver'
}
Write-Host 'PASS GCO REDscript Resolver included as framework-dependent component in original app artifact'

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
  ('cl.exe /nologo /O2 /MT /DUNICODE /D_UNICODE /Fo:"{0}" "{1}" "{2}" /link /SUBSYSTEM:WINDOWS user32.lib shell32.lib advapi32.lib /OUT:"{3}"' -f $nativeObj,$nativeSource,$nativeRes,$nativeExe)
  'exit /b %errorlevel%'
)
Set-Content $cmdPath $buildLines -Encoding ascii
& $cmdPath
if ($LASTEXITCODE -ne 0 -or !(Test-Path $nativeExe)) { throw 'Native launcher build failed' }

# Exercise the real compiled native observer control: strict session-ID
# parsing, medium-integrity security descriptor, named-event signal/ack,
# and clean process exit. No ETW or UAC is needed for this source contract.
$observerCheck=Start-Process -FilePath $nativeExe -ArgumentList '--verify-presentmon-observer' -PassThru -Wait
try {
  if ($observerCheck.ExitCode -ne 0) {
    throw "GCO native PresentMon observer control failed ($($observerCheck.ExitCode))"
  }
  Write-Host 'PASS native PresentMon observer control and event permissions'
} finally { $observerCheck.Dispose() }

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
# Integrated GRSP native payload is imported unchanged from GRSP native CI.
$core=@(Get-ChildItem $root -Filter 'coreclr.dll' -File -Recurse)
if ($core.Count -ne 1 -or !$core[0].FullName.StartsWith($runtime, [StringComparison]::OrdinalIgnoreCase)) {
  throw "Expected one shared .NET runtime; found $($core.Count)"
}
foreach ($exe in @((Join-Path $root 'app/G-Total-Optimizer.App.exe'),(Join-Path $cet 'app/G-CET-Runtime-Profiler.App.exe'),(Join-Path $gr 'app/G-REDscript-Profiler.App.exe'),(Join-Path $cetResolverApp 'G-CET-Resolver.exe'))) {
  if (!(Test-Path $exe)) { throw "Manager executable absent $exe" }
  $json=Get-Content ([IO.Path]::ChangeExtension($exe,'.runtimeconfig.json')) -Raw
  if ($json -notmatch '10\.0') { throw "Framework-dependent manager did not retarget to net10: $exe" }
}
New-Item -ItemType Directory -Force (Join-Path $root 'RESULTS') | Out-Null
Set-Content (Join-Path $root 'VERSION.txt') $version -NoNewline
$grSourceCommit=(git -C vendor-grsp rev-parse HEAD).Trim()
if ($grSourceCommit.Length -ne 40) { throw 'GRSP source commit not pinned' }
$nativeOwnerRun=if ($env:GCO_GRSP_NATIVE_ARTIFACT_ROOT) {
  [string]$env:GCO_GRSP_NATIVE_OWNER_RUN_ID
} else { '' }
if ($env:GCO_GRSP_NATIVE_ARTIFACT_ROOT -and
    $nativeOwnerRun -notmatch '^[1-9][0-9]{5,18}$') {
  throw 'Owner-native provenance run ID missing for GCO instrumentation'
}
$nativeMode='GRSP_CI_AUTOMATIC_GCO_STRICT_F11'
$redscriptResolverSha=(Get-FileHash $redscriptResolverDll -Algorithm SHA256).Hash.ToLowerInvariant()
$provenance=@{
  testOnly=$true
  cet=@{ sourceSha='08f080ce0cfdb7f1df25ac24fa01aa7094ae8f44'; runId=41820765951; artifactId=13317131256; nativeSha256=$cm.targetCET.profilerSha256 }
  grsp=@{ releaseTag='v1.0.0'; releaseZipSha256='15b62ff6df95fa54f6a4fb6eb992e7aa07afe69c752922db34fdbe11b615e606'; sourceSha=$grSourceCommit; gcoNativeVariant=$Integrated; nativeBuildMode=$nativeMode; nativeOwnerRunId=$nativeOwnerRun; nativeSha256=(Get-FileHash (Join-Path $gr 'payload/G-REDscript-Profiler.dll') -Algorithm SHA256).Hash.ToLowerInvariant() }
  presentMon=@{ releaseTag='v2.6.0'; executableSha256='b2a706bc6ad475749e3b7e3409263aa1e6906d45bdcf993f6dbc0f660188f1af' }
  cetResolver=@{ sourceSha='08f080ce0cfdb7f1df25ac24fa01aa7094ae8f44'; ownerCiRun=41820765951; ownerArtifactId=13317096524; targetFramework='net10.0-windows'; nativeSha256=$nativeResolverHash; assemblySha256=(Get-FileHash (Join-Path $cetResolverApp 'G.CETProfiler.Core.dll') -Algorithm SHA256).Hash.ToLowerInvariant() }
  redscriptResolver=@{ sourceRepository='GCO_MAIN'; sourceCommit=[string]$env:GITHUB_SHA; framework='net10.0'; sha256=$redscriptResolverSha; generationAuthorized=$false }
}
$provenance | ConvertTo-Json -Depth 6 | Set-Content (Join-Path $components $(if ($Integrated) {'COMPONENT-PROVENANCE.json'} else {'TEST-PROVENANCE.json'})) -Encoding UTF8
if ($Integrated) {
  $entries=@(
    @{ id='cet'; executable='components/cet/G-CET-Runtime-Profiler.exe'; sourceCommit='08f080ce0cfdb7f1df25ac24fa01aa7094ae8f44'; ciRun=41820765951; artifactId=13317131256 },
    @{ id='grsp'; executable='components/grsp/G-REDscript-Profiler.exe'; sourceCommit=$grSourceCommit; releaseSha256='15b62ff6df95fa54f6a4fb6eb992e7aa07afe69c752922db34fdbe11b615e606' }
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
  # Independent analysis component; keep CET/GRSP installer admission and
  # original five-payload manifest semantics unchanged. All published source
  # assets are hash indexed for later Stage 3 read-only invocation.
  $resolverAssetEntries=@(Get-ChildItem -LiteralPath $cetResolverApp -File -Recurse |
    Sort-Object FullName | ForEach-Object {
      $relative=[IO.Path]::GetRelativePath($root,$_.FullName).Replace('\','/')
      @{ path=$relative; sha256=(Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant() }
    })
  $resolverEntry=@{
    id='cet-resolver'
    executable='components/cet-resolver/app/G-CET-Resolver.exe'
    sourceCommit='08f080ce0cfdb7f1df25ac24fa01aa7094ae8f44'
    ownerCiRun=41820765951
    ownerArtifactId=13317096524
    framework='net10.0-windows'
    nativeSha256=$nativeResolverHash
    files=$resolverAssetEntries
  }
  $resolverEntry.sha256=(Get-FileHash (Join-Path $cetResolverApp 'G-CET-Resolver.exe') -Algorithm SHA256).Hash.ToLowerInvariant()
  $redscriptResolverFiles=@(Get-ChildItem -LiteralPath $redscriptResolverApp -File -Recurse |
    Sort-Object FullName | ForEach-Object {
      @{ path=([IO.Path]::GetRelativePath($root,$_.FullName).Replace('\','/'));
         sha256=(Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant() }
    })
  $redscriptResolverEntry=@{
    id='redscript-resolver'
    executable='components/redscript-resolver/app/GRedscript.Resolver.exe'
    sourceRepository='GCO_MAIN'
    sourceCommit=[string]$env:GITHUB_SHA
    framework='net10.0'
    generationAuthorized=$false
    files=$redscriptResolverFiles
  }
  if (!(Test-Path (Join-Path $root $redscriptResolverEntry.executable) -PathType Leaf)) {
    throw 'Bundled GCO REDscript Resolver application executable missing'
  }
  $redscriptResolverEntry.sha256=(Get-FileHash (Join-Path $root $redscriptResolverEntry.executable) -Algorithm SHA256).Hash.ToLowerInvariant()
  $redscriptResolverEntry.assemblySha256=$redscriptResolverSha
  $payloads=@($payloadPaths | ForEach-Object {
    @{ path=$_; sha256=(Get-FileHash (Join-Path $root $_) -Algorithm SHA256).Hash.ToLowerInvariant() }
  })
  VerifyFile (Join-Path $root $payloadPaths[0]) $cm.targetCET.profilerSha256
  VerifyFile (Join-Path $root $payloadPaths[1]) $cm.zeroEngine.profilerSchedulerSha256
  VerifyFile (Join-Path $root $payloadPaths[4]) 'b2a706bc6ad475749e3b7e3409263aa1e6906d45bdcf993f6dbc0f660188f1af'
  @{ schemaVersion=2; components=$entries; payloads=$payloads; analysisTools=@($resolverEntry,$redscriptResolverEntry) } |
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
  Write-Host 'PASS integrated GCO: .NET10 CET+GRSP managers, pinned complete CET Resolver, source-owned native DLLs, PresentMon and verified payload manifest'
}
