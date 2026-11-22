using System.Globalization;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using GRedscript.Resolver;

namespace GTotalOptimizer.Core;

/// <summary>
/// One GCO Generate action. CET's authoritative Resolver supplies Lua/native
/// output, the GCO-local ordered Resolver supplies REDscript sources, and
/// CI validates official SCC 0.5.31 separately; runtime generation never invokes it.
/// All work is isolated. The only published game payload is one ZIP.
/// </summary>
public sealed record CombinedPassOutcome(
    string ZipPath, int CetFiles, int RedscriptFiles, int GeneratedFiles,
    int CetTransforms, int RedscriptEdits)
{
    public int AppliedRedscriptRules { get; init; }
    // Source-owned applied outputs may span several files per rule. Count
    // distinct rule IDs, not completed-file rows or refused operations.
    public int GenericRedscriptRules { get; init; }
    public int SemanticRedscriptRules { get; init; }
    public int GenericRedscriptEdits { get; init; }
    public int SemanticRedscriptEdits { get; init; }
    public int CetSkipped { get; init; }
    public CetSkipDetail[] CetSkipDetails { get; init; } = [];
    public RedscriptGenerationPlan.SkippedRule[] RedscriptSkipped { get; init; } = [];
    public bool HotscenesEnglishOnlyRequested { get; init; }
    public int HotscenesEnglishOnlyEdits { get; init; }
}

public sealed record CombinedPassAvailability(
    int CetOptimizationTransforms, int RedscriptEdits,
    RedscriptGenerationPlan.SkippedRule[] RedscriptSkipped)
{
    public bool HasNewOptimizations => CetOptimizationTransforms > 0 || RedscriptEdits > 0;
    public int RedscriptOptimizationCount { get; init; }

    // Count actual applied rule families, not individual rewritten expressions
    // or configured catalogue entries. A multi-file stage counts once.
    internal static int CountCompletedRedscriptOptimizations(
        IEnumerable<RedscriptGenerationPlan.OutputItem> completed) =>
        completed.Select(item => item.Id).Distinct(StringComparer.Ordinal).Count();
}

public sealed class NoNewOptimizationsException : InvalidOperationException
{
    public NoNewOptimizationsException() : base(
        "NO NEW OPTIMIZATIONS AVAILABLE. Installed compatible optimizations are already present.") { }
}

public sealed class CombinedOptimizationPassService
{
    private readonly CetReadOnlyAnalysisService cet = new();
    // Distinct from immutable profiler RESULTS; date and time name a pass.
    internal static string GeneratedPassFolder(string packageRoot, DateTime when) =>
        Path.Combine(Path.GetFullPath(packageRoot), "generated_optimizations",
            "pass-" + when.ToString("yyyyMMdd_HHmmss", CultureInfo.InvariantCulture));

    // Read-only, source-proven admission for the Page 2 Generate button.
    // Uses the SAME CET owner and REDscript one-pass source generator as
    // Generate, but stages only to an exclusive disposable directory. This
    // does not publish a ZIP, touch installed source or recalibrate live G-GC.
    public async Task<CombinedPassAvailability> InspectAvailableAsync(
        string packageRoot, string gameRoot, string sessionId,
        CancellationToken token = default,
        IProgress<WorkflowStepProgress>? progress = null)
    {
        string package = Path.GetFullPath(packageRoot);
        string game = Path.GetFullPath(gameRoot);
        var captured = CollectedCaptureStore.ReadRestored(package, game, sessionId);
        if (captured.Cet is null || captured.Redscript is null)
            throw new InvalidDataException("GCO_BOTH_COMPLETED_CAPTURE_DOMAINS_REQUIRED");
        string session = CollectedCaptureStore.SessionFolder(package, sessionId);
        string plan = Path.Combine(session, "REDscript_GenerationPlan.json");
        if (!File.Exists(plan))
            throw new InvalidDataException("Analyze must create a valid REDscript source plan first.");

        string temp = Path.Combine(Path.GetTempPath(), "GCO-Availability-" +
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temp);
        try
        {
            // Personal Hotscenes source reduction is an explicit Generate
            // opt-in. Never execute it during read-only Analyze.
            var cetResult = await cet.GenerateCetStagedAsync(
                package, game, sessionId, Path.Combine(temp, "cet-owner.zip"),
                token, hotscenesEnglishOnly: false).ConfigureAwait(false);
            token.ThrowIfCancellationRequested();
            progress?.Report(new(3, 5, "ANALYZE / CET CHANGES VERIFIED"));
            string analysis = Path.Combine(temp, "redscript-analysis.json");
            await Task.Run(() => Program.AnalyzeCollected(session, game, analysis),
                token).ConfigureAwait(false);
            token.ThrowIfCancellationRequested();
            string frameworkOwner = Path.Combine(package, "components",
                "redruntime", "owner");
            string? providedRuntime = Directory.Exists(frameworkOwner)
                ? frameworkOwner : null;
            var red = await Task.Run(() =>
                RedscriptGenerationPlan.Stage(game, plan, analysis,
                    Path.Combine(temp, "redscript-stage"), providedRuntime),
                token).ConfigureAwait(false);
            token.ThrowIfCancellationRequested();
            progress?.Report(new(4, 5, "ANALYZE / REDSCRIPT CHANGES VERIFIED"));
            return new(cetResult.OptimizationTransformCount,
                red.AppliedEdits, red.Skipped)
            {
                RedscriptOptimizationCount = CombinedPassAvailability
                    .CountCompletedRedscriptOptimizations(red.Completed)
            };
        }
        finally
        {
            if (Directory.Exists(temp))
                try { Directory.Delete(temp, recursive: true); }
                catch (IOException) { /* isolated preview cleanup only */ }
                catch (UnauthorizedAccessException) { }
        }
    }

    public async Task<CombinedPassOutcome> GenerateAsync(
        string packageRoot, string gameRoot, string sessionId,
        CancellationToken token = default, bool hotscenesEnglishOnly = false,
        IProgress<WorkflowStepProgress>? progress = null)
    {
        string package = Path.GetFullPath(packageRoot);
        string game = Path.GetFullPath(gameRoot);
        var captured = CollectedCaptureStore.ReadRestored(package, game, sessionId);
        if (captured.Cet is null || captured.Redscript is null)
            throw new InvalidDataException("GCO_BOTH_COMPLETED_CAPTURE_DOMAINS_REQUIRED");
        string session = CollectedCaptureStore.SessionFolder(package, sessionId);
        string plan = Path.Combine(session, "REDscript_GenerationPlan.json");
        if (!File.Exists(plan))
            throw new InvalidDataException(
                "Run Analyze before Generate; a source-checked REDscript plan is required.");

        // Every intermediate is under a new exclusive path, never the game,
        // immutable capture, old pass, or a shared/previous partial folder.
        string temp = Path.Combine(Path.GetTempPath(), "GCO-Combined-Pass-" +
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temp);
        try
        {
            string cetZip = Path.Combine(temp, "cet-owner.zip");
            var cetResult = await cet.GenerateCetStagedAsync(
                package, game, sessionId, cetZip, token,
                hotscenesEnglishOnly).ConfigureAwait(false);
            token.ThrowIfCancellationRequested();
            progress?.Report(new(1, 6, "GENERATE / CET OPTIMIZATIONS READY"));
            string analysis = Path.Combine(temp, "redscript-analysis.json");
            await Task.Run(() =>
            {
                Program.AnalyzeCollected(session, game, analysis);
            }, token).ConfigureAwait(false);
            token.ThrowIfCancellationRequested();

            progress?.Report(new(2, 6, "GENERATE / REDSCRIPT ANALYSIS READY"));
            string planOutput = Path.Combine(temp, "redscript-stage");
            string frameworkOwner = Path.Combine(package, "components",
                "redruntime", "owner");
            // The runtime is bundled into THIS pass after REDscript staging.
            // Supply its verified declarations to semantic ABI admission now,
            // without changing the original-source plan fingerprint.
            string? providedFramework = Directory.Exists(frameworkOwner)
                ? frameworkOwner : null;
            var red = await Task.Run(() =>
                RedscriptGenerationPlan.Stage(game, plan, analysis, planOutput,
                    providedFramework),
                token).ConfigureAwait(false);
            token.ThrowIfCancellationRequested();

            progress?.Report(new(3, 6, "GENERATE / REDSCRIPT OPTIMIZATIONS READY"));
            // A recalibrated threshold is support-only. A source-proven
            // installed G-GC runtime upgrade counts as source-changing work.
            // Do not publish a ZIP when neither owner has real new work.
            var applicable = new CombinedPassAvailability(
                cetResult.OptimizationTransformCount, red.AppliedEdits, red.Skipped);
            if (!applicable.HasNewOptimizations)
                throw new NoNewOptimizationsException();

            string overlay = Path.Combine(temp, "game-overlay");
            Directory.CreateDirectory(overlay);
            var owners = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            int cetFiles = cetResult.FileCount == 0 ? 0 :
                CopyOwnerZip(cetZip, overlay, owners);
            int redFiles = CopyScriptPreview(Path.Combine(
                planOutput, "effective-preview", "r6", "scripts"),
                overlay, owners);

            // The complete verified G-REDruntime core accompanies every REAL
            // optimization pass, like the CET-owned 0-Engine/G-GC/native support.
            // If the player already has a framework file (including a compatible
            // customization), package those exact installed bytes, not an older
            // historical version. Generated source takes precedence.
            _ = CopyCompleteFramework(game, overlay, frameworkOwner, owners);

            // Official SCC compilation belongs to CI; no compiler binaries
            // or profiler payloads are shipped in the optimized game ZIP.

            progress?.Report(new(4, 6, "GENERATE / COMBINED SOURCE STAGED"));
            if (cetFiles == 0 && redFiles == 0)
                throw new InvalidDataException(
                    "GCO_GENERATOR_REPORTED_EDITS_BUT_EMITTED_NO_SOURCE");

            string finalFolder = GeneratedPassFolder(package, DateTime.Now);
            string outputRoot = Path.GetDirectoryName(finalFolder)!;
            Directory.CreateDirectory(outputRoot);
            if ((File.GetAttributes(outputRoot) & FileAttributes.ReparsePoint) != 0)
                throw new InvalidDataException("Linked generated_optimizations directory refused.");
            if (Directory.Exists(finalFolder) || File.Exists(finalFolder))
                throw new IOException("An optimization pass already has this timestamp.");

            // Publish only after both ZIP and external owner report are
            // complete. A same-volume folder move exposes them together.
            string stagedFolder = Path.Combine(outputRoot,
                ".pass-pending-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(stagedFolder);
            const string zipName = "G-Cyberpunk2077_Optimization_Pass.zip";
            string pending = Path.Combine(stagedFolder, zipName);
            string final = Path.Combine(finalFolder, zipName);
            try
            {
                var indexed = CollectOutput(overlay, owners, game, token);
                if (indexed.Count == 0)
                    throw new InvalidDataException("GCO_COMBINED_EMPTY_OUTPUT");
                using (var archive = ZipFile.Open(pending, ZipArchiveMode.Create))
                {
                    int written = 0;
                    foreach (var entry in indexed)
                    {
                        token.ThrowIfCancellationRequested();
                        var zip = archive.CreateEntry(entry.Path,
                            CompressionLevel.Optimal);
                        using var input = File.OpenRead(entry.FullName);
                        using var target = zip.Open();
                        input.CopyTo(target);
                        written++;
                        // Only count completed ZIP entries, not estimated duration.
                        progress?.Report(new(4 * indexed.Count + written,
                            6 * indexed.Count, "GENERATE / WRITING ZIP"));
                    }
                }
                token.ThrowIfCancellationRequested();
                if (CombinedPassOutputInspector.Validate(pending) != indexed.Count)
                    throw new InvalidDataException("GCO_PASS_ARCHIVE_INVENTORY_MISMATCH");
                progress?.Report(new(5, 6, "GENERATE / ZIP VERIFIED"));
                // All sidecars, source hashes and exclusions stay outside the
                // game-only ZIP. Record original hashes for Vortex rollback.
                string manifest = pending + ".ownership.json";
                File.WriteAllText(manifest, JsonSerializer.Serialize(new {
                        schemaVersion = "gco.combined-pass.v1",
                        captureSession = sessionId,
                        redscriptSourceStackSha256 = red.SourceStackSha256,
                        cetFiles, redscriptFiles = redFiles,
                        redscriptEdits = red.AppliedEdits,
                        redscriptAppliedRules = red.Completed.Select(rule => new {
                            order = rule.Order, id = rule.Id,
                            rewrittenExpressions = rule.RewrittenExpressions,
                            newFiles = rule.NewFiles,
                            composedFiles = rule.ComposedFiles
                        }).ToArray(),
                        cetSkippedCount = cetResult.SkippedCount,
                        hotscenesEnglishOnly = new
                        {
                            requested = hotscenesEnglishOnly,
                            appliedEdits = cetResult.HotscenesEnglishOnlyEdits
                        },
                        cetSkipDetails = cetResult.SkipDetails.Select(item => new {
                            category = item.Category, type = item.Type,
                            owner = item.Owner, ruleId = item.RuleId,
                            file = item.File, reason = item.Reason
                        }).ToArray(),
                        redscriptSkippedRules = red.Skipped.Select(item => new {
                            order = item.Order, id = item.Id,
                            eligibleCandidates = item.EligibleCandidates,
                            reason = item.Reason
                        }).ToArray(),
                        redscriptDeferredCollisions = red.DeferredCollisions,
                        experimentalLocationGenerationIncluded = false,
                        officialScc031Passed = false,
                        officialScc031Validation = "CI_ONLY_NOT_RUN_IN_APP",
                        originalFilesUntouched = true,
                        runtimeGameplayValidated = false,
                        installRecommendation = "Vortex overlay after originals; manually back up before direct installation",
                        excludedOwners = cetResult.SkipDetails
                            .Where(item => item.Category == "MOD_BLACKLIST" &&
                                !string.IsNullOrWhiteSpace(item.Owner))
                            .Select(item => item.Owner!)
                            .Distinct(StringComparer.OrdinalIgnoreCase)
                            .OrderBy(owner => owner, StringComparer.OrdinalIgnoreCase)
                            .ToArray(),
                        files = indexed.Select(e => new {
                            path = e.Path, owner = e.Owner,
                            optimizedSha256 = Sha(e.FullName),
                            originalSha256 = File.Exists(e.LivePath)
                                ? Sha(e.LivePath) : null
                        }).ToArray()
                    }, new JsonSerializerOptions { WriteIndented = true }),
                    new UTF8Encoding(false));
                token.ThrowIfCancellationRequested();
                if (Directory.Exists(finalFolder) || File.Exists(finalFolder))
                    throw new IOException("An optimization pass already has this timestamp.");
                Directory.Move(stagedFolder, finalFolder);
                progress?.Report(new(6, 6, "GENERATE / ZIP PUBLISHED"));
                return new(final, cetFiles, redFiles, indexed.Count,
                    cetResult.OptimizationTransformCount, red.AppliedEdits)
                {
                    AppliedRedscriptRules = red.Completed
                        .Select(item => item.Id).Distinct(StringComparer.Ordinal).Count(),
                    GenericRedscriptRules = red.Completed
                        .Where(item => item.Order <= 10)
                        .Select(item => item.Id).Distinct(StringComparer.Ordinal).Count(),
                    SemanticRedscriptRules = red.Completed
                        .Where(item => item.Order > 10)
                        .Select(item => item.Id).Distinct(StringComparer.Ordinal).Count(),
                    GenericRedscriptEdits = red.Completed
                        .Where(item => item.Order <= 10)
                        .Sum(item => item.RewrittenExpressions),
                    SemanticRedscriptEdits = red.Completed
                        .Where(item => item.Order > 10)
                        .Sum(item => item.RewrittenExpressions),
                    CetSkipped = cetResult.SkippedCount,
                    CetSkipDetails = cetResult.SkipDetails,
                    RedscriptSkipped = red.Skipped,
                    HotscenesEnglishOnlyRequested = hotscenesEnglishOnly,
                    HotscenesEnglishOnlyEdits = cetResult.HotscenesEnglishOnlyEdits
                };
            }
            finally
            {
                if (Directory.Exists(stagedFolder))
                    Directory.Delete(stagedFolder, recursive: true);
            }
        }
        finally
        {
            // Never touch game or capture. Failed generation leaves no
            // ZIP and retains source owner evidence for diagnosis.
            if (Directory.Exists(temp))
                try { Directory.Delete(temp, recursive: true); }
                catch (IOException) { /* Failed sandbox cleanup is harmless. */ }
                catch (UnauthorizedAccessException) { }
        }
    }

    private sealed record IndexedFile(string Path, string FullName,
        string LivePath, string Owner);

    private static List<IndexedFile> CollectOutput(string overlay,
        Dictionary<string, string> owners, string game, CancellationToken token)
    {
        var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var files = new List<IndexedFile>();
        foreach (var file in Directory.EnumerateFiles(overlay, "*",
                     SearchOption.AllDirectories))
        {
            token.ThrowIfCancellationRequested();
            if ((File.GetAttributes(file) & FileAttributes.ReparsePoint) != 0)
                throw new InvalidDataException("GCO_PASS_SYMLINK_IN_OUTPUT");
            string rel = Path.GetRelativePath(overlay, file).Replace('\\', '/');
            if (!SafeGamePath(rel) || !paths.Add(rel) ||
                new FileInfo(file).Length == 0 ||
                !owners.TryGetValue(rel, out var owner))
                throw new InvalidDataException("GCO_PASS_UNKNOWN_OR_DUPLICATE_OUTPUT: " + rel);
            string live = Path.GetFullPath(Path.Combine(game,
                rel.Replace('/', Path.DirectorySeparatorChar)));
            if (!Inside(live, game))
                throw new InvalidDataException("GCO_PASS_PATH_ESCAPES_GAME");
            files.Add(new(rel, file, live, owner));
        }
        return files.OrderBy(x => x.Path, StringComparer.Ordinal).ToList();
    }

    private static bool SafeGamePath(string path) =>
        CombinedPassOutputInspector.AllowedRuntimePath(path);

    private static int CopyOwnerZip(string archivePath, string overlay,
        Dictionary<string, string> owners)
    {
        using var archive = ZipFile.OpenRead(archivePath);
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        int count = 0;
        foreach (var entry in archive.Entries)
        {
            string rel = entry.FullName;
            if (!SafeGamePath(rel) || !seen.Add(rel) || entry.Length == 0)
                throw new InvalidDataException("GCO_CET_OWNER_ZIP_NOT_GAME_RELATIVE");
            string to = Path.Combine(overlay, rel.Replace('/', Path.DirectorySeparatorChar));
            if (!Inside(to, overlay) || File.Exists(to))
                throw new InvalidDataException("GCO_CET_PASS_COLLISION");
            Directory.CreateDirectory(Path.GetDirectoryName(to)!);
            entry.ExtractToFile(to);
            string owner = rel.StartsWith("bin/", StringComparison.OrdinalIgnoreCase)
                ? rel.Split('/')[5] : "G-CET/native";
            owners.Add(rel, owner);
            count++;
        }
        return count;
    }

    private static int CopyScriptPreview(string scripts, string overlay,
        Dictionary<string, string> owners)
    {
        if (!Directory.Exists(scripts)) return 0;
        int count = 0;
        foreach (string file in Directory.EnumerateFiles(scripts, "*.reds",
                     SearchOption.AllDirectories))
        {
            if ((File.GetAttributes(file) & FileAttributes.ReparsePoint) != 0)
                throw new InvalidDataException("GCO_REDSCRIPT_PREVIEW_LINK");
            string sourceRel = Path.GetRelativePath(scripts, file).Replace('\\', '/');
            string rel = "r6/scripts/" + sourceRel;
            if (!SafeGamePath(rel))
                throw new InvalidDataException("GCO_REDSCRIPT_PREVIEW_UNSAFE_PATH");
            string to = Path.Combine(overlay, rel.Replace('/', Path.DirectorySeparatorChar));
            if (File.Exists(to))
                throw new InvalidDataException("GCO_CET_REDSCRIPT_PATH_COLLISION");
            Directory.CreateDirectory(Path.GetDirectoryName(to)!);
            File.Copy(file, to);
            owners.Add(rel, "REDscript/" + sourceRel.Split('/')[0]);
            count++;
        }
        return count;
    }

    private static string[] VerifyFrameworkBundle(string ownerRoot)
    {
        string manifest = Path.Combine(Path.GetDirectoryName(ownerRoot)!,
            "source-sha256.json");
        if (!File.Exists(manifest))
            throw new InvalidDataException("GCO_FRAMEWORK_BUNDLE_MANIFEST_MISSING");
        using var document = JsonDocument.Parse(File.ReadAllText(manifest));
        var records = document.RootElement;
        if (records.ValueKind != JsonValueKind.Array ||
            records.GetArrayLength() < 11)
            throw new InvalidDataException("GCO_FRAMEWORK_BUNDLE_MANIFEST_INVALID");
        var checkedPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in records.EnumerateArray())
        {
            string rel = item.GetProperty("path").GetString() ?? "";
            string sha = item.GetProperty("sha256").GetString() ?? "";
            if (!rel.StartsWith("r6/scripts/G-RedRuntime/",
                    StringComparison.OrdinalIgnoreCase) ||
                !rel.EndsWith(".reds", StringComparison.OrdinalIgnoreCase) ||
                rel.Contains('\\') || rel.Split('/').Any(x => x is "" or "." or "..") ||
                sha.Length != 64 || !sha.All(Uri.IsHexDigit) ||
                !checkedPaths.Add(rel))
                throw new InvalidDataException("GCO_FRAMEWORK_BUNDLE_PATH_INVALID");
            string path = Path.GetFullPath(Path.Combine(ownerRoot,
                rel.Replace('/', Path.DirectorySeparatorChar)));
            if (!Inside(path, ownerRoot) || !File.Exists(path) ||
                (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0 ||
                !Sha(path).Equals(sha, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("GCO_FRAMEWORK_BUNDLE_HASH_MISMATCH");
        }
        string framework = Path.Combine(ownerRoot, "r6", "scripts", "G-RedRuntime");
        if (!Directory.Exists(framework))
            throw new InvalidDataException("GCO_FRAMEWORK_OWNER_SOURCES_MISSING");
        foreach (string source in Directory.EnumerateFiles(framework, "*.reds",
                     SearchOption.AllDirectories))
        {
            if ((File.GetAttributes(source) & FileAttributes.ReparsePoint) != 0 ||
                !checkedPaths.Contains(Path.GetRelativePath(ownerRoot, source)
                    .Replace('\\', '/')))
                throw new InvalidDataException("GCO_FRAMEWORK_UNMANIFESTED_SOURCE");
        }
        return checkedPaths.OrderBy(x => x, StringComparer.Ordinal).ToArray();
    }

    private static int CopyCompleteFramework(string game, string overlay,
        string ownerRoot, Dictionary<string, string> owners)
    {
        string[] verified = VerifyFrameworkBundle(ownerRoot);
        int added = 0;
        foreach (string rel in verified)
        {
            if (!SafeGamePath(rel))
                throw new InvalidDataException("GCO_FRAMEWORK_SOURCE_PATH");
            string bundled = Path.Combine(ownerRoot,
                rel.Replace('/', Path.DirectorySeparatorChar));
            string live = Path.Combine(game,
                rel.Replace('/', Path.DirectorySeparatorChar));
            string staged = Path.Combine(overlay,
                rel.Replace('/', Path.DirectorySeparatorChar));
            if (File.Exists(staged))
            {
                // A real, structurally admitted REDscript edit wins over
                // framework packaging. Never overwrite staged optimizer output.
                continue;
            }
            // The current installed runtime always takes precedence over the
            // bundled reference, including owner-local compatible modifications.
            string source = File.Exists(live) ? live : bundled;
            if ((File.GetAttributes(source) & FileAttributes.ReparsePoint) != 0)
                throw new InvalidDataException("GCO_FRAMEWORK_SOURCE_LINK");
            Directory.CreateDirectory(Path.GetDirectoryName(staged)!);
            File.Copy(source, staged);
            owners.Add(rel, "G-REdruntime/framework");
            added++;
        }
        return added;
    }

    private static bool Inside(string path, string root)
    {
        root = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar);
        path = Path.GetFullPath(path);
        return path.StartsWith(root + Path.DirectorySeparatorChar,
            StringComparison.OrdinalIgnoreCase);
    }

    private static string Sha(string file) =>
        Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(file)));
}
