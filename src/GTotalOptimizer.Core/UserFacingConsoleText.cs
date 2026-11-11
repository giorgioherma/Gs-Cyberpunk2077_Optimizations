using System.Text.RegularExpressions;

namespace GTotalOptimizer.Core;

// The on-screen Capture console is a short status line, not an archive or a log.
// Callers retain the original full message for the tooltip and diagnostics.
public static class UserFacingConsoleText
{
    public const int MaxLength = 108;

    public static string Compact(string text)
    {
        var message = Regex.Replace(text ?? "", @"\s+", " ").Trim();
        if (message.Length <= MaxLength) return message;

        // Only a final display safety net. Authored UI messages should fit
        // without clipping; never discard the original detailed diagnostic.
        var cut = message.LastIndexOf(' ', MaxLength - 1, MaxLength);
        if (cut < 44) cut = MaxLength - 1;
        return message[..cut].TrimEnd(' ', '.', ',', ';', ':') + "…";
    }

    public static string Failure(string context, string detail)
    {
        if (detail.Contains("Close Cyberpunk", StringComparison.OrdinalIgnoreCase))
            return "Close Cyberpunk before continuing.";
        if (detail.Contains("No completed", StringComparison.OrdinalIgnoreCase) &&
            detail.Contains("capture", StringComparison.OrdinalIgnoreCase))
            return "No completed capture found. Stop profiling with F11.";
        if (detail.Contains("RECOVERY REQUIRED", StringComparison.OrdinalIgnoreCase))
            return "Restore incomplete. Game files may still be modified; retry Restore.";
        if (detail.Contains("previous capture", StringComparison.OrdinalIgnoreCase) &&
            detail.Contains("pending", StringComparison.OrdinalIgnoreCase))
            return "Previous capture is pending. Collect or start a fresh session.";
        if (detail.Contains("version", StringComparison.OrdinalIgnoreCase) &&
            (detail.Contains("requires", StringComparison.OrdinalIgnoreCase) ||
             detail.Contains("unsupported", StringComparison.OrdinalIgnoreCase)))
            return "Unsupported framework version. Check requirements above.";

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

        // Keep short actionable errors (e.g. executable missing). Large paths,
        // stack traces and owner recovery essays are available on hover instead.
        var full = prefix + ": " + Regex.Replace(detail, @"\s+", " ").Trim();
        return full.Length <= MaxLength
            ? full
            : prefix + ". Hover over this message for details.";
    }
}
