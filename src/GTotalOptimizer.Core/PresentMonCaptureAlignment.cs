using System.Globalization;
using System.Text;

namespace GTotalOptimizer.Core;

// Offline, read-only native marker correlation. Never invokes profiler hooks.
public sealed record NativeCaptureWindow(long StartUnixMs, long EndUnixMs,
    long? GrspStartQpc = null, long? GrspStopQpc = null,
    long? GrspStartUnixMs = null, long? GrspStopUnixMs = null);

public static class PresentMonCaptureAlignment
{
    private static (long Start, long End, long? StartQpc, long? StopQpc) ReadMarkers(
        string packageRoot, string id, string domain, CollectedOwner owner,
        string expectedFile)
    {
        var matches = owner.Files.Where(file =>
            Path.GetFileName(file.Path.Replace('/', Path.DirectorySeparatorChar))
                .Equals(expectedFile, StringComparison.OrdinalIgnoreCase)).ToArray();
        if (matches.Length != 1)
            throw new InvalidDataException(domain + " F11 markers are missing or ambiguous.");

        var folder = CollectedCaptureStore.DomainFolder(packageRoot, id, domain);
        var path = Path.Combine(folder,
            matches[0].Path.Replace('/', Path.DirectorySeparatorChar));
        if (!File.Exists(path) ||
            (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
            throw new InvalidDataException(domain + " F11 marker file is unavailable.");

        using var reader = new StreamReader(path);
        var header = reader.ReadLine()?.TrimStart('\uFEFF').Split(',');
        if (header is null) throw new InvalidDataException(domain + " markers lack a header.");
        var label = Array.FindIndex(header, v =>
            v.Trim().Trim('"') is "Label" or "label" or "Event" or "event" or "Marker" or "marker");
        var unix = Array.FindIndex(header, v =>
            v.Trim().Trim('"').Equals("UnixEpochMs", StringComparison.OrdinalIgnoreCase) ||
            v.Trim().Trim('"').Equals("EpochMs", StringComparison.OrdinalIgnoreCase) ||
            v.Trim().Trim('"').Equals("UnixMs", StringComparison.OrdinalIgnoreCase) ||
            v.Trim().Trim('"').Equals("unix_ms", StringComparison.OrdinalIgnoreCase));
        if (label < 0 || unix < 0)
            throw new InvalidDataException(domain + " markers lack absolute-time columns.");
        var qpc = Array.FindIndex(header, v =>
            v.Trim().Trim('"').Equals("qpc", StringComparison.OrdinalIgnoreCase));

        long start = 0;
        long end = 0;
        long? startQpc = null;
        long? stopQpc = null;
        string? line;
        while ((line = reader.ReadLine()) is not null)
        {
            var fields = line.Split(',');
            if (fields.Length <= Math.Max(label, unix) ||
                !double.TryParse(fields[unix].Trim().Trim('"'), NumberStyles.Float,
                    CultureInfo.InvariantCulture, out var value) ||
                !double.IsFinite(value) || value < 1_000_000_000_000 ||
                value > 4_102_444_800_000) continue;

            var stamp = checked((long)Math.Round(value));
            var eventName = fields[label].Trim().Trim('"');
            var begin = eventName.Equals("START", StringComparison.OrdinalIgnoreCase) ||
                eventName.Equals("CAPTURE_START", StringComparison.OrdinalIgnoreCase);
            // The actual CET F11 STOP action invokes CETProfilerPause()
            // then CETProfilerDump(); its native marker is PAUSE. CET's
            // source-owner collection explicitly admits CAPTURE_START + PAUSE.
            // GRSP instead emits START + STOP_REQUEST. Only treat PAUSE as
            // terminal for CET to avoid broadening the other owner's markers.
            var stop = eventName.Equals("STOP", StringComparison.OrdinalIgnoreCase) ||
                eventName.StartsWith("STOP_", StringComparison.OrdinalIgnoreCase) ||
                eventName is "CAPTURE_STOP" or "CAPTURE_END" ||
                (domain == "CET" &&
                 eventName.Equals("PAUSE", StringComparison.OrdinalIgnoreCase));
            // GRSP records the global Windows QueryPerformanceCounter clock
            // next to Unix milliseconds, allowing timezone-free correlation
            // with PresentMon --qpc_time, without modifying either profiler.
            long? tick = null;
            if (domain == "REDscript" && qpc >= 0 &&
                fields.Length > qpc &&
                long.TryParse(fields[qpc].Trim().Trim('"'),
                    NumberStyles.None, CultureInfo.InvariantCulture, out var parsedQpc) &&
                parsedQpc > 0)
                tick = parsedQpc;
            if (begin)
            {
                if (start == 0 || stamp < start)
                {
                    start = stamp;
                    startQpc = tick;
                }
            }
            if (stop && stamp >= end)
            {
                end = stamp;
                stopQpc = tick;
            }
        }
        if (start == 0 || end <= start)
            throw new InvalidDataException(domain + " has no complete F11 START/STOP clock markers.");
        return (start, end, startQpc, stopQpc);
    }

    public static NativeCaptureWindow RequireOverlap(string packageRoot,
        CollectedCaptureIndex captured)
    {
        if (captured.Cet is null || captured.Redscript is null)
            throw new InvalidDataException("Both verified native captures are required for frame alignment.");
        var cet = ReadMarkers(packageRoot, captured.SessionId, "CET", captured.Cet,
            "CET_Runtime_Profile_Markers.csv");
        var red = ReadMarkers(packageRoot, captured.SessionId, "REDscript", captured.Redscript,
            "GRSP_Markers.csv");
        var start = Math.Max(cet.Start, red.Start);
        var stop = Math.Min(cet.End, red.End);
        if (stop <= start)
            throw new InvalidDataException("CET and REDscript F11 windows do not overlap.");
        // A GRSP QPC anchor is optional only for legacy PresentMon date-time
        // exports. New QPC captures will explicitly require both anchors.
        return new NativeCaptureWindow(start, stop,
            red.StartQpc, red.StopQpc, red.Start, red.End);
    }

    // PresentMon --date_time writes local wall-clock timestamps, sometimes
    // with nine fractional digits. Native markers use UTC Unix milliseconds.
    // Reject ambiguous DST instants instead of claiming a false match.
    private static bool TryLocalFrameUnixMs(string raw, out long unixMs)
    {
        unixMs = 0;
        var value = raw.Trim().Trim('"');
        var dot = value.IndexOf('.');
        var basePart = dot >= 0 ? value[..dot] : value;
        if (!DateTime.TryParseExact(basePart,
            new[] { "yyyy-M-d H:mm:ss", "yyyy-M-d'T'HH:mm:ss" },
            CultureInfo.InvariantCulture, DateTimeStyles.None, out var local))
            return false;
        if (TimeZoneInfo.Local.IsAmbiguousTime(local) ||
            TimeZoneInfo.Local.IsInvalidTime(local)) return false;

        var milliseconds = 0;
        if (dot >= 0)
        {
            var fraction = value[(dot + 1)..];
            if (fraction.Length is < 1 or > 9 || fraction.Any(c => c < '0' || c > '9'))
                return false;
            milliseconds = int.Parse(fraction.PadRight(3, '0')[..3],
                CultureInfo.InvariantCulture);
        }
        unixMs = new DateTimeOffset(local, TimeZoneInfo.Local.GetUtcOffset(local))
            .ToUnixTimeMilliseconds() + milliseconds;
        return true;
    }

    // Convert the common monotonic Windows QPC clock to GRSP's native Unix
    // clock using its exact F11 START/STOP anchors. Both clocks come from the
    // same machine and session. No local timezone, DST rule or fixed offset.
    private static long FrameQpcUnixMs(string raw, NativeCaptureWindow overlap)
    {
        if (overlap.GrspStartQpc is not long qpcStart ||
            overlap.GrspStopQpc is not long qpcStop ||
            overlap.GrspStartUnixMs is not long unixStart ||
            overlap.GrspStopUnixMs is not long unixStop ||
            qpcStop <= qpcStart || unixStop <= unixStart)
            throw new InvalidDataException("GRSP F11 QPC clock anchors are missing or invalid.");
        if (!long.TryParse(raw.Trim().Trim('"'), NumberStyles.None,
                CultureInfo.InvariantCulture, out var frameQpc) || frameQpc <= 0)
            throw new InvalidDataException("Invalid PresentMon QPC frame timestamp.");

        var projected = (decimal)unixStart +
            ((decimal)frameQpc - qpcStart) * (unixStop - unixStart) /
            ((decimal)qpcStop - qpcStart);
        if (projected is < 1_000_000_000_000m or > 4_102_444_800_000m)
            throw new InvalidDataException("PresentMon QPC is outside the supported Unix clock range.");
        return (long)decimal.Round(projected, 0, MidpointRounding.AwayFromZero);
    }

    // Atomic replacement is allowed only after a verified, nonempty frame
    // intersection. Repeating the same filter is idempotent.
    public static int TrimToOverlap(string root, string csv, NativeCaptureWindow overlap)
    {
        var path = PresentMonLifecyclePolicy.ValidateOutputCsv(root, csv);
        if (overlap.EndUnixMs <= overlap.StartUnixMs)
            throw new InvalidDataException("Invalid native F11 overlap window.");
        PresentMonLifecyclePolicy.CountVerifiedFrames(path);
        // Keep temp at or below the original native MAX_PATH budget;
        // a long suffix beside PresentMon.csv would recreate truncation.
        var partial = Path.Combine(Path.GetDirectoryName(path)!,
            "PM" + Guid.NewGuid().ToString("N")[..8] + ".tmp");
        var kept = 0;
        try
        {
            using (var reader = new StreamReader(path))
            using (var writer = new StreamWriter(partial, false, new UTF8Encoding(false)))
            {
                var header = reader.ReadLine()?.TrimStart('\uFEFF') ??
                    throw new InvalidDataException("PresentMon CSV header is missing.");
                var cols = header.Split(',');
                var app = Array.FindIndex(cols, c =>
                    c.Trim().Trim('"').Equals("Application", StringComparison.Ordinal));
                var qpc = Array.FindIndex(cols, c =>
                    c.Trim().Trim('"').Equals("CPUStartQPC", StringComparison.Ordinal));
                var time = qpc >= 0 ? qpc : Array.FindIndex(cols, c =>
                    c.Trim().Trim('"').Equals("CPUStartDateTime", StringComparison.Ordinal));
                if (app < 0 || time < 0)
                    throw new InvalidDataException("PresentMon CSV has no recognized frame clock.");
                if (qpc >= 0 &&
                    (overlap.GrspStartQpc is null || overlap.GrspStopQpc is null))
                    throw new InvalidDataException("GRSP F11 QPC anchors are needed for frame alignment.");
                writer.WriteLine(header);
                string? line;
                while ((line = reader.ReadLine()) is not null)
                {
                    var fields = line.Split(',');
                    if (fields.Length <= Math.Max(app, time) ||
                        !fields[app].Trim().Trim('"').Equals("Cyberpunk2077.exe",
                            StringComparison.OrdinalIgnoreCase)) continue;
                    long ms;
                    if (qpc >= 0)
                        ms = FrameQpcUnixMs(fields[time], overlap);
                    else if (!TryLocalFrameUnixMs(fields[time], out ms))
                        continue; // Legacy properly localized CSV only.
                    if (ms < overlap.StartUnixMs || ms >= overlap.EndUnixMs)
                        continue;
                    writer.WriteLine(line);
                    kept++;
                }
            }
            if (kept == 0)
                throw new InvalidDataException("No PresentMon frames fall inside the common F11 window.");
            if (PresentMonLifecyclePolicy.CountVerifiedFrames(partial) == 0)
                throw new InvalidDataException("Aligned PresentMon CSV has no verified frames.");
            File.Move(partial, path, overwrite: true);
            return kept;
        }
        finally
        {
            if (File.Exists(partial)) File.Delete(partial);
        }
    }
}
