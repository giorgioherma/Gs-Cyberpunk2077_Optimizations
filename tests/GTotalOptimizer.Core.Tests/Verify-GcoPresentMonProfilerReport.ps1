param(
    [Parameter(Mandatory=$true)][string]$FixtureZip,
    [Parameter(Mandatory=$true)][string]$PackageRoot
)
$ErrorActionPreference='Stop'
Set-StrictMode -Version Latest
$FixtureZip=(Resolve-Path -LiteralPath $FixtureZip -ErrorAction Stop).Path
$PackageRoot=(Resolve-Path -LiteralPath $PackageRoot -ErrorAction Stop).Path
$work=Join-Path $env:RUNNER_TEMP ('gco-profiler-report-'+[Guid]::NewGuid().ToString('N'))
$game=Join-Path $work 'game'
$sessionCopy=$null
try {
    New-Item -ItemType Directory -Force -Path $game | Out-Null
    $source=Join-Path $work 'fixture'
    Expand-Archive -LiteralPath $FixtureZip -DestinationPath $source -Force
    $indexes=@(Get-ChildItem -LiteralPath $source -Recurse -File -Filter 'collection.json')
    if($indexes.Count -ne 1) {
        throw 'Public GCO fixture must contain exactly one indexed collection.'
    }
    $original=$indexes[0].Directory.FullName
    $index=Get-Content -LiteralPath $indexes[0].FullName -Raw | ConvertFrom-Json -Depth 70
    $id=[string]$index.SessionId
    if($id -notmatch '^[a-fA-F0-9]{32}$' -or $null -eq $index.Cet -or
        $null -eq $index.Redscript -or $null -eq $index.FrameTimeEvidence) {
        throw 'The real three-profiler capture is incomplete.'
    }
    $results=Join-Path $PackageRoot 'RESULTS'
    New-Item -ItemType Directory -Force -Path $results | Out-Null
    $sessionCopy=Join-Path $results (Split-Path $original -Leaf)
    if(Test-Path -LiteralPath $sessionCopy) {
        throw 'Real report test refuses to overwrite an existing captured session.'
    }
    Copy-Item -LiteralPath $original -Destination $sessionCopy -Recurse -ErrorAction Stop
    $index.GameRoot=$game
    $index.CollectionAttempted=$true
    $index.Restored=$false
    $index.FrameTimeCsv=Join-Path $sessionCopy (
        ([string]$index.FrameTimeEvidence.Path).Replace('/','\'))
    $index | ConvertTo-Json -Depth 70 | Set-Content -LiteralPath (
        Join-Path $sessionCopy 'collection.json') -Encoding utf8
    # No live mod tree and no private source repository. The two original
    # reporter CLIs consume only the collected capture + real PresentMon.
    $project='tests/GTotalOptimizer.Core.Tests/GTotalOptimizer.Core.Tests.csproj'
    $output=@(& dotnet run --configuration Release --project $project -- --stage5-profiler-report $PackageRoot $game $id 2>&1)
    if($LASTEXITCODE -ne 0) {
        $errors=@($output | ForEach-Object {[string]$_} |
            Where-Object {$_ -match 'Exception:|Unhandled exception|^FAIL ' } |
            Select-Object -First 2)
        $message=$errors -join ' | '
        foreach($private in @($work,$PackageRoot,$game,$sessionCopy)){
            $message=$message.Replace($private,'[PATH]')
        }
        $message=[regex]::Replace($message,'[A-Za-z]:\\[^:\r\n]*','[PATH]')
        if($message.Length -gt 500){$message=$message.Substring(0,500)}
        throw ('Real-source independent PresentMon report failed: '+$message)
    }
    $proof=@($output | Where-Object {
        [string]$_ -like 'PASS GCO real CET+GRSP+PresentMon profiler report:*'
    })
    if($proof.Count -ne 1) {
        throw 'Production report did not verify native CET, GRSP and genuine PresentMon.'
    }
    $reportFolder=Join-Path $sessionCopy 'Reports'
    $json=Get-Content -LiteralPath (Join-Path $reportFolder 'GCO_Profiler_Report.json') -Raw |
        ConvertFrom-Json -Depth 70
    if([int]$json.presentMonFrameCount -ne [int]$index.PresentMonAlignedFrames -or
        $json.frameEvidence.provider -ne 'PRESENTMON_2_6' -or
        $json.frameEvidence.source -ne [string]$index.FrameTimeEvidence.Path -or
        @($json.timeline | Where-Object {$null -ne $_.presentIntervalMaxMs}).Count -lt 2) {
        throw 'Consolidated profiler report does not preserve authentic indexed PresentMon evidence.'
    }
    foreach($file in @(
        'GCO_Profiler_Report.html',
        'CET/CET_Report.html',
        'CET/CET_Resolver_Input.json',
        'GRSP/GRSP_Report.html',
        'GRSP/GRSP_Summary.json'
    )){
        if(!(Test-Path -LiteralPath (Join-Path $reportFolder $file) -PathType Leaf)){
            throw 'Combined report omitted an original owner report or machine handoff.'
        }
    }
    foreach($domain in @('G-CET','G-REDscript','G-RED')){
        $ownerData=if($domain -eq 'G-CET'){$index.Cet}else{$index.Redscript}
        $folder=Join-Path $sessionCopy $domain
        if(!(Test-Path $folder)){continue}
        foreach($entry in @($ownerData.Files)){
            $p=Join-Path $folder (([string]$entry.Path).Replace('/','\'))
            if(!(Test-Path -LiteralPath $p -PathType Leaf) -or
                (Get-FileHash -LiteralPath $p -Algorithm SHA256).Hash -ne [string]$entry.Sha256){
                throw 'GCO reporting changed an indexed native owner capture.'
            }
        }
    }
    Write-Host $proof[0]
    Write-Host 'PASS combined report: genuine PresentMon, zero CapFrameX fallback, full CET+GRSP owner reports and untouched indexed evidence'
}
finally {
    if($null -ne $sessionCopy -and (Test-Path -LiteralPath $sessionCopy)){
        Remove-Item -LiteralPath $sessionCopy -Recurse -Force -ErrorAction Stop
    }
    if(Test-Path -LiteralPath $work){
        Remove-Item -LiteralPath $work -Recurse -Force -ErrorAction Stop
    }
}
$global:LASTEXITCODE=0
