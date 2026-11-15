using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;

namespace GTotalOptimizer.Core;

/// <summary>
/// Page 2 read-only CET analysis. The source owner remains responsible for
/// CET capture reporting, Resolver interpretation and G-GC. This class only
/// validates GCO restoration, immutable inputs and a disposable execution
/// workspace; it NEVER generates or applies an optimization pass.
/// </summary>
public sealed record CetAnalysisOutcome(
    string SessionId, int InstalledCetMods, int CapturedCetFiles, int CapturedRedscriptFiles,
    bool HasPresentMon, int? PresentMonFrames, int MeasuredCallbackFamilies,
    int RankedCallbacks, int GenericCandidates, int SemanticCandidates,
    int AlreadySatisfied, int MaterialRemaining, int Unresolved,
    string FrameEvidence, string RuntimeInterpretation, string SourceVerification,
    string[] BlacklistedMods);

public sealed class CetReadOnlyAnalysisService
{
    private static readonly TimeSpan ResolverTimeout = TimeSpan.FromMinutes(4);
    private const string ResolverRelative = "components/cet-resolver/app/G-CET-Resolver.exe";

    public async Task<CetAnalysisOutcome> AnalyzeAsync(
        string packageRoot, string gameRoot, string sessionId,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        packageRoot = Path.GetFullPath(packageRoot);
        gameRoot = Path.GetFullPath(gameRoot);
        var workflow = new UnifiedProfilerWorkflow(packageRoot);
        if (workflow.HasRecoveryJournal || workflow.HasOwnerRestoreState(gameRoot))
            throw new InvalidOperationException("Restore both original profilers before analyzing.");
        if (IsCyberpunkRunning())
            throw new InvalidOperationException("Close Cyberpunk before analyzing the restored capture.");

        // ReadRestored validates the exact session/game identity, restored
        // owner state and SHA-256/length of every sealed CET/GRSP/PresentMon
        // file. Never rewrite collection.json or fake a profiler handoff.
        var saved = CollectedCaptureStore.ReadRestored(
            packageRoot, gameRoot, sessionId);
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
            _ = CollectedCaptureStore.ReadRestored(packageRoot, gameRoot, sessionId);
            if (workflow.HasRecoveryJournal || workflow.HasOwnerRestoreState(gameRoot) ||
                IsCyberpunkRunning())
                throw new InvalidOperationException(
                    "Profiler/game state changed during analysis; results are invalid.");

            var grspCount = saved.Redscript.Files.Count;
            var usedFrame = saved.FrameTimeEvidence is not null;
            var installed = inspection.CetModCount;
            var blacklisted = cli.TryGetProperty("BlacklistedMods", out var blocked) &&
                blocked.ValueKind == JsonValueKind.Array
                    ? blocked.EnumerateArray().Where(x => x.ValueKind == JsonValueKind.String)
                        .Select(x => x.GetString() ?? "").ToArray()
                    : Array.Empty<string>();
            return new CetAnalysisOutcome(
                sessionId, installed, saved.Cet.Files.Count, grspCount,
                usedFrame, saved.PresentMonAlignedFrames,
                Nonnegative(cli, "FamilyCount"), Nonnegative(cli, "RankedCallbackCount"),
                Nonnegative(cli, "GenericResolvedCount"), Nonnegative(cli, "SemanticReadyRuleCount"),
                Nonnegative(cli, "AlreadySatisfiedCount"), Nonnegative(cli, "MaterialRemainingCount"),
                Nonnegative(cli, "UnresolvedCount"),
                usedFrame ? "PRESENTMON_QPC_F11_VERIFIED" : "NOT_CAPTURED",
                sourceMode,
                "CURRENT_SOURCE_READ_ONLY; CAPTURE_SOURCE_IDENTITY_UNVERIFIED",
                blacklisted);
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

    private static bool IsCyberpunkRunning()
    {
        var found = Process.GetProcessesByName("Cyberpunk2077");
        try { return found.Length != 0; }
        finally { foreach (var process in found) process.Dispose(); }
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
