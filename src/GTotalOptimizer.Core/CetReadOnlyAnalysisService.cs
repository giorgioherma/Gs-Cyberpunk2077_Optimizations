using System.Diagnostics;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;

namespace GTotalOptimizer.Core;

/// <summary>
/// Page 2 read-only CET analysis. The source owner remains responsible for
/// CET capture reporting, Resolver interpretation and G-GC. This class only
/// consumes COLLECT's indexed evidence in a disposable workspace. CET's
/// existing source-owned Resolver owns all optimization selection and ZIP generation.
/// </summary>
public sealed record CetAnalysisOutcome(
    string SessionId, int InstalledCetMods, int CapturedCetFiles, int CapturedRedscriptFiles,
    bool HasPresentMon, int? PresentMonFrames, int MeasuredCallbackFamilies,
    int RankedCallbacks, int GenericCandidates, int SemanticCandidates,
    int AlreadySatisfied, int MaterialRemaining, int Unresolved,
    string FrameEvidence, string RuntimeInterpretation, string SourceVerification,
    string[] BlacklistedMods)
{
    // Lua source files in the verified current physical CET tree, not capture CSVs.
    public int LiveLuaSourceFiles { get; init; }
}

public sealed record CetSkipDetail(
    string Category, string Type, string? Owner, string? RuleId,
    string? File, string Reason);

public sealed record CetPassOutcome(string ZipPath, int FileCount, int TransformCount, int SkippedCount)
{
    // The CET owner's transform count includes per-capture G-GC calibration.
    // Only actual work (including a proven obsolete runtime upgrade) qualifies.
    public int OptimizationTransformCount { get; init; }
    public CetSkipDetail[] SkipDetails { get; init; } = [];
    public int HotscenesEnglishOnlyEdits { get; init; }
    // Distinct physically changed CET mod roots. Excludes support-only G-GC.
    public string[] OptimizedModNames { get; init; } = [];
}

public sealed class CetReadOnlyAnalysisService
{
    private static readonly TimeSpan ResolverTimeout = TimeSpan.FromMinutes(4);
    private const string ResolverRelative = "components/cet-resolver/app/G-CET-Resolver.exe";

    public async Task<CetAnalysisOutcome> AnalyzeAsync(
        string packageRoot, string gameRoot, string sessionId,
        CancellationToken cancellationToken = default)
        => (await ExecuteAsync(packageRoot, gameRoot, sessionId,
            generatePass: false, cancellationToken).ConfigureAwait(false)).Analysis;

    public async Task<CetPassOutcome> GenerateCetPassAsync(
        string packageRoot, string gameRoot, string sessionId,
        CancellationToken cancellationToken = default,
        bool hotscenesEnglishOnly = false)
        => (await ExecuteAsync(packageRoot, gameRoot, sessionId,
            generatePass: true, cancellationToken,
            hotscenesEnglishOnly: hotscenesEnglishOnly).ConfigureAwait(false)).Pass
            ?? throw new InvalidOperationException("CET owner did not return a pass.");

    // GCO's combined generator requests an isolated owner ZIP. The CET
    // owner still performs every transformation and all source checks; only
    // the publication destination differs.
    public async Task<CetPassOutcome> GenerateCetStagedAsync(
        string packageRoot, string gameRoot, string sessionId,
        string externalZip, CancellationToken cancellationToken = default,
        bool hotscenesEnglishOnly = false)
        => (await ExecuteAsync(packageRoot, gameRoot, sessionId,
            generatePass: true, cancellationToken, externalZip, hotscenesEnglishOnly)
            .ConfigureAwait(false)).Pass
            ?? throw new InvalidOperationException("CET did not return a staged pass.");

    private async Task<(CetAnalysisOutcome Analysis, CetPassOutcome? Pass)> ExecuteAsync(
        string packageRoot, string gameRoot, string sessionId,
        bool generatePass, CancellationToken cancellationToken,
        string? stagedOutput = null, bool hotscenesEnglishOnly = false)
    {
        cancellationToken.ThrowIfCancellationRequested();
        packageRoot = Path.GetFullPath(packageRoot);
        gameRoot = Path.GetFullPath(gameRoot);
        // COLLECT owns admission; CONTINUE owns uninstall. ANALYZE and
        // GENERATE consume the selected indexed capture independently of
        // profiler install/restore state. No second lifecycle gate.
        var saved = CollectedCaptureStore.Read(packageRoot, gameRoot, sessionId);
        if (saved.Cet is null || saved.Redscript is null)
            throw new InvalidDataException("Both collected native owner archives are required.");

        var inspection = GameInspector.Inspect(gameRoot);
        if (!inspection.HasVerifiedPhysicalSource ||
            inspection.SourceKind != SourceKind.GameRoot ||
            inspection.CetModCount < 1 || inspection.CetModsRoot is null)
            throw new InvalidDataException(
                "Select the real installed Cyberpunk game folder with readable CET source; virtual MO2 sources are not supported.");
        var source = inspection.CetModsRoot;
        if (!File.Exists(Path.Combine(source, "0-Engine", "init.lua")))
            throw new InvalidDataException("0-Engine source is missing from the installed CET tree.");

        var executable = VerifyOwnedResolver(packageRoot);
        var bundle = ComponentBundleVerifier.Inspect(packageRoot);
        if (!bundle.Ready || bundle.CetExecutable is null)
            throw new InvalidDataException("The bundled source-owner CET reporter is not verified: " + bundle.Detail);

        // Fingerprints are CURRENT effective installed files, not hashes or
        // mod names recorded during capture. The capture does not prove a
        // historical source tree matches the currently installed tree.
        var originalSources = FingerprintLiveSource(source, cancellationToken);
        if (originalSources.Count == 0)
            throw new InvalidDataException("No readable live CET source files found.");
        var sessionFolder = CollectedCaptureStore.SessionFolder(packageRoot, saved.SessionId);
        var disposable = Path.Combine(Path.GetTempPath(),
            "GCO-CET-ReadOnly-" + Guid.NewGuid().ToString("N"));
        var stagedCapture = Path.Combine(disposable, "G-CET");
        try
        {
            Directory.CreateDirectory(stagedCapture);
            CopyIndexedOwner(
                CollectedCaptureStore.DomainFolder(packageRoot, saved.SessionId, "CET"),
                stagedCapture, saved.Cet, cancellationToken);

            // Force the CET-owned reporter to regenerate the handoff from
            // the staged raw capture, never rely on a previously captured
            // CET_Resolver_Input.json that predates the new frame evidence.
            var handoff = Path.Combine(stagedCapture, "CET_Resolver_Input.json");
            if (File.Exists(handoff)) File.Delete(handoff);
            if (saved.FrameTimeEvidence is not null)
            {
                var overlap = PresentMonCaptureAlignment.RequireOverlap(packageRoot, saved);
                _ = GcoPresentMonFrameEvidence.WriteToStagedCetCapture(
                    sessionFolder, saved, overlap, stagedCapture);
            }

            using (var reportResult = await RunOwnerAsync(bundle.CetExecutable,
                ["--report", "--capture", stagedCapture, "--json"],
                packageRoot, cancellationToken))
            {
                RequireOk(reportResult.RootElement, "CET reporter");
            }
            if (!File.Exists(handoff))
                throw new InvalidDataException("The CET source owner did not produce its canonical report handoff.");
            using var handoffJson = JsonDocument.Parse(File.ReadAllText(handoff));
            var root = handoffJson.RootElement;
            if (saved.FrameTimeEvidence is not null &&
                (!root.TryGetProperty("frameEvidence", out var frame) ||
                 Text(frame, "provider") != "PRESENTMON_2_6" ||
                 Text(frame, "correlation") != "QPC_F11_CET_WINDOW_CORRELATION_ONLY"))
                throw new InvalidDataException("CET reporting did not consume the verified PresentMon F11 frame evidence.");

            using var resolved = await RunOwnerAsync(executable,
                ["--capture", stagedCapture, "--game", gameRoot, "--json"],
                packageRoot, cancellationToken);
            var cli = resolved.RootElement;
            RequireOk(cli, "CET Resolver");
            var reportPath = Text(cli, "ResolverPath");
            if (!InsideDirectory(reportPath, stagedCapture) || !File.Exists(reportPath))
                throw new InvalidDataException("CET Resolver report is missing or outside the disposable capture.");
            using var resolution = JsonDocument.Parse(File.ReadAllText(reportPath));
            if (!resolution.RootElement.TryGetProperty("captureRuntimeInterpretation", out var mode) ||
                !resolution.RootElement.TryGetProperty("gGcCaptureCalibration", out _))
                throw new InvalidDataException("CET Resolver omitted source-owned runtime/G-GC evidence.");
            var sourceMode = Text(mode, "mode");
            // The source owner explicitly marks capture/live-source identity
            // as unverified; never turn a current hash snapshot into proof of
            // what the player ran during this historical capture.
            if (!mode.TryGetProperty("captureMatchesLiveSourceVerified", out var sourceMatch) ||
                sourceMatch.ValueKind != JsonValueKind.False)
                throw new InvalidDataException("CET source/capture identity contract changed; refuse unsupported claims.");

            var now = FingerprintLiveSource(source, cancellationToken);
            if (!SameInventory(originalSources, now))
                throw new InvalidDataException(
                    "Live CET source changed during analysis. No result can be trusted; capture is untouched.");
            // The source owner already recomposes the current installed tree
            // during generation. Never publish if that tree drifted mid-pass.
            CetPassOutcome? generatedPass = null;
            // A completed CET installation may have zero remaining work.
            // Combined GCO generation can still publish REDscript changes
            // without asking CET to synthesize an empty optimization ZIP.
            if (generatePass && stagedOutput is not null &&
                Nonnegative(cli, "MaterialRemainingCount") == 0 &&
                Nonnegative(cli, "GenericResolvedCount") == 0 &&
                Nonnegative(cli, "SemanticReadyRuleCount") == 0 &&
                (!hotscenesEnglishOnly || !File.Exists(Path.Combine(
                    source, "hotscenes", "init.lua"))))
            {
                generatedPass = new CetPassOutcome("", 0, 0, 0);
            }
            else if (generatePass)
            {
                var temporaryZip = Path.Combine(disposable, "G-CET_PASS.zip");
                // CET owns the source rewrite; only opt-in forwarding is
                // performed here. The flag is never sent during ANALYZE.
                using var generation = await RunOwnerAsync(executable,
                    BuildGenerateOwnerArguments(stagedCapture, gameRoot,
                        temporaryZip, hotscenesEnglishOnly),
                    packageRoot, cancellationToken).ConfigureAwait(false);
                var cliPass = generation.RootElement;
                RequireOk(cliPass, "CET pass generator");
                if (!cliPass.TryGetProperty("pass", out var pass) ||
                    pass.ValueKind != JsonValueKind.Object ||
                    Text(pass, "ZipPath") != temporaryZip)
                    throw new InvalidDataException("CET owner returned an invalid pass location.");
                var expectedFiles = Nonnegative(pass, "FileCount");
                if (expectedFiles == 0 || !File.Exists(temporaryZip))
                    throw new InvalidDataException("CET owner produced no optimization ZIP.");
                VerifyCetOverlay(temporaryZip, expectedFiles);
                if (!SameInventory(originalSources,
                        FingerprintLiveSource(source, cancellationToken)))
                    throw new InvalidDataException(
                        "CET source changed while generating; ZIP was not published.");

                // Only a clean game-relative ZIP reaches RESULTS, never the
                // owner report/manifest, profiler binaries, or source files.
                var results = Path.Combine(packageRoot, "RESULTS");
                Directory.CreateDirectory(results);
                var destination = stagedOutput is null
                    ? Path.Combine(results, "G-CET_PASS_" + sessionId + ".zip")
                    : Path.GetFullPath(stagedOutput);
                if (stagedOutput is not null)
                {
                    var realGame = Path.GetFullPath(gameRoot).TrimEnd(
                        Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
                    var realSession = Path.GetFullPath(sessionFolder).TrimEnd(
                        Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
                    if (destination.StartsWith(realGame, StringComparison.OrdinalIgnoreCase) ||
                        destination.StartsWith(realSession, StringComparison.OrdinalIgnoreCase) ||
                        Directory.Exists(destination) ||
                        !Directory.Exists(Path.GetDirectoryName(destination)))
                        throw new InvalidDataException(
                            "Combined CET intermediate must be an existing isolated directory.");
                }
                if (File.Exists(destination))
                    throw new IOException("A CET pass already exists for this capture; it was not overwritten.");
                var publishing = destination + ".partial-" + Guid.NewGuid().ToString("N");
                try
                {
                    File.Copy(temporaryZip, publishing, overwrite: false);
                    File.Move(publishing, destination, overwrite: false);
                }
                finally { if (File.Exists(publishing)) File.Delete(publishing); }
                generatedPass = new CetPassOutcome(destination, expectedFiles,
                    Nonnegative(pass, "TransformCount"), Nonnegative(pass, "SkippedCount"))
                {
                    OptimizationTransformCount =
                        ReadOwnerOptimizationTransforms(pass, temporaryZip),
                    SkipDetails = ReadOwnerSkipDetails(pass, temporaryZip),
                    HotscenesEnglishOnlyEdits = ReadOwnerEnglishOnlyEdits(
                        pass, temporaryZip, hotscenesEnglishOnly),
                    OptimizedModNames = ReadOwnerOptimizedMods(pass, temporaryZip)
                };
            }

            var grspCount = saved.Redscript.Files.Count;
            var usedFrame = saved.FrameTimeEvidence is not null;
            var installed = inspection.CetModCount;
            var blacklisted = cli.TryGetProperty("BlacklistedMods", out var blocked) &&
                blocked.ValueKind == JsonValueKind.Array
                    ? blocked.EnumerateArray().Where(x => x.ValueKind == JsonValueKind.String)
                        .Select(x => x.GetString() ?? "").ToArray()
                    : Array.Empty<string>();
            var analysis = new CetAnalysisOutcome(
                sessionId, installed, saved.Cet.Files.Count, grspCount,
                usedFrame, saved.PresentMonAlignedFrames,
                Nonnegative(cli, "FamilyCount"), Nonnegative(cli, "RankedCallbackCount"),
                Nonnegative(cli, "GenericResolvedCount"), Nonnegative(cli, "SemanticReadyRuleCount"),
                Nonnegative(cli, "AlreadySatisfiedCount"), Nonnegative(cli, "MaterialRemainingCount"),
                Nonnegative(cli, "UnresolvedCount"),
                usedFrame ? "PRESENTMON_QPC_F11_VERIFIED" : "NOT_CAPTURED",
                sourceMode,
                "CURRENT_SOURCE_READ_ONLY; CAPTURE_SOURCE_IDENTITY_UNVERIFIED",
                blacklisted)
            {
                LiveLuaSourceFiles = originalSources.Keys.Count(path =>
                    path.EndsWith(".lua", StringComparison.OrdinalIgnoreCase))
            };
            return (analysis, generatedPass);
        }
        finally
        {
            // Never delete the immutable RESULTS session or source. Only
            // this run's exclusively-created temp directory is removable.
            if (Directory.Exists(disposable))
            {
                try { Directory.Delete(disposable, recursive: true); }
                catch (IOException e) { Trace.TraceWarning("Temporary CET analysis cleanup: " + e.Message); }
                catch (UnauthorizedAccessException e) { Trace.TraceWarning("Temporary CET analysis cleanup: " + e.Message); }
            }
        }
    }

    // The production CLI argument builder is also used by the integration
    // contract to prove the localization cut is never enabled by default.
    internal static IReadOnlyList<string> BuildGenerateOwnerArguments(
        string capture, string game, string zip, bool hotscenesEnglishOnly)
    {
        var args = new List<string>
        {
            "--capture", capture, "--game", game,
            "--generate-pass", "--pass-output", zip
        };
        if (hotscenesEnglishOnly)
            args.Add("--hotscenes-english-only");
        args.Add("--json");
        return args;
    }

    // Ground actual new work in the CET owner's authoritative transform list.
    // Calibration-only G-GC support never authorizes another ZIP. A proven
    // obsolete installed G-GC runtime upgrade is actual source-changing work.
    internal static int CountOptimizationTransforms(JsonElement manifest)
    {
        if (!manifest.TryGetProperty("transforms", out var rows) ||
            rows.ValueKind != JsonValueKind.Array)
            throw new InvalidDataException("GCO_CET_TRANSFORM_MANIFEST_MISSING");
        int count = 0;
        foreach (var item in rows.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object ||
                !item.TryGetProperty("type", out var type) ||
                type.ValueKind != JsonValueKind.String)
                throw new InvalidDataException("GCO_CET_TRANSFORM_MANIFEST_INVALID");
            if (type.GetString() != "GC_CONTROLLER" ||
                (item.TryGetProperty("runtimeUpgrade", out var runtimeUpgrade) &&
                 runtimeUpgrade.ValueKind == JsonValueKind.True))
                count++;
        }
        return count;
    }

    // The CET owner reports transform operations (potentially many per mod).
    // Count a source mod once only if its actual composed file has positive
    // appliedTransforms. Fixed 0-Engine delivery and G-GC calibration do not
    // become extra modified mods; the optional ENG-only edit is counted when
    // the user enables it at Generate time.
    internal static string[] CountOptimizedSourceMods(JsonElement manifest)
    {
        if (!manifest.TryGetProperty("files", out var files) ||
            files.ValueKind != JsonValueKind.Array)
            throw new InvalidDataException("GCO_CET_FILE_MANIFEST_MISSING");
        var owners = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in files.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object ||
                !item.TryGetProperty("path", out var path) ||
                path.ValueKind != JsonValueKind.String ||
                !item.TryGetProperty("appliedTransforms", out var edits) ||
                edits.ValueKind != JsonValueKind.Number ||
                !edits.TryGetInt32(out var applied) || applied < 0)
                throw new InvalidDataException("GCO_CET_FILE_MANIFEST_INVALID");
            if (applied == 0) continue;
            string relative = (path.GetString() ?? "").Replace('\\', '/');
            int boundary = relative.IndexOf('/');
            if (boundary <= 0 || relative.StartsWith("/", StringComparison.Ordinal) ||
                relative.Split('/').Any(part => part is "" or "." or ".."))
                throw new InvalidDataException("GCO_CET_OPTIMIZED_MOD_PATH_INVALID");
            string owner = relative[..boundary];
            if (!owner.Equals("G-GC", StringComparison.OrdinalIgnoreCase))
                owners.Add(owner);
        }
        return owners.OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToArray();
    }

    private static string[] ReadOwnerOptimizedMods(JsonElement pass, string zipPath)
    {
        string expected = Path.ChangeExtension(zipPath, ".json");
        if (!pass.TryGetProperty("ManifestPath", out var location) ||
            location.ValueKind != JsonValueKind.String ||
            !string.Equals(Path.GetFullPath(location.GetString() ?? ""),
                Path.GetFullPath(expected), StringComparison.OrdinalIgnoreCase) ||
            !File.Exists(expected))
            throw new InvalidDataException("GCO_CET_FILE_MANIFEST_MISSING");
        using var doc = JsonDocument.Parse(File.ReadAllText(expected));
        return CountOptimizedSourceMods(doc.RootElement);
    }

    private static int ReadOwnerOptimizationTransforms(JsonElement pass, string zipPath)
    {
        string expected = Path.ChangeExtension(zipPath, ".json");
        if (!pass.TryGetProperty("ManifestPath", out var location) ||
            location.ValueKind != JsonValueKind.String ||
            !string.Equals(Path.GetFullPath(location.GetString() ?? ""),
                Path.GetFullPath(expected), StringComparison.OrdinalIgnoreCase) ||
            !File.Exists(expected))
            throw new InvalidDataException("GCO_CET_TRANSFORM_MANIFEST_MISSING");
        using var manifest = JsonDocument.Parse(File.ReadAllText(expected));
        int count = CountOptimizationTransforms(manifest.RootElement);
        int recorded = manifest.RootElement.GetProperty("transforms").GetArrayLength();
        if (recorded != Nonnegative(pass, "TransformCount"))
            throw new InvalidDataException("GCO_CET_TRANSFORM_MANIFEST_COUNT_MISMATCH");
        return count;
    }

    private static int ReadOwnerEnglishOnlyEdits(
        JsonElement pass, string zipPath, bool requested)
    {
        // Read the authoritative CET manifest, not an inferred Hotscenes
        // source marker or the presence of unrelated generated Lua.
        string manifestPath = Path.ChangeExtension(zipPath, ".json");
        if (!pass.TryGetProperty("ManifestPath", out var output) ||
            output.ValueKind != JsonValueKind.String ||
            !string.Equals(Path.GetFullPath(output.GetString() ?? ""),
                Path.GetFullPath(manifestPath), StringComparison.OrdinalIgnoreCase) ||
            !File.Exists(manifestPath))
            throw new InvalidDataException(
                "CET owner did not provide its English-only opt-in audit manifest.");

        using var manifest = JsonDocument.Parse(File.ReadAllText(manifestPath));
        var policy = manifest.RootElement.GetProperty("policy");
        bool actualRequest = policy.GetProperty(
            "personalEnglishOnlyHotscenesRequested").GetBoolean();
        int applied = policy.GetProperty(
            "personalEnglishOnlyHotscenesStages").GetInt32();
        if (actualRequest != requested || applied is < 0 or > 1 ||
            (!actualRequest && applied != 0))
            throw new InvalidDataException(
                "CET Hotscenes English-only opt-in differs from the owner output.");
        return applied;
    }

    private static CetSkipDetail[] ReadOwnerSkipDetails(JsonElement pass, string zipPath)
    {
        // The CET source owner already emits an authoritative companion
        // manifest with categorized skip reasons. Read that report as-is;
        // never infer refused mods from callback counts or regenerate rules.
        // Older owner packages may omit this companion: preserve the owner's
        // numeric skipped count rather than blocking safe output.
        string expected = Path.ChangeExtension(zipPath, ".json");
        if (!pass.TryGetProperty("ManifestPath", out var location) ||
            location.ValueKind != JsonValueKind.String ||
            !string.Equals(Path.GetFullPath(location.GetString() ?? ""),
                Path.GetFullPath(expected), StringComparison.OrdinalIgnoreCase) ||
            !File.Exists(expected))
            return [];
        using var manifest = JsonDocument.Parse(File.ReadAllText(expected));
        var root = manifest.RootElement;
        if (!root.TryGetProperty("skipOutcomes", out var outcomes) ||
            !outcomes.TryGetProperty("entries", out var rows) ||
            rows.ValueKind != JsonValueKind.Array)
            return [];

        static string? Field(JsonElement row, string key) =>
            row.TryGetProperty(key, out var value) &&
            value.ValueKind == JsonValueKind.String
                ? value.GetString() : null;
        return rows.EnumerateArray()
            .Where(row => row.ValueKind == JsonValueKind.Object)
            .Select(row => new CetSkipDetail(
                Field(row, "category") ?? "GENERATION_FAILURE",
                Field(row, "type") ?? "UNKNOWN",
                Field(row, "owner"), Field(row, "ruleId"),
                Field(row, "file"), Field(row, "reason") ?? "No reason supplied"))
            .ToArray();
    }

    private static void VerifyCetOverlay(string path, int expectedFiles)
    {
        // Inspect only the owner's actual output, do not transform or repair.
        using var archive = ZipFile.OpenRead(path);
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (archive.Entries.Count != expectedFiles || expectedFiles < 1)
            throw new InvalidDataException("CET generated ZIP file inventory disagrees with owner manifest.");
        foreach (var entry in archive.Entries)
        {
            var name = entry.FullName;
            if (!seen.Add(name) || entry.Length == 0 ||
                name.Contains('\\') || name.StartsWith('/') ||
                name.Split('/').Any(x => x is "" or "." or "..") ||
                !(name.StartsWith("bin/x64/plugins/cyber_engine_tweaks/mods/",
                    StringComparison.OrdinalIgnoreCase) ||
                  name.StartsWith("red4ext/plugins/G-CET-StaticLocations/",
                    StringComparison.OrdinalIgnoreCase)))
                throw new InvalidDataException("CET generated ZIP contains an invalid game-relative entry.");
        }
    }

    private static string VerifyOwnedResolver(string root)
    {
        var manifest = Path.Combine(root, "components", "manifest.json");
        if (!File.Exists(manifest)) throw new InvalidDataException("GCO component manifest is missing.");
        using var doc = JsonDocument.Parse(File.ReadAllText(manifest));
        if (!doc.RootElement.TryGetProperty("analysisTools", out var tools) ||
            tools.ValueKind != JsonValueKind.Array ||
            tools.GetArrayLength() != 1)
            throw new InvalidDataException("CET Resolver package identity is missing.");
        var component = tools[0];
        if (Text(component, "id") != "cet-resolver" ||
            Text(component, "executable").Replace('\\', '/') != ResolverRelative)
            throw new InvalidDataException("CET Resolver manifest changed or is unverified.");
        var binary = Path.Combine(root, ResolverRelative.Replace('/', Path.DirectorySeparatorChar));
        if (!File.Exists(binary) || (File.GetAttributes(binary) & FileAttributes.ReparsePoint) != 0 ||
            !Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(binary)))
                .Equals(Text(component, "sha256"), StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("CET Resolver executable does not match its package manifest.");
        return binary;
    }

    private static Dictionary<string, string> FingerprintLiveSource(string source, CancellationToken token)
    {
        var contents = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        void Visit(string folder)
        {
            token.ThrowIfCancellationRequested();
            if ((File.GetAttributes(folder) & FileAttributes.ReparsePoint) != 0)
                throw new InvalidDataException("Linked CET source folder refused; physical effective source is required.");
            foreach (var file in Directory.EnumerateFiles(folder).OrderBy(x => x, StringComparer.OrdinalIgnoreCase))
            {
                token.ThrowIfCancellationRequested();
                if ((File.GetAttributes(file) & FileAttributes.ReparsePoint) != 0)
                    throw new InvalidDataException("Linked CET source file refused.");
                var relative = Path.GetRelativePath(source, file).Replace('\\', '/');
                using var stream = File.OpenRead(file);
                if (!contents.TryAdd(relative, Convert.ToHexString(SHA256.HashData(stream))))
                    throw new InvalidDataException("CET source contains conflicting case-insensitive paths.");
            }
            foreach (var dir in Directory.EnumerateDirectories(folder).OrderBy(x => x, StringComparer.OrdinalIgnoreCase))
                Visit(dir);
        }
        Visit(source);
        return contents;
    }

    private static bool SameInventory(Dictionary<string, string> before, Dictionary<string, string> after) =>
        before.Count == after.Count &&
        before.All(x => after.TryGetValue(x.Key, out var hash) && hash == x.Value);

    private static void CopyIndexedOwner(
        string original, string stage, CollectedOwner owner, CancellationToken token)
    {
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var file in owner.Files)
        {
            token.ThrowIfCancellationRequested();
            var relative = file.Path.Replace('\\', '/');
            var elements = relative.Split('/');
            if (elements.Any(x => x is "" or "." or "..") ||
                Path.IsPathRooted(relative) || !names.Add(relative))
                throw new InvalidDataException("CET capture index has duplicate/unsafe file paths.");
            // The standalone owner may have historical CapFrameX files.
            // GCO reporting/Resolver execution uses only real PresentMon or
            // NOT_CAPTURED. Never stage a legacy FrameTime companion here.
            if (elements.Any(x => x.Equals("FrameTime", StringComparison.OrdinalIgnoreCase)))
                continue;
            var current = Path.Combine(original, relative.Replace('/', Path.DirectorySeparatorChar));
            var target = Path.Combine(stage, relative.Replace('/', Path.DirectorySeparatorChar));
            if (!InsideDirectory(current, original) || !InsideDirectory(target, stage) ||
                !File.Exists(current) || (File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                throw new InvalidDataException("Indexed CET file is unavailable or outside the sealed capture.");
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(current, target, overwrite: false);
            using var stream = File.OpenRead(target);
            if (stream.Length != file.Bytes ||
                !Convert.ToHexString(SHA256.HashData(stream))
                    .Equals(file.Sha256, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("Staged CET evidence no longer matches its sealed hash.");
        }
    }

    private static bool InsideDirectory(string file, string root)
    {
        var path = Path.GetFullPath(file);
        return path.StartsWith(Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar) +
            Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
    }

    private static async Task<JsonDocument> RunOwnerAsync(
        string executable, IReadOnlyList<string> args, string packageRoot,
        CancellationToken token)
    {
        using var process = new Process
        {
            StartInfo = new ProcessStartInfo(executable)
            {
                UseShellExecute = false,
                RedirectStandardError = true,
                RedirectStandardOutput = true,
                CreateNoWindow = true,
                WorkingDirectory = Path.GetDirectoryName(executable)!
            }
        };
        foreach (var arg in args) process.StartInfo.ArgumentList.Add(arg);
        process.StartInfo.Environment["DOTNET_ROOT_X64"] = Path.Combine(packageRoot, "runtime");
        process.StartInfo.Environment["DOTNET_ROOT"] = Path.Combine(packageRoot, "runtime");
        process.StartInfo.Environment["DOTNET_MULTILEVEL_LOOKUP"] = "0";
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
        timeout.CancelAfter(ResolverTimeout);
        if (!process.Start()) throw new InvalidOperationException("Bundled CET analysis command did not start.");
        try
        {
            var stdout = process.StandardOutput.ReadToEndAsync(timeout.Token);
            var stderr = process.StandardError.ReadToEndAsync(timeout.Token);
            await process.WaitForExitAsync(timeout.Token).ConfigureAwait(false);
            var output = await stdout.ConfigureAwait(false);
            var diagnostic = await stderr.ConfigureAwait(false);
            if (process.ExitCode != 0)
            {
                // stderr can contain live local mod paths. Put private details
                // only in developer Trace; normal UI stays concise.
                Trace.TraceWarning("CET analysis owner refusal: " + diagnostic);
                throw new InvalidDataException("CET source-owned analysis refused the capture (exit " +
                    process.ExitCode + "). Original evidence and game files are unchanged.");
            }
            try { return JsonDocument.Parse(output); }
            catch (JsonException e)
            { throw new InvalidDataException("CET analysis returned malformed JSON.", e); }
        }
        catch (OperationCanceledException)
        {
            try { if (!process.HasExited) process.Kill(entireProcessTree: true); }
            catch (InvalidOperationException) { }
            if (!token.IsCancellationRequested)
                throw new TimeoutException("CET source-owned analysis exceeded its time limit.");
            throw;
        }
    }

    private static void RequireOk(JsonElement element, string component)
    {
        if (!element.TryGetProperty("ok", out var ok) ||
            ok.ValueKind != JsonValueKind.True)
            throw new InvalidDataException(component + " returned an unsuccessful result.");
    }

    private static string Text(JsonElement element, string key)
    {
        if (!element.TryGetProperty(key, out var value) ||
            value.ValueKind != JsonValueKind.String ||
            string.IsNullOrWhiteSpace(value.GetString()))
            throw new InvalidDataException("CET analysis contract lacks " + key + ".");
        return value.GetString()!;
    }

    private static int Nonnegative(JsonElement element, string key)
    {
        if (!element.TryGetProperty(key, out var value) ||
            value.ValueKind != JsonValueKind.Number ||
            !value.TryGetInt32(out var result) || result < 0)
            throw new InvalidDataException("CET Resolver metric invalid: " + key + ".");
        return result;
    }
}
