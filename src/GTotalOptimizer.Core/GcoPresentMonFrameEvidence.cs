using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace GTotalOptimizer.Core;

/// <summary>
/// Versioned, derived GCO-to-CET frame evidence. The sealed PresentMon CSV and
/// collection.json remain untouched. TimeInQPC is the Present event clock;
/// MsBetweenPresents is the interval ending at that event, NOT displayed latency.
/// </summary>
public sealed record GcoPresentMonFrame(
    long SourceRow, long CpuStartQpc, long PresentQpc,
    double RelativeStartMs, double IntervalMs,
    double? CpuBusyMs, double? GpuBusyMs, double? GpuTimeMs);

public sealed record GcoPresentMonChronology(
    string SchemaVersion,
    string Producer,
    string SourceFile,
    string SourceSha256,
    string Clock,
    string IntervalSemantics,
    string Correlation,
    long CetStartUnixMs,
    long OverlapStartUnixMs,
    long OverlapEndUnixMs,
    int SourceFrameCount,
    IReadOnlyList<GcoPresentMonFrame> Frames);

public static class GcoPresentMonFrameEvidence
{
    public const string ContractFile = "GCO-PresentMon-Frames.v1.json";
    public const string ContractVersion = "gco.presentmon.chronology.v1";
    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false
    };

    private static double ProjectUnixMs(long tick, NativeCaptureWindow window)
    {
        if (window.GrspStartQpc is not long q0 ||
            window.GrspStopQpc is not long q1 ||
            window.GrspStartUnixMs is not long u0 ||
            window.GrspStopUnixMs is not long u1 ||
            q0 <= 0 || q1 <= q0 || u1 <= u0)
            throw new InvalidDataException("GRSP QPC/Unix F11 anchor pair is missing or invalid.");
        // Both endpoints are from the same live capture. Decimal avoids
        // catastrophic cancellation at large Windows QPC/Unix values.
        var mapped = (decimal)u0 + ((decimal)tick - q0) *
            (u1 - u0) / (q1 - q0);
        if (mapped is < 1_000_000_000_000m or > 4_102_444_800_000m)
            throw new InvalidDataException("PresentMon clock falls outside supported Unix time.");
        return (double)mapped;
    }

    public static GcoPresentMonChronology Build(
        string sealedCaptureFolder,
        CollectedCaptureIndex index,
        NativeCaptureWindow verifiedOverlap)
    {
        if (index.Cet is null || index.Redscript is null)
            throw new InvalidDataException("CET and GRSP F11 evidence is required.");
        if (index.FrameTimeEvidence is not CollectedFile expected)
            throw new InvalidDataException("Collected PresentMon is not hash-indexed.");
        if (verifiedOverlap.CetStartUnixMs is not long cetStart ||
            verifiedOverlap.CetStopUnixMs is not long cetStop ||
            cetStart <= 0 || cetStop <= cetStart ||
            verifiedOverlap.StartUnixMs < cetStart ||
            verifiedOverlap.EndUnixMs > cetStop ||
            verifiedOverlap.EndUnixMs <= verifiedOverlap.StartUnixMs)
            throw new InvalidDataException("CET F11 capture/overlap start anchor is missing or invalid.");
        if (index.PresentMonOverlapStartUnixMs is long indexedStart &&
            indexedStart != verifiedOverlap.StartUnixMs ||
            index.PresentMonOverlapEndUnixMs is long indexedStop &&
            indexedStop != verifiedOverlap.EndUnixMs)
            throw new InvalidDataException("Collected frame-window index does not match native F11 markers.");

        var folder = Path.GetFullPath(sealedCaptureFolder);
        var relative = expected.Path.Replace('/', Path.DirectorySeparatorChar);
        if (Path.IsPathRooted(relative) || relative.Split(Path.DirectorySeparatorChar)
                .Any(part => part is "" or "." or ".."))
            throw new InvalidDataException("PresentMon index contains an unsafe relative file path.");
        var csv = Path.GetFullPath(Path.Combine(folder, relative));
        if (!csv.StartsWith(folder.TrimEnd(Path.DirectorySeparatorChar) +
                Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) ||
            !File.Exists(csv) ||
            (File.GetAttributes(csv) & FileAttributes.ReparsePoint) != 0)
            throw new InvalidDataException("Indexed PresentMon source is missing, linked or outside the capture.");
        using (var file = File.OpenRead(csv))
        {
            if (file.Length != expected.Bytes ||
                !Convert.ToHexString(SHA256.HashData(file)).Equals(expected.Sha256,
                    StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("PresentMon collected source hash/size does not match collection.json.");
        }

        using var reader = new StreamReader(csv, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
        var header = reader.ReadLine()?.TrimStart('\uFEFF');
        if (header is null) throw new InvalidDataException("PresentMon is missing its CSV header.");
        var columns = header.Split(',').Select(x => x.Trim().Trim('"')).ToArray();
        static int Required(string[] cols, string key)
        {
            var found = Array.FindIndex(cols, x => x == key);
            if (found < 0) throw new InvalidDataException("PresentMon 2.6.0 missing field " + key + ".");
            return found;
        }
        var app = Required(columns, "Application");
        var cpuStart = Required(columns, "CPUStartQPC");
        var present = Required(columns, "TimeInQPC");
        var interval = Required(columns, "MsBetweenPresents");
        static int Optional(string[] cols, string key) => Array.FindIndex(cols, x => x == key);
        var cpuBusy = Optional(columns, "MsCPUBusy");
        var gpuBusy = Optional(columns, "MsGPUBusy");
        var gpuTime = Optional(columns, "MsGPUTime");
        if (columns.Distinct(StringComparer.Ordinal).Count() != columns.Length)
            throw new InvalidDataException("Duplicate PresentMon CSV columns cannot be safely interpreted.");
        if (index.PresentMonAlignedFrames is not > 0)
            throw new InvalidDataException("F11-aligned PresentMon frame-count proof is absent.");

        static long Tick(string raw, string field)
        {
            if (!long.TryParse(raw.Trim().Trim('"'), NumberStyles.None,
                    CultureInfo.InvariantCulture, out var tick) || tick <= 0)
                throw new InvalidDataException("Invalid PresentMon QPC value for " + field + ".");
            return tick;
        }
        static double Ms(string raw, string field, bool positive)
        {
            if (!double.TryParse(raw.Trim().Trim('"'), NumberStyles.Float,
                    CultureInfo.InvariantCulture, out var value) ||
                !double.IsFinite(value) || value < 0 || positive && value == 0)
                throw new InvalidDataException("Invalid PresentMon millisecond value for " + field + ".");
            return value;
        }
        static double? OptionalMs(string[] row, int index, string field)
        {
            if (index < 0 || string.IsNullOrWhiteSpace(row[index].Trim('"')))
                return null;
            return Ms(row[index], field, positive: false);
        }

        var frames = new List<GcoPresentMonFrame>();
        long lastCpuTick = 0, lastPresentTick = 0;
        long sourceRow = 0;
        string? line;
        while ((line = reader.ReadLine()) is not null)
        {
            sourceRow++;
            var fields = line.Split(',');
            if (fields.Length != columns.Length)
                throw new InvalidDataException("PresentMon row has wrong column count at source row " + sourceRow + ".");
            if (!fields[app].Trim().Trim('"')
                    .Equals("Cyberpunk2077.exe", StringComparison.OrdinalIgnoreCase))
                continue;
            var cpuTick = Tick(fields[cpuStart], "CPUStartQPC");
            var presentTick = Tick(fields[present], "TimeInQPC");
            var frameMs = Ms(fields[interval], "MsBetweenPresents", positive: true);
            if (lastCpuTick != 0 && cpuTick < lastCpuTick ||
                lastPresentTick != 0 && presentTick < lastPresentTick)
                throw new InvalidDataException("PresentMon QPC sequence is not monotonic.");
            lastCpuTick = cpuTick;
            lastPresentTick = presentTick;

            // GCO's existing Collect filter is CPUStartQPC rounded to Unix ms.
            // Cross-check its retained F11 window rather than admitting a
            // separate, unproven frame range.
            var cpuUnix = ProjectUnixMs(cpuTick, verifiedOverlap);
            var cpuUnixRounded = checked((long)Math.Round(cpuUnix, 0, MidpointRounding.AwayFromZero));
            if (cpuUnixRounded < verifiedOverlap.StartUnixMs ||
                cpuUnixRounded >= verifiedOverlap.EndUnixMs)
                throw new InvalidDataException("PresentMon row is outside the sealed CET/GRSP F11 overlap.");

            // Frame interval ENDS at TimeInQPC (Present event); do not treat
            // CPUStartQPC as the Present boundary or invent displayed frames.
            var relativePresentMs = ProjectUnixMs(presentTick, verifiedOverlap) - cetStart;
            var relativeStartMs = relativePresentMs - frameMs;
            frames.Add(new GcoPresentMonFrame(
                sourceRow, cpuTick, presentTick,
                relativeStartMs, frameMs,
                OptionalMs(fields, cpuBusy, "MsCPUBusy"),
                OptionalMs(fields, gpuBusy, "MsGPUBusy"),
                OptionalMs(fields, gpuTime, "MsGPUTime")));
        }

        if (frames.Count < 2 || frames.Count != index.PresentMonAlignedFrames)
            throw new InvalidDataException(
                "PresentMon retained frame count differs from sealed overlap index.");
        // G-GC headroom / hitch analysis must have a plausible, genuinely
        // clocked interval series. Missing optional CPU/GPU values stay null.
        return new GcoPresentMonChronology(
            ContractVersion,
            "GCO_PRESENTMON_2_6",
            expected.Path.Replace('\\', '/'),
            expected.Sha256.ToUpperInvariant(),
            "WINDOWS_QPC_INTERPOLATED_FROM_GRSP_F11_UNIX_ANCHORS",
            "MILLISECONDS_BETWEEN_CONSECUTIVE_PRESENT_EVENTS_NOT_DISPLAY_LATENCY",
            "CET_GRSP_F11_INTERSECTION_VERIFIED",
            cetStart,
            verifiedOverlap.StartUnixMs,
            verifiedOverlap.EndUnixMs,
            frames.Count,
            frames);
    }

    public static string WriteToStagedCetCapture(
        string sealedCaptureFolder,
        CollectedCaptureIndex index,
        NativeCaptureWindow verifiedOverlap,
        string stagedCetCaptureFolder)
    {
        var source = Path.GetFullPath(sealedCaptureFolder).TrimEnd(Path.DirectorySeparatorChar);
        var destination = Path.GetFullPath(stagedCetCaptureFolder).TrimEnd(Path.DirectorySeparatorChar);
        // Path.GetRelativePath returns an ABSOLUTE destination for different
        // Windows volumes; it is NOT evidence of child containment. Authorize
        // a temp workspace on C: when the game/capture is on D:, while still
        // rejecting exact equality or any actual descendant of the sealed
        // session. Compare fully normalized native paths, not relative
        // strings that assume both directories share one drive.
        if (destination.Equals(source, StringComparison.OrdinalIgnoreCase) ||
            destination.StartsWith(source + Path.DirectorySeparatorChar,
                StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Derived PresentMon chronology cannot be written inside sealed capture.");
        var result = Build(source, index, verifiedOverlap);
        var destinationFolder = Path.Combine(destination, "PresentMon");
        Directory.CreateDirectory(destinationFolder);
        var output = Path.Combine(destinationFolder, ContractFile);
        if (File.Exists(output))
            throw new InvalidDataException("Derived frame evidence already exists in staged CET capture.");
        // Atomic create: a failed write cannot be mistaken for valid evidence.
        var temp = Path.Combine(destinationFolder, Guid.NewGuid().ToString("N") + ".tmp");
        try
        {
            using (var stream = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                JsonSerializer.Serialize(stream, result, Json);
            File.Move(temp, output);
        }
        finally
        {
            if (File.Exists(temp)) File.Delete(temp);
        }
        return output;
    }
}
