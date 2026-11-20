using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using GRedscript.Resolver;

namespace GTotalOptimizer.Core;

/// <summary>
/// One GCO Generate action. CET's authoritative Resolver supplies Lua/native
/// output, the GCO-local ordered Resolver supplies REDscript sources, and
/// official SCC 0.5.31 validates the effective game script stack.
/// All work is isolated. The only published game payload is one ZIP.
/// </summary>
public sealed record CombinedPassOutcome(
    string ZipPath, int CetFiles, int RedscriptFiles, int GeneratedFiles,
    int CetTransforms, int RedscriptEdits, bool SccPassed);

public sealed class CombinedOptimizationPassService
{
    private readonly CetReadOnlyAnalysisService cet = new();

    public async Task<CombinedPassOutcome> GenerateAsync(
        string packageRoot, string gameRoot, string sessionId,
        CancellationToken token = default)
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
                package, game, sessionId, cetZip, token).ConfigureAwait(false);
            token.ThrowIfCancellationRequested();
            string analysis = Path.Combine(temp, "redscript-analysis.json");
            await Task.Run(() =>
            {
                Program.AnalyzeCollected(session, game, analysis);
            }, token).ConfigureAwait(false);
            token.ThrowIfCancellationRequested();

            string planOutput = Path.Combine(temp, "redscript-stage");
            var red = await Task.Run(() =>
                RedscriptGenerationPlan.Stage(game, plan, analysis, planOutput),
                token).ConfigureAwait(false);
            token.ThrowIfCancellationRequested();

            string overlay = Path.Combine(temp, "game-overlay");
            Directory.CreateDirectory(overlay);
            var owners = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            int cetFiles = cetResult.FileCount == 0 ? 0 :
                CopyOwnerZip(cetZip, overlay, owners);
            int redFiles = CopyScriptPreview(Path.Combine(
                planOutput, "effective-preview", "r6", "scripts"),
                overlay, owners);

            // Framework files are deployable game scripts, not profiler output.
            // Stage only missing source files; do not silently overwrite an
            // installed framework that may have local source modifications.
            if (redFiles > 0)
            {
                string framework = Path.Combine(package, "components",
                    "redruntime", "owner");
                // Resolve native dependencies from the generated source BEFORE
                // adding base framework files. A framework declaration by
                // itself must not activate an unused native service.
                bool nativeRequired = RequiresNativeSubscriber(overlay);
                int newFramework = nativeRequired || RequiresGRedRuntime(overlay)
                    ? CopyMissingFramework(game, overlay, framework, owners) : 0;
                if (nativeRequired)
                {
                    string cetOwner = Path.Combine(package, "components",
                        "cet-resolver", "app");
                    var native = SharedNativeRuntimeStage.Stage(
                        cetOwner, game, overlay, framework);
                    string bridge = "r6/scripts/G-RedRuntime/Native/GCETNativeLocationBackend.reds";
                    owners[bridge] = "G-REDruntime/optional-native";
                    owners["red4ext/plugins/G-CET-StaticLocations/GCETStaticLocations.dll"] =
                        "G-CET/native";
                    owners[SharedNativeRuntimeStage.LocationsPath] =
                        "G-CET+G-REDruntime/native-locations";
                    _ = native;
                }
                _ = newFramework;

                string compiler = Path.Combine(package, "components",
                    "redscript-compiler", "scc_lib.dll");
                await Task.Run(() => RedscriptPassCompiler.Verify(
                    game, overlay, Path.Combine(temp, "scc-work"),
                    compiler, red.SourceStackSha256), token).ConfigureAwait(false);
                token.ThrowIfCancellationRequested();
            }

            if (cetFiles == 0 && redFiles == 0)
                throw new InvalidOperationException(
                    "No missing CET or REDscript optimizations were generated.");

            string results = Path.Combine(package, "RESULTS");
            Directory.CreateDirectory(results);
            string name = "G-Cyberpunk2077-" +
                DateTime.Now.ToString("yyyyMMdd_HHmmss") + "_Optimization_Pass.zip";
            string final = Path.Combine(results, name);
            if (File.Exists(final))
                throw new IOException("An optimization pass already has this timestamp.");
            string pending = final + ".partial-" + Guid.NewGuid().ToString("N");
            try
            {
                var indexed = CollectOutput(overlay, owners, game, token);
                if (indexed.Count == 0)
                    throw new InvalidDataException("GCO_COMBINED_EMPTY_OUTPUT");
                using (var archive = ZipFile.Open(pending, ZipArchiveMode.Create))
                {
                    foreach (var entry in indexed)
                    {
                        token.ThrowIfCancellationRequested();
                        var zip = archive.CreateEntry(entry.Path,
                            CompressionLevel.Optimal);
                        using var input = File.OpenRead(entry.FullName);
                        using var target = zip.Open();
                        input.CopyTo(target);
                    }
                }
                token.ThrowIfCancellationRequested();
                if (CombinedPassOutputInspector.Validate(pending) != indexed.Count)
                    throw new InvalidDataException("GCO_PASS_ARCHIVE_INVENTORY_MISMATCH");
                // All sidecars, source hashes and exclusions stay outside the
                // game-only ZIP. Record original hashes for Vortex rollback.
                string manifest = final + ".ownership.json";
                string draft = manifest + ".partial-" + Guid.NewGuid().ToString("N");
                try
                {
                    File.WriteAllText(draft, JsonSerializer.Serialize(new {
                        schemaVersion = "gco.combined-pass.v1",
                        captureSession = sessionId,
                        redscriptSourceStackSha256 = red.SourceStackSha256,
                        cetFiles, redscriptFiles = redFiles,
                        redscriptEdits = red.AppliedEdits,
                        officialScc031Passed = redFiles > 0,
                        originalFilesUntouched = true,
                        runtimeGameplayValidated = false,
                        installRecommendation = "Vortex overlay after originals; manually back up before direct installation",
                        excludedOwners = Array.Empty<string>(),
                        files = indexed.Select(e => new {
                            path = e.Path, owner = e.Owner,
                            optimizedSha256 = Sha(e.FullName),
                            originalSha256 = File.Exists(e.LivePath)
                                ? Sha(e.LivePath) : null
                        }).ToArray()
                    }, new JsonSerializerOptions { WriteIndented = true }),
                        new UTF8Encoding(false));
                    File.Move(pending, final, overwrite: false);
                    try { File.Move(draft, manifest, overwrite: false); }
                    catch
                    {
                        if (File.Exists(final)) File.Delete(final);
                        throw;
                    }
                }
                finally { if (File.Exists(draft)) File.Delete(draft); }
                return new(final, cetFiles, redFiles, indexed.Count,
                    cetResult.TransformCount, red.AppliedEdits, redFiles > 0);
            }
            finally { if (File.Exists(pending)) File.Delete(pending); }
        }
        finally
        {
            // Never touch game or capture. Failed compiler/generator leaves no
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

    private static bool SafeGamePath(string path)
    {
        if (string.IsNullOrEmpty(path) || path.StartsWith('/') ||
            path.Contains(':') || path.Contains('\\') ||
            path.Split('/').Any(x => x is "" or "." or ".."))
            return false;
        return (path.StartsWith("bin/x64/plugins/cyber_engine_tweaks/mods/",
                StringComparison.OrdinalIgnoreCase) &&
                !path.Contains("/CETProfiler", StringComparison.OrdinalIgnoreCase) &&
                !path.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) &&
                !path.EndsWith(".pdb", StringComparison.OrdinalIgnoreCase) &&
                !path.EndsWith(".dll", StringComparison.OrdinalIgnoreCase)) ||
            (path.StartsWith("red4ext/plugins/G-CET-StaticLocations/",
                StringComparison.OrdinalIgnoreCase) &&
                (path.EndsWith(".dll", StringComparison.OrdinalIgnoreCase) ||
                 path.EndsWith(".locations", StringComparison.OrdinalIgnoreCase))) ||
            (path.StartsWith("r6/scripts/", StringComparison.OrdinalIgnoreCase) &&
                path.EndsWith(".reds", StringComparison.OrdinalIgnoreCase));
    }

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

    private static void VerifyFrameworkBundle(string ownerRoot)
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
            if ((!rel.StartsWith("r6/scripts/G-RedRuntime/",
                    StringComparison.OrdinalIgnoreCase) &&
                 !rel.StartsWith("optional-native/r6/scripts/G-RedRuntime/",
                    StringComparison.OrdinalIgnoreCase)) ||
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
    }

    private static int CopyMissingFramework(string game, string overlay,
        string ownerRoot, Dictionary<string, string> owners)
    {
        VerifyFrameworkBundle(ownerRoot);
        string framework = Path.Combine(ownerRoot, "r6", "scripts", "G-RedRuntime");
        if (!Directory.Exists(framework))
            throw new InvalidDataException(
                "Bundled authoritative G-REdruntime framework sources missing.");
        int added = 0;
        foreach (string source in Directory.EnumerateFiles(framework, "*.reds",
                     SearchOption.AllDirectories))
        {
            if ((File.GetAttributes(source) & FileAttributes.ReparsePoint) != 0)
                throw new InvalidDataException("GCO_FRAMEWORK_SOURCE_LINK");
            string rel = Path.GetRelativePath(ownerRoot, source).Replace('\\', '/');
            if (!SafeGamePath(rel))
                throw new InvalidDataException("GCO_FRAMEWORK_SOURCE_PATH");
            string live = Path.Combine(game, rel.Replace('/', Path.DirectorySeparatorChar));
            string staged = Path.Combine(overlay,
                rel.Replace('/', Path.DirectorySeparatorChar));
            if (File.Exists(staged) || File.Exists(live))
                continue;
            Directory.CreateDirectory(Path.GetDirectoryName(staged)!);
            File.Copy(source, staged);
            owners.Add(rel, "G-REdruntime/framework");
            added++;
        }
        return added;
    }

    private static bool RequiresGRedRuntime(string overlay)
    {
        string scripts = Path.Combine(overlay, "r6", "scripts");
        return Directory.Exists(scripts) &&
            Directory.EnumerateFiles(scripts, "*.reds", SearchOption.AllDirectories)
                .Any(path => File.ReadAllText(path).Contains(
                    "import GRedRuntime.*", StringComparison.Ordinal));
    }

    private static bool RequiresNativeSubscriber(string overlay)
    {
        string scripts = Path.Combine(overlay, "r6", "scripts");
        return Directory.Exists(scripts) &&
            Directory.EnumerateFiles(scripts, "*.reds", SearchOption.AllDirectories)
                .Any(path => Path.GetFileName(path).StartsWith("GCO_NativeLocation_",
                        StringComparison.Ordinal) ||
                    File.ReadAllText(path).Contains(
                        "GCETNativeLocationGuard.DefinitelyFarPlayer(",
                        StringComparison.Ordinal) ||
                    File.ReadAllText(path).Contains(
                        "new GCETNativeLocationBackend(",
                        StringComparison.Ordinal));
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
