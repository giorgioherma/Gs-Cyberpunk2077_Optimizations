namespace GTotalOptimizer.Core;

// PresentMon is a separate, external ETW capture component. Its lifecycle is
// Start Capture -> Collect Results, never native profiler install/restore.
public static class PresentMonLifecyclePolicy
{
    public const string SessionName = "GCO";

    public static string Executable(string packageRoot) =>
        Path.GetFullPath(Path.Combine(packageRoot, "components", "presentmon", "PresentMon.exe"));

    public static bool IsOwnedProcess(string packageRoot, string executablePath) =>
        Path.GetFullPath(executablePath).Equals(Executable(packageRoot),
            StringComparison.OrdinalIgnoreCase);

    public static string ValidateOutputCsv(string packageRoot, string outputCsv)
    {
        var expected = Path.GetFullPath(Path.Combine(packageRoot, "RESULTS"));
        var actual = Path.GetFullPath(outputCsv);
        var relative = Path.GetRelativePath(expected, actual);
        if (Path.IsPathRooted(relative) ||
            relative.Equals("..", StringComparison.Ordinal) ||
            relative.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal) ||
            !Path.GetFileName(actual).Equals("PresentMon.csv", StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("PresentMon destination must be a GCO session CSV inside RESULTS.");
        return actual;
    }

    // No RegisterHotKey and no extra F11 handler. Recording begins immediately;
    // CET / GRSP timestamps define the useful analysis window later.
    public static IReadOnlyList<string> StartArguments(string csv) =>
    [
        "--process_name", "Cyberpunk2077.exe",
        "--session_name", SessionName,
        "--stop_existing_session",
        "--output_file", csv,
        "--date_time",
        "--no_console_stats",
        "--no_track_input"
    ];

    // Official v2.6.0 CLI operation: gracefully end the named ETW recording,
    // permitting the writer to flush the CSV before verification.
    public static IReadOnlyList<string> StopArguments() =>
        ["--terminate_existing_session", "--session_name", SessionName];

    // Upstream PresentMon CommandLineTests: code 7 means the named session
    // does not exist. Accept only if no matching owned process remained.
    public static bool StopExitAccepted(int exitCode, int ownedProcessCount) =>
        exitCode == 0 || (exitCode == 7 && ownedProcessCount == 0);

    public static string NewSessionCsv(string packageRoot, string sessionId) =>
        ValidateOutputCsv(packageRoot, Path.Combine(
            CollectedCaptureStore.SessionFolder(packageRoot, sessionId),
            "FrameTime", Guid.NewGuid().ToString("N"), "PresentMon.csv"));

    // Data evidence, not merely a non-empty file: verify a real Cyberpunk
    // frame row and the timestamp column required for later correlation.
    public static int CountVerifiedFrames(string csv)
    {
        if (!File.Exists(csv)) throw new InvalidDataException("PresentMon CSV is missing.");
        if ((File.GetAttributes(csv) & FileAttributes.ReparsePoint) != 0)
            throw new InvalidDataException("Linked PresentMon CSV is not accepted.");
        if (new FileInfo(csv).Length < 80)
            throw new InvalidDataException("PresentMon CSV is empty or too small.");
        using var reader = new StreamReader(csv);
        var header = reader.ReadLine()?.TrimStart('\uFEFF');
        if (header is null) throw new InvalidDataException("PresentMon CSV lacks a header.");
        var columns = header.Split(',');
        var app = Array.FindIndex(columns, c => c.Trim().Trim('"') == "Application");
        var timestamp = Array.FindIndex(columns, c => c.Trim().Trim('"') == "CPUStartDateTime");
        if (app < 0 || timestamp < 0)
            throw new InvalidDataException("PresentMon CSV lacks Application or CPUStartDateTime.");
        var matches = 0;
        string? line;
        while ((line = reader.ReadLine()) is not null)
        {
            var row = line.Split(',');
            if (row.Length <= Math.Max(app, timestamp)) continue;
            if (!row[app].Trim().Trim('"').Equals("Cyberpunk2077.exe", StringComparison.OrdinalIgnoreCase) ||
                string.IsNullOrWhiteSpace(row[timestamp])) continue;
            if (++matches == 2) return matches;
        }
        if (matches == 0)
            throw new InvalidDataException("PresentMon CSV contains no Cyberpunk2077.exe frames.");
        return matches;
    }
}
