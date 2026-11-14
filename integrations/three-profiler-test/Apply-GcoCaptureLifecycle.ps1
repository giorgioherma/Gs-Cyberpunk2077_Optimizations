# Applies only to disposable, exact-pinned owner checkouts during GCO packaging.
# Standalone CET/GRSP repositories, releases and their capture behavior are unchanged.
$ErrorActionPreference='Stop'
Set-StrictMode -Version Latest
function Patch([string]$Path,[string]$Old,[string]$New) {
  $file=(Resolve-Path -LiteralPath $Path).Path
  $crlf=[string][char]13 + [string][char]10
  $lf=[string][char]10
  $txt=[IO.File]::ReadAllText($file).Replace($crlf,$lf)
  $Old=$Old.Replace($crlf,$lf); $New=$New.Replace($crlf,$lf)
  if (!$txt.Contains($Old) -or $txt.IndexOf($Old,[StringComparison]::Ordinal) -ne $txt.LastIndexOf($Old,[StringComparison]::Ordinal)) {
    throw "Expected one pinned source anchor in $Path : $($Old.Substring(0,[Math]::Min($Old.Length,90)))"
  }
  [IO.File]::WriteAllText($file,$txt.Replace($Old,$New),[Text.UTF8Encoding]::new($false))
}
$cc='vendor-cet/src/G.CETProfiler.Core/Services/ProfilerService.cs'
$cp='vendor-cet/src/G.CETProfiler.App/Program.cs'
$gm='vendor-grsp/manager/ManagerServices.cs'
$gp='vendor-grsp/manager/Program.cs'
$gr='vendor-grsp/src/lib.rs'
# The collectors write their temporary archives directly under GCO's results staging.
Patch $cc 'resultsRoot = Path.Combine(this.packageRoot, "RESULTS");' 'resultsRoot = Environment.GetEnvironmentVariable("GCO_COMPONENT_RESULTS_ROOT") ?? Path.Combine(this.packageRoot, "RESULTS");'
Patch $gm 'public static string ArchiveResultsDirectory => Path.Combine(PackageRoot, "RESULTS");' 'public static string ArchiveResultsDirectory => Environment.GetEnvironmentVariable("GCO_COMPONENT_RESULTS_ROOT") ?? Path.Combine(PackageRoot, "RESULTS");'

# The CET managed state and manifest inventory prove deletion ownership.
Patch $cc '    public string? Restore(string gameRoot)' @'
    public int DiscardLive(string gameRoot)
    {
        AssertGameClosed();
        var paths = GetValidatedPaths(gameRoot);
        _ = ReadState(paths, allowMissing: false) ??
            throw new InvalidOperationException("CET managed installation required for discard.");
        if (Directory.Exists(paths.CetRoot) &&
            (File.GetAttributes(paths.CetRoot) & FileAttributes.ReparsePoint) != 0)
            throw new InvalidDataException("Linked CET results directory refused.");
        var live = GetLiveResults(paths);
        foreach (var file in live)
            if ((File.GetAttributes(file) & FileAttributes.ReparsePoint) != 0)
                throw new InvalidDataException("Linked CET result refused.");
        foreach (var file in live) FileSystemService.DeleteFileIfExists(file);
        if (GetLiveResults(paths).Count != 0)
            throw new IOException("CET live results still present.");
        return live.Count;
    }

    public string? Restore(string gameRoot)
'@
Patch $cc 'var archived = ArchiveCompletedCaptureOrClearTemplates(paths);' 'DiscardLive(gameRoot); string? archived = null;'
Patch $cp 'IProfilerService service = new ProfilerService();' 'var service = new ProfilerService();'
Patch $cp 'case "--reset": command = "reset"; break;' ('case "--reset": command = "reset"; break;' + [Environment]::NewLine + '                case "--discard-live": command = "discard-live"; break;')
Patch $cp '            "restore" => new' @'
            "discard-live" => new { ok = true, discarded = service.DiscardLive(game), status = service.GetStatus(game) },
            "restore" => new
'@

# GRSP's GCO variant never implicitly completes recording on game shutdown.
Patch $gr @'
unsafe extern "C" fn on_shutdown(_app: &GameApp) {
    // There is no pause/resume mode. Closing the game while recording acts as
    // an implicit STOP so useful work is exported instead of discarded.
    // Use a short drain window so shutdown cannot be delayed by the normal
    // interactive STOP timeout.
    if PROFILE_STATE.load(Ordering::Acquire) == STATE_RECORDING {
        finish_capture_internal(false, 1_000);
    }
}
'@ @'
unsafe extern "C" fn on_shutdown(_app: &GameApp) {
    // Bundled GCO capture: no export without the user's second F11.
}
'@
Patch $gr @'
    let dump_ok = dump_results().is_ok();
    LAST_DUMP_OK.store(dump_ok, Ordering::Release);
    let _ = write_status_file();

    if dump_ok {
'@ @'
    let dump_ok = dump_results().is_ok();
    LAST_DUMP_OK.store(dump_ok, Ordering::Release);
    let _ = write_status_file();
    if dump_ok && play_stop_signal {
        if let Some(dir) = capture_results_dir() {
            let _ = fs::write(dir.join("GCO-F11-Complete.flag"), b"F11_STOP\n");
        }
    }

    if dump_ok {
'@
Patch $gm '        File.Exists(Path.Combine(path, "GRSP_Status.txt"));' @'
        File.Exists(Path.Combine(path, "GRSP_Status.txt")) &&
        File.Exists(Path.Combine(path, "GCO-F11-Complete.flag")) &&
        File.ReadAllText(Path.Combine(path, "GCO-F11-Complete.flag")).Trim() == "F11_STOP";
'@
Patch $gm '    public static string Restore(string gameRoot)' @'
    // The original owner state FILE is the restore ownership marker even if its
    // JSON is damaged. Reject linked paths before deleting any managed data.
    private static void AssertUnlinkedManagedTree(string directory)
    {
        if (!Directory.Exists(directory)) return;
        if ((File.GetAttributes(directory) & FileAttributes.ReparsePoint) != 0)
            throw new InvalidDataException("Linked GRSP managed directory refused.");
        foreach (var entry in Directory.EnumerateFileSystemEntries(directory))
        {
            if ((File.GetAttributes(entry) & FileAttributes.ReparsePoint) != 0)
                throw new InvalidDataException("Linked GRSP managed entry refused.");
            if (Directory.Exists(entry)) AssertUnlinkedManagedTree(entry);
        }
    }

    // GCO-only: discard explicitly uncollected output, not DLLs or manager state.
    public static int DiscardLive(string gameRoot)
    {
        EnsureValidRoot(gameRoot);
        if (IsGameRunning()) throw new InvalidOperationException("Close Cyberpunk before discarding results.");
        if (!File.Exists(StatePath(gameRoot)))
            throw new InvalidOperationException("GRSP ownership marker is missing.");
        _ = LoadState(StatePath(gameRoot));
        var results = NativeResultsPath(gameRoot);
        if (!Directory.Exists(results)) return 0;
        AssertUnlinkedManagedTree(DataDirectory(gameRoot));
        var count = Directory.EnumerateFileSystemEntries(results).Count();
        DeleteDirectoryContentsForce(results);
        if (Directory.EnumerateFileSystemEntries(results).Any())
            throw new IOException("GRSP live output still present.");
        return count;
    }

    public static string Restore(string gameRoot)
'@
# GRSP managed restore must never archive uncollected data.
Patch $gm @'
        string? archived = null;
        string? archiveWarning = null;

        try
        {
            archived = CollectLiveResultsInternal(gameRoot, requireCompletedCapture: false);
        }
        catch (Exception ex)
        {
            archiveWarning = ex.Message;

            // Best-effort preservation of the whole managed data folder before
            // cleanup. Failure here is reported, but it does not block restore.
            try
            {
                var dataDir = DataDirectory(gameRoot);
                if (DirectoryHasEntries(dataDir))
                {
                    Directory.CreateDirectory(ArchiveResultsDirectory);
                    var recovery = UniqueDirectory(
                        ArchiveResultsDirectory,
                        $"RestoreRecovery_{DateTime.Now:yyyyMMdd-HHmmss}");
                    CopyDirectoryVerified(dataDir, recovery);
                    archived = recovery;
                }
            }
            catch (Exception recoveryEx)
            {
                archiveWarning += " Recovery copy also failed: " + recoveryEx.Message;
            }
        }
'@ @'
        // RESTORE already owns the entire managed data directory through the
        // original marker. Capture-discard JSON validation must not gate it.
        AssertUnlinkedManagedTree(DataDirectory(gameRoot));
        string? archived = null;
        string? archiveWarning = null;
'@
# Latest complete F11 run only. Earlier F11 attempts stay uncollected.
Patch $gm @'
        string? latestDestination = null;
        foreach (var source in captures)
            latestDestination = ArchiveDirectoryAndRemoveSource(source);

        var leftovers = Directory.EnumerateFileSystemEntries(results).ToList();
        if (leftovers.Count > 0)
        {
            if (latestDestination is null)
            {
                latestDestination = UniqueDirectory(
                    ArchiveResultsDirectory,
                    $"RecoveredLiveOutput_{DateTime.Now:yyyyMMdd-HHmmss}");
                Directory.CreateDirectory(latestDestination);
            }

            var metadataRoot = Path.Combine(latestDestination, "Data", "Metadata");
            Directory.CreateDirectory(metadataRoot);

            foreach (var entry in leftovers)
            {
                var target = UniquePath(metadataRoot, Path.GetFileName(entry));
                if (File.Exists(entry))
                {
                    CopyFileVerified(entry, target);
                    File.Delete(entry);
                }
                else if (Directory.Exists(entry))
                {
                    CopyDirectoryVerified(entry, target);
                    Directory.Delete(entry, true);
                }
            }
        }

        if (Directory.Exists(results) && !Directory.EnumerateFileSystemEntries(results).Any())
            Directory.Delete(results);

        return latestDestination;
'@ @'
        string? latestDestination = captures.Count == 0
            ? null : ArchiveDirectoryAndRemoveSource(captures[^1]);
        _ = DiscardLive(gameRoot);
        return latestDestination;
'@
Patch $gp '?? (parsed.Has("collect") ? "collect" : null)' ('?? (parsed.Has("collect") ? "collect" : null)' + [Environment]::NewLine + '                ?? (parsed.Has("discard-live") ? "discard-live" : null)')
Patch $gp '                "collect" => CollectWithReport(gameRoot),' ('                "collect" => CollectWithReport(gameRoot),' + [Environment]::NewLine + '                "discard-live" => new { discarded = ManagerServices.DiscardLive(gameRoot) },')
Write-Host 'PASS pinned disposable GCO-only source adaptations'
