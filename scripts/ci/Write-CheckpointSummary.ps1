param(
    [Parameter(Mandatory=$true)][string]$WorkflowFile,
    [Parameter(Mandatory=$true)][string]$CheckpointFile
)
# Evidence metadata only: failures must not turn a valid build red or hide a failed test.
if ([string]::IsNullOrWhiteSpace($env:GITHUB_STEP_SUMMARY)) { exit 0 }
$repo = $env:GITHUB_REPOSITORY
$branch = $env:GITHUB_REF_NAME
$head = $env:GITHUB_SHA
$runId = $env:GITHUB_RUN_ID
$checkpoint = 'Manual dispatch'
if (Test-Path -LiteralPath $CheckpointFile -PathType Leaf) {
    $marker = Get-Content -LiteralPath $CheckpointFile |
        Where-Object { $_ -match '^checkpoint=' } | Select-Object -Last 1
    if ($marker) { $checkpoint = $marker.Substring('checkpoint='.Length) }
}
function WriteSummary([string]$line) {
    Add-Content -LiteralPath $env:GITHUB_STEP_SUMMARY -Value $line -Encoding utf8
}
WriteSummary '## Consolidated validation checkpoint'
WriteSummary ''
WriteSummary ('- Branch: {0}' -f $branch)
WriteSummary ('- Milestone: {0}' -f $checkpoint)
WriteSummary ('- Commit: [{0}](https://github.com/{1}/commit/{2})' -f $head.Substring(0,8),$repo,$head)
WriteSummary ('- Run: [#{0}](https://github.com/{1}/actions/runs/{2})' -f $env:GITHUB_RUN_NUMBER,$repo,$runId)
WriteSummary ''
WriteSummary 'This describes change coverage, not compilation/gameplay success. The actual job result remains authoritative.'
try {
    $encoded = [Uri]::EscapeDataString($branch)
    $endpoint = ('repos/{0}/actions/workflows/{1}/runs?branch={2}&status=success&per_page=30' -f $repo,$WorkflowFile,$encoded)
    $historyText = (& gh api $endpoint 2>$null | Out-String)
    if ($LASTEXITCODE -ne 0) { throw 'Cannot retrieve previous workflow runs' }
    $history = ConvertFrom-Json -InputObject $historyText
    $previous = $null
    $comparison = $null
    foreach ($candidate in @($history.workflow_runs)) {
        if ($candidate.head_branch -ne $branch -or
            $candidate.conclusion -ne 'success' -or
            $candidate.head_sha -eq $head -or
            [string]$candidate.id -eq $runId) { continue }
        $compareEndpoint = ('repos/{0}/compare/{1}...{2}?per_page=100' -f $repo,$candidate.head_sha,$head)
        $compareText = (& gh api $compareEndpoint 2>$null | Out-String)
        if ($LASTEXITCODE -ne 0 -or !$compareText) { continue }
        $diff = ConvertFrom-Json -InputObject $compareText
        if ($diff.behind_by -eq 0 -and $diff.status -in @('ahead','identical')) {
            $previous = $candidate
            $comparison = $diff
            break
        }
    }
    WriteSummary ''
    WriteSummary '### Changes covered since the previous successful checkpoint'
    if ($null -eq $previous) {
        WriteSummary 'No prior successful ancestor found in the most recent 30 successful workflow runs. No unrelated comparison baseline was assumed.'
    } else {
        WriteSummary ('- Previous verified run: [#{0}](https://github.com/{1}/actions/runs/{2})' -f $previous.run_number,$repo,$previous.id)
        WriteSummary ('- Number of commits: **{0}**' -f $comparison.total_commits)
        WriteSummary ('- [Compare all changed files and commits]({0})' -f $comparison.html_url)
        WriteSummary ''
        foreach ($entry in @($comparison.commits | Select-Object -Last 12)) {
            $title = ([string]$entry.commit.message -split "\r?\n")[0]
            $title = $title.Replace('|','-').Replace('<','').Replace('>','')
            WriteSummary ('- [{0}](https://github.com/{1}/commit/{2}): {3}' -f $entry.sha.Substring(0,8),$repo,$entry.sha,$title)
        }
        if ($comparison.total_commits -gt $comparison.commits.Count) {
            WriteSummary 'Commit list abbreviated; the compare link contains the complete range.'
        }
    }
} catch {
    WriteSummary ''
    WriteSummary 'Change-history lookup unavailable. This non-blocking presentation step does not alter validation results.'
    Write-Warning ('Non-blocking summary lookup: ' + $_.Exception.Message)
}
