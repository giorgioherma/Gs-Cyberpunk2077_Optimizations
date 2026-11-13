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

    // Recover the original target from the durable session, not mutable UI
    // settings. This works even if the game exe / preflight is now unavailable.
    // Never infer a path from partial or corrupt journal data.
    public string? ReadRecoveryGameRoot()
    {
        if (!HasRecoveryJournal) return null;
        try
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(Journal));
            if (!doc.RootElement.TryGetProperty("gameRoot", out var root) ||
                root.ValueKind != JsonValueKind.String ||
                string.IsNullOrWhiteSpace(root.GetString()) ||
                !Path.IsPathFullyQualified(root.GetString()!))
                return null;
            return Normalize(root.GetString()!);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException
            or JsonException or ArgumentException or InvalidOperationException
            or NotSupportedException)
        {
            return null;
        }
    }

    // This ID is used only for stopping GCO-owned PresentMon on recovery.
    // Restoring the original native files must not require a completed capture.
    public string? ReadRecoverySessionId(string game)
    {
        if (!HasSessionForGame(game)) return null;
        try
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(Journal));
            if (!doc.RootElement.TryGetProperty("sessionId", out var id) ||
                id.ValueKind != JsonValueKind.String) return null;
            var value = id.GetString();
            return value is { Length: 32 } &&
                   value.All(Uri.IsHexDigit) ? value : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException
            or JsonException or ArgumentException or InvalidOperationException)
        {
            return null;
        }
    }

    // The recovery journal is package-wide, but its owner is one game root.
    // A different selected installation must not acquire the old session's
    // RESTORE button, nor start a new install while recovery is outstanding.
    // After the owners verify restoration, RestoreOwnedAsync removes Journal
    // and the normal INSTALL state returns without a separate UI flag.
    public bool HasSessionForGame(string game)
    {
        if (string.IsNullOrWhiteSpace(game) || !HasRecoveryJournal) return false;
        try
        {
            var saved = ReadRecoveryGameRoot();
            return saved is not null &&
                   string.Equals(saved, Normalize(game),
                       StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException)
        {
            return false;
        }
    }

    private static bool GameRunning() => Process.GetProcessesByName("Cyberpunk2077").Length != 0;
    private static string Normalize(string game) => Path.GetFullPath(game).TrimEnd(Path.DirectorySeparatorChar);
    private string Exe(BackendKind kind) => BackendLocator.Executable(packageRoot, kind);

    private string OwnerStaging(string id, BackendKind kind) =>
        Path.Combine(packageRoot, "RESULTS", ".staging", id,
            kind == BackendKind.Cet ? "CET" : "REDscript");

    private async Task<ComponentCommandResult> Run(BackendKind kind, string command,
        string game, CancellationToken token) =>
        await owner.RunAsync(kind, Exe(kind), command, game, token,
            resultsRoot: command == "collect" ? OwnerStaging(SessionId(), kind) : null);

    // Only a verified owner-produced staging archive can be deleted after GCO
    // has copied and hashed its complete payload into the collected session.
    private void RetireStagedArchive(string id, BackendKind kind, string? archive)
    {
        if (string.IsNullOrWhiteSpace(archive))
            throw new InvalidDataException("No owner collection archive to retire.");
        var root = Path.GetFullPath(OwnerStaging(id, kind))
            .TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        var target = Path.GetFullPath(archive);
        if (!target.StartsWith(root, StringComparison.OrdinalIgnoreCase) ||
            !Directory.Exists(target))
            throw new InvalidDataException("Owner archive is outside the GCO staging root.");
        void Check(string dir)
        {
            if ((File.GetAttributes(dir) & FileAttributes.ReparsePoint) != 0)
                throw new InvalidDataException("Linked owner staging directory refused.");
            foreach (var entry in Directory.EnumerateFileSystemEntries(dir))
            {
                if ((File.GetAttributes(entry) & FileAttributes.ReparsePoint) != 0)
                    throw new InvalidDataException("Linked owner staging entry refused.");
                if (Directory.Exists(entry)) Check(entry);
            }
        }
        Check(target);
        Directory.Delete(target, recursive: true);
    }

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

    // The strict framework preflight ran before this session installed its
    // temporary profiler binaries. Only a matching journal and verified owner
    // state may retain that approval while CET's native file is replaced.
    public bool HasInstalledFrameworkBaseline(string game, LiveProfilerState state)
    {
        if (!state.BothInstalled || !HasRecoveryJournal) return false;
        try
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(Journal));
            var data = doc.RootElement;
            if (!data.TryGetProperty("gameRoot", out var gamePath) ||
                gamePath.ValueKind != JsonValueKind.String ||
                !string.Equals(gamePath.GetString(), Normalize(game),
                    StringComparison.OrdinalIgnoreCase) ||
                !data.TryGetProperty("sessionId", out var id) ||
                id.ValueKind != JsonValueKind.String ||
                string.IsNullOrWhiteSpace(id.GetString()) ||
                !data.TryGetProperty("stage", out var stage) ||
                stage.ValueKind != JsonValueKind.String)
                return false;
            // v1.2.1 sessions used these same two durable stages after
            // preflight and owner installations; both remain compatible.
            return stage.GetString() is "both-installed" or "capture-attempt";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException
            or JsonException or ArgumentException)
        {
            return false;
        }
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
    public string BeginCaptureAttempt(string game, bool observedGameAlreadyRunning = false,
        LiveProfilerState? ownerBeforeCapture = null)
    {
        var gameRunning = GameRunning();
        if (gameRunning && !observedGameAlreadyRunning)
            throw new InvalidOperationException("Cyberpunk is running.");
        if (!HasRecoveryJournal || !OwnsSession(game))
            throw new InvalidOperationException("No matching GCO profiler installation.");
        // The external-game branch is used only after WPF detected a fresh
        // stopped->running process transition and verified owner state.
        if (observedGameAlreadyRunning && !gameRunning)
            throw new InvalidOperationException("No running Cyberpunk process to attach.");
        if (HasCaptureAttempt(game))
        {
            var prior = ReadCollected(game);
            if (prior is not null && (!prior.CollectionAttempted || !prior.PresentMonStopVerified))
                throw new InvalidOperationException("Previous capture is still pending. Stop/collect or abandon its owned output first.");
        }
        var id = Guid.NewGuid().ToString("N");
        // Create a short, readable folder and durable identity before writing
        // the journal; previous captures remain immutable and independently
        // readable even after owner restoration.
        CollectedCaptureStore.CreateDatedSessionFolder(
            packageRoot, id, DateTimeOffset.Now, game,
            ownerBeforeCapture?.CetCaptureReady == true,
            ownerBeforeCapture?.GrspCaptureReady == true);
        WriteJournal(game, "capture-attempt", id);
        return id;
    }

    // Owner-owned live files only: every new game run and explicit Restore
    // starts from an empty CET + GRSP recording state. Never call from a game
    // process or without an admitted GCO managed installation.
    public async Task<ProfilerWorkflowResult> ResetNativeOutputAsync(string game,
        CancellationToken token = default)
    {
        if (GameRunning()) return new(false, "Close Cyberpunk before resetting captures.");
        if (!OwnsSession(game)) return new(false, "GCO capture ownership is not verified.");
        try
        {
            var state = await InspectAsync(game, token);
            if (!state.BothInstalled)
                return new(false, "Profiler ownership changed; preserve results and restore safely.");
            Require(await Run(BackendKind.Cet, "discard-live", game, token));
            Require(await Run(BackendKind.Grsp, "discard-live", game, token));
            var after = await InspectAsync(game, token);
            if (!after.BothInstalled || after.CetCaptureReady || after.GrspCaptureReady)
                return new(false, "Native capture reset could not be verified. No game launch authorized.");
            return new(true, "Uncollected CET and REDscript native output reset.");
        }
        catch (Exception ex)
        {
            return new(false, "Native capture reset incomplete: " + ex.Message +
                ". Session journal preserved for recovery.");
        }
    }

    // Safe to run only after GCO's PresentMon was stopped/verified.
    // No file outside GCO/RESULTS is modified; native owner data remains
    // source-owned. The journal and the install/restore transaction survive.
    public bool AbandonCurrentUncollected(string game)
    {
        if (!HasRecoveryJournal || !OwnsSession(game) || !HasCaptureAttempt(game))
            return false;
        var id = SessionId();
        var state = CollectedCaptureStore.Read(packageRoot, game, id, verifyHashes: true);
        if (state.CollectionAttempted || state.HasAny || state.Restored)
            return false;
        var discarded = CollectedCaptureStore.DiscardUncollected(packageRoot, game, id);
        WriteJournal(game, "both-installed");
        return discarded;
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
            WriteJournal(game, "owners-installed");
            var post = await InspectAsync(game, token);
            if (!post.BothInstalled)
                throw new InvalidOperationException($"Post-install identity failed: CET={post.Cet}, GRSP={post.Grsp}.");
            // The admitted baseline is frozen only after both owners prove
            // installed identity. Interrupted installs remain recovery-only.
            WriteJournal(game, "both-installed");
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
            string? evidenceWarning = null;
            if (preserveIncompleteEvidence && !status.BothCaptured)
            {
                try
                {
                    var snapshot = ProfilerCrashEvidence.Preserve(packageRoot, game);
                    evidenceFolder = snapshot.Folder;
                }
                catch (Exception ex)
                {
                    // Recover original native files even if supplemental crash
                    // diagnostics cannot be copied. Saved collected capture
                    // archives are separate and are never deleted here.
                    evidenceWarning = "Diagnostic snapshot failed: " + ex.Message;
                }
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
                (evidenceFolder is null ? "" : " Diagnostics preserved at: " + evidenceFolder) +
                (evidenceWarning is null ? "" : " WARNING: " + evidenceWarning),
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
            // The owner status is only a Boolean: if native results were
            // already ready before this launch, that old result cannot be
            // attributed to this attempt. Do not collect/admit it under a
            // new GCO identity without source-owner proof of a fresh capture.
            if (state.CetCaptureReady && saved.Cet is null &&
                !saved.CetReadyBeforeAttempt)
            {
                var c = Require(await Run(BackendKind.Cet, "collect", game, token));
                saved = CollectedCaptureStore.CopyOwner(packageRoot, game, id,
                    BackendKind.Cet, c.ArchivePath);
                RetireStagedArchive(id, BackendKind.Cet, c.ArchivePath);
            }
            if (state.GrspCaptureReady && saved.Redscript is null &&
                !saved.RedscriptReadyBeforeAttempt)
            {
                var g = Require(await Run(BackendKind.Grsp, "collect", game, token));
                saved = CollectedCaptureStore.CopyOwner(packageRoot, game, id,
                    BackendKind.Grsp, g.ArchivePath);
                RetireStagedArchive(id, BackendKind.Grsp, g.ArchivePath);
            }
            // Partial/uncompleted output must not remain in the game for the
            // next capture, even if only one native domain was collected.
            var reset = await ResetNativeOutputAsync(game, token);
            if (!reset.Ok) throw new InvalidOperationException(reset.Detail);
        }
        catch (Exception ex)
        {
            return new(false, "Collection incomplete: " + ex.Message +
                ". Previously copied results remain in RESULTS; profilers are still installed.",
                saved.Cet is null ? null : CollectedCaptureStore.DomainFolder(packageRoot,id,"CET"),
                saved.Redscript is null ? null : CollectedCaptureStore.DomainFolder(packageRoot,id,"REDscript"));
        }

        var staleNotice = (saved.CetReadyBeforeAttempt
                ? " CET had pre-existing native results; excluded from this attempt."
                : "") +
            (saved.RedscriptReadyBeforeAttempt
                ? " REDscript had pre-existing native results; excluded from this attempt."
                : "");
        return new(saved.HasAny, "Collected " +
            (saved.Cet is null ? "no CET" : "CET") + " / " +
            (saved.Redscript is null ? "no REDscript" : "REDscript") +
            ". Original game files are still instrumented until Continue or explicit Restore." +
            staleNotice,
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
