using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace GTotalOptimizer.Core;

public sealed record Mo2OverlayFile(string GameRelativePath, string Sha256, long Size);

public sealed record Mo2OverlayPreparation(
    string Profile, string SessionId, string StagingDirectory, string JournalFile,
    IReadOnlyList<Mo2OverlayFile> Files, int InputFiles, string Detail);

// Only the existing CET/GRSP managers may transform fixture input. The builder
// projects current selected-profile sources, invokes those managers against an
// isolated fixture, and extracts their verified delta. No Lua rewriting here.
public static class Mo2ProfilerOverlayBuilder
{
    private static readonly Regex SessionIdPattern = new(
        "^[a-f0-9]{32}$", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    public static async Task<Mo2OverlayPreparation> PrepareFromBundleAsync(
        string packageRoot, string liveGameRoot, Mo2Stack selected,
        string sessionId, CancellationToken token = default,
        string? localAppData = null)
    {
        if (RequiresVerificationAfterRestore(localAppData))
            throw new InvalidOperationException("Click VERIFY before installing profilers.");
        var bundle = ComponentBundleVerifier.Inspect(packageRoot);
        if (!bundle.Ready || bundle.CetExecutable is null || bundle.GrspExecutable is null)
            throw new InvalidOperationException(
                "Both verified CET/GRSP source-owner managers are required: " + bundle.Detail);
        var runner = new ComponentCommandRunner();
        return await PrepareAsync(liveGameRoot, selected, sessionId, async (fixture, ct) =>
        {
            var cet = await runner.RunAsync(BackendKind.Cet, bundle.CetExecutable,
                "install", fixture, ct);
            if (!cet.Ok) throw new InvalidOperationException(
                "Authoritative CET owner refused disposable overlay fixture: " + cet.Detail);
            var grsp = await runner.RunAsync(BackendKind.Grsp, bundle.GrspExecutable,
                "install", fixture, ct);
            if (!grsp.Ok) throw new InvalidOperationException(
                "Authoritative GRSP owner refused disposable overlay fixture: " + grsp.Detail);
        }, token, localAppData);
    }

    // Package internals stay private: the callback exists for structural fixture
    // tests and cannot be wired to the public UI or used to grant install status.
    internal static async Task<Mo2OverlayPreparation> PrepareAsync(
        string liveGameRoot, Mo2Stack selected, string sessionId,
        Func<string, CancellationToken, Task> ownersInstallFixture,
        CancellationToken token = default, string? localAppData = null)
    {
        if (!SessionIdPattern.IsMatch(sessionId))
            throw new InvalidDataException("Invalid GCO session ID.");
        if (!selected.Ready || !selected.ProfileVerified || selected.GameOnlyFallback ||
            string.IsNullOrWhiteSpace(selected.Profile))
            throw new InvalidDataException("Selected MO2 profile effective source is not admitted.");
        if (System.Diagnostics.Process.GetProcessesByName("Cyberpunk2077").Length != 0)
            throw new InvalidOperationException("Close Cyberpunk before preparing the MO2 profiler overlay.");
        var game = Path.GetFullPath(liveGameRoot).TrimEnd(Path.DirectorySeparatorChar);
        if (!File.Exists(Path.Combine(game, "bin", "x64", "Cyberpunk2077.exe")))
            throw new InvalidDataException("Live Cyberpunk game executable not found.");
        if (!selected.HasCetAsi ||
            !selected.EffectiveFiles.ContainsKey("bin/x64/plugins/cyber_engine_tweaks.asi"))
            throw new InvalidDataException(
                "Effective supported CET ASI is unavailable; no filename or version guess.");
        var local = localAppData ??
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        if (string.IsNullOrWhiteSpace(local)) throw new InvalidDataException("LocalAppData is required.");
        var bridge = Path.Combine(Path.GetFullPath(local), "GCO", "MO2Bridge");
        var fixture = Path.Combine(bridge, "fixtures", sessionId);
        var stage = Path.Combine(bridge, "staging", sessionId);
        var journal = Path.Combine(bridge, "journals", sessionId + ".json");
        var allPaths = new[] { fixture, stage, journal };
        if (allPaths.Any(p => Directory.Exists(p) || File.Exists(p)))
            throw new InvalidOperationException("GCO overlay session already exists. Recovery required.");
        EnsureNoReparseParents(bridge);
        Directory.CreateDirectory(Path.GetDirectoryName(journal)!);
        if (File.Exists(journal)) throw new InvalidOperationException("GCO session journal collision.");
        // Journal is durable before creating any source copy; failures leave
        // evidence and do not silently reuse a partial session.
        WriteJournal(journal, new
        {
            schemaVersion = 1, sessionId, profile = selected.Profile, gameRoot = game,
            fixtureRoot = fixture, stageRoot = stage, phase = "preparing",
            utc = DateTimeOffset.UtcNow
        });
        Directory.CreateDirectory(fixture);
        Directory.CreateDirectory(stage);

        var baselines = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var owners = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var priorResults = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var priorResultSources = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var evidence = Path.Combine(bridge, "recovery", sessionId, "prior-grsp-results");
        try
        {
            // No unnecessary REDscript or user red4ext files are copied.
            // Source-owner profiler installers only need CET source, bindings,
            // original ASI, and the existing/legacy GRSP footprints for refusal.
            int count = 0;
            long bytes = 0;
            foreach (var (key, winning) in selected.EffectiveFiles
                .OrderBy(x => x.Key, StringComparer.OrdinalIgnoreCase))
            {
                token.ThrowIfCancellationRequested();
                var relative = NormalizeRelative(key);
                if (IsHistoricalGrspResult(relative))
                {
                    // Previous capture output is not an installation footprint.
                    // Preserve it outside both the fixture and runtime stage:
                    // the next capture can update shared status/index files.
                    var historical = Path.GetFullPath(winning.PhysicalPath);
                    EnsureNoReparseParents(Path.GetDirectoryName(historical)!);
                    RequireRegularUnlinkedFile(historical);
                    var hash = Hash(historical);
                    bytes += new FileInfo(historical).Length;
                    if (++count > 100_000 || bytes > 2L * 1024 * 1024 * 1024)
                        throw new InvalidDataException("MO2 prior capture and source inventory exceeds safe bounds.");
                    var archived = Path.Combine(evidence, relative);
                    EnsureNoReparseParents(Path.GetDirectoryName(archived)!);
                    Directory.CreateDirectory(Path.GetDirectoryName(archived)!);
                    File.Copy(historical, archived, overwrite: false);
                    if (Hash(archived) != hash || Hash(historical) != hash)
                        throw new InvalidDataException("Previous GRSP capture changed during preservation: " + relative);
                    priorResults[relative] = hash;
                    priorResultSources[relative] = historical;
                    continue;
                }
                if (!IsRequiredOwnerInput(relative)) continue;
                var source = Path.GetFullPath(winning.PhysicalPath);
                RequireRegularUnlinkedFile(source);
                var original = Hash(source);
                bytes += new FileInfo(source).Length;
                if (++count > 100_000 || bytes > 2L * 1024 * 1024 * 1024)
                    throw new InvalidDataException("MO2 source fixture exceeds safe bounded inventory.");
                var destination = Path.Combine(fixture, relative.Replace('/', Path.DirectorySeparatorChar));
                Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                File.Copy(source, destination, overwrite: false);
                if (!Hash(destination).Equals(original, StringComparison.OrdinalIgnoreCase) ||
                    !Hash(source).Equals(original, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException("Effective owner source changed during projection.");
                baselines[relative] = original;
                owners[relative] = source;
            }

            Directory.CreateDirectory(Path.Combine(fixture, "bin", "x64", "plugins"));
            Directory.CreateDirectory(Path.Combine(fixture, "red4ext", "plugins"));
            // Fixture's executable is an inert zero-byte presence stub; NEVER
            // copy or execute actual Cyberpunk inside the temporary tree.
            File.WriteAllBytes(Path.Combine(fixture, "bin", "x64", "Cyberpunk2077.exe"), []);
            var before = Scan(fixture);
            await ownersInstallFixture(fixture, token);
            token.ThrowIfCancellationRequested();
            var after = Scan(fixture);

            foreach (var (relative, previous) in baselines)
                if (!Hash(owners[relative]).Equals(previous, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException("Live MO2 winner changed while owners prepared the overlay: " + relative);
            // A MO2 file override cannot delete an existing source file.
            // Refuse owners that remove any user file in the fixture, instead
            // of leaving an active duplicate worker/callback under the VFS.
            foreach (var removed in before.Keys.Except(after.Keys, StringComparer.OrdinalIgnoreCase))
                if (!IsOwnerTransactionMetadata(removed))
                    throw new InvalidDataException(
                        "Source owner deleted a file not representable by a MO2 overlay: " + removed);

            var changes = new List<Mo2OverlayFile>();
            foreach (var (relative, sha) in after.OrderBy(x => x.Key, StringComparer.OrdinalIgnoreCase))
            {
                if (before.TryGetValue(relative, out var prior) &&
                    sha.Equals(prior, StringComparison.OrdinalIgnoreCase))
                    continue;
                if (IsOwnerTransactionMetadata(relative)) continue;
                if (!IsAllowedOutput(relative))
                    throw new InvalidDataException(
                        "Source owner wrote unapproved profiler overlay path: " + relative);
                var source = Path.Combine(fixture, relative.Replace('/', Path.DirectorySeparatorChar));
                var dest = Path.Combine(stage, relative.Replace('/', Path.DirectorySeparatorChar));
                Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
                File.Copy(source, dest);
                if (!Hash(dest).Equals(sha, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException("Owner-generated overlay copy verification failed.");
                changes.Add(new(relative, sha, new FileInfo(dest).Length));
            }
            if (changes.Count == 0 ||
                !changes.Any(x => x.GameRelativePath.Equals(
                    "bin/x64/plugins/cyber_engine_tweaks.asi", StringComparison.OrdinalIgnoreCase)) ||
                !changes.Any(x => x.GameRelativePath.Equals(
                    "red4ext/plugins/G-REDscript-Profiler.dll", StringComparison.OrdinalIgnoreCase)) ||
                !changes.Any(x => x.GameRelativePath.Equals(
                    "bin/x64/plugins/cyber_engine_tweaks/mods/CETProfilerControls/init.lua",
                    StringComparison.OrdinalIgnoreCase)))
                throw new InvalidDataException("Owner overlay lacks CET ASI, GRSP DLL or CET control entry point.");

            // USVFS root-level sparse directory mappings can hide physical
            // siblings during relative reads. Explicit identity file links
            // preserve original physical winners without staging their bytes
            // or overriding enabled MO2 packages / Overwrite.
            var passthrough = PhysicalPassthrough(selected, game, changes.Select(f => f.GameRelativePath));
            foreach (var relative in passthrough)
                Directory.CreateDirectory(Path.GetDirectoryName(Path.Combine(stage, relative))!);

            // This does not claim actual native DLL or gameplay proof.
            WriteJournal(journal, new
            {
                schemaVersion = 1, sessionId, profile = selected.Profile, gameRoot = game,
                fixtureRoot = fixture, stageRoot = stage, phase = "prepared",
                originalSources = baselines, originalSourcePaths = owners, generatedFiles = changes,
                physicalPassthrough = passthrough,
                priorGrspResults = priorResults, priorGrspResultSources = priorResultSources,
                priorGrspResultsRoot = evidence,
                utc = DateTimeOffset.UtcNow
            });
            return new(selected.Profile, sessionId, stage, journal, changes, count,
                "Both source-owner managers generated verified profiler overrides in an isolated fixture.");
        }
        catch (Exception ex)
        {
            WriteJournal(journal, new
            {
                schemaVersion = 1, sessionId, profile = selected.Profile, gameRoot = game,
                fixtureRoot = fixture, stageRoot = stage, phase = "recovery-required",
                error = ex.Message, originalSources = baselines,
                originalSourcePaths = owners, priorGrspResults = priorResults,
                priorGrspResultSources = priorResultSources, priorGrspResultsRoot = evidence,
                utc = DateTimeOffset.UtcNow
            });
            throw;
        }
    }

    // Experimental sessions retain all stage/fixture/output bytes. A pending
    // or ambiguous launch cannot be reused, even after a GCO restart.
    public static bool HasUnresolvedTestSession() => HasUnresolvedTestSession(null);

    internal static bool HasUnresolvedTestSession(string? localAppData)
    {
        var folder = Path.Combine(localAppData ?? Environment.GetFolderPath(
            Environment.SpecialFolder.LocalApplicationData), "GCO", "MO2Bridge", "journals");
        if (!Directory.Exists(folder)) return false;
        EnsureNoReparseParents(folder);
        foreach (var file in Directory.EnumerateFiles(folder, "*.json"))
        {
            RequireRegularUnlinkedFile(file);
            using var doc = JsonDocument.Parse(File.ReadAllText(file));
            var phase = doc.RootElement.GetProperty("phase").GetString();
            if (!IsRetiredPhase(phase) && phase != "retired-awaiting-verify") return true;
        }
        return false;
    }

    public static bool RequiresVerificationAfterRestore(string? localAppData = null)
    {
        var folder = Path.Combine(localAppData ?? Environment.GetFolderPath(
            Environment.SpecialFolder.LocalApplicationData), "GCO", "MO2Bridge", "journals");
        if (!Directory.Exists(folder)) return false;
        EnsureNoReparseParents(folder);
        foreach (var file in Directory.EnumerateFiles(folder, "*.json"))
        {
            RequireRegularUnlinkedFile(file);
            using var doc = JsonDocument.Parse(File.ReadAllText(file));
            if (doc.RootElement.GetProperty("phase").GetString() == "retired-awaiting-verify")
                return true;
        }
        return false;
    }

    private static bool IsRetiredPhase(string? phase) =>
        phase is "retired-unlaunched" or "retired-test-capture" or "retired-recovered";

    // Restore is a recovery entry point, not dependent on an in-memory
    // preparation, a successful launch, or a live bridge. Never delete stage,
    // fixture, journal or capture files. Pending mailbox commands can only
    // be quarantined after every MO2 process has exited.
    public static int RestoreOutstandingSessions()
    {
        RequireNoProcess("Cyberpunk2077", "Close Cyberpunk before restoring profilers.");
        return RestoreOutstandingSessions(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            () => RequireNoProcess("ModOrganizer", "MO2 is still running."),
            deferPending: true);
    }

    // Called only by VERIFY's elevated setup helper, after graceful shutdown
    // and before reopening MO2. Never revive an interrupted launch.
    public static int CompleteDeferredRestore()
    {
        RequireNoProcess("Cyberpunk2077", "Close Cyberpunk before verifying MO2.");
        RequireNoProcess("ModOrganizer", "MO2 is still running.");
        return RestoreOutstandingSessions(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            () => RequireNoProcess("ModOrganizer", "MO2 is still running."),
            deferredOnly: true);
    }

    internal static int RestoreOutstandingSessions(string localAppData, Action requireMo2Closed,
        bool deferPending = false, bool deferredOnly = false)
    {
        var bridge = Path.Combine(Path.GetFullPath(localAppData), "GCO", "MO2Bridge");
        var folder = Path.Combine(bridge, "journals");
        if (!Directory.Exists(folder)) return 0;
        EnsureNoReparseParents(folder);
        var count = 0;
        foreach (var file in Directory.EnumerateFiles(folder, "*.json"))
        {
            RequireRegularUnlinkedFile(file);
            var j = System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(file))!.AsObject();
            string? phase = j["phase"]?.GetValue<string>();
            if (IsRetiredPhase(phase) ||
                (deferredOnly && phase != "retired-awaiting-verify") ||
                (deferPending && phase == "retired-awaiting-verify")) continue;
            string id = j["sessionId"]?.GetValue<string>() ?? "";
            if (!SessionIdPattern.IsMatch(id) ||
                !string.Equals(Path.GetFileName(file), id + ".json", StringComparison.OrdinalIgnoreCase) ||
                j["schemaVersion"]?.GetValue<int>() != 1 ||
                !string.Equals(j["stageRoot"]?.GetValue<string>(), Path.Combine(bridge, "staging", id),
                    StringComparison.OrdinalIgnoreCase) ||
                !string.Equals(j["fixtureRoot"]?.GetValue<string>(), Path.Combine(bridge, "fixtures", id),
                    StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("MO2 recovery journal identity is invalid. Original files preserved.");
            EnsureNoReparseParents(Path.Combine(bridge, "staging", id));
            EnsureNoReparseParents(Path.Combine(bridge, "fixtures", id));
            if (phase == "test-launched")
            {
                int pid = j["processId"]?.GetValue<int>() ?? 0;
                if (pid <= 0) throw new InvalidDataException("MO2 launched process identity is missing.");
                try
                {
                    using var process = System.Diagnostics.Process.GetProcessById(pid);
                    if (!process.HasExited)
                        throw new InvalidOperationException("Close the launched process before restoring profilers.");
                }
                catch (ArgumentException) { /* The process has exited. */ }
                // Signed completion means the one-shot mapper has disarmed.
                // A normal completed launch does not require closing MO2.
            }
            else if (phase == "test-launch-pending" && deferPending)
            {
                // Reset the UI without asking the user to close MO2. The
                // request may still be in progress; retain it and all stage
                // bytes, and prohibit INSTALL/START until VERIFY restarts MO2.
                j["previousPhase"] = phase;
                j["phase"] = "retired-awaiting-verify";
                j["utc"] = DateTimeOffset.UtcNow.ToString("O");
                WriteJournal(file, j);
                count++;
                continue;
            }
            else if (phase is "test-launch-pending" or "retired-awaiting-verify")
            {
                requireMo2Closed();
                var archive = Path.Combine(bridge, "recovery", id);
                foreach (var command in Directory.EnumerateFiles(bridge, "instance-*.json"))
                {
                    var name = Path.GetFileName(command);
                    if (!Regex.IsMatch(name, @"^instance-[a-f0-9]{24}\.(request|processing)\.json$",
                            RegexOptions.CultureInvariant)) continue;
                    RequireRegularUnlinkedFile(command);
                    using var doc = JsonDocument.Parse(File.ReadAllText(command));
                    var root = doc.RootElement;
                    if (!root.TryGetProperty("sessionId", out var sid) || sid.GetString() != id) continue;
                    if (root.GetProperty("operation").GetString() != "launch" ||
                        !string.Equals(root.GetProperty("stagingRoot").GetString(),
                            Path.Combine(bridge, "staging", id), StringComparison.OrdinalIgnoreCase))
                        throw new InvalidDataException("MO2 recovery command identity is invalid.");
                    EnsureNoReparseParents(archive);
                    Directory.CreateDirectory(archive);
                    requireMo2Closed();
                    File.Move(command, Path.Combine(archive, name + "." + Guid.NewGuid().ToString("N")),
                        overwrite: false);
                }
            }
            else if (phase is not ("prepared" or "preparing" or "recovery-required"))
                throw new InvalidDataException("Unknown MO2 recovery phase. Original files preserved.");
            if (phase != "retired-awaiting-verify") j["previousPhase"] = phase;
            j["phase"] = phase == "test-launched" ? "retired-test-capture" :
                phase is "test-launch-pending" or "retired-awaiting-verify"
                    ? "retired-recovered" : "retired-unlaunched";
            j["utc"] = DateTimeOffset.UtcNow.ToString("O");
            WriteJournal(file, j);
            count++;
        }
        return count;
    }

    private static void RequireNoProcess(string name, string message)
    {
        var processes = System.Diagnostics.Process.GetProcessesByName(name);
        try { if (processes.Length != 0) throw new InvalidOperationException(message); }
        finally { foreach (var process in processes) process.Dispose(); }
    }

    internal static void RecordTestLaunchPhase(Mo2OverlayPreparation prepared,
        string expected, string phase, int processId = 0)
    {
        EnsureNoReparseParents(prepared.JournalFile);
        RequireRegularUnlinkedFile(prepared.JournalFile);
        var journal = System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(prepared.JournalFile))!
            .AsObject();
        if (journal["sessionId"]?.GetValue<string>() != prepared.SessionId ||
            journal["phase"]?.GetValue<string>() != expected)
            throw new InvalidOperationException("Unexpected MO2 session phase; retain recovery evidence.");
        journal["phase"] = phase;
        journal["processId"] = processId;
        journal["utc"] = DateTimeOffset.UtcNow.ToString("O");
        WriteJournal(prepared.JournalFile, journal);
    }

    public static bool IsStillPrepared(Mo2OverlayPreparation prepared)
    {
        try
        {
            EnsureNoReparseParents(prepared.JournalFile);
            RequireRegularUnlinkedFile(prepared.JournalFile);
            using var doc = JsonDocument.Parse(File.ReadAllText(prepared.JournalFile));
            return doc.RootElement.GetProperty("sessionId").GetString() == prepared.SessionId &&
                doc.RootElement.GetProperty("phase").GetString() == "prepared";
        }
        catch { return false; } // An unreadable journal is never proof of no launch.
    }

    public static void RetireCompletedTest(Mo2OverlayPreparation prepared)
    {
        if (System.Diagnostics.Process.GetProcessesByName("Cyberpunk2077").Length != 0)
            throw new InvalidOperationException("Close Cyberpunk before retiring this test.");
        // A positive signed launch is required; a timeout stays unresolved.
        // Check the returned process too, even if it was not named Cyberpunk.
        using var doc = JsonDocument.Parse(File.ReadAllText(prepared.JournalFile));
        int pid = doc.RootElement.GetProperty("processId").GetInt32();
        if (pid <= 0) throw new InvalidDataException("MO2 test process identity missing.");
        try
        {
            using var process = System.Diagnostics.Process.GetProcessById(pid);
            if (!process.HasExited) throw new InvalidOperationException("MO2 test launch process is still running.");
        }
        catch (ArgumentException) { /* The returned process no longer exists. */ }
        // Preserve the full manifest; subsequent collection must resolve this session.
        RecordTestLaunchPhase(prepared, "test-launched", "retired-test-capture", pid);
    }

    // Only an unlaunched prepared session may be retired here. Launched or
    // uncertain sessions must retain their evidence for capture recovery.
    public static void RetireUnlaunched(Mo2OverlayPreparation prepared)
    {
        if (System.Diagnostics.Process.GetProcessesByName("Cyberpunk2077").Length != 0)
            throw new InvalidOperationException("Close Cyberpunk before retiring the prepared overlay.");
        var bridge = Path.Combine(Environment.GetFolderPath(
            Environment.SpecialFolder.LocalApplicationData), "GCO", "MO2Bridge");
        if (!SessionIdPattern.IsMatch(prepared.SessionId))
            throw new InvalidDataException("Invalid MO2 session identity.");
        var journal = Path.Combine(bridge, "journals", prepared.SessionId + ".json");
        var stage = Path.Combine(bridge, "staging", prepared.SessionId);
        var fixture = Path.Combine(bridge, "fixtures", prepared.SessionId);
        if (!string.Equals(journal, prepared.JournalFile, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(stage, prepared.StagingDirectory, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("MO2 session is outside its owned storage.");
        EnsureNoReparseParents(stage);
        EnsureNoReparseParents(fixture);
        EnsureNoReparseParents(journal);
        RequireRegularUnlinkedFile(journal);
        using var doc = JsonDocument.Parse(File.ReadAllText(journal));
        var j = doc.RootElement;
        if (j.GetProperty("phase").GetString() != "prepared" ||
            j.GetProperty("sessionId").GetString() != prepared.SessionId)
            throw new InvalidOperationException("MO2 session is not proven unlaunched; retain recovery evidence.");
        // Preserve journal and bytes rather than recursively deleting a tree
        // which may contain unexpected links or runtime outputs.
        RecordTestLaunchPhase(prepared, "prepared", "retired-unlaunched");
    }

    // Recheck the selected source winners and sealed overlay immediately before
    // launch admission. A prepared journal is not evidence of a launched game.
    public static void VerifyPrepared(Mo2OverlayPreparation prepared, Mo2Stack selected)
    {
        if (!selected.Ready || !selected.ProfileVerified || selected.GameOnlyFallback ||
            !string.Equals(selected.Profile, prepared.Profile, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("MO2 profile changed or is no longer admitted.");
        EnsureNoReparseParents(prepared.StagingDirectory);
        EnsureNoReparseParents(Path.GetDirectoryName(prepared.JournalFile)!);
        RequireRegularUnlinkedFile(prepared.JournalFile);
        using var doc = JsonDocument.Parse(File.ReadAllText(prepared.JournalFile));
        var journal = doc.RootElement;
        if (journal.GetProperty("phase").GetString() != "prepared" ||
            journal.GetProperty("sessionId").GetString() != prepared.SessionId ||
            journal.GetProperty("profile").GetString() != prepared.Profile ||
            !string.Equals(journal.GetProperty("stageRoot").GetString(), prepared.StagingDirectory,
                StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("MO2 prepared session identity changed.");
        var originals = journal.GetProperty("originalSources");
        var paths = journal.GetProperty("originalSourcePaths");
        var expected = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in originals.EnumerateObject())
        {
            expected.Add(entry.Name);
            if (!selected.EffectiveFiles.TryGetValue(entry.Name, out var winner) ||
                !string.Equals(Path.GetFullPath(winner.PhysicalPath),
                    paths.GetProperty(entry.Name).GetString(), StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("MO2 source winner changed: " + entry.Name);
            EnsureNoReparseParents(Path.GetDirectoryName(winner.PhysicalPath)!);
            RequireRegularUnlinkedFile(winner.PhysicalPath);
            if (!Hash(winner.PhysicalPath).Equals(entry.Value.GetString(), StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("MO2 source bytes changed: " + entry.Name);
        }
        if (!expected.SetEquals(selected.EffectiveFiles.Keys.Where(IsRequiredOwnerInput)))
            throw new InvalidDataException("MO2 owner input inventory changed.");
        var historicalResults = journal.GetProperty("priorGrspResults");
        var historicalSources = journal.GetProperty("priorGrspResultSources");
        var evidenceRoot = Path.Combine(Path.GetDirectoryName(Path.GetDirectoryName(prepared.JournalFile)!)!,
            "recovery", prepared.SessionId, "prior-grsp-results");
        if (!string.Equals(evidenceRoot, journal.GetProperty("priorGrspResultsRoot").GetString(),
                StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("MO2 prior result archive identity changed.");
        var historicalKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in historicalResults.EnumerateObject())
        {
            if (!IsHistoricalGrspResult(NormalizeRelative(entry.Name)) ||
                !selected.EffectiveFiles.TryGetValue(entry.Name, out var winner) ||
                !string.Equals(Path.GetFullPath(winner.PhysicalPath),
                    historicalSources.GetProperty(entry.Name).GetString(), StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("Previous GRSP result winner changed: " + entry.Name);
            var archived = Path.Combine(evidenceRoot, entry.Name);
            EnsureNoReparseParents(Path.GetDirectoryName(archived)!);
            RequireRegularUnlinkedFile(archived);
            RequireRegularUnlinkedFile(winner.PhysicalPath);
            if (Hash(archived) != entry.Value.GetString() || Hash(winner.PhysicalPath) != entry.Value.GetString())
                throw new InvalidDataException("Previous GRSP capture changed before launch: " + entry.Name);
            historicalKeys.Add(entry.Name);
        }
        if (!historicalKeys.SetEquals(selected.EffectiveFiles.Keys.Where(IsHistoricalGrspResult)))
            throw new InvalidDataException("Previous GRSP result inventory changed.");
        var actual = Scan(prepared.StagingDirectory);
        var sealedFiles = journal.GetProperty("generatedFiles").Deserialize<Mo2OverlayFile[]>()
            ?? throw new InvalidDataException("MO2 overlay manifest missing.");
        if (actual.Count != sealedFiles.Length || !sealedFiles.SequenceEqual(prepared.Files))
            throw new InvalidDataException("MO2 overlay inventory changed.");
        var passthrough = journal.GetProperty("physicalPassthrough").Deserialize<string[]>()
            ?? throw new InvalidDataException("MO2 physical dependency manifest missing.");
        var latest = PhysicalPassthrough(selected, journal.GetProperty("gameRoot").GetString()!,
            sealedFiles.Select(f => f.GameRelativePath));
        if (!passthrough.SequenceEqual(latest, StringComparer.OrdinalIgnoreCase))
            throw new InvalidDataException("MO2 physical dependency winners changed.");
        foreach (var relative in passthrough)
        {
            var parent = Path.GetDirectoryName(Path.Combine(prepared.StagingDirectory, relative))!;
            EnsureNoReparseParents(parent);
            if (!Directory.Exists(parent))
                throw new InvalidDataException("MO2 dependency mapping parent missing: " + relative);
        }
        foreach (var file in sealedFiles)
        {
            if (!IsAllowedOutput(file.GameRelativePath) ||
                !actual.TryGetValue(file.GameRelativePath, out var hash) ||
                !hash.Equals(file.Sha256, StringComparison.OrdinalIgnoreCase) ||
                new FileInfo(Path.Combine(prepared.StagingDirectory, file.GameRelativePath)).Length != file.Size)
                throw new InvalidDataException("MO2 overlay bytes changed: " + file.GameRelativePath);
        }
    }

    private static string[] PhysicalPassthrough(Mo2Stack selected, string game,
        IEnumerable<string> stagedPaths)
    {
        var overridden = stagedPaths.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var result = new List<string>();
        foreach (var (key, winner) in selected.EffectiveFiles)
        {
            if (winner.FromMo2 || overridden.Contains(key) ||
                !(key.StartsWith("bin/x64/plugins/", StringComparison.OrdinalIgnoreCase) ||
                  key.StartsWith("red4ext/plugins/", StringComparison.OrdinalIgnoreCase) ||
                  key.Equals("red4ext/RED4ext.dll", StringComparison.OrdinalIgnoreCase)))
                continue;
            var relative = NormalizeRelative(key);
            var physical = Path.GetFullPath(Path.Combine(game, relative));
            if (!physical.Equals(Path.GetFullPath(winner.PhysicalPath), StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("Invalid physical MO2 dependency winner: " + relative);
            EnsureNoReparseParents(Path.GetDirectoryName(physical)!);
            RequireRegularUnlinkedFile(physical);
            result.Add(relative);
        }
        return result.OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToArray();
    }

    private static bool IsHistoricalGrspResult(string path) =>
        path.StartsWith("red4ext/plugins/G-REDscript-Profiler/RESULTS/", StringComparison.OrdinalIgnoreCase);

    internal static bool IsRequiredOwnerInput(string path) =>
        path.Equals("bin/x64/plugins/cyber_engine_tweaks.asi", StringComparison.OrdinalIgnoreCase) ||
        path.Equals("bin/x64/plugins/cyber_engine_tweaks/bindings.json", StringComparison.OrdinalIgnoreCase) ||
        path.StartsWith("bin/x64/plugins/cyber_engine_tweaks/mods/", StringComparison.OrdinalIgnoreCase) ||
        path.Equals("red4ext/plugins/G-REDscript-Profiler.dll", StringComparison.OrdinalIgnoreCase) ||
        path.Equals("red4ext/plugins/redscript_profiler_alpha.dll", StringComparison.OrdinalIgnoreCase) ||
        (path.StartsWith("red4ext/plugins/G-REDscript-Profiler/", StringComparison.OrdinalIgnoreCase) &&
         !IsHistoricalGrspResult(path));

    internal static bool IsAllowedOutput(string path)
    {
        if (path.Equals("bin/x64/plugins/cyber_engine_tweaks.asi", StringComparison.OrdinalIgnoreCase) ||
            path.Equals("bin/x64/plugins/cyber_engine_tweaks/bindings.json", StringComparison.OrdinalIgnoreCase) ||
            path.Equals("red4ext/plugins/G-REDscript-Profiler.dll", StringComparison.OrdinalIgnoreCase) ||
            path.Equals("bin/x64/plugins/cyber_engine_tweaks/mods/0-Engine/init.lua",
                StringComparison.OrdinalIgnoreCase)) return true;
        const string schedulerBase =
            "bin/x64/plugins/cyber_engine_tweaks/mods/0-Engine/modules/";
        if (path.StartsWith(schedulerBase, StringComparison.OrdinalIgnoreCase))
        {
            var tail = path[schedulerBase.Length..];
            return tail.Equals("Scheduler.lua", StringComparison.OrdinalIgnoreCase) ||
                tail.Equals("CETProfilerScheduler.lua", StringComparison.OrdinalIgnoreCase) ||
                tail.Equals("G-CET/Scheduler.lua", StringComparison.OrdinalIgnoreCase);
        }
        if (path.StartsWith("bin/x64/plugins/cyber_engine_tweaks/mods/CETProfilerControls/",
                StringComparison.OrdinalIgnoreCase))
            return path.EndsWith(".lua", StringComparison.OrdinalIgnoreCase) ||
                path.EndsWith(".json", StringComparison.OrdinalIgnoreCase) ||
                path.EndsWith(".txt", StringComparison.OrdinalIgnoreCase);
        return path.Equals("red4ext/plugins/G-REDscript-Profiler/CaptureTitle.txt",
            StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsOwnerTransactionMetadata(string path) =>
        path.StartsWith("bin/x64/plugins/.cet_runtime_profiler/", StringComparison.OrdinalIgnoreCase) ||
        path.Equals("red4ext/plugins/G-REDscript-Profiler/.manager_state.json",
            StringComparison.OrdinalIgnoreCase);

    private static Dictionary<string, string> Scan(string root)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var path in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories))
        {
            RequireRegularUnlinkedFile(path);
            var relative = NormalizeRelative(Path.GetRelativePath(root, path).Replace('\\', '/'));
            if (!result.TryAdd(relative, Hash(path)))
                throw new InvalidDataException("Case-colliding files in owner fixture.");
        }
        return result;
    }

    private static string NormalizeRelative(string value)
    {
        var normalized = value.Replace('\\', '/');
        if (normalized.Length == 0 || normalized.StartsWith('/') || normalized.Contains(':') ||
            normalized.Split('/').Any(p => p is "" or "." or "..") ||
            normalized.Contains('\0'))
            throw new InvalidDataException("Invalid game-relative owner path.");
        return normalized;
    }

    private static void RequireRegularUnlinkedFile(string path)
    {
        if (!File.Exists(path) || (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
            throw new InvalidDataException("Linked or unavailable MO2 source refused: " + path);
    }

    private static void EnsureNoReparseParents(string path)
    {
        for (var parent = Path.GetFullPath(path); !string.IsNullOrEmpty(parent);
             parent = Path.GetDirectoryName(parent))
            if (Directory.Exists(parent) &&
                (File.GetAttributes(parent) & FileAttributes.ReparsePoint) != 0)
                throw new InvalidDataException("Linked MO2 GCO stage parent refused.");
    }

    private static string Hash(string path)
    {
        using var file = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(file)).ToLowerInvariant();
    }

    private static void WriteJournal(string path, object data)
    {
        var tmp = path + ".tmp";
        File.WriteAllText(tmp, JsonSerializer.Serialize(data));
        File.Move(tmp, path, overwrite: true);
    }
}
