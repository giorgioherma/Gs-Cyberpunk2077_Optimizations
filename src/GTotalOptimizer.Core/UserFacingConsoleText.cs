using System.Text.RegularExpressions;

namespace GTotalOptimizer.Core;

// Capture is a concise, actionable console, not an unbounded owner log.
// Normal statuses use one line; structured troubleshooting keeps distinct rows.
// Full native diagnostics remain in Trace and the source-owner reports.
public static class UserFacingConsoleText
{
    public const int MaxLength = 108;
    public const int MaxGuidanceLines = 6;
    private const string FrameworkBlocked = "INSTALL BLOCKED — ";

    public static string Compact(string text)
    {
        var message = Regex.Replace(text ?? "", @"\s+", " ").Trim();
        if (message.Length <= MaxLength) return message;
        var cut = message.LastIndexOf(' ', MaxLength - 1, MaxLength);
        if (cut < 44) cut = MaxLength - 1;
        return message[..cut].TrimEnd(' ', '.', ',', ';', ':') + "…";
    }

    // Bound unexpected backend output; preserve distinct authored guidance rows.
    public static string Render(string text)
    {
        // Authored framework guidance is a bounded, single flowing paragraph:
        // let WPF wrap it at the available console width, not at 108 chars
        // or at hardcoded line breaks intended for a narrow previous footer.
        // This exception applies only to the structured five-gate preflight;
        // arbitrary backend output remains compact/bounded below.
        if (text?.StartsWith(FrameworkBlocked, StringComparison.Ordinal) == true)
        {
            var singleLine = Regex.Replace(text, @"\s+", " ").Trim();
            return singleLine.Length <= 768 ? singleLine : singleLine[..767] + "…";
        }

        var lines = (text ?? "").Split('\n', StringSplitOptions.RemoveEmptyEntries |
            StringSplitOptions.TrimEntries);
        return string.Join("\n", lines.Take(MaxGuidanceLines).Select(Compact));
    }

    // Preserve all five actual preflight gates and each required/found version.
    // Never override or guess the authoritative compatibility decision.
    public static string FrameworkGuidance(FrameworkPreflightResult result)
    {
        if (result.Ready) return "Frameworks verified. Click INSTALL PROFILERS.";
        // Keep all authoritative required/found values while letting the
        // console consume its actual width up to Continue at every window size.
        var issues = result.Issues.Select(DescribeFrameworkIssue);
        return Render(FrameworkBlocked + string.Join("  |  ", issues));
    }

    private static string DescribeFrameworkIssue(string raw)
    {
        var issue = Regex.Replace(raw ?? "", @"\s+", " ").Trim();
        if (issue.StartsWith("0-Engine", StringComparison.OrdinalIgnoreCase))
            return "0-Engine: missing init.lua in CET mods/0-Engine. Install 0-Engine.";
        if (issue.StartsWith("Cyberpunk 2077", StringComparison.OrdinalIgnoreCase))
        {
            if (issue.Contains("; found ", StringComparison.OrdinalIgnoreCase))
                return issue.Replace(" is required; found ", " required; found ",
                    StringComparison.OrdinalIgnoreCase) + ". Use the supported game version.";
            return issue + ". Select the supported game installation.";
        }
        if (issue.StartsWith("CET ", StringComparison.OrdinalIgnoreCase) ||
            issue.StartsWith("RED4ext ", StringComparison.OrdinalIgnoreCase) ||
            issue.StartsWith("redscript ", StringComparison.OrdinalIgnoreCase))
        {
            if (issue.Contains(" is missing", StringComparison.OrdinalIgnoreCase))
                return issue + ". Install the required version.";
            if (issue.Contains(" requires ", StringComparison.OrdinalIgnoreCase))
                return issue + ". Install the supported version.";
            if (issue.Contains("scc_lib.dll", StringComparison.OrdinalIgnoreCase))
                return "redscript: scc_lib.dll mismatched. Reinstall official 0.5.31 files.";
            if (issue.Contains("interfaces", StringComparison.OrdinalIgnoreCase))
                return issue + ". Repair the framework installation.";
            if (issue.Contains("could not be verified", StringComparison.OrdinalIgnoreCase))
                return issue + ". Reinstall the supported release.";
            return issue + ". Check framework files and version.";
        }
        return issue;
    }

    public static string CollectionGuidance(CollectedCaptureIndex capture)
    {
        var status = "CET " + (capture.Cet is null ? "MISSING" : "OK") +
            " | REDscript " + (capture.Redscript is null ? "MISSING" : "OK") +
            " | PresentMon " + (capture.FrameTimeCsv is null ? "MISSING" : "OK");
        if (!capture.PresentMonStopVerified)
            return Render(status + ". Collect ended; START again or RESTORE.");
        if (capture.Cet is null || capture.Redscript is null)
        {
            var missing = new List<string>();
            if (capture.Cet is null) missing.Add("CET");
            if (capture.Redscript is null) missing.Add("REDscript");
            return Render("Saved: " + status + ".\n" +
                string.Join(" + ", missing) + " MISSING. Click START CYBERPUNK; F11 start/stop.");
        }
        return Render("Saved: " + status + ". Click CONTINUE.");
    }

    public static string Failure(string context, string detail)
    {
        if (detail.Contains("Close Cyberpunk", StringComparison.OrdinalIgnoreCase))
            return "Close Cyberpunk before continuing.";
        if (detail.Contains("No completed", StringComparison.OrdinalIgnoreCase) &&
            detail.Contains("capture", StringComparison.OrdinalIgnoreCase))
            return "No completed capture found. Stop profiling with F11.";
        if (detail.Contains("Original profiler files restored", StringComparison.OrdinalIgnoreCase))
            return "Original files restored. PresentMon still stopping; retry RESTORE.";
        if (detail.Contains("RECOVERY REQUIRED", StringComparison.OrdinalIgnoreCase))
            return "Restore incomplete. Game files may still be modified. Retry RESTORE.";
        if (detail.Contains("previous capture", StringComparison.OrdinalIgnoreCase) &&
            detail.Contains("pending", StringComparison.OrdinalIgnoreCase))
            return "Previous capture pending. Collect or start a fresh session.";
        if (detail.Contains("version", StringComparison.OrdinalIgnoreCase) &&
            (detail.Contains("requires", StringComparison.OrdinalIgnoreCase) ||
             detail.Contains("unsupported", StringComparison.OrdinalIgnoreCase)))
            return "Unsupported framework version. See the required version in GAME ROOT.";

        var prefix = context switch
        {
            "Recovery required" or "Restoration blocked" => "Restore failed",
            "Profiler installation" => "Installation failed",
            "Capture collection" or "Collection" => "Collection failed",
            "Launch and capture" or "Start" => "Game launch failed",
            "Evidence verification" => "Capture verification failed",
            "Framework preflight" => "Framework check failed",
            _ => context
        };

        var full = prefix + ": " + Regex.Replace(detail ?? "", @"\s+", " ").Trim();
        if (full.Length <= MaxLength) return full;

        return prefix + (context switch
        {
            "Recovery required" or "Restoration blocked" =>
                ". Close Cyberpunk; retry RESTORE PROFILERS.",
            "Profiler installation" =>
                ". Close Cyberpunk; restore profilers if recovery is available.",
            "Capture collection" or "Collection" =>
                ". Close Cyberpunk; retry COLLECT RESULTS.",
            "Launch and capture" or "Start" =>
                ". Check the game folder, then retry.",
            "Evidence verification" =>
                ". Record a new F11 capture and collect again.",
            "Framework preflight" =>
                ". Fix the requirements listed under GAME ROOT.",
            _ => ". Retry the step; check the diagnostic log if it persists."
        });
    }
}
