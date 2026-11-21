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
        public int CetWindows { get; set; }
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
            // Keep the exact same hash-sealed GCO chronology that CET
            // already consumes. GRSP will read a report-side copy; never
            // create a second QPC conversion or touch the sealed capture.
            string? stagedFrameHandoff = null;
            if (mon is not null)
                stagedFrameHandoff = GcoPresentMonFrameEvidence.WriteToStagedCetCapture(
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
                    var bucket = At(start.Value);
                    bucket.CetExclusiveMs += exclusive.Value;
                    bucket.CetWindows++;
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

            // The REDscript spike CSV has absolute native Unix clocks.
            // Use the same CET F11 origin as the verified PresentMon chronology;
            // do not align spikes by frame index, inferred lag or guessed timezone.
            var redSpikeMarkers = new List<GcoProfilerSpike>();
            var redSpikeCsv = FindNamed(red, "GRSP_Spikes.csv");
            if (mon is not null && redSpikeCsv is not null)
                ReadCsv(redSpikeCsv, row =>
                {
                    var unix = Number(row, "unix_ms");
                    var cost = Number(row, "exclusive_instrumented_ms");
                    if (unix is null || unix < 1_000_000_000_000 ||
                        cost is null || cost <= 0) return;
                    var relative = unix.Value - mon.CetStartUnixMs;
                    if (relative < mon.OverlapStartUnixMs - mon.CetStartUnixMs ||
                        relative >= mon.OverlapEndUnixMs - mon.CetStartUnixMs)
                        return;
                    var owner = row.TryGetValue("owner", out var name) ? name : "";
                    redSpikeMarkers.Add(new GcoProfilerSpike(
                        relative, "REDscript", owner, cost.Value));
                }, cancellationToken);

            var rows = bins.Select(pair => new
            {
                timeMs = pair.Key * BucketMs,
                presentIntervalMaxMs = mon is null || pair.Value.PresentCount == 0
                    ? (double?)null : pair.Value.PresentMaxMs,
                presentFrames = pair.Value.PresentCount,
                cpuBusyMaxMs = pair.Value.CpuBusyMaxMs,
                gpuBusyMaxMs = pair.Value.GpuBusyMaxMs,
                // A bucket without a CET measurement is unknown, not zero.
                cetExclusiveMeasuredMs = pair.Value.CetWindows == 0
                    ? (double?)null : pair.Value.CetExclusiveMs,
                // No GRSP frame anchored in this 50ms window means
                // NO OBSERVATION, not proof of zero REDscript execution.
                redscriptExclusiveInstrumentedMs = mon is null ||
                    pair.Value.RedscriptFrames == 0
                    ? (double?)null : pair.Value.RedscriptExclusiveMs,
                redscriptCalls = mon is null || pair.Value.RedscriptFrames == 0
                    ? (long?)null : pair.Value.RedscriptCalls,
                redscriptNativeFrames = mon is null ? (int?)null : pair.Value.RedscriptFrames
            }).ToArray();

            // Only source-owned owner summaries and verified coarse chronology
            // feed the human digest. The original reports remain intact.
            var timelineJson = JsonSerializer.Serialize(rows, Json);
            var digest = GcoProfilerDigest.Build(
                cetJson.RootElement, grspJson.RootElement,
                mon is null ? Array.Empty<double>() :
                    mon.Frames.Select(frame => frame.IntervalMs).ToArray(),
                timelineJson, redSpikeMarkers);

            Directory.CreateDirectory(publishing);
            if (stagedFrameHandoff is not null)
                File.Copy(stagedFrameHandoff,
                    Path.Combine(publishing, GcoPresentMonFrameEvidence.ContractFile));
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
                    chronologyFile = mon is null ? null : GcoPresentMonFrameEvidence.ContractFile,
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
                digest = digest.Data,
                spikeMarkers = digest.SpikeMarkers,
                timeline = rows
            };

            File.WriteAllText(Path.Combine(publishing, JsonFile),
                JsonSerializer.Serialize(report, Json), new UTF8Encoding(false));
            File.WriteAllText(Path.Combine(publishing, HtmlFile),
                HtmlReport(report.frameEvidence.provider, mon?.Frames.Count ?? 0,
                    timelineJson, grspFrames, cetWindows,
                    digest.Html, JsonSerializer.Serialize(digest.SpikeMarkers, Json)),
                new UTF8Encoding(false));
            // Keep the owner reports and all derived files. Byte-identical
            // raw CSVs already exist in the hash-sealed owner capture, so link
            // to that canonical copy instead of multiplying results storage.
            PublishOwnerReport(cet, Path.Combine(publishing, "CET"),
                CollectedCaptureStore.DomainFolder(packageRoot, sessionId, "CET"),
                cancellationToken);
            PublishOwnerReport(red, Path.Combine(publishing, "GRSP"),
                CollectedCaptureStore.DomainFolder(packageRoot, sessionId, "REDscript"),
                cancellationToken);
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

    private static bool SameBytesByHash(string first, string second)
    {
        if (new FileInfo(first).Length != new FileInfo(second).Length)
            return false;
        // Native raw captures can be hundreds of MB. Compare streams rather
        // than allocating two complete CSV files into GCO's managed heap.
        using var a = File.OpenRead(first);
        using var b = File.OpenRead(second);
        return SHA256.HashData(a).AsSpan().SequenceEqual(SHA256.HashData(b));
    }

    private static void PublishOwnerReport(
        string source, string destination, string sealedOriginals,
        CancellationToken token)
    {
        // Report writers operate only on temporary, verified copies. Preserve
        // every derived file, including summary JSON and original HTML.
        // Only omit a CSV when its path AND contents match its sealed original.
        // The HTML's downloadable CSV links then resolve back to the source
        // capture, whose hash-indexed inventory remains unchanged.
        string rawFolder = Path.GetFileName(
            sealedOriginals.TrimEnd(Path.DirectorySeparatorChar));
        var links = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var file in Directory.EnumerateFiles(source, "*",
                     SearchOption.AllDirectories))
        {
            token.ThrowIfCancellationRequested();
            if ((File.GetAttributes(file) & FileAttributes.ReparsePoint) != 0)
                throw new InvalidDataException("Linked report output refused.");
            string relative = Path.GetRelativePath(source, file).Replace('\\', '/');
            if (relative.Split('/').Any(part => part is "" or "." or ".."))
                throw new InvalidDataException("Unsafe report output path.");
            string original = Path.Combine(sealedOriginals,
                relative.Replace('/', Path.DirectorySeparatorChar));
            if (relative.EndsWith(".csv", StringComparison.OrdinalIgnoreCase) &&
                File.Exists(original) &&
                (File.GetAttributes(original) & FileAttributes.ReparsePoint) == 0 &&
                SameBytesByHash(file, original))
            {
                links.Add(relative, "../../" + rawFolder + "/" + relative);
                continue;
            }

            string target = Path.Combine(destination,
                relative.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(file, target, overwrite: false);
        }

        // Owner HTML reports provide raw CSV download hyperlinks. Repoint
        // only exact href targets, never rewrite source reports, measurements,
        // embedded chart data, or an unrelated relative URL.
        foreach (var html in Directory.EnumerateFiles(
                     destination, "*.html", SearchOption.AllDirectories))
        {
            token.ThrowIfCancellationRequested();
            string document = File.ReadAllText(html);
            string localFolder = Path.GetRelativePath(destination,
                Path.GetDirectoryName(html)!).Replace('\\', '/');
            foreach (var (relative, originalLink) in links)
            {
                string localLink = localFolder == "." ? relative :
                    Path.GetRelativePath(Path.GetDirectoryName(html)!,
                        Path.Combine(destination,
                            relative.Replace('/', Path.DirectorySeparatorChar)))
                        .Replace('\\', '/');
                string href = originalLink;
                if (localFolder != ".")
                {
                    // Each ancestor below the report root needs one more "../".
                    int depth = localFolder.Split('/').Length;
                    href = string.Concat(Enumerable.Repeat("../", depth)) + originalLink;
                }
                document = document.Replace("href=\"" + localLink + "\"",
                    "href=\"" + href + "\"", StringComparison.Ordinal);
                document = document.Replace("href='" + localLink + "'",
                    "href='" + href + "'", StringComparison.Ordinal);
            }
            File.WriteAllText(html, document, new UTF8Encoding(false));
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
        string timelineJson, int grspFrames, int cetWindows,
        string digestHtml, string markersJson)
    {
        // Standalone/offline HTML: no CDN, network, external scripts or files.
        // Three independent y-axis ranges with ONE shared F11 time axis.
        const string template = """
<!doctype html>
<html lang="en">
<head><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1">
<title>GCO — Combined Profiler Report</title>
<style>
:root{color-scheme:dark;--bg:#050b0e;--panel:#101b21;--border:#34505a;--fg:#e1f2f5;--muted:#91abb4;--cyan:#33daec;--yellow:#f9d854;--magenta:#ef71c2}
*{box-sizing:border-box}body{font:14px "Segoe UI",Arial,sans-serif;background:var(--bg);color:var(--fg);margin:0}
main{max-width:1450px;margin:auto;padding:20px 26px 45px}h1{font-size:25px;letter-spacing:.05em;color:#fff000}h2{font-size:19px;color:var(--cyan);margin:26px 0 12px}
p{line-height:1.5}.muted{color:var(--muted)}.grid{display:grid;grid-template-columns:repeat(auto-fit,minmax(185px,1fr));gap:12px}
.card{padding:16px;border:1px solid var(--border);background:var(--panel);border-radius:6px}
.card strong{display:block;font-size:22px;margin:5px 0}.chartbox{position:relative;padding:12px;background:var(--panel);border:1px solid var(--border);border-radius:8px}
canvas{display:block;width:100%;height:570px;cursor:crosshair}
nav{display:flex;gap:14px;flex-wrap:wrap;margin-top:12px}a{color:var(--cyan);text-underline-offset:4px}
button{background:#132b34;border:1px solid #3daabc;color:#dff5fa;border-radius:4px;padding:7px 12px;cursor:pointer}
button:hover{background:#1f3f49}.toolbar{display:flex;align-items:center;gap:12px;margin-bottom:12px}
.tip{display:none;position:absolute;pointer-events:none;z-index:2;white-space:pre-line;max-width:300px;background:#071014f5;border:1px solid var(--cyan);padding:10px;border-radius:6px;font:12px Consolas,monospace}
.legend{display:flex;gap:18px;flex-wrap:wrap;font-size:13px}.sw{display:inline-block;width:14px;height:3px;vertical-align:middle;margin-right:5px;background:currentColor}
.note{background:#152128;border-left:3px solid var(--yellow);padding:10px 13px;margin:12px 0}
.digest-kpis{display:grid;grid-template-columns:repeat(auto-fit,minmax(210px,1fr));gap:12px}
.digest-metric{background:#101b21;border:1px solid var(--border);padding:14px;border-radius:8px}
.digest-metric span{display:block;font-size:12px;color:var(--muted);letter-spacing:.04em}
.digest-metric strong{display:block;font-size:22px;margin:7px 0}
.digest-metric small{display:block;font-size:12px;line-height:1.4;color:var(--muted)}
.side-by-side{display:grid;grid-template-columns:minmax(0,1fr) minmax(0,1fr);gap:14px;align-items:start}
.digest-panel{background:var(--panel);border:1px solid var(--border);border-radius:8px;padding:12px;min-width:0}
.digest-panel h3{font-size:15px;color:#e5f7fa;margin:0 0 12px}
.finding{padding:10px 0;border-bottom:1px solid #243b45}.finding:last-child{border-bottom:0}
.finding strong{color:#e4edf1;font-size:13px}.finding p{color:var(--muted);font-size:12px;margin:5px 0 0;line-height:1.5}
.digest-table{border-collapse:collapse;width:100%;table-layout:fixed;font-variant-numeric:tabular-nums;font-size:12px}
.digest-table th{font-weight:600;color:var(--muted);text-align:left;border-bottom:1px solid var(--border);padding:7px}
.digest-table td{border-bottom:1px solid #243b45;padding:8px 7px;overflow:hidden;text-overflow:ellipsis;white-space:nowrap}
.digest-table tr:last-child td{border-bottom:0}
.digest-table.ranking th:first-child,.digest-table.ranking td:first-child{width:60%}
.digest-table.ranking th:nth-child(2),.digest-table.ranking td:nth-child(2){width:23%}
.digest-table.ranking th:last-child,.digest-table.ranking td:last-child{width:17%}
.digest-table.spikes th:first-child,.digest-table.spikes td:first-child{width:20%}
.digest-table.spikes th:nth-child(2),.digest-table.spikes td:nth-child(2){width:57%;text-align:left}
.digest-table.spikes th:last-child,.digest-table.spikes td:last-child{width:23%}
.digest-table.windows th:first-child,.digest-table.windows td:first-child{width:14%}
.digest-table.windows th:not(:first-child),.digest-table.windows td:not(:first-child){width:21.5%}

.digest-table td:first-child{max-width:0}
.digest-table td:not(:first-child),.digest-table th:not(:first-child){text-align:right}
.digest-table .jump-row,.jump-row{cursor:pointer}
.digest-table .jump-row:hover,.jump-row:hover{background:#173741}
.table-scroll{overflow-x:auto}
.facts{display:grid;grid-template-columns:minmax(0,1fr) auto;column-gap:10px;row-gap:12px;margin:0}
.facts dt{color:var(--muted)}.facts dd{margin:0;text-align:right;font-weight:600}
.small{font-size:12px}
.chartbox canvas{height:410px}
.graph-tabs{display:flex;gap:0;align-items:flex-end;margin-top:14px;border-bottom:1px solid var(--border)}
.graph-tabs [role="tab"]{border:1px solid transparent;border-bottom:0;border-radius:6px 6px 0 0;background:transparent;color:var(--muted);padding:11px 18px;font-weight:600;letter-spacing:.02em}
.graph-tabs [role="tab"]:hover{color:var(--fg);background:#162d36}
.graph-tabs [role="tab"][aria-selected="true"]{color:var(--cyan);background:var(--panel);border-color:var(--border);box-shadow:inset 0 3px 0 var(--cyan)}
.graph-tabs [role="tab"]:focus-visible{outline:2px solid var(--yellow);outline-offset:-3px}
.chartbox [role="tabpanel"][hidden]{display:none}
.chartbox [role="tabpanel"]:focus-visible{outline:2px solid var(--cyan);outline-offset:2px}
#scaleNote{font-size:12px;min-height:20px;margin-top:8px;color:var(--muted)}
@media(max-width:850px){.side-by-side{grid-template-columns:1fr}}
@media(max-width:650px){main{padding:12px}.chartbox canvas{height:400px}.toolbar{flex-wrap:wrap}.digest-kpis{grid-template-columns:1fr 1fr}}
</style></head><body><main>
<h1>GCO / Combined Profiler Report</h1>
<p class="muted">Real source-owned CET and REDscript measurements, alongside <strong>__PROVIDER__</strong> frame evidence. Derived from the collected capture; no game or original capture files modified.</p>
__DIGEST__
<h2>CET + REDscript + PresentMon timeline</h2>
<div class="note"><strong>Measurement boundaries:</strong> PresentMon measures present-to-present intervals, not displayed latency. CET and REDscript values are distinct observed/instrumented workloads, not whole-process CPU; they can overlap and must <strong>not</strong> be added or subtracted. Timing alignment is coarse 50 ms association using recorded QPC/Unix F11 anchors, not proof that either profiler caused a particular hitch. PC latency and frame-generation flags are NOT_CAPTURED.</div>
<div class="graph-tabs" role="tablist" aria-label="Timeline graph selection">
<button id="combined" role="tab" aria-controls="panel-combined" aria-selected="true" tabindex="0">Combined timeline</button>
<button id="tracks" role="tab" aria-controls="panel-tracks" aria-selected="false" tabindex="-1">Three synchronized tracks</button>
</div>
<div class="chartbox"><div class="toolbar">
<button id="full">Full capture</button>
<button id="worst">Worst PresentMon interval</button>
<span class="muted">Shift + scroll to zoom · Select a time in the tables above</span>
</div>
<div id="panel-combined" role="tabpanel" aria-labelledby="combined" tabindex="0">
<canvas id="graph" role="img" aria-label="Combined CET REDscript and PresentMon normalized timeline"></canvas>
</div>
<div id="panel-tracks" role="tabpanel" aria-labelledby="tracks" tabindex="0" hidden></div>
<div class="tip" id="tip"></div><div class="legend">
<span style="color:var(--cyan)"><i class="sw"></i>PresentMon maximum interval / 50ms bucket</span>
<span style="color:var(--yellow)"><i class="sw"></i>CET measured exclusive work / 50ms bucket</span>
<span style="color:var(--magenta)"><i class="sw"></i>GRSP measured exclusive instrumented work / 50ms bucket</span>
</div><div id="scaleNote"></div></div>
<p class="muted">Combined view scales each source to its own visible peak to compare <strong>timing patterns</strong>, not CPU cost. Three-track mode shows each source in measured milliseconds with independent axes and verified native spike timestamps. Short dashed colored connectors across missing 50 ms samples are <strong>unobserved time, not interpolated measurements</strong>; solid segments contain actual observations. GRSP uses absolute Unix F11 anchors; no frame-index lag fitting or CapFrameX substitution.</p>
<h2>Full source-owned profiler reports</h2>
<nav><a href="CET/CET_Report.html">Full CET report (native/GC/allocations/callbacks)</a>
<a href="GRSP/GRSP_Report.html">Full REDscript report (functions/frames/spikes)</a>
<a href="CET/CET_Resolver_Input.json">CET Resolver input (native PresentMon evidence)</a>
<a href="GCO_Profiler_Report.json">Consolidated machine data</a></nav>
<p class="muted">GCO owns this combined view. CET and GRSP own their measurements and report producers. Standalone profiler input support is unchanged.</p>
</main>
<script>
(function(){
const data=__TIMELINE__,spikes=__SPIKES__,canvas=document.getElementById('graph');
const tip=document.getElementById('tip'),scaleNote=document.getElementById('scaleNote');
if(!data.length){canvas.replaceWith(document.createTextNode("No synchronized timeline available. The native CET and REDscript reports remain available below."));return;}
let xmin=data[0].timeMs,xmax=data[data.length-1].timeMs+50,mode='combined';
const fullMin=xmin,fullMax=xmax;
let worst=data.find(p=>p.presentIntervalMaxMs!=null)||data[0];
for(const p of data)if((p.presentIntervalMaxMs||0)>(worst.presentIntervalMaxMs||0))worst=p;
const series=[
 {key:'presentIntervalMaxMs',name:'PRESENTMON / maximum present interval',short:'PresentMon',color:'#33daec'},
 {key:'cetExclusiveMeasuredMs',name:'CET / exclusive Lua work',short:'CET',color:'#f9d854'},
 {key:'redscriptExclusiveInstrumentedMs',name:'REDSCRIPT / instrumented VM work',short:'REDscript',color:'#ef71c2'}
];
const wPad=88,rPad=18,top=14,bottom=31;
function visible(){
 // Include one neighboring bucket beyond each side of the zoom viewport,
 // so a zoom/focus does not accidentally cut a legitimate line segment.
 let first=data.findIndex(p=>p.timeMs+50>=xmin);
 if(first<0)return [];
 let end=first;
 while(end<data.length&&data[end].timeMs<=xmax)end++;
 return data.slice(Math.max(0,first-1),Math.min(data.length,end+1));
}
function draw(){
 const ctx=canvas.getContext('2d'),w=canvas.clientWidth,h=canvas.clientHeight;
 ctx.clearRect(0,0,w,h);
 const view=visible(),span=Math.max(1,xmax-xmin),plotW=Math.max(20,w-wPad-rPad);
 const x=t=>wPad+(t-xmin)/span*plotW;
 const peaks=series.map(s=>{let peak=0;for(const row of view){const value=row[s.key];if(value!=null&&value>peak)peak=value;}return peak;});
 const panels=mode==='combined'?1:3,panelH=(h-top-bottom)/panels;
 for(let i=0;i<panels;i++){
  const y0=top+i*panelH,yt=y0+26,yb=y0+panelH-24,box=Math.max(1,yb-yt);
  ctx.fillStyle='#10242c';ctx.fillRect(wPad,y0,plotW,panelH-5);
  ctx.strokeStyle='#243f4b';ctx.lineWidth=1;
  for(let k=0;k<=4;k++){
   const y=yt+box*k/4;ctx.beginPath();ctx.moveTo(wPad,y);ctx.lineTo(w-rPad,y);ctx.stroke();
  }
  ctx.textAlign='left';ctx.font='12px Segoe UI';
  if(mode==='combined'){
   ctx.fillStyle='#d2e1e6';
   ctx.fillText('RELATIVE TO EACH VISIBLE SERIES PEAK',5,y0+13);
   ctx.fillStyle='#a6bbc4';ctx.fillText('peak',wPad-47,yt+4);
   ctx.fillText('zero',wPad-34,yb+1);
  }else{
   const s=series[i],mx=Math.max(1,peaks[i])*1.08;
   ctx.fillStyle=s.color;ctx.fillText(s.name,6,y0+14);
   ctx.fillStyle='#a6bbc4';ctx.fillText(mx.toFixed(1)+' ms',wPad-66,yt+4);
   ctx.fillText('0 ms',wPad-45,yb+1);
  }
  for(let j=0;j<series.length;j++){
   if(mode!=='combined'&&i!==j)continue;
   const s=series[j],max=mode==='combined'?Math.max(0.000001,peaks[j]):Math.max(1,peaks[j])*1.08;
   ctx.strokeStyle=s.color;ctx.lineWidth=mode==='combined'?1.8:1.4;
   // Null means no matched observation, not a measured zero. Zoom/focus
   // must keep real samples continuous without painting invented readings.
   // Bridge only visually with a distinctly dashed UNKNOWN interval.
   ctx.save();ctx.beginPath();ctx.rect(wPad,y0,plotW,panelH-5);ctx.clip();
   ctx.beginPath();let previous=null,hadUnobserved=false;
   for(const p of view){
    const v=p[s.key];
    if(v==null){hadUnobserved=true;continue;}
    const xx=x(p.timeMs),yy=yb-Math.min(1,Math.max(0,v/max))*box;
    if(previous==null){ctx.moveTo(xx,yy);}
    else if(hadUnobserved||p.timeMs-previous.timeMs>50.001){
     // Solid only between two consecutive measured 50ms buckets.
     ctx.stroke();
     ctx.save();ctx.setLineDash([5,5]);ctx.globalAlpha=.65;
     ctx.beginPath();ctx.moveTo(previous.x,previous.y);ctx.lineTo(xx,yy);
     ctx.stroke();ctx.restore();
     ctx.beginPath();ctx.moveTo(xx,yy);
    }else ctx.lineTo(xx,yy);
    previous={x:xx,y:yy,timeMs:p.timeMs};
    hadUnobserved=false;
   }
   ctx.stroke();ctx.restore();
  }
 }
 if(mode==='tracks'){
  // Only top source-owned events. Markers are time coincidences, not
  // cause assignments to the corresponding PresentMon intervals.
  ctx.setLineDash([4,5]);ctx.lineWidth=1;
  for(const s of spikes){
   if(s.timeMs<xmin||s.timeMs>xmax)continue;
   ctx.strokeStyle=s.domain==='CET'?'#f9d85499':'#ef71c299';
   ctx.beginPath();ctx.moveTo(x(s.timeMs),top);ctx.lineTo(x(s.timeMs),h-bottom);ctx.stroke();
  }
  ctx.setLineDash([]);
 }
 ctx.font='12px Segoe UI';ctx.fillStyle='#a6bbc4';ctx.textAlign='left';
 for(let k=0;k<=4;k++){
  const t=xmin+k*(xmax-xmin)/4,xx=x(t);
  ctx.fillText((t/1000).toFixed(1)+'s',Math.max(2,Math.min(w-52,xx-17)),h-8);
 }
 scaleNote.textContent=mode==='combined'
  ? 'Visible peaks (raw): '+series.map((s,i)=>s.short+' '+peaks[i].toFixed(2)+' ms').join('  ·  ')+' — short dashed connectors indicate unobserved intervals, NOT measured values.'
  : 'Three independent millisecond axes. Short dashed color segments bridge UNOBSERVED measurement gaps (not zero); vertical dashed lines mark recorded spikes. Shared F11 origin.';
}
function resize(){
 canvas.style.height=mode==='combined'?'410px':'575px';
 const ratio=window.devicePixelRatio||1,w=Math.max(320,canvas.clientWidth),h=canvas.clientHeight;
 canvas.width=Math.round(w*ratio);canvas.height=Math.round(h*ratio);
 canvas.getContext('2d').setTransform(ratio,0,0,ratio,0,0);draw();
}
function setMode(next,focusTab=false){
 if(next!=='combined'&&next!=='tracks')return;
 mode=next;
 for(const name of ['combined','tracks']){
  const active=name===mode,tab=document.getElementById(name),panel=document.getElementById('panel-'+name);
  tab.setAttribute('aria-selected',String(active));
  tab.tabIndex=active?0:-1;
  panel.hidden=!active;
 }
 const currentPanel=document.getElementById('panel-'+mode);
 currentPanel.appendChild(canvas);
 canvas.setAttribute('aria-label',mode==='combined'
  ?'Combined CET REDscript and PresentMon normalized timeline'
  :'Three synchronized millisecond graphs for PresentMon CET and REDscript, with spike markers');
 tip.style.display='none';
 resize();
 if(focusTab)document.getElementById(mode).focus();
}
function focusAt(t){
 if(!Number.isFinite(t))return;
 const windowMs=Math.min(8000,fullMax-fullMin),half=windowMs/2;
 xmin=Math.max(fullMin,Math.min(fullMax-windowMs,t-half));
 xmax=xmin+windowMs;setMode('tracks');
 document.querySelector('.chartbox').scrollIntoView({block:'center',behavior:'smooth'});
}
for(const name of ['combined','tracks']){
 const tab=document.getElementById(name);
 tab.addEventListener('click',()=>setMode(name));
 tab.addEventListener('keydown',e=>{
  if(!['ArrowLeft','ArrowRight','Home','End'].includes(e.key))return;
  e.preventDefault();
  const next=e.key==='Home'?'combined':e.key==='End'?'tracks':
    (name==='combined'?'tracks':'combined');
  setMode(next,true);
 });
}
document.getElementById('full').onclick=()=>{xmin=fullMin;xmax=fullMax;draw();};
document.getElementById('worst').onclick=()=>focusAt(worst.timeMs);
document.querySelectorAll('[data-jump]').forEach(node=>{
 node.setAttribute('tabindex','0');node.setAttribute('role','button');
 const jump=()=>focusAt(Number(node.getAttribute('data-jump')));
 node.addEventListener('click',jump);
 node.addEventListener('keydown',e=>{if(e.key==='Enter'||e.key===' '){e.preventDefault();jump();}});
});
canvas.addEventListener('mousemove',e=>{
 const r=canvas.getBoundingClientRect(),ratio=Math.max(0,Math.min(1,(e.clientX-r.left-wPad)/(r.width-wPad-rPad)));
 const t=xmin+ratio*(xmax-xmin);let lo=0,hi=data.length-1;
 while(lo<hi){const m=(lo+hi)>>1;if(data[m].timeMs<t)lo=m+1;else hi=m;}
 const p=data[Math.max(0,lo-1)];if(!p)return;
 const fmt=v=>v==null?'No sample':v.toFixed(3)+' ms';
 const near=spikes.filter(s=>Math.abs(s.timeMs-p.timeMs)<75).sort((a,b)=>b.exclusiveMs-a.exclusiveMs).slice(0,2);
 const markerText=near.length?'\nNearby profiler marker(s): '+near.map(s=>s.domain+' '+s.owner+' '+s.exclusiveMs.toFixed(2)+' ms').join(' | '):'';
 tip.textContent=(p.timeMs/1000).toFixed(3)+' s (CET F11 origin)\nPresent interval max: '+fmt(p.presentIntervalMaxMs)+' ('+p.presentFrames+' presents)\nCPU busy max: '+fmt(p.cpuBusyMaxMs)+'\nGPU busy max: '+fmt(p.gpuBusyMaxMs)+'\nCET exclusive: '+fmt(p.cetExclusiveMeasuredMs)+'\nREDscript instrumented: '+fmt(p.redscriptExclusiveInstrumentedMs)+'\nRED outgoing calls: '+(p.redscriptCalls??'No sample')+markerText;
 tip.style.display='block';tip.style.left=Math.max(4,Math.min(r.width-305,e.clientX-r.left+9))+'px';
 tip.style.top=Math.max(5,e.clientY-r.top-115)+'px';
});
canvas.addEventListener('mouseleave',()=>tip.style.display='none');
canvas.addEventListener('wheel',e=>{
 if(!e.shiftKey)return;e.preventDefault();
 const r=canvas.getBoundingClientRect(),ratio=Math.max(0,Math.min(1,(e.clientX-r.left-wPad)/(r.width-wPad-rPad)));
 const anchor=xmin+ratio*(xmax-xmin),next=Math.max(500,Math.min(fullMax-fullMin,(xmax-xmin)*(e.deltaY<0?.78:1.28)));
 let a=anchor-ratio*next,b=a+next;
 if(a<fullMin){a=fullMin;b=a+next;}if(b>fullMax){b=fullMax;a=b-next;}
 xmin=a;xmax=b;draw();
},{passive:false});
window.addEventListener('resize',resize);
requestAnimationFrame(resize);
})();
</script></body></html>
""";
        return template.Replace("__PROVIDER__", WebUtility.HtmlEncode(provider), StringComparison.Ordinal)
            .Replace("__MONFRAMES__", monFrames.ToString("N0", CultureInfo.InvariantCulture), StringComparison.Ordinal)
            .Replace("__CETWINDOWS__", cetWindows.ToString("N0", CultureInfo.InvariantCulture), StringComparison.Ordinal)
            .Replace("__REDFRAMES__", grspFrames.ToString("N0", CultureInfo.InvariantCulture), StringComparison.Ordinal)
            .Replace("__TIMELINE__", timelineJson, StringComparison.Ordinal)
            .Replace("__DIGEST__", digestHtml, StringComparison.Ordinal)
            .Replace("__SPIKES__", markersJson, StringComparison.Ordinal);
    }
}
