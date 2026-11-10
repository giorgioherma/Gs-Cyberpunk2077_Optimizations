using System.Diagnostics;
using System.Text.Json;

namespace GTotalOptimizer.Core;

// The unified application coordinates the owners' existing transactions;
// it does not implement a second native profiler installer.
public sealed record LiveProfilerState(
    string Cet, bool CetManaged, bool CetCaptureReady,
    string Grsp, bool GrspManaged, bool GrspCaptureReady)
{
    public bool BothInstalled => Cet == "PROFILER_ACTIVE" && CetManaged &&
                                 Grsp == "INSTALLED_CURRENT" && GrspManaged;
    public bool CleanForInstall => Cet == "OFFICIAL" && !CetManaged &&
                                   Grsp == "NOT_INSTALLED" && !GrspManaged;
    public bool BothCaptured => BothInstalled && CetCaptureReady && GrspCaptureReady;

    // The two source owners decide capture completion. A journal alone means
    // only that profiler files were installed; it never unlocks collection.
    public bool CanCollect(bool hasCurrentSession, bool gameRunning) =>
        hasCurrentSession && !gameRunning && BothInstalled &&
        (CetCaptureReady || GrspCaptureReady);
    public bool CanLaunch(bool hasCurrentSession, bool gameRunning) =>
        hasCurrentSession && !gameRunning && BothInstalled && !BothCaptured;
}

public sealed record ProfilerWorkflowResult(
    bool Ok, string Detail, string? CetArchive = null, string? GrspArchive = null,
    bool CaptureVerified = false, bool RestoreVerified = false);

public static class LiveProfilerStateParser
{
    public static LiveProfilerState Parse(string cetJson, string grspJson)
    {
        using var cetDoc = JsonDocument.Parse(cetJson);
        using var grspDoc = JsonDocument.Parse(grspJson);
        var c = cetDoc.RootElement;
        var r = grspDoc.RootElement;
        if (!c.TryGetProperty("ok", out var cok) || cok.ValueKind != JsonValueKind.True ||
            !c.TryGetProperty("cet", out var state) || state.ValueKind != JsonValueKind.String ||
            !c.TryGetProperty("managed", out var managed) ||
            !c.TryGetProperty("captureReadyForCollection", out var ready) ||
            !r.TryGetProperty("ok", out var rok) || rok.ValueKind != JsonValueKind.True ||
            !r.TryGetProperty("result", out var result) || result.ValueKind != JsonValueKind.Object ||
            !result.TryGetProperty("State", out var grState) || grState.ValueKind != JsonValueKind.String ||
            !result.TryGetProperty("ManagedStatePresent", out var grManaged) ||
            !result.TryGetProperty("CompletedCaptureCount", out var grCount) ||
            !result.TryGetProperty("LegacyDllPresent", out var legacy))
            throw new InvalidDataException("Profiler owner status schema changed; do not install or restore.");
        if (managed.ValueKind is not (JsonValueKind.True or JsonValueKind.False) ||
            ready.ValueKind is not (JsonValueKind.True or JsonValueKind.False) ||
            grManaged.ValueKind is not (JsonValueKind.True or JsonValueKind.False) ||
            grCount.ValueKind != JsonValueKind.Number ||
            legacy.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
            throw new InvalidDataException("Profiler owner status types are not recognized.");

        // A legacy REDscript profiler prevents a clean new installation.
        var gr = legacy.GetBoolean() ? "LEGACY_GRSP_PRESENT" : grState.GetString()!;
        return new LiveProfilerState(state.GetString()!, managed.GetBoolean(), ready.GetBoolean(),
            gr, grManaged.GetBoolean(), grCount.GetInt32() > 0);
    }
}

public sealed class UnifiedProfilerWorkflow
{
    private readonly string packageRoot;
    private readonly ComponentCommandRunner owner = new();
    private string Journal => Path.Combine(packageRoot, "RESULTS", "GCO-active-profile-session.json");

    public UnifiedProfilerWorkflow(string packageRoot) => this.packageRoot = Path.GetFullPath(packageRoot);
    public bool HasRecoveryJournal => File.Exists(Journal);

    private static bool GameRunning() => Process.GetProcessesByName("Cyberpunk2077").Length != 0;
    private static string Normalize(string game) => Path.GetFullPath(game).TrimEnd(Path.DirectorySeparatorChar);
    private string Exe(BackendKind kind) => BackendLocator.Executable(packageRoot, kind);

    private async Task<ComponentCommandResult> Run(BackendKind kind, string command,
        string game, CancellationToken token) =>
        await owner.RunAsync(kind, Exe(kind), command, game, token);

    private static ComponentCommandResult Require(ComponentCommandResult result)
    {
        if (!result.Ok || string.IsNullOrWhiteSpace(result.RawJson))
            throw new InvalidOperationException(result.Kind + " " + result.Command + ": " + result.Detail);
        return result;
    }

    public async Task<LiveProfilerState> InspectAsync(string game, CancellationToken token = default)
    {
        var c = Require(await Run(BackendKind.Cet, "status", game, token));
        var g = Require(await Run(BackendKind.Grsp, "status", game, token));
        return LiveProfilerStateParser.Parse(c.RawJson!, g.RawJson!);
    }

    private string? JournalSessionId()
    {
        if (!File.Exists(Journal)) return null;
        using var doc = JsonDocument.Parse(File.ReadAllText(Journal));
        return doc.RootElement.TryGetProperty("sessionId", out var id) &&
               id.ValueKind == JsonValueKind.String ? id.GetString() : null;
    }

    public CollectedCaptureIndex? ReadCollected(string game, bool verifyHashes = false)
    {
        if (!OwnsSession(game)) return null;
        var id = JournalSessionId();
        return id is null ? null :
            CollectedCaptureStore.Read(packageRoot, game, id, verifyHashes);
    }

    private string SessionId() => JournalSessionId() ??
        throw new InvalidDataException("Profiler journal lacks a capture session identity. Use owner recovery.");

    private string? JournalGame()
    {
        if (!File.Exists(Journal)) return null;
        using var doc = JsonDocument.Parse(File.ReadAllText(Journal));
        if (!doc.RootElement.TryGetProperty("gameRoot", out var game) ||
            game.ValueKind != JsonValueKind.String)
            throw new InvalidDataException("Stored profiler session is invalid. Owner recovery required.");
        return game.GetString();
    }

    private void WriteJournal(string game, string stage, string? newSessionId = null)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Journal)!);
        var temp = Journal + ".tmp";
        var id = newSessionId ?? JournalSessionId() ?? Guid.NewGuid().ToString("N");
        File.WriteAllText(temp, JsonSerializer.Serialize(new
        {
            gameRoot = Normalize(game), stage, sessionId = id,
            updatedUtc = DateTimeOffset.UtcNow
        }));
        File.Move(temp, Journal, overwrite: true);
    }

    private bool OwnsSession(string game) =>
        string.Equals(JournalGame(), Normalize(game), StringComparison.OrdinalIgnoreCase);

    public bool HasCaptureAttempt(string game)
    {
        if (!HasRecoveryJournal || !OwnsSession(game)) return false;
        using var doc = JsonDocument.Parse(File.ReadAllText(Journal));
        return doc.RootElement.TryGetProperty("stage", out var stage) &&
            stage.ValueKind == JsonValueKind.String &&
            stage.GetString() == "capture-attempt";
    }

    // A new attempt uses a fresh identity, so a previous partial or complete
    // archive cannot accidentally qualify the next capture. Old evidence remains.
    public string BeginCaptureAttempt(string game)
    {
        if (GameRunning()) throw new InvalidOperationException("Cyberpunk is running.");
        if (!HasRecoveryJournal || !OwnsSession(game))
            throw new InvalidOperationException("No matching GCO profiler installation.");
        var id = Guid.NewGuid().ToString("N");
        WriteJournal(game, "capture-attempt", id);
        return id;
    }

    public CollectedCaptureIndex RecordCollectionAttempt(string game, bool presentMonStopped)
    {
        if (GameRunning()) throw new InvalidOperationException("Close Cyberpunk before collecting results.");
        if (!HasCaptureAttempt(game))
            throw new InvalidOperationException("A capture launch has not been recorded.");
        return CollectedCaptureStore.MarkCollectionAttempted(packageRoot, game, SessionId(), presentMonStopped);
    }

    public async Task<ProfilerWorkflowResult> InstallAsync(string game, CancellationToken token = default)
    {
        if (GameRunning()) return new(false, "Close Cyberpunk before installing profilers.");
        var compatible = FrameworkPreflight.Inspect(game);
        if (!compatible.Ready) return new(false, compatible.Detail);
        var bundle = ComponentBundleVerifier.Inspect(packageRoot);
        if (!bundle.Ready) return new(false, bundle.Detail);
        if (HasRecoveryJournal)
            return new(false, "A previous GCO profiler session requires collection/restoration before installing.");
        LiveProfilerState pre;
        try { pre = await InspectAsync(game, token); }
        catch (Exception ex) { return new(false, "Preflight status failed: " + ex.Message); }
        if (!pre.CleanForInstall)
            return new(false, $"Existing profiler state: CET={pre.Cet} (managed={pre.CetManaged}), GRSP={pre.Grsp} (managed={pre.GrspManaged}). No files changed.");

        try
        {
            // Journal exists BEFORE the first owned install, so interrupted
            // installations remain recoverable from the same portable package.
            WriteJournal(game, "install-started");
            Require(await Run(BackendKind.Cet, "install", game, token));
            WriteJournal(game, "cet-installed");
            Require(await Run(BackendKind.Grsp, "install", game, token));
            WriteJournal(game, "both-installed");
            var post = await InspectAsync(game, token);
            if (!post.BothInstalled)
                throw new InvalidOperationException($"Post-install identity failed: CET={post.Cet}, GRSP={post.Grsp}.");
            return new(true, "CET and REDscript profiler installed and verified by owner status.");
        }
        catch (Exception ex)
        {
            // Restore only managed state discovered by the owners, not arbitrary files.
            var recovery = await RestoreOwnedAsync(game, CancellationToken.None, preserveIncompleteEvidence: false);
            return new(false, "Profiler installation failed: " + ex.Message + " " + recovery.Detail,
                RestoreVerified: recovery.RestoreVerified);
        }
    }

    public async Task<ProfilerWorkflowResult> RestoreOwnedAsync(string game, CancellationToken token = default, bool preserveIncompleteEvidence = true)
    {
        if (GameRunning()) return new(false, "Close Cyberpunk before profiler restoration.");
        try
        {
            if (!OwnsSession(game))
                return new(false, "No matching GCO ownership journal. Use the authoritative standalone manager for recovery.");
            var status = await InspectAsync(game, token);
            string? evidenceFolder = null;
            if (preserveIncompleteEvidence && !status.BothCaptured)
            {
                var snapshot = ProfilerCrashEvidence.Preserve(packageRoot, game);
                evidenceFolder = snapshot.Folder;
            }
            var problems = new List<string>();
            if (status.GrspManaged)
            {
                var result = await Run(BackendKind.Grsp, "restore", game, token);
                if (!result.Ok) problems.Add("REDscript restore: " + result.Detail);
            }
            if (status.CetManaged)
            {
                var result = await Run(BackendKind.Cet, "restore", game, token);
                if (!result.Ok) problems.Add("CET restore: " + result.Detail);
            }
            var end = await InspectAsync(game, token);
            if (end.CetManaged || end.GrspManaged || end.Cet != "OFFICIAL" ||
                end.Grsp != "NOT_INSTALLED" || problems.Count != 0)
                return new(false, "RECOVERY REQUIRED: " + string.Join("; ", problems) +
                    $" Owner status CET={end.Cet}; GRSP={end.Grsp}. Journal preserved.");
            File.Delete(Journal);
            return new(true, "Both owners verified restoration of original game state." +
                (evidenceFolder is null ? "" : " Diagnostics preserved at: " + evidenceFolder),
                RestoreVerified: true);
        }
        catch (Exception ex)
        {
            return new(false, "RECOVERY REQUIRED: " + ex.Message +
                " Keep the portable package and use the owner restore commands.");
        }
    }

    // Collection never restores; source-owner archives remain untouched.
    public async Task<ProfilerWorkflowResult> CollectResultsAsync(string game, CancellationToken token = default)
    {
        if (GameRunning()) return new(false, "Close Cyberpunk before collecting.");
        if (!OwnsSession(game)) return new(false, "No matching GCO session. No files changed.");
        var id = SessionId();
        var saved = CollectedCaptureStore.Read(packageRoot, game, id, verifyHashes: true);
        LiveProfilerState state;
        try { state = await InspectAsync(game, token); }
        catch (Exception ex) { return new(false, "Owner capture status unavailable: " + ex.Message); }
        if (!state.BothInstalled && !saved.HasAny)
            return new(false, "Profiler owners are not both installed and no frame-time evidence was saved.");
        if (!state.CetCaptureReady && !state.GrspCaptureReady && !saved.HasAny)
            return new(false, "No completed CET, REDscript or frame-time capture.");
        if (!state.BothInstalled)
            return new(true, "Only verified frame-time evidence collected; profiler owner state requires recovery. " +
                "Original game files are still instrumented until Continue or explicit Restore.",
                CaptureVerified: saved.HasAny);

        try
        {
            if (state.CetCaptureReady && saved.Cet is null)
            {
                var c = Require(await Run(BackendKind.Cet, "collect", game, token));
                saved = CollectedCaptureStore.CopyOwner(packageRoot, game, id,
                    BackendKind.Cet, c.ArchivePath);
            }
            if (state.GrspCaptureReady && saved.Redscript is null)
            {
                var g = Require(await Run(BackendKind.Grsp, "collect", game, token));
                saved = CollectedCaptureStore.CopyOwner(packageRoot, game, id,
                    BackendKind.Grsp, g.ArchivePath);
            }
        }
        catch (Exception ex)
        {
            return new(false, "Collection incomplete: " + ex.Message +
                ". Previously copied results remain in RESULTS; profilers are still installed.",
                saved.Cet is null ? null : CollectedCaptureStore.DomainFolder(packageRoot,id,"CET"),
                saved.Redscript is null ? null : CollectedCaptureStore.DomainFolder(packageRoot,id,"REDscript"));
        }

        return new(saved.HasAny, "Collected " +
            (saved.Cet is null ? "no CET" : "CET") + " / " +
            (saved.Redscript is null ? "no REDscript" : "REDscript") +
            ". Original game files are still instrumented until Continue or explicit Restore.",
            saved.Cet is null ? null : CollectedCaptureStore.DomainFolder(packageRoot,id,"CET"),
            saved.Redscript is null ? null : CollectedCaptureStore.DomainFolder(packageRoot,id,"REDscript"),
            CaptureVerified: saved.HasAny, RestoreVerified: false);
    }

    public async Task<ProfilerWorkflowResult> ContinueAsync(string game,
        bool skipCet, bool skipRedscript, bool skipFrameTime,
        CancellationToken token = default)
    {
        if (GameRunning()) return new(false, "Close Cyberpunk before restoring.");
        if (!OwnsSession(game)) return new(false, "No matching GCO session.");
        var id = SessionId();
        CollectedCaptureIndex saved;
        try { saved = CollectedCaptureStore.Read(packageRoot, game, id, verifyHashes: true); }
        catch (Exception ex) { return new(false, "Saved collection verification failed: " + ex.Message); }
        if (!CaptureAdmissionPolicy.CanContinue(saved, presentMonStopped: true))
            return new(false, "Both current-attempt G-CET and G-REDscript profiler results are required before CONTINUE.");
        // The external ETW collector must be stopped at Collect, not during
        // the native-owner restoration transition.
        if (File.Exists(Path.Combine(packageRoot, "RESULTS", "GCO-current-PresentMon.txt")))
            return new(false, "PresentMon is not yet collected. Use COLLECT RESULTS to stop it first.");
        if (skipCet || skipRedscript || (saved.FrameTimeCsv is null && !skipFrameTime))
            return new(false, "Native captures cannot be skipped; missing frame data must be recorded.");
        try
        {
            var state = await InspectAsync(game, token);
            if ((state.CetCaptureReady && saved.Cet is null) ||
                (state.GrspCaptureReady && saved.Redscript is null))
                return new(false, "A completed native capture remains uncollected. Collect results first.");
        }
        catch (Exception ex) { return new(false, "Cannot inspect native capture state: " + ex.Message); }

        var restored = await RestoreOwnedAsync(game, CancellationToken.None,
            preserveIncompleteEvidence: true);
        if (!restored.RestoreVerified) return restored;
        CollectedCaptureStore.MarkRestored(packageRoot, saved,
            skipCet, skipRedscript, skipFrameTime);
        return new(true, "Original profiler files restored. Results retained under " +
            CollectedCaptureStore.SessionFolder(packageRoot, id),
            saved.Cet is null ? null : CollectedCaptureStore.DomainFolder(packageRoot,id,"CET"),
            saved.Redscript is null ? null : CollectedCaptureStore.DomainFolder(packageRoot,id,"REDscript"),
            CaptureVerified: true, RestoreVerified: true);
    }


}
