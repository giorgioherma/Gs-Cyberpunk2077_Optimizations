using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace GTotalOptimizer.Core;

/// <summary>
/// GCO's derived, unified PROFILER report. CET and GRSP keep their authoritative
/// native measurements/report engines; GCO only aligns their existing outputs to
/// the real sealed PresentMon chronology and presents them together.
/// No source rewriting, Resolver decisions, or CapFrameX emulation.
/// </summary>
public sealed record GcoProfilerReportOutcome(
    string HtmlPath, string JsonPath, int PresentMonFrames,
    int CetWindowCount, int RedscriptFrameCount);

public static class GcoProfilerReportService
{
    public const string HtmlFile = "GCO_Profiler_Report.html";
    public const string JsonFile = "GCO_Profiler_Report.json";
    private const double BucketMs = 50;
    private static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private sealed class Window
    {
        public long Index { get; init; }
        public double PresentMaxMs { get; set; }
        public int PresentCount { get; set; }
        public double? CpuBusyMaxMs { get; set; }
        public double? GpuBusyMaxMs { get; set; }
        public double CetExclusiveMs { get; set; }
        public double RedscriptExclusiveMs { get; set; }
        public long RedscriptCalls { get; set; }
        public int RedscriptFrames { get; set; }
    }

    public static string ReportFile(string packageRoot, string sessionId) =>
        Path.Combine(CollectedCaptureStore.SessionFolder(packageRoot, sessionId),
            "Reports", HtmlFile);

    public static async Task<GcoProfilerReportOutcome> GenerateAsync(
        string packageRoot, string gameRoot, string sessionId,
        CancellationToken cancellationToken = default)
    {
        packageRoot = Path.GetFullPath(packageRoot);
        var saved = CollectedCaptureStore.Read(packageRoot, gameRoot, sessionId,
            verifyHashes: true);
        if (saved.Cet is null || saved.Redscript is null || !saved.CollectionAttempted)
            throw new InvalidDataException("A collected CET and REDscript capture is required for profiler reporting.");

        var sealedFolder = CollectedCaptureStore.SessionFolder(packageRoot, sessionId);
        var outputFolder = Path.Combine(sealedFolder, "Reports");
        var html = Path.Combine(outputFolder, HtmlFile);
        var machine = Path.Combine(outputFolder, JsonFile);
        // Results are derived, never part of the sealed native owner inventory.
        // A completed report is immutable; do not replace it on reopening GCO.
        if (File.Exists(html) && File.Exists(machine))
        {
            using var existing = JsonDocument.Parse(File.ReadAllText(machine));
            if (existing.RootElement.GetProperty("sessionId").GetString() != sessionId)
                throw new InvalidDataException("Previous GCO report belongs to a different session.");
            var frameCount = existing.RootElement.GetProperty("presentMonFrameCount").GetInt32();
            return new(html, machine, frameCount, 0, 0);
        }

        var bundle = ComponentBundleVerifier.Inspect(packageRoot);
        if (!bundle.Ready || bundle.CetExecutable is null || bundle.GrspExecutable is null)
            throw new InvalidDataException("Verified CET and REDscript reporting components are required.");

        GcoPresentMonChronology? mon = null;
        if (saved.FrameTimeEvidence is not null)
        {
            var overlap = PresentMonCaptureAlignment.RequireOverlap(packageRoot, saved);
            mon = GcoPresentMonFrameEvidence.Build(sealedFolder, saved, overlap);
        }

        var work = Path.Combine(Path.GetTempPath(),
            "GCO-Profiler-Report-" + Guid.NewGuid().ToString("N"));
        var cet = Path.Combine(work, "CET");
        var red = Path.Combine(work, "GRSP");
        var publishing = Path.Combine(sealedFolder, "Reports-" + Guid.NewGuid().ToString("N") + ".tmp");
        try
        {
            Directory.CreateDirectory(cet);
            Directory.CreateDirectory(red);
            CopyOwner(CollectedCaptureStore.DomainFolder(packageRoot, sessionId, "CET"),
                cet, saved.Cet, cancellationToken);
            CopyOwner(CollectedCaptureStore.DomainFolder(packageRoot, sessionId, "REDscript"),
                red, saved.Redscript, cancellationToken);
            if (mon is not null)
                _ = GcoPresentMonFrameEvidence.WriteToStagedCetCapture(
                    sealedFolder, saved,
                    PresentMonCaptureAlignment.RequireOverlap(packageRoot, saved), cet);

            // Run the ORIGINAL reporters on copies. Neither standalone owner
            // sees a made-up CapFrameX JSON or modifies the sealed capture.
            await RunOwnerAsync(bundle.CetExecutable,
                ["--report", "--capture", cet, "--json"],
                packageRoot, cancellationToken).ConfigureAwait(false);
            await RunOwnerAsync(bundle.GrspExecutable,
                ["--report", "--capture", red, "--json"],
                packageRoot, cancellationToken).ConfigureAwait(false);

            var cetSummary = Path.Combine(cet, "CET_Summary.json");
            var cetInput = Path.Combine(cet, "CET_Resolver_Input.json");
            var grspSummary = Path.Combine(red, "GRSP_Summary.json");
            if (!File.Exists(cetSummary) || !File.Exists(cetInput) ||
                !File.Exists(Path.Combine(cet, "CET_Report.html")) ||
                !File.Exists(grspSummary) ||
                !File.Exists(Path.Combine(red, "GRSP_Report.html")))
                throw new InvalidDataException("A source-owned profiler report or Resolver handoff is missing.");

            using var cetJson = JsonDocument.Parse(File.ReadAllText(cetSummary));
            using var handoff = JsonDocument.Parse(File.ReadAllText(cetInput));
            using var grspJson = JsonDocument.Parse(File.ReadAllText(grspSummary));
            var cetFrame = cetJson.RootElement.TryGetProperty("frameTime", out var cf)
                ? cf : default;
            var inputFrame = handoff.RootElement.TryGetProperty("frameEvidence", out var hf)
                ? hf : default;
            var grspFrame = grspJson.RootElement.TryGetProperty("frameTime", out var gf)
                ? gf : default;
            if (mon is not null)
            {
                if (Text(cetFrame, "provider") != "PRESENTMON_2_6" ||
                    Text(inputFrame, "provider") != "PRESENTMON_2_6" ||
                    Text(inputFrame, "correlation") != "QPC_F11_CET_WINDOW_CORRELATION_ONLY")
                    throw new InvalidDataException("CET owner report/Resolver input did not consume genuine PresentMon.");
                if (!cetFrame.TryGetProperty("pcLatencyStatus", out var latency) ||
                    latency.GetString() != "NOT_CAPTURED")
                    throw new InvalidDataException("Missing PresentMon latency must not be inferred.");
            }
            else if (cetFrame.ValueKind is not (JsonValueKind.Null or JsonValueKind.Undefined))
                throw new InvalidDataException("GCO CET report must not fall back to CapFrameX.");

            // GRSP standalone's CapFrameX frame correlation is deliberately not
            // run in GCO. GRSP's extensive native report stays intact; the
            // synchronized PresentMon/GRSP view is GCO's responsibility.
            if (grspFrame.ValueKind is not (JsonValueKind.Null or JsonValueKind.Undefined))
                throw new InvalidDataException("GCO GRSP report must not consume legacy CapFrameX.");

            var bins = new SortedDictionary<long, Window>();
            Window At(double timeMs)
            {
                var key = checked((long)Math.Floor(timeMs / BucketMs));
                if (!bins.TryGetValue(key, out var bucket))
                    bins.Add(key, bucket = new Window { Index = key });
                return bucket;
            }

            if (mon is not null)
                foreach (var frame in mon.Frames)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var b = At(frame.RelativeStartMs);
                    b.PresentMaxMs = Math.Max(b.PresentMaxMs, frame.IntervalMs);
                    b.PresentCount++;
                    if (frame.CpuBusyMs is double cpu)
                        b.CpuBusyMaxMs = Math.Max(b.CpuBusyMaxMs ?? 0, cpu);
                    if (frame.GpuBusyMs is double gpu)
                        b.GpuBusyMaxMs = Math.Max(b.GpuBusyMaxMs ?? 0, gpu);
                }

            // CET 50ms buckets are owner-measured script work, NOT total CPU.
            var cetWindows = 0;
            var cetTimeline = FindNamed(cet, "CET_Runtime_Profile_Timeline.csv");
            if (cetTimeline is not null)
                ReadCsv(cetTimeline, (row) =>
                {
                    var start = Number(row, "BucketStartMs");
                    var exclusive = Number(row, "ExclusiveMs");
                    if (start is null || exclusive is null || exclusive < 0) return;
                    At(start.Value).CetExclusiveMs += exclusive.Value;
                    cetWindows++;
                }, cancellationToken);

            // GRSP records absolute Unix frame clocks; anchor them to the
            // SAME CET F11 epoch as PresentMon. Do not guess a frame-index lag.
            var grspFrames = 0;
            var redTimeline = FindNamed(red, "GRSP_Frames.csv");
            if (redTimeline is not null)
                ReadCsv(redTimeline, row =>
                {
                    if (mon is null) return; // No proven common QPC/Unix clock.
                    var unix = Number(row, "frame_start_unix_ms");
                    var ms = Number(row, "exclusive_instrumented_ms");
                    if (unix is null || unix < 1_000_000_000_000 || ms is null || ms < 0)
                        return;
                    var relative = unix.Value - mon.CetStartUnixMs;
                    if (relative < mon.OverlapStartUnixMs - mon.CetStartUnixMs ||
                        relative >= mon.OverlapEndUnixMs - mon.CetStartUnixMs)
                        return;
                    var b = At(relative);
                    b.RedscriptExclusiveMs += ms.Value;
                    b.RedscriptCalls += Whole(row, "total_calls");
                    b.RedscriptFrames++;
                    grspFrames++;
                }, cancellationToken);

            var rows = bins.Select(pair => new
            {
                timeMs = pair.Key * BucketMs,
                presentIntervalMaxMs = mon is null || pair.Value.PresentCount == 0
                    ? (double?)null : pair.Value.PresentMaxMs,
                presentFrames = pair.Value.PresentCount,
                cpuBusyMaxMs = pair.Value.CpuBusyMaxMs,
                gpuBusyMaxMs = pair.Value.GpuBusyMaxMs,
                cetExclusiveMeasuredMs = pair.Value.CetExclusiveMs,
                redscriptExclusiveInstrumentedMs = mon is null
                    ? (double?)null : pair.Value.RedscriptExclusiveMs,
                redscriptCalls = mon is null ? (long?)null : pair.Value.RedscriptCalls,
                redscriptNativeFrames = mon is null ? (int?)null : pair.Value.RedscriptFrames
            }).ToArray();

            Directory.CreateDirectory(publishing);
            var report = new
            {
                schemaVersion = "gco.profiler.report.v1",
                sessionId,
                generatedUtc = DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture),
                frameEvidence = new
                {
                    provider = mon is null ? "NOT_CAPTURED" : "PRESENTMON_2_6",
                    source = mon?.SourceFile,
                    sha256 = mon?.SourceSha256,
                    clock = mon?.Clock,
                    intervalSemantics = mon?.IntervalSemantics,
                    correlation = mon?.Correlation,
                    latencyStatus = "NOT_CAPTURED",
                    frameTypeStatus = "NOT_CAPTURED"
                },
                presentMonFrameCount = mon?.Frames.Count ?? 0,
                bucketWidthMs = BucketMs,
                correlationPolicy = "COMMON_F11_WINDOW_50MS_ASSOCIATION_ONLY_NOT_CAUSATION",
                cpuPolicy = "CET_GRSP_MEASURED_SCOPES_OVERLAP_NOT_ADDITIVE",
                cetReport = "CET/CET_Report.html",
                cetResolverInput = "CET/CET_Resolver_Input.json",
                grspReport = "GRSP/GRSP_Report.html",
                grspNativeSummary = "GRSP/GRSP_Summary.json",
                cetWindows = cetWindows,
                redscriptFrames = grspFrames,
                timeline = rows
            };

            File.WriteAllText(Path.Combine(publishing, JsonFile),
                JsonSerializer.Serialize(report, Json), new UTF8Encoding(false));
            File.WriteAllText(Path.Combine(publishing, HtmlFile),
                HtmlReport(report.frameEvidence.provider, mon?.Frames.Count ?? 0,
                    rows, grspFrames, cetWindows), new UTF8Encoding(false));
            // Keep the full original CET and GRSP reports and their data links
            // accessible beneath the consolidated report. This is RESULTS
            // only, never part of the optimizer pass ZIP.
            CopyTree(cet, Path.Combine(publishing, "CET"), cancellationToken);
            CopyTree(red, Path.Combine(publishing, "GRSP"), cancellationToken);
            if (Directory.Exists(outputFolder))
                throw new IOException("Another report already exists for this capture; no overwrite.");
            Directory.Move(publishing, outputFolder);
            return new(html, machine, mon?.Frames.Count ?? 0, cetWindows, grspFrames);
        }
        finally
        {
            if (Directory.Exists(work)) TryDelete(work);
            if (Directory.Exists(publishing)) TryDelete(publishing);
        }
    }

    private static string? FindNamed(string root, string name) =>
        Directory.EnumerateFiles(root, name, SearchOption.AllDirectories)
            .OrderBy(x => x, StringComparer.OrdinalIgnoreCase).FirstOrDefault();

    private static string Text(JsonElement node, string name) =>
        node.ValueKind == JsonValueKind.Object &&
        node.TryGetProperty(name, out var value) &&
        value.ValueKind == JsonValueKind.String
            ? value.GetString() ?? "" : "";

    private static double? Number(IReadOnlyDictionary<string, string> row, string name)
    {
        if (!row.TryGetValue(name, out var text) ||
            !double.TryParse(text, NumberStyles.Float,
                CultureInfo.InvariantCulture, out var n) || !double.IsFinite(n))
            return null;
        return n;
    }

    private static long Whole(IReadOnlyDictionary<string, string> row, string name) =>
        row.TryGetValue(name, out var s) &&
        long.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out var n)
            ? n : 0;

    private static void ReadCsv(string path,
        Action<IReadOnlyDictionary<string, string>> consume, CancellationToken token)
    {
        using var stream = new StreamReader(path, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
        var first = stream.ReadLine();
        if (string.IsNullOrWhiteSpace(first)) return;
        var names = Fields(first.TrimStart('\uFEFF'));
        string? line;
        while ((line = stream.ReadLine()) is not null)
        {
            token.ThrowIfCancellationRequested();
            if (string.IsNullOrWhiteSpace(line)) continue;
            var parts = Fields(line);
            if (parts.Length != names.Length) continue;
            var row = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            for (var i = 0; i < names.Length; i++) row[names[i]] = parts[i];
            consume(row);
        }
    }

    private static string[] Fields(string input)
    {
        var values = new List<string>();
        var field = new StringBuilder();
        bool quoted = false;
        for (int i = 0; i < input.Length; i++)
        {
            var c = input[i];
            if (c == '"')
            {
                if (quoted && i + 1 < input.Length && input[i + 1] == '"')
                {
                    field.Append('"');
                    i++;
                }
                else quoted = !quoted;
            }
            else if (c == ',' && !quoted)
            {
                values.Add(field.ToString());
                field.Clear();
            }
            else field.Append(c);
        }
        values.Add(field.ToString());
        return values.ToArray();
    }

    private static void CopyOwner(string original, string output,
        CollectedOwner owner, CancellationToken token)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in owner.Files)
        {
            token.ThrowIfCancellationRequested();
            var p = item.Path.Replace('\\', '/');
            var parts = p.Split('/');
            if (parts.Any(x => x is "" or "." or "..") ||
                Path.IsPathRooted(p) || !seen.Add(p))
                throw new InvalidDataException("Unsafe or duplicate indexed owner report source.");
            // GCO never stages any legacy CapFrameX files, even when a
            // standalone owner capture happened to contain them.
            if (parts.Any(x => x.Equals("FrameTime", StringComparison.OrdinalIgnoreCase)))
                continue;
            var src = Path.GetFullPath(Path.Combine(original,
                p.Replace('/', Path.DirectorySeparatorChar)));
            if (!src.StartsWith(Path.GetFullPath(original).TrimEnd(Path.DirectorySeparatorChar) +
                    Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) ||
                !File.Exists(src) || (File.GetAttributes(src) & FileAttributes.ReparsePoint) != 0)
                throw new InvalidDataException("Indexed profiler file unavailable or linked.");
            var dest = Path.Combine(output, p.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
            File.Copy(src, dest, overwrite: false);
            using var file = File.OpenRead(dest);
            if (file.Length != item.Bytes ||
                !Convert.ToHexString(SHA256.HashData(file))
                    .Equals(item.Sha256, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("Indexed profiler file changed before reporting.");
        }
    }

    private static void CopyTree(string source, string dest, CancellationToken token)
    {
        Directory.CreateDirectory(dest);
        foreach (var folder in Directory.EnumerateDirectories(source, "*", SearchOption.AllDirectories))
        {
            token.ThrowIfCancellationRequested();
            Directory.CreateDirectory(Path.Combine(dest, Path.GetRelativePath(source, folder)));
        }
        foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
        {
            token.ThrowIfCancellationRequested();
            var target = Path.Combine(dest, Path.GetRelativePath(source, file));
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(file, target, overwrite: false);
        }
    }

    private static void TryDelete(string root)
    {
        try { Directory.Delete(root, recursive: true); }
        catch (IOException x) { Trace.TraceWarning("GCO report scratch: " + x.Message); }
        catch (UnauthorizedAccessException x) { Trace.TraceWarning("GCO report scratch: " + x.Message); }
    }

    private static async Task RunOwnerAsync(string exe, string[] args,
        string package, CancellationToken token)
    {
        using var process = new Process
        {
            StartInfo = new ProcessStartInfo(exe)
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                WorkingDirectory = Path.GetDirectoryName(exe)!
            }
        };
        foreach (var arg in args) process.StartInfo.ArgumentList.Add(arg);
        process.StartInfo.Environment["DOTNET_ROOT_X64"] = Path.Combine(package, "runtime");
        process.StartInfo.Environment["DOTNET_ROOT"] = Path.Combine(package, "runtime");
        process.StartInfo.Environment["DOTNET_MULTILEVEL_LOOKUP"] = "0";
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
        timeout.CancelAfter(TimeSpan.FromMinutes(3));
        if (!process.Start()) throw new InvalidOperationException("Profiler report owner could not start.");
        try
        {
            var stdout = process.StandardOutput.ReadToEndAsync(timeout.Token);
            var stderr = process.StandardError.ReadToEndAsync(timeout.Token);
            await process.WaitForExitAsync(timeout.Token).ConfigureAwait(false);
            var outText = await stdout.ConfigureAwait(false);
            var errText = await stderr.ConfigureAwait(false);
            if (process.ExitCode != 0)
            {
                Trace.TraceWarning("Profiler report owner: " + errText);
                throw new InvalidDataException("Source-owned profiler reporting refused capture (exit " +
                    process.ExitCode + "). Original data remains unchanged.");
            }
            using var result = JsonDocument.Parse(outText);
            if (result.RootElement.TryGetProperty("ok", out var ok) &&
                ok.ValueKind != JsonValueKind.True)
                throw new InvalidDataException("Profiler reporter returned an unsuccessful result.");
        }
        catch (OperationCanceledException)
        {
            try { if (!process.HasExited) process.Kill(entireProcessTree: true); }
            catch (InvalidOperationException) { }
            if (!token.IsCancellationRequested)
                throw new TimeoutException("Profiler report owner exceeded its time limit.");
            throw;
        }
    }

    private static string HtmlReport(string provider, int monFrames,
        IEnumerable<dynamic> unused, int grspFrames, int cetWindows)
        => throw new NotSupportedException();
}
