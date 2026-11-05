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
        hasCurrentSession && !gameRunning && BothCaptured;
    public bool CanLaunch(bool hasCurrentSession, bool gameRunning) =>
        hasCurrentSession && !gameRunning && BothInstalled;
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

    private string? JournalGame()
    {
        if (!File.Exists(Journal)) return null;
        using var doc = JsonDocument.Parse(File.ReadAllText(Journal));
        if (!doc.RootElement.TryGetProperty("gameRoot", out var game) ||
            game.ValueKind != JsonValueKind.String)
            throw new InvalidDataException("Stored profiler session is invalid. Owner recovery required.");
        return game.GetString();
    }

    private void WriteJournal(string game, string stage)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Journal)!);
        var temp = Journal + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(new
        {
            gameRoot = Normalize(game), stage,
            createdUtc = DateTimeOffset.UtcNow
        }));
        File.Move(temp, Journal, overwrite: true);
    }

    private bool OwnsSession(string game) =>
        string.Equals(JournalGame(), Normalize(game), StringComparison.OrdinalIgnoreCase);

    public async Task<ProfilerWorkflowResult> InstallAsync(string game, CancellationToken token = default)
    {
        if (GameRunning()) return new(false, "Close Cyberpunk before installing profilers.");
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
            var recovery = await RestoreOwnedAsync(game, CancellationToken.None);
            return new(false, "Profiler installation failed: " + ex.Message + " " + recovery.Detail,
                RestoreVerified: recovery.RestoreVerified);
        }
    }

    public async Task<ProfilerWorkflowResult> RestoreOwnedAsync(string game, CancellationToken token = default)
    {
        if (GameRunning()) return new(false, "Close Cyberpunk before profiler restoration.");
        try
        {
            if (!OwnsSession(game))
                return new(false, "No matching GCO ownership journal. Use the authoritative standalone manager for recovery.");
            var status = await InspectAsync(game, token);
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
            return new(true, "Both owners verified restoration of original game state.",
                RestoreVerified: true);
        }
        catch (Exception ex)
        {
            return new(false, "RECOVERY REQUIRED: " + ex.Message +
                " Keep the portable package and use the owner restore commands.");
        }
    }

    public async Task<ProfilerWorkflowResult> CollectAndRestoreAsync(string game, CancellationToken token = default)
    {
        if (GameRunning()) return new(false, "Close Cyberpunk before collecting/restoring.");
        if (!OwnsSession(game))
            return new(false, "No matching GCO session. No game files changed.");
        string? cet = null, grsp = null;
        bool captured = false;
        string detail = "";
        try
        {
            var state = await InspectAsync(game, token);
            if (!state.BothInstalled)
                detail = $"Incomplete profiler installation: CET={state.Cet}, GRSP={state.Grsp}. ";
            else if (!state.BothCaptured)
                detail = "Capture incomplete in CET and/or REDscript. ";
            else
            {
                captured = true;
                // Preserve owner archives and never move external PresentMon CSVs.
                var c = Require(await Run(BackendKind.Cet, "collect", game, token));
                cet = c.ArchivePath;
                if (string.IsNullOrWhiteSpace(cet) || !Directory.Exists(cet))
                    throw new InvalidDataException("CET archive destination is missing.");
                var g = Require(await Run(BackendKind.Grsp, "collect", game, token));
                grsp = g.ArchivePath;
                if (string.IsNullOrWhiteSpace(grsp) || !Directory.Exists(grsp))
                    throw new InvalidDataException("GRSP archive destination is missing.");
            }
        }
        catch (Exception ex) { captured = false; detail += "Collection failed: " + ex.Message + ". "; }
        // Owner restoration is mandatory even when capture failed.
        var restored = await RestoreOwnedAsync(game, CancellationToken.None);
        return new(captured && restored.RestoreVerified,
            detail + restored.Detail, cet, grsp, captured && restored.RestoreVerified,
            restored.RestoreVerified);
    }
}
