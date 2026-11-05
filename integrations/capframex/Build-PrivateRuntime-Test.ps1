param(
  [string]$VendorFolder = 'vendor-capframex',
  [string]$OutputFolder = 'private-capx-test'
)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$version = (dotnet msbuild src/GTotalOptimizer.App/GTotalOptimizer.App.csproj -getProperty:Version).Trim()
if ($LASTEXITCODE -ne 0 -or $version -ne '1.1.1') { throw "Version drift: $version" }
$expectedCapXCommit = 'd1ab1e7e87a989ce8d55f38d5769327bb9f89369'
$commit = (& git -C $VendorFolder rev-parse HEAD).Trim()
if ($commit -ne $expectedCapXCommit) { throw "Unexpected CapFrameX source $commit" }
$expectedBlob = '7982880fd9746df69070522a78b8b5954c5f62cf'
$blob = (& git -C $VendorFolder hash-object source/CapFrameX.Configuration/AppDependencyChecker.cs).Trim()
if ($blob -ne $expectedBlob) { throw "CapFrameX upstream source drift $blob" }

# Rebuild a source-owned managed assembly; never alter the vendor EXE/native DLLs.
$checker = Join-Path $VendorFolder 'source/CapFrameX.Configuration/AppDependencyChecker.cs'
Copy-Item 'integrations/capframex/AppDependencyChecker.cs' $checker -Force
$project = Join-Path $VendorFolder 'source/CapFrameX.Configuration/CapFrameX.Configuration.csproj'
dotnet restore $project -p:Platform=x64
if ($LASTEXITCODE -ne 0) { throw 'CapX configuration source restore failed' }
dotnet build $project -c Release -p:Platform=x64 --no-restore
if ($LASTEXITCODE -ne 0) { throw 'CapX configuration source build failed' }
$built = @(Get-ChildItem (Join-Path $VendorFolder 'source/CapFrameX.Configuration/bin') -Filter 'CapFrameX.Configuration.dll' -Recurse -File | Where-Object { $_.FullName -match '[\\/]Release[\\/]' })
if ($built.Count -ne 1) { throw "Expected one CapFrameX.Configuration.dll, found $($built.Count)" }
Write-Host 'PASS source-pinned CapFrameX.Configuration compilation'

# Use official Microsoft installers to extract TWO frameworks into ONE private
# runtime root. No system installation, registry change or multiple runtimes.
New-Item -ItemType Directory -Force $OutputFolder | Out-Null
$root = (Resolve-Path $OutputFolder).Path
$runtime = Join-Path $root 'runtime'
$install = Join-Path $env:RUNNER_TEMP 'dotnet-install.ps1'
Invoke-WebRequest -Uri 'https://dot.net/v1/dotnet-install.ps1' -OutFile $install
& $install -Channel '10.0' -Runtime 'dotnet' -Architecture 'x64' -InstallDir $runtime -NoPath
if ($LASTEXITCODE -ne 0) { throw 'Private .NET core download failed' }
& $install -Channel '10.0' -Runtime 'windowsdesktop' -Architecture 'x64' -InstallDir $runtime -NoPath
if ($LASTEXITCODE -ne 0) { throw 'Private WindowsDesktop download failed' }
$info = & (Join-Path $runtime 'dotnet.exe') --list-runtimes
if ($LASTEXITCODE -ne 0 -or -not ($info | Select-String 'Microsoft.NETCore.App 10\.') -or -not ($info | Select-String 'Microsoft.WindowsDesktop.App 10\.')) { throw "Incomplete private runtime: $info" }
Write-Host "PASS private frameworks: $($info -join ' | ')"

# Framework-dependent unified app (no embedded duplicate runtime).
$app = Join-Path $root 'app'
dotnet publish src/GTotalOptimizer.App/GTotalOptimizer.App.csproj -c Release -r win-x64 --self-contained false -p:SelfContained=false -p:PublishSingleFile=false -p:PublishTrimmed=false -o $app
if ($LASTEXITCODE -ne 0 -or (Test-Path (Join-Path $app 'coreclr.dll'))) { throw 'Framework-dependent unified app publish failed' }

# Compile current native root launcher; it passes the private runtime to app.
$vswhere = Join-Path ([Environment]::GetFolderPath('ProgramFilesX86')) 'Microsoft Visual Studio/Installer/vswhere.exe'
if (-not (Test-Path $vswhere)) { throw 'VSwhere missing' }
$vs = (& $vswhere -latest -products * -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -property installationPath).Trim()
if (-not $vs) { throw 'C++ toolchain missing' }
$vcvars = Join-Path $vs 'VC/Auxiliary/Build/vcvars64.bat'
$native = Join-Path $env:RUNNER_TEMP 'g-total-test-launcher'
New-Item -ItemType Directory -Force $native | Out-Null
$rcText = (Get-Content 'src/GTotalOptimizer.Launcher/launcher.rc.in' -Raw).Replace('@VERSION_COMMAS@', $version.Replace('.', ',')).Replace('@VERSION_DOTS@', $version)
Set-Content (Join-Path $native 'launcher.rc') $rcText -Encoding ASCII
$source = (Resolve-Path 'src/GTotalOptimizer.Launcher/launcher.c').Path
$rc = Join-Path $native 'launcher.rc'
$res = Join-Path $native 'launcher.res'
$obj = Join-Path $native 'launcher.obj'
$exe = Join-Path $root 'G-Total-Optimizer.exe'
$cmd = Join-Path $native 'build.cmd'
@"
@echo off
call "$vcvars" >nul
if errorlevel 1 exit /b %errorlevel%
rc.exe /nologo /fo"$res" "$rc"
if errorlevel 1 exit /b %errorlevel%
cl.exe /nologo /O2 /MT /DUNICODE /D_UNICODE /Fo:"$obj" "$source" "$res" /link /SUBSYSTEM:WINDOWS user32.lib /OUT:"$exe"
exit /b %errorlevel%
"@ | Set-Content $cmd -Encoding ASCII
& $cmd
if ($LASTEXITCODE -ne 0 -or -not (Test-Path $exe)) { throw 'Root launcher native compile failed' }

# Keep the official CapFrameX exe, PresentMon, hooks, graphics plugins and
# other files unchanged. Only replace the source-rebuilt managed checker DLL.
$url = 'https://github.com/CXWorld/CapFrameX/releases/download/v1.9.1_beta/CapFrameX_1.9.1.2_Beta_Portable.zip'
$archive = Join-Path $env:RUNNER_TEMP 'CapFrameX-1.9.1.2-Beta-Portable.zip'
Invoke-WebRequest -Uri $url -OutFile $archive
$expectedZip = 'acd8f82598bc794d8f666996eb8157c445c05af0fc04c9e4129e4ac76bf1086a'
$hash = (Get-FileHash $archive -Algorithm SHA256).Hash.ToLowerInvariant()
if ($hash -ne $expectedZip) { throw "Official CapFrameX checksum differs $hash" }
$extract = Join-Path $env:RUNNER_TEMP 'CapFrameX-official'
Expand-Archive $archive $extract -Force
$original = @(Get-ChildItem $extract -Filter CapFrameX.exe -Recurse -File)
if ($original.Count -ne 1) { throw "Unexpected release layout: $($original.Count) executables" }
$capx = Join-Path $root 'modules/CapFrameX'
New-Item -ItemType Directory -Force $capx | Out-Null
Copy-Item (Join-Path $original[0].Directory.FullName '*') $capx -Recurse -Force
$dest = Join-Path $capx 'CapFrameX.Configuration.dll'
if (-not (Test-Path $dest)) { throw 'Official CapFrameX.Configuration.dll missing' }
$vendorDll = (Get-FileHash $dest -Algorithm SHA256).Hash
Copy-Item $built[0].FullName $dest -Force
Write-Host "CapFrameX managed dependency component: official=$vendorDll modified=$((Get-FileHash $dest -Algorithm SHA256).Hash)"

# App-local Visual C++ Redistributable DLLs from official VS redist tree.
$base = Join-Path $vs 'VC/Redist/MSVC'
$dirs = @(Get-ChildItem $base -Directory -Recurse -ErrorAction SilentlyContinue | Where-Object { $_.Name -like 'Microsoft.VC*.CRT' -and $_.FullName -match '[\\/]x64[\\/]' -and (Test-Path (Join-Path $_.FullName 'vcruntime140.dll')) -and (Test-Path (Join-Path $_.FullName 'vcruntime140_1.dll')) -and (Test-Path (Join-Path $_.FullName 'msvcp140.dll')) } | Sort-Object FullName -Descending)
if ($dirs.Count -eq 0) { throw 'No approved complete MSVC x64 CRT redist payload in Visual Studio' }
foreach ($dll in @(Get-ChildItem $dirs[0].FullName -Filter '*.dll' -File)) { Copy-Item $dll.FullName (Join-Path $capx $dll.Name) -Force }
Write-Host "PASS app-local x64 VC runtime from $($dirs[0].FullName)"

# CapFrameX.exe requires elevation. A plain .cmd child can lose custom
# process environment through UAC; an elevated native parent is reliable.
$helperSource = (Resolve-Path 'integrations/capframex/launcher.c').Path
$helperObj = Join-Path $native 'capframex-helper.obj'
$helperExe = Join-Path $capx 'Start-CapFrameX-Private.exe'
$helperBat = Join-Path $native 'build-capframex-helper.cmd'
@"
@echo off
call "$vcvars" >nul
if errorlevel 1 exit /b %errorlevel%
cl.exe /nologo /O2 /MT /DUNICODE /D_UNICODE /Fo:"$helperObj" "$helperSource" /link /SUBSYSTEM:WINDOWS /MANIFESTUAC:"level='requireAdministrator' uiAccess='false'" user32.lib /OUT:"$helperExe"
exit /b %errorlevel%
"@ | Set-Content $helperBat -Encoding ASCII
& $helperBat
if ($LASTEXITCODE -ne 0 -or -not (Test-Path $helperExe)) { throw 'Elevated native CapX helper failed compilation' }
New-Item -ItemType Directory -Force (Join-Path $root 'RESULTS') | Out-Null
Set-Content -Path (Join-Path $root 'VERSION.txt') -Value $version -NoNewline
if (-not (Test-Path (Join-Path $capx 'LICENSE'))) {
  Copy-Item (Join-Path $VendorFolder 'LICENSE') (Join-Path $capx 'LICENSE-CapFrameX.txt')
}
Set-Content -Path (Join-Path $root 'README-TEST.txt') -Encoding UTF8 -Value @'
G-Total-Optimizer v1.1.1 — CapFrameX private runtime TEST only.
1. Extract the ENTIRE archive to a writable folder.
2. Double-click G-Total-Optimizer.exe to verify our app opens.
3. Double-click modules\CapFrameX\Start-CapFrameX-Private.exe to test CapFrameX.
4. Accept Windows UAC elevation; verify CapFrameX opens, select Cyberpunk 2077 and test F11.
5. If errors occur, send modules\CapFrameX\Portable\Logs\private-dotnet-host-trace.txt
   and the most recent CapFrameX log.

Private .NET 10 Desktop is in runtime/; one coreclr.dll.
CapFrameX.exe and its native capture hooks are from the official unmodified
1.9.1.2 beta portable release; the dependency-check DLL was source-rebuilt.
No .NET or VC++ installation or registry changes are made.
CET/GRSP remain unintegrated. No optimization generation is enabled.
'@
if (-not (Test-Path (Join-Path $capx 'portable.json'))) { throw 'CapFrameX portable.json missing' }
if (-not (Test-Path (Join-Path $capx 'CapFrameX.runtimeconfig.json'))) { throw 'CapFrameX runtimeconfig.json missing' }
if (-not (Test-Path (Join-Path $app 'G-Total-Optimizer.App.runtimeconfig.json'))) { throw 'Unified app runtimeconfig.json missing' }
$core = @(Get-ChildItem $root -Filter coreclr.dll -Recurse -File)
if ($core.Count -ne 1 -or -not $core[0].FullName.StartsWith($runtime, [StringComparison]::OrdinalIgnoreCase)) { throw "Expected one shared runtime, found $($core.Count)" }
Write-Host "PASS private one-runtime bundle; CapFrameX source check rebuilt; app-local x64 VC; root=$root"
