namespace GTotalOptimizer.Core;

public static class CaptureAdmissionPolicy
{
    // Collect is an explicit one-time completion per capture attempt, not a
    // permanent launch-state toggle. A failed PresentMon stop may be retried;
    // after verified shutdown the button stays disabled even if no native
    // F11 capture exists. BeginCaptureAttempt assigns a new session ID and
    // therefore a fresh uncollected index for the next game run.
    public static bool CanCollect(bool hasOwnedSession, bool launchAttemptStarted,
        CollectedCaptureIndex? capture = null) =>
        hasOwnedSession && launchAttemptStarted &&
        !(capture?.CollectionAttempted == true && capture.PresentMonStopVerified);

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
        if (!capture.PresentMonStopVerified)
            return status + "  |  PresentMon shutdown unverified. CONTINUE disabled.";
        if (capture.Cet is null || capture.Redscript is null)
            return status + "  |  Native capture incomplete. CONTINUE disabled.";
        return status + (capture.FrameTimeCsv is null
            ? "  |  Frame-time data unavailable. CONTINUE enabled."
            : "  |  Native captures verified. CONTINUE enabled.");
    }
}
