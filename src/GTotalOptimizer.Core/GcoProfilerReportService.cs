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

    // A partial report directory is never an analyzer-ready capture. Verify
    // both source-owned reports and the combined session identity in RESULTS.
    public static bool HasCompletedReport(string packageRoot, string sessionId)
    {
        var html = ReportFile(packageRoot, sessionId);
        var folder = Path.GetDirectoryName(html)!;
        if (!File.Exists(html) ||
            !File.Exists(Path.Combine(folder, JsonFile)) ||
            !File.Exists(Path.Combine(folder, "CET", "CET_Report.html")) ||
            !File.Exists(Path.Combine(folder, "CET", "CET_Resolver_Input.json")) ||
            !File.Exists(Path.Combine(folder, "GRSP", "GRSP_Report.html")) ||
            !File.Exists(Path.Combine(folder, "GRSP", "GRSP_Summary.json")))
            return false;
        try
        {
            using var document = JsonDocument.Parse(File.ReadAllText(
                Path.Combine(folder, JsonFile)));
            var root = document.RootElement;
            return root.TryGetProperty("schemaVersion", out var schema) &&
                schema.ValueKind == JsonValueKind.String &&
                schema.GetString() == "gco.profiler.report.v1" &&
                root.TryGetProperty("sessionId", out var identity) &&
                identity.ValueKind == JsonValueKind.String &&
                identity.GetString() == sessionId;
        }
        catch (IOException) { return false; }
        catch (UnauthorizedAccessException) { return false; }
        catch (JsonException) { return false; }
    }

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
        if (File.Exists(html) || File.Exists(machine))
        {
            if (!HasCompletedReport(packageRoot, sessionId))
                throw new InvalidDataException(
                    "Previous profiler report is incomplete; captured evidence is preserved.");
            using var existing = JsonDocument.Parse(File.ReadAllText(machine));
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
                    JsonSerializer.Serialize(rows, Json), grspFrames, cetWindows), new UTF8Encoding(false));
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
        string timelineJson, int grspFrames, int cetWindows)
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
@media(max-width:650px){main{padding:12px}canvas{height:450px}}
</style></head><body><main>
<h1>GCO / Combined Profiler Report</h1>
<p class="muted">Real source-owned CET and REDscript measurements, alongside <strong>__PROVIDER__</strong> frame evidence. Derived from the collected capture; no game or original capture files modified.</p>
<section class="grid">
<div class="card">Frame-time source<strong>__PROVIDER__</strong><span class="muted">No CapFrameX substitution or synthetic frame data</span></div>
<div class="card">PresentMon frames<strong>__MONFRAMES__</strong><span class="muted">Verified common F11 intersection</span></div>
<div class="card">CET timeline rows<strong>__CETWINDOWS__</strong><span class="muted">Measured native 50 ms owner buckets</span></div>
<div class="card">GRSP frames<strong>__REDFRAMES__</strong><span class="muted">Measured instrumented call-edge work in the overlap</span></div>
</section>
<h2>CET + REDscript + PresentMon</h2>
<div class="note"><strong>Measurement boundaries:</strong> PresentMon measures present-to-present intervals, not displayed latency. CET and REDscript values are distinct observed/instrumented workloads, not whole-process CPU; they can overlap and must <strong>not</strong> be added or subtracted. Timing alignment is coarse 50 ms association using recorded QPC/Unix F11 anchors, not proof that either profiler caused a particular hitch. PC latency and frame-generation flags are NOT_CAPTURED.</div>
<div class="chartbox"><div class="toolbar">
<button id="full">Full capture</button><button id="worst">Worst PresentMon interval</button>
<span class="muted">Hold Shift and scroll to zoom · Hover for actual values</span></div>
<canvas id="graph" role="img" aria-label="Three aligned time-series panels: PresentMon, measured CET and REDscript work"></canvas>
<div class="tip" id="tip"></div><div class="legend">
<span style="color:var(--cyan)"><i class="sw"></i>PresentMon maximum interval / 50ms bucket</span>
<span style="color:var(--yellow)"><i class="sw"></i>CET measured exclusive work / 50ms bucket</span>
<span style="color:var(--magenta)"><i class="sw"></i>GRSP measured exclusive instrumented work / 50ms bucket</span>
</div></div>
<p class="muted">A missing PresentMon capture is shown as NOT_CAPTURED. GRSP's recorded frame Unix clocks and CET 50ms bucket clocks are used directly; no frame-index lag fitting or CapFrameX format is synthesized.</p>
<h2>Full source-owned profiler reports</h2>
<nav><a href="CET/CET_Report.html">Full CET report (native/GC/allocations/callbacks)</a>
<a href="GRSP/GRSP_Report.html">Full REDscript report (functions/frames/spikes)</a>
<a href="CET/CET_Resolver_Input.json">CET Resolver input (native PresentMon evidence)</a>
<a href="GCO_Profiler_Report.json">Consolidated machine data</a></nav>
<p class="muted">GCO owns this combined view. CET and GRSP own their measurements and report producers. Standalone profiler input support is unchanged.</p>
</main>
<script>
(function(){
const data=__TIMELINE__,canvas=document.getElementById('graph'),tip=document.getElementById('tip');
if(!data.length){canvas.replaceWith(document.createTextNode("No synchronized timeline available; native reports remain accessible."));return;}
let xmin=data[0].timeMs,xmax=data[data.length-1].timeMs+50;
const fullMin=xmin,fullMax=xmax;let worst=data.find(p=>p.presentIntervalMaxMs!=null)||data[0];
for(const p of data)if((p.presentIntervalMaxMs||0)>(worst.presentIntervalMaxMs||0))worst=p;
const panels=[
 {key:'presentIntervalMaxMs',name:'PRESENTMON / present interval',color:'#33daec'},
 {key:'cetExclusiveMeasuredMs',name:'CET / measured exclusive work',color:'#f9d854'},
 {key:'redscriptExclusiveInstrumentedMs',name:'REDSCRIPT / instrumented exclusive work',color:'#ef71c2'}
];
const wPad=61,rPad=20,top=16,bottom=28;
function visible(){return data.filter(p=>p.timeMs+50>=xmin&&p.timeMs<=xmax);}
function draw(){
 const w=canvas.clientWidth,h=canvas.clientHeight,ctx=canvas.getContext('2d');
 ctx.clearRect(0,0,w,h);
 const view=visible(),span=Math.max(1,xmax-xmin),panelH=(h-top-bottom)/3;
 const x=t=>wPad+(t-xmin)/span*(w-wPad-rPad);
 for(let i=0;i<3;i++){
  const p=panels[i],y0=top+i*panelH,vals=view.map(d=>d[p.key]).filter(v=>v!=null);
  const yMax=Math.max(1,...vals)*1.12;
  ctx.fillStyle='#10242c';ctx.fillRect(wPad,y0,w-wPad-rPad,panelH-14);
  ctx.fillStyle=p.color;ctx.font='12px Segoe UI';ctx.fillText(p.name,5,y0+13);
  ctx.textAlign='right';ctx.fillStyle='#a4b8bf';ctx.fillText(yMax.toFixed(1)+' ms',wPad-6,y0+30);
  ctx.textAlign='left';ctx.strokeStyle='#243f4b';ctx.lineWidth=1;
  for(let k=0;k<=3;k++){const y=y0+25+(panelH-52)*k/3;ctx.beginPath();ctx.moveTo(wPad,y);ctx.lineTo(w-rPad,y);ctx.stroke();}
  ctx.strokeStyle=p.color;ctx.lineWidth=1.35;ctx.beginPath();let started=false;
  for(const d of view){
   const v=d[p.key];if(v==null){started=false;continue;}
   const xx=x(d.timeMs),yy=y0+25+(1-Math.min(1,v/yMax))*(panelH-52);
   if(!started){ctx.moveTo(xx,yy);started=true;}else ctx.lineTo(xx,yy);
  }
  ctx.stroke();
 }
 ctx.fillStyle='#a6bbc4';ctx.font='12px Segoe UI';ctx.fillText((xmin/1000).toFixed(1)+' s',wPad,h-8);
 ctx.textAlign='right';ctx.fillText((xmax/1000).toFixed(1)+' s',w-rPad,h-8);ctx.textAlign='left';
}
function resize(){
 const ratio=window.devicePixelRatio||1,w=Math.max(320,canvas.clientWidth),h=canvas.clientHeight;
 canvas.width=Math.round(w*ratio);canvas.height=Math.round(h*ratio);
 canvas.getContext('2d').setTransform(ratio,0,0,ratio,0,0);draw();
}
document.getElementById('full').onclick=()=>{xmin=fullMin;xmax=fullMax;draw();};
document.getElementById('worst').onclick=()=>{xmin=Math.max(fullMin,worst.timeMs-5000);xmax=Math.min(fullMax,worst.timeMs+5000);draw();};
canvas.addEventListener('mousemove',e=>{
 const r=canvas.getBoundingClientRect(),ratio=Math.max(0,Math.min(1,(e.clientX-r.left-wPad)/(r.width-wPad-rPad)));
 const t=xmin+ratio*(xmax-xmin);let lo=0,hi=data.length-1;
 while(lo<hi){const m=(lo+hi)>>1;if(data[m].timeMs<t)lo=m+1;else hi=m;}const p=data[Math.max(0,lo-1)];
 if(!p)return;const fmt=v=>v==null?'NOT_CAPTURED':v.toFixed(3)+' ms';
 tip.textContent=(p.timeMs/1000).toFixed(3)+' s (CET F11 origin)\nPresentMon: '+fmt(p.presentIntervalMaxMs)+' ('+p.presentFrames+' frames)\nCPU busy max: '+fmt(p.cpuBusyMaxMs)+'\nGPU busy max: '+fmt(p.gpuBusyMaxMs)+'\nCET measured: '+fmt(p.cetExclusiveMeasuredMs)+'\nGRSP measured: '+fmt(p.redscriptExclusiveInstrumentedMs)+'\nRED calls: '+(p.redscriptCalls??'NOT_CAPTURED');
 tip.style.display='block';tip.style.left=Math.max(5,Math.min(r.width-290,e.clientX-r.left+10))+'px';tip.style.top=Math.max(3,e.clientY-r.top-95)+'px';
});
canvas.addEventListener('mouseleave',()=>tip.style.display='none');
canvas.addEventListener('wheel',e=>{
 if(!e.shiftKey)return;e.preventDefault();
 const r=canvas.getBoundingClientRect(),ratio=Math.max(0,Math.min(1,(e.clientX-r.left-wPad)/(r.width-wPad-rPad))),anchor=xmin+ratio*(xmax-xmin);
 const next=Math.max(500,Math.min(fullMax-fullMin,(xmax-xmin)*(e.deltaY<0?.78:1.28)));
 let a=anchor-ratio*next,b=a+next;if(a<fullMin){a=fullMin;b=a+next;}if(b>fullMax){b=fullMax;a=b-next;}
 xmin=a;xmax=b;draw();
},{passive:false});window.addEventListener('resize',resize);requestAnimationFrame(resize);
})();
</script></body></html>
""";
        return template.Replace("__PROVIDER__", WebUtility.HtmlEncode(provider), StringComparison.Ordinal)
            .Replace("__MONFRAMES__", monFrames.ToString("N0", CultureInfo.InvariantCulture), StringComparison.Ordinal)
            .Replace("__CETWINDOWS__", cetWindows.ToString("N0", CultureInfo.InvariantCulture), StringComparison.Ordinal)
            .Replace("__REDFRAMES__", grspFrames.ToString("N0", CultureInfo.InvariantCulture), StringComparison.Ordinal)
            .Replace("__TIMELINE__", timelineJson, StringComparison.Ordinal);
    }
}
