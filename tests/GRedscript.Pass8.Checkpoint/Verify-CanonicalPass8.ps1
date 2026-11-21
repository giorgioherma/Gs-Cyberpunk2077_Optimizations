param(
  [Parameter(Mandatory=$true)][string]$PostGenericRoot,
  [Parameter(Mandatory=$true)][string]$IsolatedOverlayRoot
)
$ErrorActionPreference = 'Stop'
$game = (Resolve-Path -LiteralPath $PostGenericRoot).Path
$isolated = (Resolve-Path -LiteralPath $IsolatedOverlayRoot).Path
$dll = 'src/GRedscript.Resolver/bin/Release/net10.0/GRedscript.Resolver.dll'
if (!(Test-Path -LiteralPath $dll -PathType Leaf)) {
  throw 'PASS8_CANONICAL_RESOLVER_NOT_BUILT'
}
$work = Join-Path $env:RUNNER_TEMP ('gco-pass8-canonical-' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Force -Path $work | Out-Null
try {
  $plan = Join-Path $work 'plan.json'
  $analysis = Join-Path $work 'analysis.json'
  $output = Join-Path $work 'canonical'
  & dotnet $dll plan-redscript --game $game --out $plan
  if ($LASTEXITCODE -ne 0) { throw 'PASS8_CANONICAL_PLAN_FAILED' }
  $p = Get-Content -LiteralPath $plan -Raw | ConvertFrom-Json
  if ([int]$p.implementedRuleCount -ne 10 -or
      [string]::IsNullOrWhiteSpace($p.sourceStackSha256)) {
    throw 'PASS8_CANONICAL_TEN_GENERIC_PLAN_INVALID'
  }
  # A minimal sealed-capture schema fixture is used for this *CI-only*
  # source-stage contract. It is not accepted as real captured evidence.
  $fixture = @{
    schemaVersion = 'grsp.resolver.input.v1'
    generationAuthorized = $false
    capture = @{ collectionProvenance = 'GCO_INDEXED_COLLECTED_BYTES_VERIFIED' }
    source = @{ sourceStackSha256 = [string]$p.sourceStackSha256 }
  }
  $fixture | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $analysis -Encoding utf8
  & dotnet $dll stage-redscript-plan --game $game --plan $plan --analysis $analysis --out $output
  if ($LASTEXITCODE -ne 0) { throw 'PASS8_CANONICAL_STAGE_FAILED' }
  $report = Get-Content -LiteralPath (Join-Path $output 'stage-result.json') -Raw | ConvertFrom-Json
  $required = @(
    @{ Id='pass8-chipware-throughput'; Order=37; Edits=3 },
    @{ Id='pass8-chipware-combat-cache'; Order=38; Edits=9 },
    @{ Id='pass8-immersive-cyberware'; Order=39; Edits=3 }
  )
  foreach($stage in $required) {
    $rows = @($report.completed | Where-Object { $_.id -eq $stage.Id })
    if($rows.Count -ne 1 -or [int]$rows[0].order -ne $stage.Order -or
       [int]$rows[0].rewrittenExpressions -ne $stage.Edits -or
       @($report.skipped | Where-Object { $_.id -eq $stage.Id }).Count -gt 0) {
      throw ('PASS8_CANONICAL_STAGE_MISSING_OR_REFUSED:' + $stage.Id)
    }
  }
  $actual = Join-Path $output 'effective-preview'
  $files = @(Get-ChildItem -LiteralPath (Join-Path $isolated 'r6/scripts') -Filter '*.reds' -File -Recurse)
  if ($files.Count -ne 8) { throw ('PASS8_ISOLATED_FINAL_FILE_COUNT:' + $files.Count) }
  foreach($file in $files) {
    $rel = [IO.Path]::GetRelativePath($isolated, $file.FullName)
    $dest = Join-Path $actual $rel
    if (!(Test-Path -LiteralPath $dest -PathType Leaf)) {
      throw ('PASS8_CANONICAL_OUTPUT_MISSING:' + $rel)
    }
    $a = (Get-FileHash -LiteralPath $file.FullName -Algorithm SHA256).Hash
    $b = (Get-FileHash -LiteralPath $dest -Algorithm SHA256).Hash
    if ($a -ne $b) {
      throw ('PASS8_CANONICAL_INDEPENDENT_BYTE_MISMATCH:' + $rel)
    }
  }
  $effectiveFiles = @(Get-ChildItem -LiteralPath (Join-Path $actual 'r6/scripts') -Filter '*.reds' -Recurse -File)
  if ($effectiveFiles.Count -ne [int]$report.generatedFiles) {
    throw 'PASS8_CANONICAL_STAGE_GENERATED_COUNT_MISMATCH'
  }
  Write-Host ('PASS8_CANONICAL_ONE_PASS_ACCEPTED: stages 37-39, edits=15, ' +
    'independent file byte parity=8, combined files=' + $effectiveFiles.Count)
  & ./tests/GRedscript.Resolver.Tests/Verify-Phase2Checkpoint.ps1 `
    -PostGenericRoot $game `
    -CompilerRoot '.phase2-private/Compiler' `
    -ResolverDll $dll `
    -SemanticOverlayRoot $actual
  if (!$? -or $LASTEXITCODE -ne 0) { throw 'PASS8_CANONICAL_SCC_FAILED' }
  Write-Host 'PASS8_CANONICAL_FULL_EFFECTIVE_SOURCE_SCC_PASS'
}
finally {
  if (Test-Path -LiteralPath $work) {
    Remove-Item -LiteralPath $work -Recurse -Force
  }
}
