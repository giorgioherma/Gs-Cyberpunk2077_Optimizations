namespace GTotalOptimizer.Core;

public static class CaptureAdmissionPolicy
{
    public static bool CanCollect(bool hasOwnedSession, bool launchAttemptStarted) =>
        hasOwnedSession && launchAttemptStarted;

    public static bool CanContinue(CollectedCaptureIndex? capture, bool presentMonStopped) =>
        presentMonStopped && capture?.PresentMonStopVerified == true && capture.CollectionAttempted &&
        capture.Cet is not null && capture.Redscript is not null;

    public static string CollectionStatus(CollectedCaptureIndex capture,
        string? monWarning = null, string? nativeWarning = null)
    {
        static string Item(string name, bool present) => name + ": RESULTS " +
            (present ? "VERIFIED" : "MISSING");
        var status = string.Join("  |  ", new[]
        {
            Item("G-CET Profiler", capture.Cet is not null),
            Item("G-REDscript Profiler", capture.Redscript is not null),
            Item("PresentMon", capture.FrameTimeCsv is not null)
        });
        if (!string.IsNullOrWhiteSpace(monWarning))
            status += "  |  PresentMon: " + monWarning;
        if (!string.IsNullOrWhiteSpace(nativeWarning))
            status += "  |  " + nativeWarning;
        if (capture.Cet is null || capture.Redscript is null)
            return status + "  |  Native capture incomplete. CONTINUE disabled.";
        return status + (capture.FrameTimeCsv is null
            ? "  |  Frame-time data unavailable. CONTINUE enabled."
            : "  |  Native captures verified. CONTINUE enabled.");
    }
}
