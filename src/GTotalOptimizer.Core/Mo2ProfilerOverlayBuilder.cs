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

            // This does not claim actual MO2 USVFS, native DLL or gameplay proof.
            WriteJournal(journal, new
            {
                schemaVersion = 1, sessionId, profile = selected.Profile, gameRoot = game,
                fixtureRoot = fixture, stageRoot = stage, phase = "prepared",
                originalSources = baselines, originalSourcePaths = owners, generatedFiles = changes,
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
                utc = DateTimeOffset.UtcNow
            });
            throw;
        }
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
        var actual = Scan(prepared.StagingDirectory);
        var sealedFiles = journal.GetProperty("generatedFiles").Deserialize<Mo2OverlayFile[]>()
            ?? throw new InvalidDataException("MO2 overlay manifest missing.");
        if (actual.Count != sealedFiles.Length || !sealedFiles.SequenceEqual(prepared.Files))
            throw new InvalidDataException("MO2 overlay inventory changed.");
        foreach (var file in sealedFiles)
        {
            if (!IsAllowedOutput(file.GameRelativePath) ||
                !actual.TryGetValue(file.GameRelativePath, out var hash) ||
                !hash.Equals(file.Sha256, StringComparison.OrdinalIgnoreCase) ||
                new FileInfo(Path.Combine(prepared.StagingDirectory, file.GameRelativePath)).Length != file.Size)
                throw new InvalidDataException("MO2 overlay bytes changed: " + file.GameRelativePath);
        }
    }

    internal static bool IsRequiredOwnerInput(string path) =>
        path.Equals("bin/x64/plugins/cyber_engine_tweaks.asi", StringComparison.OrdinalIgnoreCase) ||
        path.Equals("bin/x64/plugins/cyber_engine_tweaks/bindings.json", StringComparison.OrdinalIgnoreCase) ||
        path.StartsWith("bin/x64/plugins/cyber_engine_tweaks/mods/", StringComparison.OrdinalIgnoreCase) ||
        path.Equals("red4ext/plugins/G-REDscript-Profiler.dll", StringComparison.OrdinalIgnoreCase) ||
        path.Equals("red4ext/plugins/redscript_profiler_alpha.dll", StringComparison.OrdinalIgnoreCase) ||
        path.StartsWith("red4ext/plugins/G-REDscript-Profiler/", StringComparison.OrdinalIgnoreCase);

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
